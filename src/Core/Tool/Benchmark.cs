using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SysToolbox.Core
{
    /// <summary>一次基准测试结果。</summary>
    public sealed class BenchmarkResult
    {
        public double CpuSingleMops = 0;   // 单核：百万次运算/秒
        public double CpuMultiMops = 0;    // 多核：百万次运算/秒
        public double MemReadMBs = 0;      // 内存顺序读 MB/s
        public double MemWriteMBs = 0;     // 内存顺序写 MB/s
        public double DiskWriteMBs = 0;    // 磁盘顺序写 MB/s
        public double DiskReadMBs = 0;     // 磁盘顺序读 MB/s
        public double AesMBs = 0;          // AES-256 加密吞吐 MB/s（数据安全/加密场景）
        public double GzipMBs = 0;         // GZip 压缩吞吐 MB/s（压缩/打包场景）
        public int Disk4kIops = 0;         // 磁盘 4K 随机读 IOPS（系统响应/小文件场景）
        public int Score = 0;              // 综合评分（相对值，越大越好）
        public int CoreCount = 0;
        public DateTime When = DateTime.Now;

        public string ToLine()
        {
            return string.Join("|",
                When.ToString("yyyy-MM-dd HH:mm:ss"),
                CpuSingleMops.ToString("0.00", CultureInfo.InvariantCulture),
                CpuMultiMops.ToString("0.00", CultureInfo.InvariantCulture),
                MemReadMBs.ToString("0", CultureInfo.InvariantCulture),
                MemWriteMBs.ToString("0", CultureInfo.InvariantCulture),
                DiskWriteMBs.ToString("0", CultureInfo.InvariantCulture),
                DiskReadMBs.ToString("0", CultureInfo.InvariantCulture),
                CoreCount.ToString(),
                Score.ToString(),
                AesMBs.ToString("0", CultureInfo.InvariantCulture),
                GzipMBs.ToString("0", CultureInfo.InvariantCulture),
                Disk4kIops.ToString(CultureInfo.InvariantCulture));
        }

        public static BenchmarkResult FromLine(string line)
        {
            BenchmarkResult r = new BenchmarkResult();
            try
            {
                string[] f = line.Split('|');
                if (f.Length < 9) return r;
                DateTime dt;
                if (DateTime.TryParse(f[0], out dt)) r.When = dt;
                r.CpuSingleMops = ParseD(f[1]);
                r.CpuMultiMops = ParseD(f[2]);
                r.MemReadMBs = ParseD(f[3]);
                r.MemWriteMBs = ParseD(f[4]);
                r.DiskWriteMBs = ParseD(f[5]);
                r.DiskReadMBs = ParseD(f[6]);
                int c;
                if (int.TryParse(f[7], out c)) r.CoreCount = c;
                int s;
                if (int.TryParse(f[8], out s)) r.Score = s;
                if (f.Length >= 12) // 新格式扩展段（老记录缺省为 0）
                {
                    r.AesMBs = ParseD(f[9]);
                    r.GzipMBs = ParseD(f[10]);
                    int iops;
                    if (int.TryParse(f[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out iops)) r.Disk4kIops = iops;
                }
            }
            catch
            {
            }
            return r;
        }

        private static double ParseD(string s)
        {
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }
    }

    public static class Benchmark
    {
        private static double _sink; // 防止 JIT 优化掉计算

        public static string HistoryPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SysToolbox", "benchmarks.csv");
            }
        }

        // ---------------- 运行 ----------------

        public static BenchmarkResult Run(Action<string> status)
        {
            BenchmarkResult r = new BenchmarkResult();
            r.CoreCount = Environment.ProcessorCount;

            if (status != null) status("CPU 单核…");
            r.CpuSingleMops = CpuSingle(1500);
            if (status != null) status("CPU 多核…");
            r.CpuMultiMops = CpuMulti(1500);
            if (status != null) status("内存读写…");
            Memory(out r.MemReadMBs, out r.MemWriteMBs);
            if (status != null) status("磁盘读写…");
            Disk(out r.DiskWriteMBs, out r.DiskReadMBs);
            if (status != null) status("AES 加密吞吐…");
            r.AesMBs = AesThroughput();
            if (status != null) status("GZip 压缩吞吐…");
            r.GzipMBs = GzipThroughput();
            if (status != null) status("磁盘 4K 随机读…");
            r.Disk4kIops = Disk4kRandomRead();

            r.Score = ComputeScore(r);
            r.When = DateTime.Now;
            Save(r);
            return r;
        }

        /// <summary>轻量单核探测（供测试用，约 300ms）。</summary>
        public static double QuickCpuProbe()
        {
            return CpuSingle(300);
        }

        private static double CpuSingle(int ms)
        {
            DoWork(20000);
            Stopwatch sw = Stopwatch.StartNew();
            long iters = 0;
            const int batch = 20000;
            double acc = 0;
            while (sw.ElapsedMilliseconds < ms)
            {
                acc += DoWork(batch);
                iters += batch;
            }
            sw.Stop();
            _sink += acc;
            return iters / sw.Elapsed.TotalSeconds / 1e6;
        }

        private static double CpuMulti(int ms)
        {
            int cores = Math.Max(1, Environment.ProcessorCount);
            long total = 0;
            int remaining = cores;
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                for (int k = 0; k < cores; k++)
                {
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        DoWork(20000);
                        Stopwatch sw = Stopwatch.StartNew();
                        long local = 0;
                        double acc = 0;
                        const int batch = 20000;
                        while (sw.ElapsedMilliseconds < ms)
                        {
                            acc += DoWork(batch);
                            local += batch;
                        }
                        _sink += acc;
                        Interlocked.Add(ref total, local);
                        if (Interlocked.Decrement(ref remaining) == 0) done.Set();
                    });
                }
                done.WaitOne();
            }
            return (total / (ms / 1000.0)) / 1e6;
        }

        private static double DoWork(int n)
        {
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                acc += Math.Sqrt(i * 0.001 + 1.0) * Math.Sin(i * 0.0001 + 0.5);
            }
            return acc;
        }

        private static void Memory(out double readMBs, out double writeMBs)
        {
            int mb = 128;
            byte[] buf = new byte[mb * 1024 * 1024];

            Stopwatch sw = Stopwatch.StartNew();
            for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(i & 0xFF);
            sw.Stop();
            writeMBs = mb / sw.Elapsed.TotalSeconds;

            sw.Restart();
            long sum = 0;
            for (int i = 0; i < buf.Length; i += 8)
            {
                sum += buf[i] + buf[i + 1] + buf[i + 2] + buf[i + 3] + buf[i + 4] + buf[i + 5] + buf[i + 6] + buf[i + 7];
            }
            sw.Stop();
            readMBs = mb / sw.Elapsed.TotalSeconds;
            _sink += sum;
        }

        private static void Disk(out double writeMBs, out double readMBs)
        {
            writeMBs = 0;
            readMBs = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "SysToolbox_benchmark.tmp");
            int chunk = 8 * 1024 * 1024;
            int totalMB = 256;
            int loops = totalMB / 8;
            byte[] buf = new byte[chunk];
            for (int i = 0; i < buf.Length; i++) buf[i] = (byte)(i & 0xFF);

            try
            {
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                    FileShare.None, chunk, FileOptions.SequentialScan))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    for (int i = 0; i < loops; i++) fs.Write(buf, 0, chunk);
                    fs.Flush();
                    sw.Stop();
                    writeMBs = totalMB / sw.Elapsed.TotalSeconds;
                }

                long sum = 0;
                using (FileStream fs = new FileStream(tmp, FileMode.Open, FileAccess.Read,
                    FileShare.Read, chunk, FileOptions.SequentialScan))
                {
                    byte[] rb = new byte[chunk];
                    Stopwatch sw = Stopwatch.StartNew();
                    int n;
                    while ((n = fs.Read(rb, 0, chunk)) > 0)
                    {
                        for (int j = 0; j < n; j += 8) sum += rb[j];
                    }
                    sw.Stop();
                    readMBs = totalMB / sw.Elapsed.TotalSeconds;
                }
                _sink += sum;
            }
            finally
            {
                try { File.Delete(tmp); }
                catch { }
            }
        }

        private static int ComputeScore(BenchmarkResult r)
        {
            double score = r.CpuMultiMops * 4.0
                         + r.CpuSingleMops * 2.0
                         + r.MemReadMBs / 1000.0
                         + r.MemWriteMBs / 1000.0
                         + r.DiskReadMBs / 1000.0
                         + r.DiskWriteMBs / 1000.0
                         + r.AesMBs / 100.0
                         + r.GzipMBs / 50.0
                         + r.Disk4kIops / 50.0;
            return (int)Math.Round(score);
        }

        // ---------------- 新增测试 ----------------

        /// <summary>AES-256 加密吞吐（MB/s）——衡量数据加密/解密场景的 CPU+指令集性能。</summary>
        private static double AesThroughput()
        {
            int mb = 64;
            byte[] data = new byte[mb * 1024 * 1024];
            for (int i = 0; i < data.Length; i += 4096) data[i] = (byte)(i >> 12);
            try
            {
                using (System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.GenerateKey();
                    aes.GenerateIV();
                    using (System.Security.Cryptography.ICryptoTransform enc = aes.CreateEncryptor())
                    using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                    using (System.Security.Cryptography.CryptoStream cs = new System.Security.Cryptography.CryptoStream(
                        ms, enc, System.Security.Cryptography.CryptoStreamMode.Write))
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        cs.Write(data, 0, data.Length);
                        cs.FlushFinalBlock();
                        sw.Stop();
                        _sink += ms.Length;
                        return mb / sw.Elapsed.TotalSeconds;
                    }
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>GZip 压缩吞吐（MB/s）——衡量压缩/打包场景的综合性能。</summary>
        private static double GzipThroughput()
        {
            int mb = 48;
            byte[] data = new byte[mb * 1024 * 1024];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)((i * 31 + 7) & 0xFF);
            try
            {
                using (System.IO.MemoryStream output = new System.IO.MemoryStream())
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(
                        output, System.IO.Compression.CompressionLevel.Fastest, true))
                    {
                        gz.Write(data, 0, data.Length);
                    }
                    sw.Stop();
                    _sink += output.Length;
                    return mb / sw.Elapsed.TotalSeconds;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>磁盘 4K 随机读 IOPS——系统响应速度与小文件场景的关键指标。</summary>
        private static int Disk4kRandomRead()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "SysToolbox_bench4k.tmp");
            try
            {
                const int fileMB = 32;
                const int blockSize = 4096;
                byte[] block = new byte[blockSize];
                using (FileStream w = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    for (int i = 0; i < fileMB; i++) w.Write(block, 0, blockSize);
                }

                long blocks = (long)fileMB * 1024 * 1024 / blockSize;
                Random rnd = new Random(12345);
                int ops = 0;
                long sum = 0;
                Stopwatch sw = Stopwatch.StartNew();
                using (FileStream fs = new FileStream(tmp, FileMode.Open, FileAccess.Read,
                    FileShare.Read, blockSize, FileOptions.RandomAccess))
                {
                    while (sw.ElapsedMilliseconds < 1500)
                    {
                        for (int i = 0; i < 200; i++)
                        {
                            long pos = (long)(rnd.NextDouble() * blocks) * blockSize;
                            if (fs.Seek(pos, SeekOrigin.Begin) >= 0 && fs.Read(block, 0, blockSize) > 0)
                            {
                                sum += block[0];
                                ops++;
                            }
                        }
                    }
                    _sink += sum;
                }
                sw.Stop();
                return (int)(ops / sw.Elapsed.TotalSeconds);
            }
            catch
            {
                return 0;
            }
            finally
            {
                try { File.Delete(tmp); }
                catch { }
            }
        }

        // ---------------- 历史记录 ----------------

        public static void Save(BenchmarkResult r)
        {
            try
            {
                string dir = Path.GetDirectoryName(HistoryPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(HistoryPath, r.ToLine() + Environment.NewLine);
            }
            catch
            {
            }
        }

        public static List<BenchmarkResult> Load()
        {
            List<BenchmarkResult> list = new List<BenchmarkResult>();
            try
            {
                if (!File.Exists(HistoryPath)) return list;
                string[] lines = File.ReadAllLines(HistoryPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    BenchmarkResult r = BenchmarkResult.FromLine(line);
                    if (r.Score > 0) list.Add(r);
                }
            }
            catch
            {
            }
            list.Sort(delegate (BenchmarkResult a, BenchmarkResult b) { return b.When.CompareTo(a.When); });
            return list;
        }

        public static void ClearHistory()
        {
            try
            {
                if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            }
            catch
            {
            }
        }
    }
}
