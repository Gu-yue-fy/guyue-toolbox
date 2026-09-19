using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GuyueBox.Core
{
    /// <summary>
    /// 磁盘空间分析：遍历指定根目录，找出占用空间最大的文件与目录。
    /// 只做读取，不修改任何内容。
    /// 遍历按「第一层子目录」分片并行：每个 worker 独立遍历自己的子树（本地栈 / 本地聚合），
    /// 结束后一次性合并进总表——大目录树上接近核数倍提速，且各分片不共享可变状态。
    /// </summary>
    public sealed class DiskAnalyzer
    {
        public sealed class Entry
        {
            public string PathText = "";
            public long Size;
            public DateTime Modified;
            public bool IsDirectory;

            public string SizeText
            {
                get { return SysInfo.FormatSize(Size); }
            }

            public string NameText
            {
                get
                {
                    if (IsDirectory) return PathText;
                    try { return Path.GetFileName(PathText); }
                    catch { return PathText; }
                }
            }
        }

        public sealed class Result
        {
            public List<Entry> Files = new List<Entry>();
            public List<Entry> Directories = new List<Entry>();
            public long ScannedBytes;
            public int FileCount;
            public int DirectoryCount;
            public int ErrorCount;
            public long ElapsedMs;
            public bool Cancelled;
        }

        private volatile bool _cancel;

        /// <summary>并行遍历时供 Progress 回调读取的全局进度计数（Interlocked 累加）。</summary>
        private int _progFiles;
        private int _progDirs;

        /// <summary>进度回调：当前目录、已扫文件数、已扫目录数。</summary>
        public event Action<string, int, int> Progress;

        public void Cancel()
        {
            _cancel = true;
        }

        public Result Analyze(string root, int topFiles, int topDirs, long minFileSize, int maxDepth)
        {
            Result result = new Result();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return result;

            _cancel = false;
            _progFiles = 0;
            _progDirs = 0;
            Stopwatch sw = Stopwatch.StartNew();

            List<Entry> topFileList = new List<Entry>();
            Dictionary<string, long> dirSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            object gate = new object();

            try
            {
                string rootFull = null;
                try { rootFull = Path.GetFullPath(root); }
                catch { rootFull = root; }

                // ---- 分片：根下第一层子目录，每片是一棵互不重叠的子树 ----
                // 被排除的分片（重解析点 / 超深）与旧版串行行为一致：不遍历
                List<string> shards = new List<string>();
                try
                {
                    string[] level1 = Directory.GetDirectories(rootFull);
                    for (int i = 0; i < level1.Length; i++)
                    {
                        try
                        {
                            DirectoryInfo di = new DirectoryInfo(level1[i]);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (DepthOf(level1[i], rootFull) > maxDepth) continue;
                            shards.Add(level1[i]);
                        }
                        catch
                        {
                            result.ErrorCount++;
                        }
                    }
                }
                catch
                {
                    result.ErrorCount++;
                }

                Parallel.ForEach(shards,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    delegate(string shard)
                    {
                        List<Entry> localTop = new List<Entry>();
                        Dictionary<string, long> localSizes =
                            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                        int files = 0, dirs = 0, errors = 0;
                        long bytes = 0;

                        WalkShard(shard, rootFull, maxDepth, minFileSize, topFiles,
                            localTop, localSizes, true, ref files, ref dirs, ref errors, ref bytes);

                        Merge(localTop, localSizes, files, dirs, errors, bytes,
                            topFileList, dirSizes, result, gate, topFiles);
                    });

                // ---- 根目录直属文件：不属于任何子树分片，单独统计（不再向下展开） ----
                {
                    List<Entry> localTop = new List<Entry>();
                    Dictionary<string, long> localSizes =
                        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    int files = 0, dirs = 0, errors = 0;
                    long bytes = 0;

                    WalkShard(rootFull, rootFull, maxDepth, minFileSize, topFiles,
                        localTop, localSizes, false, ref files, ref dirs, ref errors, ref bytes);

                    Merge(localTop, localSizes, files, dirs, errors, bytes,
                        topFileList, dirSizes, result, gate, topFiles);
                }

                // 目录排名
                List<Entry> dirList = new List<Entry>();
                foreach (KeyValuePair<string, long> kv in dirSizes)
                    dirList.Add(MakeEntry(kv.Key, kv.Value, DateTime.MinValue, true));
                dirList.Sort(delegate (Entry a, Entry b) { return b.Size.CompareTo(a.Size); });
                if (dirList.Count > topDirs) dirList = dirList.GetRange(0, topDirs);

                result.Files = topFileList;
                result.Directories = dirList;
            }
            catch
            {
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Cancelled = _cancel;
            return result;
        }

        /// <summary>把一个分片的本地结果合并进总表（锁内一次性汇入，避免逐文件竞争）。</summary>
        private static void Merge(List<Entry> localTop, Dictionary<string, long> localSizes,
            int files, int dirs, int errors, long bytes,
            List<Entry> topFileList, Dictionary<string, long> dirSizes, Result result,
            object gate, int topFiles)
        {
            lock (gate)
            {
                for (int i = 0; i < localTop.Count; i++)
                {
                    AddTop(topFileList, localTop[i], topFiles, topFiles);
                }
                foreach (KeyValuePair<string, long> kv in localSizes)
                {
                    long cur;
                    dirSizes.TryGetValue(kv.Key, out cur);
                    dirSizes[kv.Key] = cur + kv.Value;
                }
                result.FileCount += files;
                result.DirectoryCount += dirs;
                result.ScannedBytes += bytes;
                result.ErrorCount += errors;
            }
        }

        /// <summary>
        /// 串行遍历一个分片子树：栈 / 计数 / 目录大小表全部是本地状态，天然线程安全。
        /// descend = false 时只统计 startDir 直属文件（用于根目录直属文件这一"分片"）。
        /// </summary>
        private void WalkShard(string startDir, string rootFull, int maxDepth, long minFileSize, int topFiles,
            List<Entry> topFileList, Dictionary<string, long> dirSizes, bool descend,
            ref int files, ref int dirs, ref int errors, ref long bytes)
        {
            int reportedFiles = 0;
            int reportedDirs = 0;

            Stack<string> stack = new Stack<string>();
            stack.Push(startDir);

            while (stack.Count > 0)
            {
                if (_cancel) break;
                string current = stack.Pop();
                dirs++;

                if (dirs % 200 == 0)
                {
                    // 把本分片自上次上报以来的增量并入全局计数，再按全局值回调
                    int gf = Interlocked.Add(ref _progFiles, files - reportedFiles);
                    int gd = Interlocked.Add(ref _progDirs, dirs - reportedDirs);
                    Report(current, gf, gd);
                    reportedFiles = files;
                    reportedDirs = dirs;
                }

                if (descend)
                {
                    // 子目录
                    string[] subs;
                    try { subs = Directory.GetDirectories(current); }
                    catch { subs = new string[0]; errors++; }

                    for (int i = 0; i < subs.Length; i++)
                    {
                        try
                        {
                            DirectoryInfo di = new DirectoryInfo(subs[i]);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            int depth = DepthOf(subs[i], rootFull);
                            if (depth > maxDepth) continue;
                        }
                        catch
                        {
                            errors++;
                            continue;
                        }
                        stack.Push(subs[i]);
                    }
                }

                // 文件
                string[] fs;
                try { fs = Directory.GetFiles(current); }
                catch { fs = new string[0]; errors++; }

                for (int i = 0; i < fs.Length; i++)
                {
                    if (_cancel) break;
                    try
                    {
                        FileInfo fi = new FileInfo(fs[i]);
                        files++;
                        bytes += fi.Length;

                        if (fi.Length >= minFileSize)
                        {
                            AddTop(topFileList, MakeEntry(fi.FullName, fi.Length, fi.LastWriteTime, false),
                                topFiles, topFiles);
                        }

                        // 把文件大小累加到自身及所有上级目录：
                        // 各分片只写自己的本地表，合并时按键求和，上级目录天然得到正确总量
                        Accumulate(dirSizes, current, fi.Length, rootFull);
                    }
                    catch
                    {
                        errors++;
                    }
                }
            }

            // 收尾把未上报的余量并入全局进度，保证最终读数与总数一致
            if (files != reportedFiles || dirs != reportedDirs)
            {
                int gf = Interlocked.Add(ref _progFiles, files - reportedFiles);
                int gd = Interlocked.Add(ref _progDirs, dirs - reportedDirs);
                Report(startDir, gf, gd);
            }
        }

        private static int DepthOf(string path, string root)
        {
            try
            {
                string rel = path.Substring(root.Length).TrimStart('\\');
                if (rel.Length == 0) return 0;
                return rel.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static void Accumulate(Dictionary<string, long> dirSizes, string dir, long size, string root)
        {
            string current = dir;
            // 一路累加到 root（或磁盘根）。此处原有一个硬编码的 16 层上限：
            // 目录深度超过 16 时，第 17 层以上的祖先（含 root）会被漏算，
            // 导致浅层目录体积与 ScannedBytes 对不上。
            // 两个终止条件（到达 root / 没有父目录）本身已能保证收敛，不需要层数阀门
            while (true)
            {
                long cur;
                dirSizes.TryGetValue(current, out cur);
                dirSizes[current] = cur + size;

                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;

                DirectoryInfo parent;
                try { parent = Directory.GetParent(current); }
                catch { break; }
                if (parent == null) break;
                current = parent.FullName;
            }
        }

        private static void AddTop(List<Entry> top, Entry e, int capacity, int _unused)
        {
            if (capacity <= 0) return;

            int i = 0;
            while (i < top.Count && top[i].Size >= e.Size) i++;

            if (top.Count < capacity)
            {
                top.Insert(i, e);
                return;
            }

            if (i >= top.Count) return;
            top.Insert(i, e);
            top.RemoveAt(top.Count - 1);
        }

        private static Entry MakeEntry(string path, long size, DateTime modified, bool isDir)
        {
            Entry e = new Entry();
            e.PathText = path == null ? "" : path;
            e.Size = size;
            e.Modified = modified;
            e.IsDirectory = isDir;
            return e;
        }

        private void Report(string current, int files, int dirs)
        {
            Action<string, int, int> h = Progress;
            if (h != null)
            {
                try { h(current, files, dirs); }
                catch { }
            }
        }
    }
}
