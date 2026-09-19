using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace GuyueBox.Core
{
    /// <summary>
    /// 安全删除（文件粉碎）：先用随机数据覆盖指定次数，再删除，
    /// 使被删内容难以通过数据恢复软件还原。所有失败都做隔离，不中断整体流程。
    /// </summary>
    public static class Shredder
    {
        /// <summary>单次擦除的缓冲区大小。</summary>
        private const int BufferSize = 65536;

        public sealed class ShredResult
        {
            public long Bytes;
            public int Files;
            public int Errors;
            public int SkippedDirs;
            public List<string> ErrorMessages = new List<string>();
        }

        /// <summary>估算一个文件或目录（含子目录）的总字节数。</summary>
        public static long Measure(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                // 联接 / 符号链接必须在这里拦掉：Directory.Exists 会穿透到目标，
                // 但 Shred 对联接只删链接本身（SkippedDirs++、释放 0 字节）。
                // 不拦截的话，列表会显示目标体积（可能几十 GB），实际却粉碎 0 字节
                if (Directory.Exists(path) && !IsReparseDir(path)) return MeasureDir(path);
            }
            catch
            {
            }
            return 0;
        }

        /// <summary>目录联接/符号链接判定：跳过，防止越界粉碎目标真实文件或无限递归。</summary>
        private static bool IsReparseDir(string dir)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                return (di.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch { return true; } // 无法判定时按危险处理（跳过）
        }

        private static long MeasureDir(string dir)
        {
            long total = 0;
            try
            {
                string[] files = Directory.GetFiles(dir);
                object gate = new object();

                // 并行统计本目录直属文件：每个文件都是一次独立的元数据查询
                // 只并行「文件」不并行「子目录」，避免递归时嵌套并行导致并行度爆炸
                Parallel.For(0, files.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    () => 0L,
                    (i, state, local) =>
                    {
                        try { local += new FileInfo(files[i]).Length; }
                        catch { }
                        return local;
                    },
                    delegate(long local) { lock (gate) { total += local; } });

                string[] sub = Directory.GetDirectories(dir);
                for (int i = 0; i < sub.Length; i++)
                {
                    if (IsReparseDir(sub[i])) continue;
                    total += MeasureDir(sub[i]);
                }
            }
            catch
            {
            }
            return total;
        }

        /// <summary>
        /// 粉碎一个文件或目录。passes 为覆盖次数（1~7）。
        /// 返回处理结果（已删除字节、文件数、错误数）。
        /// </summary>
        public static ShredResult Shred(string path, int passes)
        {
            ShredResult result = new ShredResult();
            if (passes < 1) passes = 1;
            if (passes > 7) passes = 7;

            if (string.IsNullOrEmpty(path))
            {
                result.Errors++;
                result.ErrorMessages.Add("路径为空。");
                return result;
            }

            try
            {
                if (File.Exists(path)) ShredFile(path, passes, result);
                else if (Directory.Exists(path)) ShredDir(path, passes, result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                else
                {
                    result.Errors++;
                    result.ErrorMessages.Add("不存在：" + path);
                }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add(path + "：" + ex.Message);
            }
            return result;
        }

        private static void ShredDir(string dir, int passes, ShredResult result, HashSet<string> visited)
        {
            // 联接/符号链接防护：绝不进入目标目录，防止越界粉碎真实文件；visited 防循环联接死递归
            if (IsReparseDir(dir) || !visited.Add(dir.ToLowerInvariant()))
            {
                result.SkippedDirs++;
                try { Directory.Delete(dir, false); } // 只删联接本身，不动目标
                catch { }
                return;
            }
            try
            {
                string[] files = Directory.GetFiles(dir);
                object gate = new object();

                // 粉碎是 IO + CPU 双密集（每个 pass 都要重写整个文件），
                // 同一目录内的文件彼此独立，并行可显著缩短总耗时。
                // 结果用「每线程本地累加 + 结束合并」，避免逐文件加锁竞争。
                Parallel.For(0, files.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    () => new ShredResult(),
                    (i, state, local) => { ShredFile(files[i], passes, local); return local; },
                    delegate(ShredResult local) { lock (gate) { Merge(result, local); } });

                // 子目录仍串行递归：visited 防环集合不是线程安全的，
                // 且嵌套并行会在深层目录树上造成并行度爆炸
                string[] sub = Directory.GetDirectories(dir);
                for (int i = 0; i < sub.Length; i++) ShredDir(sub[i], passes, result, visited);

                try { Directory.Delete(dir, false); }
                catch { }
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add(dir + "：" + ex.Message);
            }
        }

        /// <summary>把并行粉碎的本地结果合并进总结果（锁内一次性汇入）。</summary>
        private static void Merge(ShredResult target, ShredResult local)
        {
            target.Bytes += local.Bytes;
            target.Files += local.Files;
            target.Errors += local.Errors;
            target.SkippedDirs += local.SkippedDirs;
            if (local.ErrorMessages.Count > 0) target.ErrorMessages.AddRange(local.ErrorMessages);
        }

        private static void ShredFile(string path, int passes, ShredResult result)
        {
            long len;
            try
            {
                FileInfo fi = new FileInfo(path);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // 符号链接/重解析点：仅删除链接本身，不跟踪目标
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    result.Files++;
                    return;
                }
                len = fi.Length;
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add(Path.GetFileName(path) + "：" + ex.Message);
                return;
            }

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[BufferSize];
                    RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
                    try
                    {
                        for (int p = 0; p < passes; p++)
                        {
                            fs.Seek(0, SeekOrigin.Begin);
                            long remaining = len;
                            while (remaining > 0)
                            {
                                int toWrite = remaining > BufferSize ? BufferSize : (int)remaining;
                                rng.GetBytes(buf);
                                fs.Write(buf, 0, toWrite);
                                remaining -= toWrite;
                            }
                            fs.Flush();
                        }
                    }
                    finally
                    {
                        rng.Dispose();
                    }
                }

                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                result.Bytes += len;
                result.Files++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add(Path.GetFileName(path) + "：" + ex.Message);
            }
        }
    }
}
