using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GuyueBox.Core
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

        public void Scan(JunkCategory cat)
        {
            cat.Size = 0;
            cat.FileCount = 0;
            cat.LastError = null;

            if (cat.IsRecycleBin)
            {
                Native.SHQUERYRBINFO info;
                if (Native.QueryRecycleBin(out info))
                {
                    cat.Size = info.i64Size;
                    cat.FileCount = (int)Math.Min(info.i64NumItems, int.MaxValue);
                }
                else
                {
                    // 查询失败（回收站被禁用/拒绝访问）不能再静默当 0，否则会误报"回收站为空"
                    cat.LastError = "无法读取回收站信息（可能被系统禁用或拒绝访问）。";
                    cat.Size = 0;
                    cat.FileCount = 0;
                }
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

            // 模式预转小写：大目录逐文件匹配时不再每个文件重复分配小写字符串/数组
            string[] patsLower = PreparePatterns(patterns);

            Stack<string> stack = new Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                string current = stack.Pop();

                // 流式枚举：不再为每一层目录/文件各分配一个完整数组
                IEnumerable<string> subDirs;
                try { subDirs = Directory.EnumerateDirectories(current); }
                catch { subDirs = null; }

                if (subDirs != null)
                {
                    foreach (string sub in subDirs)
                    {
                        // 跳过符号链接 / 重解析点，避免无限递归
                        try
                        {
                            DirectoryInfo di = new DirectoryInfo(sub);
                            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }
                        stack.Push(sub);
                    }
                }

                IEnumerable<string> fileEnum;
                try { fileEnum = Directory.EnumerateFiles(current); }
                catch { fileEnum = null; }

                if (fileEnum == null) continue;

                foreach (string f in fileEnum)
                {
                    if (patsLower != null && !MatchAny(Path.GetFileName(f), patsLower)) continue;
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

        /// <summary>并行扫描全部类别：各类别的目录遍历相互独立，并发可显著缩短总耗时。</summary>
        /// <param name="categories">要扫描的类别</param>
        /// <param name="onEachDone">每个类别扫完后的回调（参数是类别下标）；不需要可传 null</param>
        public void ScanAll(List<JunkCategory> categories, Action<int> onEachDone)
        {
            if (categories == null) return;
            Parallel.For(0, categories.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(int i)
                {
                    Scan(categories[i]);
                    if (onEachDone != null)
                    {
                        try { onEachDone(i); }
                        catch { }
                    }
                });
        }

        public CleanResult Clean(List<JunkCategory> categories)
        {
            CleanResult result = new CleanResult();
            if (categories == null) return result;

            // 各清理类别的目录互不重叠，可并行执行；
            // 每类先写自己的结果，结束再合并，避免逐文件加锁竞争
            object gate = new object();
            int done = 0;
            Parallel.For(0, categories.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(int i)
                {
                    if (_cancel) return;
                    JunkCategory cat = categories[i];
                    CleanResult local = new CleanResult();
                    CleanOne(cat, local);

                    int percent;
                    lock (gate)
                    {
                        result.FreedBytes += local.FreedBytes;
                        result.DeletedFiles += local.DeletedFiles;
                        result.SkippedFiles += local.SkippedFiles;
                        if (local.Errors.Count > 0) result.Errors.AddRange(local.Errors);
                        done++;
                        percent = (int)((done * 100.0) / categories.Count);
                    }
                    // 在锁外回调：锁内调用外部事件处理器是重入 / 死锁的经典模式
                    // （当前 Progress 无订阅者，但一旦有人订阅就会变成真实风险）
                    Report("正在清理：" + cat.Name, percent);
                });

            Report("清理完成", 100);
            return result;
        }

        /// <summary>清理单个类别（供 Clean 并行调用）。</summary>
        private void CleanOne(JunkCategory cat, CleanResult result)
        {
            if (cat.IsRecycleBin)
            {
                // SHEmptyRecycleBin 返回 Win32 错误码（0=成功），失败不抛异常——必须检查
                long rc = Native.SHEmptyRecycleBin(IntPtr.Zero, null,
                    Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                if (rc == 0)
                {
                    result.FreedBytes += cat.Size;
                    result.DeletedFiles += cat.FileCount;
                }
                else
                {
                    result.Errors.Add("清空回收站失败（Win32 错误码 " + rc + "），本次未计入释放量。");
                }
                cat.Size = 0;
                cat.FileCount = 0;
                return;
            }

            for (int d = 0; d < cat.Directories.Count; d++)
            {
                if (_cancel) break;
                string dir = cat.Directories[d];
                if (!Directory.Exists(dir)) continue;
                Purge(dir, cat.Patterns, result, true);
            }

            cat.Size = 0;
            cat.FileCount = 0;
        }

        /// <summary>
        /// 递归删除目录内容。removeRoot 为 true 时同时尝试删除空目录本身（但不会删除传入的根目录）。
        /// </summary>
        private static void Purge(string dir, List<string> patterns, CleanResult result, bool isRoot)
        {
            // 模式预转小写：清理阶段逐文件匹配，避免重复分配小写字符串/数组
            string[] patsLower = PreparePatterns(patterns);

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
                if (patsLower != null && !MatchAny(Path.GetFileName(f), patsLower)) continue;

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
            return MatchLower(text.ToLowerInvariant(), pattern.ToLowerInvariant());
        }

        /// <summary>把模式列表一次性预转小写：扫描 / 清理按文件逐个匹配时不再重复分配。</summary>
        private static string[] PreparePatterns(List<string> patterns)
        {
            if (patterns == null || patterns.Count == 0) return null;
            string[] lows = new string[patterns.Count];
            for (int i = 0; i < patterns.Count; i++)
            {
                lows[i] = (patterns[i] ?? "").ToLowerInvariant();
            }
            return lows;
        }

        /// <summary>文件名对一组（已小写的）模式做任意匹配：文件名只转一次小写。</summary>
        private static bool MatchAny(string fileName, string[] patternsLower)
        {
            if (patternsLower == null) return true;
            if (string.IsNullOrEmpty(fileName)) return false;
            string name = fileName.ToLowerInvariant();
            for (int i = 0; i < patternsLower.Length; i++)
            {
                if (MatchLower(name, patternsLower[i])) return true;
            }
            return false;
        }

        /// <summary>通配符匹配核心（入参必须都已转小写）：支持 * 与 ?，无分配热点。</summary>
        private static bool MatchLower(string textLower, string patternLower)
        {
            int t = 0, p = 0, star = -1, mark = 0;
            char[] tx = textLower.ToCharArray();
            char[] pt = patternLower.ToCharArray();

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
    }
}
