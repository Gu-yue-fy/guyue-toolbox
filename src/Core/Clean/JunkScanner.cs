using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SysToolbox.Core
{
    /// <summary>
    /// 一个可清理的垃圾类别。
    /// </summary>
    public sealed class JunkCategory
    {
        public string Id;
        public string Name;
        public string Description;
        public string Glyph;
        /// <summary>要遍历的目录列表。</summary>
        public List<string> Directories = new List<string>();
        /// <summary>目录下的文件匹配模式，为空表示全部文件。</summary>
        public List<string> Patterns = new List<string>();
        /// <summary>是否属于回收站（走 Shell API）。</summary>
        public bool IsRecycleBin;
        /// <summary>默认是否勾选。</summary>
        public bool SelectedByDefault = true;
        /// <summary>是否建议普通用户清理（否则标注为高级）。</summary>
        public bool Advanced;

        public long Size;
        public int FileCount;
        public bool Scanned;
        public string LastError;

        public bool Selected;

        public string SizeText
        {
            get { return Scanned ? SysInfo.FormatSize(Size) : "—"; }
        }
    }

    /// <summary>
    /// 扫描 / 清理磁盘垃圾。所有删除操作都做异常隔离，遇到占用中的文件直接跳过。
    /// </summary>
    public sealed class JunkScanner
    {
        /// <summary>扫描过程中的进度回调：string 说明, int 百分比(-1 表示不确定)。</summary>
        public event Action<string, int> Progress;

        private volatile bool _cancel;

        public void Cancel()
        {
            _cancel = true;
        }

        public void Reset()
        {
            _cancel = false;
        }

        // ---------------------------------------------------------------
        // 默认清理项
        // ---------------------------------------------------------------

        public static List<JunkCategory> BuildDefaultCategories()
        {
            List<JunkCategory> list = new List<JunkCategory>();

            string win = SafeFolder(Environment.SpecialFolder.Windows);
            string temp = SafePath(Path.GetTempPath());
            string local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
            string common = SafeFolder(Environment.SpecialFolder.CommonApplicationData);

            // 1. 回收站
            JunkCategory recycle = new JunkCategory();
            recycle.Id = "recyclebin";
            recycle.Name = "回收站";
            recycle.Description = "彻底删除回收站中已删除的文件，释放磁盘空间。";
            recycle.Glyph = "recycle";
            recycle.IsRecycleBin = true;
            list.Add(recycle);

            // 2. 用户临时文件
            JunkCategory userTemp = new JunkCategory();
            userTemp.Id = "usertemp";
            userTemp.Name = "用户临时文件";
            userTemp.Description = "当前用户运行程序时产生的临时文件（%TEMP%）。";
            userTemp.Glyph = "temp";
            AddDir(userTemp, temp);
            AddDir(userTemp, Path.Combine(local, "Temp"));
            list.Add(userTemp);

            // 3. 系统临时文件
            JunkCategory sysTemp = new JunkCategory();
            sysTemp.Id = "systemp";
            sysTemp.Name = "系统临时文件";
            sysTemp.Description = "系统组件运行时产生的临时文件（Windows\\Temp）。";
            sysTemp.Glyph = "temp";
            AddDir(sysTemp, Path.Combine(win, "Temp"));
            list.Add(sysTemp);

            // 4. Windows 更新缓存
            JunkCategory wu = new JunkCategory();
            wu.Id = "wucache";
            wu.Name = "Windows 更新缓存";
            wu.Description = "已下载的更新安装包残留。更新完成后可安全删除，但会影响更新回滚。";
            wu.Glyph = "shield";
            wu.Advanced = true;
            AddDir(wu, Path.Combine(win, "SoftwareDistribution", "Download"));
            list.Add(wu);

            // 5. 缩略图与图标缓存
            JunkCategory thumb = new JunkCategory();
            thumb.Id = "thumbcache";
            thumb.Name = "缩略图 / 图标缓存";
            thumb.Description = "资源管理器生成的缩略图与图标缓存数据库，删除后会自动重建。";
            thumb.Glyph = "image";
            string explorerCache = Path.Combine(local, "Microsoft", "Windows", "Explorer");
            AddDir(thumb, explorerCache);
            thumb.Patterns.Add("thumbcache_*.db");
            thumb.Patterns.Add("iconcache_*.db");
            list.Add(thumb);

            // 6. 浏览器缓存
            JunkCategory browser = new JunkCategory();
            browser.Id = "browsercache";
            browser.Name = "浏览器缓存";
            browser.Description = "Chrome / Edge / IE 的网页缓存与代码缓存，不影响书签和登录状态。";
            browser.Glyph = "globe";
            AddBrowser(browser, Path.Combine(local, "Google", "Chrome", "User Data"));
            AddBrowser(browser, Path.Combine(local, "Microsoft", "Edge", "User Data"));
            AddBrowser(browser, Path.Combine(local, "Chromium", "User Data"));
            AddBrowser(browser, Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
            AddDir(browser, Path.Combine(local, "Microsoft", "Windows", "INetCache"));
            AddDir(browser, Path.Combine(local, "Microsoft", "Windows", "WebCache"));
            list.Add(browser);

            // 7. 崩溃转储与错误报告
            JunkCategory dump = new JunkCategory();
            dump.Id = "crashdump";
            dump.Name = "崩溃转储 / 错误报告";
            dump.Description = "程序崩溃时生成的转储文件与 Windows 错误报告。";
            dump.Glyph = "bug";
            AddDir(dump, Path.Combine(local, "CrashDumps"));
            AddDir(dump, Path.Combine(local, "Microsoft", "Windows", "WER"));
            AddDir(dump, Path.Combine(common, "Microsoft", "Windows", "WER"));
            list.Add(dump);

            // 8. 系统日志与日志文件
            JunkCategory logs = new JunkCategory();
            logs.Id = "logs";
            logs.Name = "系统日志文件";
            logs.Description = "Windows 组件写入的日志（CBS、DISM 等），排查问题后会保留大量文本。";
            logs.Glyph = "list";
            logs.Advanced = true;
            AddDir(logs, Path.Combine(win, "Logs", "CBS"));
            AddDir(logs, Path.Combine(win, "Logs", "DISM"));
            AddDir(logs, Path.Combine(win, "Logs", "MoSetup"));
            AddDir(logs, Path.Combine(win, "Logs", "WindowsUpdate"));
            logs.Patterns.Add("*.log");
            logs.Patterns.Add("*.etl");
            logs.Patterns.Add("*.cab");
            list.Add(logs);

            // 9. 预读取文件
            JunkCategory prefetch = new JunkCategory();
            prefetch.Id = "prefetch";
            prefetch.Name = "预读取文件 (Prefetch)";
            prefetch.Description = "用于加速程序启动的缓存。清除后首次启动会变慢，通常不建议清理。";
            prefetch.Glyph = "bolt";
            prefetch.Advanced = true;
            prefetch.SelectedByDefault = false;
            AddDir(prefetch, Path.Combine(win, "Prefetch"));
            prefetch.Patterns.Add("*.pf");
            list.Add(prefetch);

            // 10. 传递优化缓存
            JunkCategory doCache = new JunkCategory();
            doCache.Id = "deliveryopt";
            doCache.Name = "传递优化缓存";
            doCache.Description = "Windows 用于在局域网内共享更新文件的缓存。";
            doCache.Glyph = "share";
            doCache.Advanced = true;
            AddDir(doCache, Path.Combine(win, "SoftwareDistribution", "DeliveryOptimization"));
            AddDir(doCache, Path.Combine(win, "ServiceProfiles", "NetworkService",
                "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization"));
            list.Add(doCache);

            foreach (JunkCategory c in list)
            {
                c.Selected = c.SelectedByDefault;
            }
            return list;
        }

        private static void AddBrowser(JunkCategory cat, string userDataRoot)
        {
            if (string.IsNullOrEmpty(userDataRoot)) return;
            if (!Directory.Exists(userDataRoot)) return;

            List<string> profiles = new List<string>();
            profiles.Add("Default");
            profiles.Add("Profile 1");
            profiles.Add("Profile 2");
            profiles.Add("Profile 3");

            foreach (string p in profiles)
            {
                string root = Path.Combine(userDataRoot, p);
                if (!Directory.Exists(root)) continue;
                AddDir(cat, Path.Combine(root, "Cache"));
                AddDir(cat, Path.Combine(root, "Code Cache"));
                AddDir(cat, Path.Combine(root, "GPUCache"));
                AddDir(cat, Path.Combine(root, "Service Worker", "CacheStorage"));
                AddDir(cat, Path.Combine(root, "Media Cache"));
            }

            // 顶层 Shared 缓存
            AddDir(cat, Path.Combine(userDataRoot, "ShaderCache"));
            AddDir(cat, Path.Combine(userDataRoot, "GrShaderCache"));
        }

        private static void AddDir(JunkCategory cat, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (cat.Directories.Contains(dir)) return;
            cat.Directories.Add(dir);
        }

        private static string SafeFolder(Environment.SpecialFolder folder)
        {
            try { return Environment.GetFolderPath(folder); }
            catch { return ""; }
        }

        private static string SafePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return p.TrimEnd('\\', '/'); }
            catch { return p; }
        }

        // ---------------------------------------------------------------
        // 扫描
        // ---------------------------------------------------------------

        public void ScanAll(List<JunkCategory> categories)
        {
            if (categories == null) return;
            for (int i = 0; i < categories.Count; i++)
            {
                if (_cancel) break;
                Report("正在扫描：" + categories[i].Name, (int)((i * 100.0) / categories.Count));
                Scan(categories[i]);
            }
            Report("扫描完成", 100);
        }

        public void Scan(JunkCategory cat)
        {
            cat.Size = 0;
            cat.FileCount = 0;
            cat.LastError = null;

            if (cat.IsRecycleBin)
            {
                Native.SHQUERYRBINFO info = Native.QueryRecycleBin();
                cat.Size = info.i64Size;
                cat.FileCount = (int)Math.Min(info.i64NumItems, int.MaxValue);
                cat.Scanned = true;
                return;
            }

            long total = 0;
            int count = 0;
            for (int i = 0; i < cat.Directories.Count; i++)
            {
                if (_cancel) break;
                string dir = cat.Directories[i];
                if (!Directory.Exists(dir)) continue;
                long size;
                int files;
                Measure(dir, cat.Patterns, out size, out files);
                total += size;
                count += files;
            }

            cat.Size = total;
            cat.FileCount = count;
            cat.Scanned = true;
        }

        private static void Measure(string dir, List<string> patterns, out long size, out int files)
        {
            size = 0;
            files = 0;

            Stack<string> stack = new Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] subDirs;
                try { subDirs = Directory.GetDirectories(current); }
                catch { subDirs = new string[0]; }

                for (int i = 0; i < subDirs.Length; i++)
                {
                    // 跳过符号链接 / 重解析点，避免无限递归
                    try
                    {
                        DirectoryInfo di = new DirectoryInfo(subDirs[i]);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { continue; }
                    stack.Push(subDirs[i]);
                }

                string[] fileList;
                try { fileList = Directory.GetFiles(current); }
                catch { fileList = new string[0]; }

                for (int i = 0; i < fileList.Length; i++)
                {
                    string f = fileList[i];
                    if (patterns != null && patterns.Count > 0)
                    {
                        string name = Path.GetFileName(f);
                        bool match = false;
                        for (int p = 0; p < patterns.Count; p++)
                        {
                            if (MatchWildcard(name, patterns[p])) { match = true; break; }
                        }
                        if (!match) continue;
                    }
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        size += fi.Length;
                        files++;
                    }
                    catch
                    {
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // 清理
        // ---------------------------------------------------------------

        public sealed class CleanResult
        {
            public long FreedBytes;
            public int DeletedFiles;
            public int SkippedFiles;
            public List<string> Errors = new List<string>();
        }

        public CleanResult Clean(List<JunkCategory> categories)
        {
            CleanResult result = new CleanResult();
            if (categories == null) return result;

            for (int i = 0; i < categories.Count; i++)
            {
                if (_cancel) break;
                JunkCategory cat = categories[i];
                Report("正在清理：" + cat.Name, (int)((i * 100.0) / categories.Count));
                long before = result.FreedBytes;

                if (cat.IsRecycleBin)
                {
                    result.FreedBytes += cat.Size;
                    result.DeletedFiles += cat.FileCount;
                    try
                    {
                        Native.SHEmptyRecycleBin(IntPtr.Zero, null,
                            Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add("回收站：" + ex.Message);
                        result.FreedBytes -= cat.Size;
                    }
                    cat.Size = 0;
                    cat.FileCount = 0;
                    continue;
                }

                for (int d = 0; d < cat.Directories.Count; d++)
                {
                    if (_cancel) break;
                    string dir = cat.Directories[d];
                    if (!Directory.Exists(dir)) continue;
                    Purge(dir, cat.Patterns, result, true);
                }

                long freed = result.FreedBytes - before;
                if (freed < 0) freed = 0;
                cat.Size = 0;
                cat.FileCount = 0;
            }

            Report("清理完成", 100);
            return result;
        }

        /// <summary>
        /// 递归删除目录内容。removeRoot 为 true 时同时尝试删除空目录本身（但不会删除传入的根目录）。
        /// </summary>
        private static void Purge(string dir, List<string> patterns, CleanResult result, bool isRoot)
        {
            List<string> subDirs = new List<string>();
            try
            {
                subDirs.AddRange(Directory.GetDirectories(dir));
            }
            catch
            {
            }

            for (int i = 0; i < subDirs.Count; i++)
            {
                string sub = subDirs[i];
                try
                {
                    DirectoryInfo di = new DirectoryInfo(sub);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // 仅删除链接本身，不递归
                        try
                        {
                            Directory.Delete(sub, false);
                        }
                        catch
                        {
                        }
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                Purge(sub, patterns, result, false);
                try
                {
                    if (Directory.GetFileSystemEntries(sub).Length == 0)
                        Directory.Delete(sub, false);
                }
                catch
                {
                }
            }

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                if (patterns != null && patterns.Count > 0)
                {
                    string name = Path.GetFileName(f);
                    bool match = false;
                    for (int p = 0; p < patterns.Count; p++)
                    {
                        if (MatchWildcard(name, patterns[p])) { match = true; break; }
                    }
                    if (!match) continue;
                }

                long len = 0;
                try { len = new FileInfo(f).Length; }
                catch { }

                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                    result.FreedBytes += len;
                    result.DeletedFiles++;
                }
                catch
                {
                    result.SkippedFiles++;
                }
            }
        }

        // ---------------------------------------------------------------
        // 通配符匹配（支持 * 与 ?）
        // ---------------------------------------------------------------

        public static bool MatchWildcard(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            if (string.IsNullOrEmpty(text)) return false;

            int t = 0, p = 0, star = -1, mark = 0;
            char[] tx = text.ToLowerInvariant().ToCharArray();
            char[] pt = pattern.ToLowerInvariant().ToCharArray();

            while (t < tx.Length)
            {
                if (p < pt.Length && (pt[p] == '?' || pt[p] == tx[t]))
                {
                    t++; p++;
                }
                else if (p < pt.Length && pt[p] == '*')
                {
                    star = p;
                    mark = t;
                    p++;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    mark++;
                    t = mark;
                }
                else
                {
                    return false;
                }
            }

            while (p < pt.Length && pt[p] == '*') p++;
            return p == pt.Length;
        }

        private void Report(string text, int percent)
        {
            Action<string, int> h = Progress;
            if (h != null)
            {
                try { h(text, percent); }
                catch { }
            }
        }

        // ---------------------------------------------------------------
        // 全盘分析（用于找出大文件，占位实现：扫描用户目录下的前 N 个大文件）
        // ---------------------------------------------------------------

        public sealed class BigFile
        {
            public string Path;
            public long Size;
        }

        public static List<BigFile> FindLargeFiles(string root, int top, CancellationToken token)
        {
            List<BigFile> found = new List<BigFile>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;
            if (top <= 0) top = 50;

            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            int guard = 0;

            while (stack.Count > 0 && guard < 400000)
            {
                guard++;
                if (token.IsCancellationRequested) break;
                string current = stack.Pop();

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(current); }
                catch { subDirs = new string[0]; }
                for (int i = 0; i < subDirs.Length; i++)
                {
                    try
                    {
                        DirectoryInfo di = new DirectoryInfo(subDirs[i]);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((di.Attributes & FileAttributes.System) != 0) continue;
                    }
                    catch { continue; }
                    stack.Push(subDirs[i]);
                }

                string[] files;
                try { files = Directory.GetFiles(current); }
                catch { files = new string[0]; }

                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        if (fi.Length < 50L * 1024 * 1024) continue;
                        BigFile bf = new BigFile();
                        bf.Path = fi.FullName;
                        bf.Size = fi.Length;
                        found.Add(bf);
                    }
                    catch
                    {
                    }
                }
            }

            found.Sort(delegate (BigFile a, BigFile b) { return b.Size.CompareTo(a.Size); });
            if (found.Count > top) found = found.GetRange(0, top);
            return found;
        }
    }
}
