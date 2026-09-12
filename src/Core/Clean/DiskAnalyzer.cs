using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GuyueBox.Core
{
    /// <summary>
    /// 磁盘空间分析：遍历指定根目录，找出占用空间最大的文件与目录。
    /// 只做读取，不修改任何内容。
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
            Stopwatch sw = Stopwatch.StartNew();

            List<Entry> topFileList = new List<Entry>();
            Dictionary<string, long> dirSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string rootFull = null;
                try { rootFull = Path.GetFullPath(root); }
                catch { rootFull = root; }

                Stack<string> stack = new Stack<string>();
                stack.Push(rootFull);

                while (stack.Count > 0)
                {
                    if (_cancel) break;
                    string current = stack.Pop();
                    result.DirectoryCount++;

                    if (result.DirectoryCount % 200 == 0)
                    {
                        Report(current, result.FileCount, result.DirectoryCount);
                    }

                    // 子目录
                    string[] subs;
                    try { subs = Directory.GetDirectories(current); }
                    catch { subs = new string[0]; result.ErrorCount++; }

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
                            continue;
                        }
                        stack.Push(subs[i]);
                    }

                    // 文件
                    string[] files;
                    try { files = Directory.GetFiles(current); }
                    catch { files = new string[0]; result.ErrorCount++; }

                    for (int i = 0; i < files.Length; i++)
                    {
                        if (_cancel) break;
                        try
                        {
                            FileInfo fi = new FileInfo(files[i]);
                            result.FileCount++;
                            result.ScannedBytes += fi.Length;

                            if (fi.Length >= minFileSize)
                            {
                                AddTop(topFileList, MakeEntry(fi.FullName, fi.Length, fi.LastWriteTime, false), topFileList.Count, topFileList.Count);
                            }

                            // 把文件大小累加到自身及所有上级目录
                            Accumulate(dirSizes, current, fi.Length, rootFull);
                        }
                        catch
                        {
                            result.ErrorCount++;
                        }
                    }
                }

                // 目录排名
                List<Entry> dirList = new List<Entry>();
                foreach (KeyValuePair<string, long> kv in dirSizes) dirList.Add(MakeEntry(kv.Key, kv.Value, DateTime.MinValue, true));
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
            for (int i = 0; i < 16; i++)
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
