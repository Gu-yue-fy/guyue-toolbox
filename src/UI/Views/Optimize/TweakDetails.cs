/* UI/Views/Optimize/TweakDetails.cs — 详情文案引擎：由优化项自身数据推导四段式详情。
 * v2.2 起重写「原理」段：不再按分类套模板（同一分类 385 项显示同一句空话），
 * 而是读取每个优化项真正写入的注册表键 / 服务 / 命令，生成它自己的说明。 */

using System;
using System.Collections.Generic;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    internal static class TweakDetails
    {
        // --------------------------------------------------------------
        // 注册表路径 → 区域语义（按「包含」匹配，先长后短；命中即用）
        // --------------------------------------------------------------

        private static readonly string[][] PathMap = new string[][]
        {
            new string[] { "Session Manager\\kernel", "内核与会话调度" },
            new string[] { "Session Manager\\Memory Management", "内存管理器" },
            new string[] { "Session Manager\\I/O System", "I/O 子系统" },
            new string[] { "Session Manager\\Executive", "执行体（Executive）" },
            new string[] { "Control Panel\\Mouse", "鼠标指针选项" },
            new string[] { "Control Panel\\Keyboard", "键盘重复响应" },
            new string[] { "Control Panel\\Cursors", "鼠标指针方案" },
            new string[] { "Control Panel\\Access", "辅助功能（粘滞键等）" },
            new string[] { "Services\\Dnscache\\Parameters", "DNS 客户端缓存" },
            new string[] { "Windows\\DWM", "桌面窗口管理器（DWM）" },
            new string[] { "ContentDeliveryManager", "预装内容与推广分发" },
            new string[] { "Services\\Tcpip\\Parameters", "TCP/IP 协议栈" },
            new string[] { "Services\\Tcpip\\ServiceProvider", "各网络服务解析优先级" },
            new string[] { "Control\\FileSystem", "NTFS 文件系统行为" },
            new string[] { "Control\\GraphicsDrivers", "显卡驱动与 WDDM" },
            new string[] { "Control\\PriorityControl", "前后台优先级控制" },
            new string[] { "Control\\Power", "电源控制" },
            new string[] { "Control\\CrashControl", "崩溃转储控制" },
            new string[] { "Control\\Lsa", "本地安全策略（LSA）" },
            new string[] { "AppCompat", "应用兼容性（AppCompat）" },
            new string[] { "Explorer\\Advanced", "资源管理器高级选项" },
            new string[] { "GameConfigStore", "Xbox 游戏配置存储" },
            new string[] { "GameBar", "Xbox 游戏栏" },
            new string[] { "Policies\\System", "系统电源与登录策略" },
            new string[] { "Policies\\WindowsUpdate", "Windows 更新策略" },
            new string[] { "InputPersonalization", "输入个性化（在线词典）" },
            new string[] { "Policies\\Microsoft\\Edge", "Edge 浏览器策略" },
            new string[] { "WOW6432Node\\Microsoft\\Direct3D", "Direct3D 运行时" },
            new string[] { "Microsoft\\DirectX", "DirectX 配置" },
            new string[] { "LanmanServer\\Parameters", "SMB 文件共享服务端" },
            new string[] { "LanmanWorkstation\\Parameters", "SMB 网络客户端" },
            new string[] { "CloudContent", "云内容与推广" },
            new string[] { "WindowsAI", "Windows AI（Copilot 相关）" },
            new string[] { "Services\\USBXHCI", "USB 3.x 主控驱动" },
            new string[] { "Services\\mouhid\\Parameters", "鼠标 HID 输入驱动" },
            new string[] { "Services\\HidUsb\\Parameters", "USB HID 输入设备" },
            new string[] { "Services\\USB\\Parameters", "USB 总线驱动" },
            new string[] { "Control\\usbflags", "USB 设备标志" },
            new string[] { "Services\\kbdclass\\Parameters", "键盘输入驱动" },
            new string[] { "Services\\mouclass\\Parameters", "鼠标输入缓冲" },
            new string[] { "Multimedia\\SystemProfile", "多媒体类调度（MMCSS）" },
            new string[] { "Windows NT\\CurrentVersion\\Windows", "窗口与关机行为" },
            new string[] { "Policies\\Explorer", "资源管理器策略" },
            new string[] { "Policies\\Windows\\System", "系统策略" },
            new string[] { "Windows\\CurrentVersion\\Explorer", "资源管理器" },
            new string[] { "DataCollection", "诊断数据收集" },
            new string[] { "AdvertisingInfo", "广告标识符" },
            new string[] { "Notifications\\Settings", "通知设置" },
            new string[] { "Search", "Windows 搜索" },
            new string[] { "Personalization", "个性化设置" },
        };

        // --------------------------------------------------------------
        // 注册表值名 → 动作语义（有把握才写；未命中时展示键名本身）
        // --------------------------------------------------------------

        private static readonly Dictionary<string, string> KeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Win32PrioritySeparation", "前后台进程的 CPU 时间分配倾向" },
            { "SystemResponsiveness", "系统为后台/多媒体保留的 CPU 份额" },
            { "NetworkThrottlingIndex", "每毫秒网络中断节流阈值" },
            { "SchedulerTimerResolution", "MMCSS 调度器计时精度（微秒）" },
            { "MouseSpeed", "鼠标指针加速度档位" },
            { "MouseThreshold1", "指针加速触发阈值 1" },
            { "MouseThreshold2", "指针加速触发阈值 2" },
            { "MouseSensitivity", "指针移动灵敏度（1–20 档）" },
            { "MouseDataQueueSize", "鼠标输入缓冲队列大小" },
            { "KeyboardDataQueueSize", "键盘输入缓冲队列大小" },
            { "GameDVR_Enabled", "后台游戏录制开关" },
            { "AppCaptureEnabled", "游戏画面捕获开关" },
            { "AllowAutoGameMode", "自动游戏模式开关" },
            { "ChillEnabled", "游戏栏性能干预（Game DVR Chill）" },
            { "OverlayTestMode", "MPO 多平面覆盖测试模式" },
            { "HWschMode", "硬件加速 GPU 调度" },
            { "TdrLevel", "显卡超时检测与恢复（TDR）级别" },
            { "TdrDelay", "显卡无响应判定延迟（秒）" },
            { "AllowTelemetry", "诊断与遥测数据级别" },
            { "CEIPEnable", "客户体验改善计划（CEIP）" },
            { "AutoDownload", "推广内容自动下载开关" },
            { "Start_IrisRecommendations", "开始菜单推荐内容" },
            { "SilentInstalledAppsEnabled", "预装应用静默安装" },
            { "SystemResponsiveness_", "" },
            { "TcpAckFrequency", "TCP ACK 确认频率（0=每包即回）" },
            { "TCPDelAckTicks", "延迟 ACK 计时（200ms 档）" },
            { "TCPNoDelay", "Nagle 小包合并开关" },
            { "DefaultTTL", "TCP 默认生存时间（TTL）" },
            { "TcpTimedWaitDelay", "TIME_WAIT 端口回收时间（秒）" },
            { "KeepAliveTime", "TCP 保活探测间隔（毫秒）" },
            { "KeepAliveInterval", "TCP 保活重试间隔（毫秒）" },
            { "MaxUserPort", "动态端口上限" },
            { "MaxNegativeCacheTtl", "负解析缓存时效（秒）" },
            { "NegativeCacheTime", "负解析缓存时效" },
            { "NetFailureCacheTime", "解析失败缓存时效" },
            { "MaxCacheEntryTtlLimit", "正解析缓存时效上限" },
            { "DnsPriority", "DNS 解析源优先级" },
            { "HostsPriority", "hosts 文件解析优先级" },
            { "LocalPriority", "本地名解析优先级" },
            { "NetbtPriority", "NetBIOS 解析优先级" },
            { "EnableVirtualizationBasedSecurity", "基于虚拟化的安全（VBS/HVCI）" },
            { "PowerThrottlingOff", "该进程的 CPU 频率节流豁免" },
            { "CpuPriorityClass", "进程 CPU 优先级类别" },
            { "IoPriority", "进程 I/O 优先级" },
            { "Win32PriorityValue", "进程优先级数值" },
            { "SelectiveSuspendEnabled", "USB 选择性暂停（省电挂起）" },
            { "IdleTimeout", "设备空闲超时（毫秒）" },
            { "DisableIdlePowerManagement", "设备空闲省电管理" },
            { "EnhancedPowerManagementEnabled", "USB 增强电源管理" },
            { "DisableSelectiveSuspend", "USB 选择性暂停（设备级）" },
            { "PnPCapabilities", "网卡电源管理能力位" },
            { "Start", "服务启动类型（2=自动 3=手动 4=禁用）" },
            { "StartupDelayInMSec", "启动项分批加载延迟（毫秒）" },
            { "MouseHoverTime", "悬停提示触发延迟（毫秒）" },
            { "MenuShowDelay", "菜单弹出延迟（毫秒）" },
            { "HungAppTimeout", "程序无响应判定时长（毫秒）" },
            { "WaitToKillServiceTimeout", "关机时强杀服务等待（毫秒）" },
            { "AutoEndTasks", "关机时自动结束未响应程序" },
            { "NtfsDisableLastAccessUpdate", "NTFS 最后访问时间戳更新" },
            { "NtfsDisable8dot3NameCreation", "NTFS 8.3 短文件名生成" },
            { "NtfsMemoryUsage", "NTFS 内存占用模式" },
            { "NtfsMftZoneReservation", "NTFS 主文件表预留区" },
            { "NtfsDisableFileMetadataOptimization", "NTFS 元数据后台优化" },
            { "NoLockScreenCamera", "锁屏界面摄像头开关" },
            { "AllowAdvertising", "广告标识符开关" },
            { "MaxThreads", "服务最大工作线程数" },
            { "MaxCollectionCount", "数据收集缓冲大小" },
            { "MaxCmds", "SMB 命令缓冲数" },
            { "MaxFreeTcbs", "TCP 控制块上限" },
            { "SessionPoolSize", "会话池大小" },
            { "SessionViewSize", "会话视图缓冲大小" },
            { "EnableQpcBypass", "高精度时钟绕过校验" },
            { "ConvertibleSlateMode", "平板/笔记本形态判定" },
        };

        /// <summary>把一条优化项推导为四段式详情（原理段逐项生成，见 BuildWhy）。</summary>
        public static DetailInfo Build(ITweak t)
        {
            DetailInfo d = new DetailInfo();
            if (t == null) return d;

            string group = string.IsNullOrEmpty(t.Group) ? TweakPackProvider.GPack : t.Group;

            d.Title = t.Name + "（" + group + "）";
            d.Icon = t.Risky ? "warn" : "tune";
            d.Accent = t.Risky ? Theme.Warning : Theme.Accent;

            // ① 作用：逐项撰写的 Description（数据源本身质量好，直接用）
            d.What = string.IsNullOrEmpty(t.Description) ? t.Name : t.Description;

            // ② 原理：由该项自身写入内容推导（见下方）
            d.Why = BuildWhy(t);

            // ③ 机制说明：一句话（不再长篇模板）
            d.How = t.AdminOnly
                ? "写入 HKLM / 服务 / 电源设置，需管理员权限；应用前原值自动进入备份链。"
                : "只写当前用户注册表，无需管理员；应用前原值自动进入备份链。";

            // ④ 风险与影响
            if (t.Risky)
            {
                d.Risk = "标记为「谨慎」：可能影响某些程序的兼容性，或依赖它的系统功能。建议先单项启用、确认无异常后再继续。";
                d.RiskTone = true;
            }
            else
            {
                d.Risk = "标记为「安全」：影响面可控，出问题概率低。";
            }

            // ⑤ 可撤销
            bool applied = false;
            try { applied = t.IsApplied(); }
            catch { }
            d.Reversible = "可撤销：随时在本页关闭该项即按备份写回原值；也可在「软件设置 → 操作日志」按组整体回滚。当前状态：" + (applied ? "已启用" : "未启用") + "。";
            d.Irreversible = false;

            return d;
        }

        // --------------------------------------------------------------
        // 原理段推导
        // --------------------------------------------------------------

        private static string BuildWhy(ITweak t)
        {
            // 声明式注册表项：读它真正写入的键
            RegTweak reg = t as RegTweak;
            if (reg != null && reg.Enable != null && reg.Enable.Count > 0)
            {
                return ExplainWrites(reg);
            }

            // 服务项：读服务名与当前启动类型
            ServiceTweak svc = t as ServiceTweak;
            if (svc != null && !string.IsNullOrEmpty(svc.ServiceName))
            {
                string cur = "未知";
                try { cur = svc.CurrentStartText; }
                catch { }
                return "把系统服务「" + svc.ServiceName + "」的启动类型改为「禁用」（当前：" + cur +
                    "），它不再随开机自启、也不再后台常驻；需要时随时可还原为原类型。";
            }

            // 命令式项：读它执行的命令
            CommandTweak cmd = t as CommandTweak;
            if (cmd != null && !string.IsNullOrEmpty(cmd.EnableFile))
            {
                string c = cmd.EnableFile + (string.IsNullOrEmpty(cmd.EnableArgs) ? "" : " " + cmd.EnableArgs);
                return "通过系统命令落盘配置：" + c.Trim() + "。命令由系统自带工具执行，结果可由本工具备份链还原。";
            }

            // 计划任务项：读任务清单
            ScheduledTaskTweak task = t as ScheduledTaskTweak;
            if (task != null)
            {
                string[] paths = task.TaskPaths;
                if (paths != null && paths.Length > 0)
                {
                    string sample = paths[0];
                    int slash = sample.LastIndexOf('\\');
                    if (slash >= 0 && slash + 1 < sample.Length) sample = sample.Substring(slash + 1);
                    return "把 " + paths.Length + " 个计划任务设为「禁用」（如「" + sample +
                        "」），这些系统后台维护任务不再定时唤醒 CPU；可在任务计划程序中查看同名条目。";
                }
                return "对一组系统计划任务做批量禁用/启用。";
            }

            // 兜底（电源 powercfg 项等）：写清机制，不写分类空话
            return "该项通过系统自带机制调整默认配置，应用前原值由备份链记录，关闭即还原。";
        }

        /// <summary>由 RegTweak 的写入清单生成"原理"正文：单写展开说，多写概括 + 指向清单。</summary>
        private static string ExplainWrites(RegTweak reg)
        {
            List<RegWrite> ws = reg.Enable;
            if (ws.Count == 1)
            {
                return ExplainOne(ws[0]);
            }

            // 多写：找最有代表性的一条展开，其余指向下方清单
            RegWrite main = ws[0];
            for (int i = 0; i < ws.Count; i++)
            {
                if (KeyMap.ContainsKey(ws[i].Name)) { main = ws[i]; break; }
            }
            return "共修改 " + ws.Count + " 处注册表设置，核心是" + ExplainOne(main, true) +
                "。其余写入点见下方「将写入的项」清单，全部改动都会进入备份链。";
        }

        /// <summary>单条写入的完整说明。</summary>
        private static string ExplainOne(RegWrite r)
        {
            return ExplainOne(r, false);
        }

        /// <summary>单条写入的说明。shortMode 用于多写场景的主句（去掉句尾备份链提示）。</summary>
        private static string ExplainOne(RegWrite r, bool shortMode)
        {
            string area = ExplainPath(r.Path);
            string key;
            string keyMeaning;
            if (!KeyMap.TryGetValue(r.Name, out keyMeaning)) keyMeaning = null;

            string value = ValueBrief(r);

            // 服务键（HKLM\SYSTEM\...\Services\X 下的 Start）：换一种说法
            if (string.Equals(r.Name, "Start", StringComparison.OrdinalIgnoreCase) &&
                r.Path != null && r.Path.IndexOf("\\Services\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                key = "该服务的启动类型";
            }
            else
            {
                key = string.IsNullOrEmpty(r.Name) ? "（默认值）" : r.Name;
                if (keyMeaning != null) key += "（" + keyMeaning + "）";
            }

            string head = "把" + area + "中的「" + key + "」" + (r.Delete ? "删除（回到系统默认）" : "设为 " + value);
            return shortMode
                ? head
                : head + "。";
        }

        /// <summary>路径 → 区域名。命中映射表用中文名；未命中时用键的根+叶子段（仍具体到本项）。</summary>
        private static string ExplainPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "注册表";
            for (int i = 0; i < PathMap.Length; i++)
            {
                if (path.IndexOf(PathMap[i][0], StringComparison.OrdinalIgnoreCase) >= 0)
                    return "「" + PathMap[i][1] + "」";
            }
            // 未映射：显示末两段路径，好过一句分类空话
            string[] segs = path.Split('\\');
            string tail = segs.Length >= 2 ? segs[segs.Length - 2] + "\\" + segs[segs.Length - 1] : path;
            return "「" + tail + "」";
        }

        private static string ValueBrief(RegWrite r)
        {
            if (r.Delete) return "";
            if (r.Value is byte[]) return ((byte[])r.Value).Length + " 字节二进制";
            return r.Value == null ? "" : Convert.ToString(r.Value);
        }
    }
}