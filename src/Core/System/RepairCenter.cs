/* ============================================================
 * 文件说明：修复中心：扫描系统潜在故障并给出可执行的修复动作。
 *           设计原则（对齐设计 repair-center）：每一项都必须"真检测 + 真修复"，
 *           宁可少列几项，也不放无法验证或点了没用的假项；
 *           不能自动修的（如驱动异常）明确标注为"仅提示"，不提供假按钮。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>一条诊断项。</summary>
    public sealed class RepairItem
    {
        public string Id = "";

        /// <summary>分类（常见问题 / Windows 系统 / 网络与连接 / 驱动与设备 / 优化项校验）。</summary>
        public string Category = "";

        public string Title = "";

        /// <summary>检出原因（说明原理，让用户明白在修什么）。</summary>
        public string Detail = "";

        /// <summary>检出问题时给出的修复动作名（空 = 该检查未发现问题）。</summary>
        public string FixLabel = "";

        /// <summary>是否检出问题。</summary>
        public bool Detected;

        /// <summary>是否可自动修复（false = 只能提示，不提供假按钮）。</summary>
        public bool Fixable = true;

        /// <summary>本次会话内是否已修复。</summary>
        public bool Fixed;

        /// <summary>加权分（用于健康分计算：检出时扣多少分）。</summary>
        public int Weight = 6;

        /// <summary>预计耗时（设计：提前给出预期，降低等待焦虑）。空 = 无需耗时或不可自动处理。</summary>
        public string TimeText = "";

        /// <summary>执行修复的委托（由 RepairCenter 装配）。</summary>
        internal Func<RepairItem, string> Action;
    }

    public static class RepairCenter
    {
        // ==============================================================
        // 扫描
        // ==============================================================

        /// <summary>
        /// 扫描全部检查项。<paramref name="phase"/> 回调参数为（阶段名, 阶段内百分比），
        /// 供界面显示分阶段进度——与设计 rc-dx-progress 的四段进度一致。
        /// </summary>
        /// <param name="phase">阶段回调（阶段名, 阶段内百分比），供界面显示分阶段进度。</param>
        /// <param name="cancelled">返回 true 表示用户已取消；在每个阶段边界检查一次。</param>
        /// <param name="wasCancelled">是否因取消而提前结束（界面据此提示"已取消，保留已完成部分"）。</param>
        public static List<RepairItem> Scan(Action<string, int> phase, Func<bool> cancelled,
            out bool wasCancelled)
        {
            List<RepairItem> list = new List<RepairItem>();
            wasCancelled = false;

            Report(phase, "系统组件", 0);
            AddSystemChecks(list, phase);
            if (IsCancelled(cancelled)) { wasCancelled = true; SetTimes(list); return list; }

            Report(phase, "网络与连接", 0);
            AddNetworkChecks(list, phase);
            if (IsCancelled(cancelled)) { wasCancelled = true; SetTimes(list); return list; }

            Report(phase, "驱动与设备", 0);
            AddDeviceChecks(list, phase);
            if (IsCancelled(cancelled)) { wasCancelled = true; SetTimes(list); return list; }

            Report(phase, "优化项校验", 0);
            AddTweakChecks(list, phase);

            SetTimes(list);
            Report(phase, "完成", 100);
            return list;
        }

        private static bool IsCancelled(Func<bool> cancelled)
        {
            try { return cancelled != null && cancelled(); }
            catch { return false; }
        }

        /// <summary>
        /// 预计耗时按项标注。集中在这里而不是散在各检查里：
        /// 耗时是"给用户看的文案"，与检查逻辑无关，改文案时不必翻遍检查代码。
        /// </summary>
        private static void SetTimes(List<RepairItem> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                RepairItem it = list[i];
                switch (it.Id)
                {
                    case "icon_cache": it.TimeText = "约 20 秒（含重启资源管理器）"; break;
                    case "temp_files": it.TimeText = "约 10 秒"; break;
                    case "wu_cache": it.TimeText = "约 30 秒（含停止/启动服务）"; break;
                    case "event_log": it.TimeText = "约 5 秒"; break;
                    case "proxy": it.TimeText = "即时"; break;
                    case "dns_cache": it.TimeText = "即时"; break;
                    case "hosts_abnormal": it.TimeText = "即时"; break;
                    case "tweak_recommend": it.TimeText = "视项数，通常 10~30 秒"; break;
                    default: it.TimeText = ""; break;
                }
            }
        }

        private static void Report(Action<string, int> phase, string name, int percent)
        {
            if (phase != null) phase(name, percent);
        }

        private static RepairItem New(string id, string category, string title, int weight)
        {
            RepairItem it = new RepairItem();
            it.Id = id;
            it.Category = category;
            it.Title = title;
            it.Weight = weight;
            return it;
        }

        // ---------------- 系统组件 ----------------

        private static void AddSystemChecks(List<RepairItem> list, Action<string, int> phase)
        {
            // ① 缩略图 / 图标缓存体积（图标错乱、资源管理器变慢的常见原因）
            RepairItem iconCache = New("icon_cache", "Windows 系统", "图标与缩略图缓存", 8);
            iconCache.FixLabel = "重建缓存";
            iconCache.Action = delegate (RepairItem it)
            {
                long removed = ClearIconCache();
                return removed > 0
                    ? "已清理 " + SysInfo.FormatSize(removed) + " 缓存，资源管理器会自动重建。"
                    : "没有可清理的缓存文件（可能被占用，重启后再试）。";
            };
            try
            {
                long size = IconCacheSize();
                iconCache.Detected = size > 300L * 1024 * 1024; // 超过 300 MB 视为异常
                iconCache.Detail = iconCache.Detected
                    ? "图标与缩略图缓存已占 " + SysInfo.FormatSize(size) + "，偏大时会导致资源管理器卡顿、图标错乱。"
                    : "缓存体积 " + SysInfo.FormatSize(size) + "，正常。";
            }
            catch
            {
                iconCache.Detected = false;
                iconCache.Detail = "无法读取缓存体积。";
            }
            list.Add(iconCache);
            Report(phase, "系统组件", 40);

            // ② 用户临时目录体积
            RepairItem temp = New("temp_files", "Windows 系统", "用户临时文件", 10);
            temp.FixLabel = "清理临时文件";
            temp.Action = delegate (RepairItem it)
            {
                long freed = ClearTemp();
                return freed > 0
                    ? "已删除 " + SysInfo.FormatSize(freed) + " 临时文件（正在使用的文件已跳过）。"
                    : "没有可删除的临时文件。";
            };
            try
            {
                long size = TempSize();
                temp.Detected = size > 1024L * 1024 * 1024; // 超过 1 GB
                temp.Detail = temp.Detected
                    ? "临时目录已占 " + SysInfo.FormatSize(size) + "，多为安装残渣与程序缓存，可安全删除。"
                    : "临时目录占 " + SysInfo.FormatSize(size) + "，正常。";
            }
            catch
            {
                temp.Detected = false;
                temp.Detail = "无法读取临时目录体积。";
            }
            list.Add(temp);
            Report(phase, "系统组件", 75);

            // ③ Windows 更新下载缓存
            RepairItem wu = New("wu_cache", "Windows 系统", "Windows 更新缓存", 12);
            wu.FixLabel = "清理更新缓存";
            wu.Action = delegate (RepairItem it)
            {
                string msg;
                long freed = ClearUpdateCache(out msg);
                return freed > 0
                    ? "已释放 " + SysInfo.FormatSize(freed) + " 更新缓存。" + (msg.Length > 0 ? "（" + msg + "）" : "")
                    : (msg.Length > 0 ? msg : "没有可清理的更新缓存。");
            };
            try
            {
                long size = DirSize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"SoftwareDistribution\Download"));
                wu.Detected = size > 2L * 1024 * 1024 * 1024; // 超过 2 GB
                wu.Detail = wu.Detected
                    ? "更新下载缓存已占 " + SysInfo.FormatSize(size) + "，清理后不影响已安装的更新。"
                    : "更新缓存占 " + SysInfo.FormatSize(size) + "，正常。";
            }
            catch
            {
                wu.Detected = false;
                wu.Detail = "无法读取更新缓存体积（需要管理员权限）。";
            }
            list.Add(wu);

            // ④ 系统盘剩余空间
            RepairItem disk = New("sys_disk_space", "常见问题", "系统盘剩余空间", 14);
            disk.Fixable = false; // 只提示：清理交给「清理与磁盘」页，避免两处重复的删除逻辑
            try
            {
                List<DiskInfo> disks = SysInfo.GetDisks(true);
                if (disks.Count > 0)
                {
                    DiskInfo d = disks[0];
                    double freePercent = d.TotalBytes > 0 ? (double)d.FreeBytes * 100.0 / d.TotalBytes : 100;
                    disk.Detected = freePercent < 10;
                    disk.Detail = disk.Detected
                        ? d.Name + " 仅剩 " + SysInfo.FormatSize(d.FreeBytes) + "（" + freePercent.ToString("0") +
                          "%），空间不足会拖慢系统。请到「清理与磁盘」页处理。"
                        : d.Name + " 剩余 " + SysInfo.FormatSize(d.FreeBytes) + "（" + freePercent.ToString("0") + "%），正常。";
                }
                else
                {
                    disk.Detail = "未检测到固定磁盘。";
                }
            }
            catch
            {
                disk.Detail = "无法读取磁盘信息。";
            }
            list.Add(disk);
            Report(phase, "系统组件", 100);
        }

        // ---------------- 网络与连接 ----------------

        private static void AddNetworkChecks(List<RepairItem> list, Action<string, int> phase)
        {
            // ⑤ 系统代理是否被异常启用（被劫持时浏览器打不开网页）
            RepairItem proxy = New("proxy", "网络与连接", "系统代理设置", 10);
            proxy.FixLabel = "关闭代理";
            proxy.Action = delegate (RepairItem it)
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                    {
                        if (k == null) return "无法写入代理设置（注册表不可写）。";
                        k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    }
                    return "已关闭系统代理。若你确实在用代理，请重新打开。";
                }
                catch (Exception ex)
                {
                    return "关闭代理失败：" + ex.Message;
                }
            };
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
                {
                    int enable = 0;
                    string server = "";
                    if (k != null)
                    {
                        object v = k.GetValue("ProxyEnable");
                        if (v != null) enable = Convert.ToInt32(v);
                        object s = k.GetValue("ProxyServer");
                        if (s != null) server = s.ToString();
                    }
                    proxy.Detected = enable != 0;
                    proxy.Detail = proxy.Detected
                        ? "检测到系统代理处于启用状态（" + (server.Length > 0 ? server : "未指定服务器") +
                          "）。若不是你主动设置的，它会导致浏览器无法上网。"
                        : "未启用系统代理。";
                }
            }
            catch
            {
                proxy.Detail = "无法读取代理设置。";
            }
            list.Add(proxy);
            Report(phase, "网络与连接", 30);

            // ⑥ DNS 解析缓存（换网/改 hosts 后仍解析到旧地址的常见原因）
            RepairItem dns = New("dns_cache", "网络与连接", "DNS 解析缓存", 4);
            dns.FixLabel = "刷新缓存";
            dns.Action = delegate (RepairItem it)
            {
                Shell.Result r = NetTools.FlushDns();
                return r.Ok ? "DNS 缓存已刷新。" : "刷新失败：" + r.All;
            };
            dns.Detected = true; // 缓存总是存在：该项作为"随手可做的维护动作"常驻
            dns.Detail = "缓存会保留旧解析结果，换网或改过 hosts 后建议刷新。";
            list.Add(dns);
            Report(phase, "网络与连接", 55);

            // ⑦ hosts 文件异常（被软件写入大量重定向）
            RepairItem hosts = New("hosts_abnormal", "网络与连接", "hosts 文件", 8);
            hosts.FixLabel = "还原上次备份";
            hosts.Action = delegate (RepairItem it)
            {
                string path = HostsPath();
                string backup = path + ".bak";
                try
                {
                    if (!File.Exists(backup)) return "没有找到备份文件，无法自动还原。";
                    File.Copy(backup, path, true);
                    return "已用备份还原 hosts 文件。";
                }
                catch (Exception ex)
                {
                    return "还原失败：" + ex.Message;
                }
            };
            try
            {
                string path = HostsPath();
                int lines = 0;
                if (File.Exists(path))
                {
                    string[] all = File.ReadAllLines(path);
                    for (int i = 0; i < all.Length; i++)
                    {
                        string s = all[i].Trim();
                        if (s.Length == 0 || s.StartsWith("#")) continue;
                        lines++;
                    }
                }
                hosts.Detected = lines > 200;
                hosts.Detail = hosts.Detected
                    ? "hosts 里有 " + lines + " 条生效规则，数量异常，可能是被修改软件写入。可在「Hosts 编辑」页核对。"
                    : "hosts 生效规则 " + lines + " 条，正常。";
            }
            catch
            {
                hosts.Detail = "无法读取 hosts 文件。";
            }
            list.Add(hosts);
            Report(phase, "网络与连接", 100);
        }

        // ---------------- 驱动与设备 ----------------

        private static void AddDeviceChecks(List<RepairItem> list, Action<string, int> phase)
        {
            // ⑧ 设备管理器里的异常设备：只报告，不提供假按钮（驱动修复必须由厂商/Windows 更新完成）
            RepairItem dev = New("device_error", "驱动与设备", "异常设备", 16);
            dev.Fixable = false;
            try
            {
                List<string> bad = new List<string>();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
                {
                    foreach (ManagementBaseObject o in searcher.Get())
                    {
                        try
                        {
                            string name = o["Name"] == null ? "" : o["Name"].ToString();
                            if (name.Length > 0) bad.Add(name);
                        }
                        catch
                        {
                        }
                    }
                }
                dev.Detected = bad.Count > 0;
                if (dev.Detected)
                {
                    string sample = bad.Count <= 2 ? string.Join("、", bad.ToArray()) : bad[0] + " 等 " + bad.Count + " 个设备";
                    dev.Detail = "有 " + bad.Count + " 个设备报告异常（" + sample +
                        "）。驱动问题需由厂商驱动或 Windows 更新修复，本工具不代改驱动。";
                }
                else
                {
                    dev.Detail = "所有设备工作正常。";
                }
            }
            catch
            {
                dev.Detail = "无法查询设备状态。";
            }
            list.Add(dev);
            Report(phase, "驱动与设备", 100);
        }

        // ---------------- 优化项校验 ----------------

        private static void AddTweakChecks(List<RepairItem> list, Action<string, int> phase)
        {
            List<ITweak> all = TweakLibrary.All();

            // ⑨ 事件日志体积（系统/应用日志过大是"用久了变卡"的常见原因，且可安全清空）
            RepairItem log = New("event_log", "Windows 系统", "系统事件日志体积", 8);
            log.FixLabel = "清空超大日志";
            log.Action = delegate (RepairItem it)
            {
                string output = "";
                try
                {
                    Shell.Result r1 = Shell.Run("wevtutil.exe", "cl System", 30000);
                    Shell.Result r2 = Shell.Run("wevtutil.exe", "cl Application", 30000);
                    output = r1.Ok && r2.Ok ? "已清空系统与应用程序日志。" : "部分日志清空失败（可能需要管理员权限）。";
                }
                catch (Exception ex)
                {
                    output = "清空日志失败：" + ex.Message;
                }
                return output;
            };
            try
            {
                long total = 0;
                long biggest = 0;
                string biggestName = "";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT LogfileName, FileSize FROM Win32_NTEventlogFile"))
                {
                    foreach (ManagementBaseObject o in searcher.Get())
                    {
                        try
                        {
                            string name = o["LogfileName"] == null ? "" : o["LogfileName"].ToString();
                            object sizeObj = o["FileSize"];
                            long size = sizeObj == null ? 0 : Convert.ToInt64(sizeObj);
                            total += size;
                            if (size > biggest) { biggest = size; biggestName = name; }
                        }
                        catch
                        {
                        }
                    }
                }
                log.Detected = biggest > 200L * 1024 * 1024; // 单个日志超过 200 MB 视为异常
                log.Detail = log.Detected
                    ? "日志 " + biggestName + " 已占 " + SysInfo.FormatSize(biggest) + "（合计 " +
                      SysInfo.FormatSize(total) + "），清空不影响系统功能。"
                    : "事件日志合计 " + SysInfo.FormatSize(total) + "，正常。";
            }
            catch
            {
                log.Detail = "无法读取事件日志体积。";
            }
            list.Add(log);
            Report(phase, "优化项校验", 50);

            // ⑩ 当前状态下是否还有高价值的推荐项没启用
            RepairItem rec = New("tweak_recommend", "常见问题", "建议启用的优化项", 6);
            rec.FixLabel = "应用推荐项";
            rec.Action = delegate (RepairItem it)
            {
                List<ITweak> back = TweakLibrary.All();
                int ok = 0;
                int fail = 0;
                for (int i = 0; i < back.Count; i++)
                {
                    try
                    {
                        ITweak t = back[i];
                        if (!t.Recommended || t.IsApplied()) continue;
                        if (t.AdminOnly && !Native.IsElevated()) continue;
                        if (t.Apply()) ok++;
                        else fail++;
                    }
                    catch
                    {
                        fail++;
                    }
                }
                return ok > 0
                    ? "已启用 " + ok + " 项推荐优化" + (fail > 0 ? "，" + fail + " 项失败。" : "。")
                    : "推荐项都已启用。";
            };
            try
            {
                List<string> pending = new List<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    try
                    {
                        ITweak t = all[i];
                        if (!t.Recommended || t.IsApplied()) continue;
                        if (t.Risky) continue;              // 风险项不主动推荐
                        if (t.AdminOnly && !Native.IsElevated()) continue;
                        pending.Add(t.Name);
                    }
                    catch
                    {
                    }
                }
                rec.Detected = pending.Count > 0;
                rec.Detail = rec.Detected
                    ? "有 " + pending.Count + " 项安全且普遍有益的优化尚未启用：" +
                      (pending.Count <= 2 ? string.Join("、", pending.ToArray()) : pending[0] + " 等 " + pending.Count + " 项")
                    : "推荐优化项都已启用。";
            }
            catch
            {
                rec.Detail = "无法读取推荐项。";
            }
            list.Add(rec);
            Report(phase, "优化项校验", 100);
        }

        // ==============================================================
        // 执行修复
        // ==============================================================

        /// <summary>执行单项修复，返回给用户看的结果文案。</summary>
        public static string Fix(RepairItem item)
        {
            if (item == null) return "无效的修复项。";
            if (!item.Fixable) return "该项只能按提示手动处理。";
            if (item.Action == null) return "该项没有可执行的修复动作。";

            try
            {
                string msg = item.Action(item);
                item.Fixed = true;
                item.Detected = false;
                return msg;
            }
            catch (Exception ex)
            {
                return "修复失败：" + ex.Message;
            }
        }

        /// <summary>健康分：100 减去检出项的加权扣分（下限 0）。仅作为概览指标。</summary>
        public static int Score(List<RepairItem> items)
        {
            if (items == null) return 100;
            int score = 100;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Detected) score -= items[i].Weight;
            }
            return score < 0 ? 0 : score;
        }

        // ==============================================================
        // 具体文件操作
        // ==============================================================

        private static string HostsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");
        }

        /// <summary>用户临时目录体积。</summary>
        private static long TempSize()
        {
            return DirSize(Path.GetTempPath());
        }

        private static long DirSize(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long total = 0;
            // 逐目录安全遍历：SearchOption.AllDirectories 遇到无权限子目录会整体抛异常，
            // 导致整个目录体积被误判为 0
            List<string> files = DirWalk.ListFiles(dir, null);

            // 大临时目录常有上万文件：逐文件取长度改为并行（本地累加后一次性合并）
            object gate = new object();
            Parallel.For(0, files.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                () => 0L,
                (i, state, local) =>
                {
                    try { local += new FileInfo(files[i]).Length; }
                    catch { }
                    return local;
                },
                delegate(long local) { lock (gate) { total += local; } });

            return total;
        }

        private static string[] IconCacheFiles()
        {
            List<string> list = new List<string>();
            try
            {
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Explorer");
                if (Directory.Exists(explorer))
                {
                    list.AddRange(Directory.GetFiles(explorer, "iconcache_*.db"));
                    list.AddRange(Directory.GetFiles(explorer, "thumbcache_*.db"));
                }
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IconCache.db");
                if (File.Exists(legacy)) list.Add(legacy);
            }
            catch
            {
            }
            return list.ToArray();
        }

        private static long IconCacheSize()
        {
            long total = 0;
            string[] files = IconCacheFiles();
            for (int i = 0; i < files.Length; i++)
            {
                try { total += new FileInfo(files[i]).Length; }
                catch { }
            }
            return total;
        }

        /// <summary>
        /// 清理图标缓存。缓存文件被资源管理器占用，直接删除会失败——
        /// 因此先删除可删的，剩下的重启资源管理器后再删一次。
        /// </summary>
        private static long ClearIconCache()
        {
            long removed = 0;
            string[] files = IconCacheFiles();
            removed += DeleteFiles(files, false);

            bool blocked = false;
            for (int i = 0; i < files.Length; i++)
            {
                try { if (File.Exists(files[i])) { blocked = true; break; } }
                catch { }
            }

            if (blocked)
            {
                // 重启资源管理器以释放占用（会短暂闪烁任务栏，属预期行为）
                Shell.Run("taskkill.exe", "/F /IM explorer.exe", 15000);
                System.Threading.Thread.Sleep(800);
                removed += DeleteFiles(files, false);
                try { using (Process.Start("explorer.exe")) { } }
                catch { }
            }
            return removed;
        }

        private static long ClearTemp()
        {
            string temp = Path.GetTempPath();
            if (string.IsNullOrEmpty(temp) || !Directory.Exists(temp)) return 0;
            return DeleteFiles(DirWalk.ListFiles(temp, null).ToArray(), true);
        }

        /// <summary>
        /// 删除文件并返回释放的字节数。<paramref name="all"/> = true 时连同子目录一起清空目录本身；
        /// 正在被占用的文件会跳过（异常吞掉），因此可安全用于临时目录。
        /// </summary>
        private static long DeleteFiles(string[] files, bool all)
        {
            long removed = 0;
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    FileInfo fi = new FileInfo(files[i]);
                    long len = fi.Length;
                    fi.Delete();
                    removed += len;
                }
                catch
                {
                    // 占用中 / 无权限：跳过，不中断整体清理
                }
            }
            return removed;
        }

        /// <summary>
        /// 服务是否在运行。清理更新缓存要"先停后启"，必须知道它原本的状态，
        /// 否则会把用户主动停掉的服务重新拉起来。
        /// </summary>
        private static bool ServiceRunning(string name)
        {
            try
            {
                List<ServiceInfo> all = ServiceManager.List();
                for (int i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return all[i].IsRunning;
                }
            }
            catch
            {
            }
            return false;
        }

        private static long ClearUpdateCache(out string message)
        {
            message = "";
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                @"SoftwareDistribution\Download");
            if (!Directory.Exists(dir))
            {
                message = "更新缓存目录不存在。";
                return 0;
            }

            bool wasRunning = ServiceRunning("wuauserv");
            string error;
            if (wasRunning) ServiceManager.Stop("wuauserv", out error);
            try
            {
                long freed = DeleteFiles(DirWalk.ListFiles(dir, null).ToArray(), true);
                if (freed == 0) message = "缓存文件正被占用，可稍后重试。";
                return freed;
            }
            finally
            {
                if (wasRunning) ServiceManager.Start("wuauserv", out error);
            }
        }
    }
}
