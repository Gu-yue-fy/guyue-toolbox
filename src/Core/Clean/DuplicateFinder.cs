using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

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
        public static List<DuplicateGroup> Find(string root, bool recursive,
            Func<bool> isCancelled, Action<ScanState> onProgress)
        {
            ScanState st = new ScanState();
            List<string> files = new List<string>();
            Enumerate(root, recursive, files, isCancelled, st, onProgress);

            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    long len = new FileInfo(files[i]).Length;
                    List<string> bucket;
                    if (!bySize.TryGetValue(len, out bucket))
                    {
                        bucket = new List<string>();
                        bySize[len] = bucket;
                    }
                    bucket.Add(files[i]);
                }
                catch
                {
                }
            }

            Dictionary<string, DuplicateGroup> byHash = new Dictionary<string, DuplicateGroup>();
            foreach (KeyValuePair<long, List<string>> kv in bySize)
            {
                if (kv.Value.Count < 2) continue;
                if (isCancelled != null && isCancelled()) break;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (isCancelled != null && isCancelled()) break;
                    string path = kv.Value[i];
                    string hash = HashFile(path);
                    if (hash == null) continue;
                    DuplicateGroup g;
                    if (!byHash.TryGetValue(hash, out g))
                    {
                        g = new DuplicateGroup();
                        g.Hash = hash;
                        try { g.Size = new FileInfo(path).Length; } catch { }
                        byHash[hash] = g;
                    }
                    g.Files.Add(path);
                }
            }

            List<DuplicateGroup> result = new List<DuplicateGroup>();
            foreach (DuplicateGroup g in byHash.Values)
            {
                if (g.Files.Count > 1) result.Add(g);
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
            Stack<string> dirs = new Stack<string>();
            dirs.Push(root);
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
                            dirs.Push(d);
                        }
                    }
                }
                catch
                {
                }
            }
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
