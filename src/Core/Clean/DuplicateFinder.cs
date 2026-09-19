using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace GuyueBox.Core
{
    /// <summary>一组内容相同的重复文件。</summary>
    public sealed class DuplicateGroup
    {
        public string Hash = "";
        public long Size;
        public readonly List<string> Files = new List<string>();

        public string DisplayHash
        {
            get { return Hash.Length >= 12 ? Hash.Substring(0, 12) : Hash; }
        }

        public string SizeText
        {
            get { return Size > 0 ? SysInfo.FormatSize(Size) : "未知"; }
        }

        /// <summary>除保留一份外，可释放的空间。</summary>
        public long WastedBytes
        {
            get { return Size * Math.Max(0, Files.Count - 1); }
        }
    }

    public sealed class ScanState
    {
        public int FilesScanned;
    }

    public static class DuplicateFinder
    {
        /// <summary>快速哈希读取的文件头长度：开头不同则内容必不同。</summary>
        private const int HeadBytes = 64 * 1024;

        public static List<DuplicateGroup> Find(string root, bool recursive,
            Func<bool> isCancelled, Action<ScanState> onProgress)
        {
            ScanState st = new ScanState();
            List<string> files = new List<string>();
            Enumerate(root, recursive, files, isCancelled, st, onProgress);

            // 按大小并行分桶：大小不同的文件内容必不相同
            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            object sizeGate = new object();
            Parallel.ForEach(files,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(string path)
                {
                    long len;
                    try { len = new FileInfo(path).Length; }
                    catch { return; }
                    lock (sizeGate)
                    {
                        List<string> bucket;
                        if (!bySize.TryGetValue(len, out bucket))
                        {
                            bucket = new List<string>();
                            bySize[len] = bucket;
                        }
                        bucket.Add(path);
                    }
                });

            // 两级哈希：
            // ① 快速哈希只读文件开头 HeadBytes——绝大多数「大小相同」的文件到此即可排除；
            // ② 仅对「大小 + 开头」都相同的少量文件计算全量 SHA256。
            // 哈希（IO + CPU）是重复文件查找的主要耗时，两级预筛 + 并行可大幅缩短总耗时。
            List<DuplicateGroup> result = new List<DuplicateGroup>();
            foreach (KeyValuePair<long, List<string>> kv in bySize)
            {
                if (kv.Value.Count < 2) continue;
                if (isCancelled != null && isCancelled()) break;

                Dictionary<string, List<string>> byQuick = BucketBy(kv.Value,
                    delegate(string p) { return QuickHash(p); });
                foreach (List<string> quickBucket in byQuick.Values)
                {
                    if (quickBucket.Count < 2) continue;
                    if (isCancelled != null && isCancelled()) break;

                    Dictionary<string, List<string>> byFull = BucketBy(quickBucket,
                        delegate(string p) { return HashFile(p); });
                    foreach (KeyValuePair<string, List<string>> full in byFull)
                    {
                        if (full.Value.Count < 2) continue;
                        DuplicateGroup g = new DuplicateGroup();
                        g.Hash = full.Key;
                        // 直接用外层 size 桶的大小，不再对组内某个文件重新 stat：
                        // 那个文件可能已被删除或占用，stat 失败会让整组「可释放」显示成 0
                        g.Size = kv.Key;
                        g.Files.AddRange(full.Value);
                        // 组内文件字节完全相同，但并行完成顺序不定；
                        // 排序保证「保留哪一份、删除哪几份」每次运行都可预期
                        g.Files.Sort(StringComparer.OrdinalIgnoreCase);
                        result.Add(g);
                    }
                }
            }

            result.Sort(delegate (DuplicateGroup a, DuplicateGroup b)
            {
                return b.WastedBytes.CompareTo(a.WastedBytes);
            });
            if (onProgress != null) onProgress(st);
            return result;
        }

        private static void Enumerate(string root, bool recursive, List<string> outFiles,
            Func<bool> cancelled, ScanState st, Action<ScanState> onProgress)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            // 联接/符号链接防护 + visited 去环：防止把联接目标（目录之外）的文件纳入重复分组
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stack<string> dirs = new Stack<string>();
            dirs.Push(root);
            visited.Add(root.ToUpperInvariant());
            while (dirs.Count > 0)
            {
                if (cancelled != null && cancelled()) return;
                string dir = dirs.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        if (cancelled != null && cancelled()) return;
                        outFiles.Add(f);
                        st.FilesScanned++;
                        if (st.FilesScanned % 250 == 0 && onProgress != null) onProgress(st);
                    }
                    if (recursive)
                    {
                        foreach (string d in Directory.GetDirectories(dir))
                        {
                            DirectoryInfo di = new DirectoryInfo(d);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (!visited.Add(d.ToUpperInvariant())) continue;
                            dirs.Push(d);
                        }
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>快速哈希：只读文件开头 HeadBytes，用于全量哈希前的廉价预筛。</summary>
        private static string QuickHash(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buf = new byte[HeadBytes];
                    int read = 0;
                    while (read < HeadBytes)
                    {
                        int n = fs.Read(buf, read, HeadBytes - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    byte[] h = sha.ComputeHash(buf, 0, read);
                    StringBuilder sb = new StringBuilder(h.Length * 2);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>并行计算哈希并按哈希值分桶。哈希失败的文件直接丢弃（与原行为一致）。</summary>
        private static Dictionary<string, List<string>> BucketBy(List<string> paths, Func<string, string> hasher)
        {
            Dictionary<string, List<string>> buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            object gate = new object();
            Parallel.ForEach(paths,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(string path)
                {
                    string h = hasher(path);
                    if (h == null) return;
                    lock (gate)
                    {
                        List<string> b;
                        if (!buckets.TryGetValue(h, out b))
                        {
                            b = new List<string>();
                            buckets[h] = b;
                        }
                        b.Add(path);
                    }
                });
            return buckets;
        }

        private static string HashFile(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder(h.Length * 2);
                    for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>将文件移入回收站（可恢复）。</summary>
        public static bool Recycle(string[] paths)
        {
            if (paths == null || paths.Length == 0) return false;
            StringBuilder from = new StringBuilder();
            for (int i = 0; i < paths.Length; i++)
            {
                from.Append(paths[i]);
                from.Append('\0');
            }
            from.Append('\0');

            SHFILEOPSTRUCT fos = new SHFILEOPSTRUCT();
            fos.wFunc = 0x0003; // FO_DELETE
            fos.pFrom = from.ToString();
            fos.fFlags = (ushort)(0x0040 | 0x0010 | 0x0400); // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI
            int r = SHFileOperation(ref fos);
            return r == 0;
        }

        /// <summary>
        /// SHFileOperation 的入参结构。字段顺序与个数由 Shell 的 Win32 布局决定：
        /// pTo / fAnyOperationsAborted / hNameMappings / lpszProgressTitle 本程序不使用，
        /// 但一旦删除，前面的字段就会与结构体实际长度不符，调用必然失败。
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT fos);
    }
}
