/* 文件说明：系统类优化项：服务项、命令项、MSI 模式、BCD、svchost 拆分。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================

    // ===================================================================
    // 服务优化项
    // ===================================================================

    public sealed class ServiceTweak : ITweak
    {
        public string IdValue = "";
        public string GroupValue = "系统服务";
        public string ServiceName = "";
        public string DisplayName = "";
        public string DescriptionValue = "";
        public bool RiskyValue;
        public bool RecommendedValue;

        private int _originalStart = -2;
        private readonly string _backupId;

        public ServiceTweak()
        {
            _backupId = "";
        }

        public ServiceTweak(string id)
        {
            IdValue = id;
            _backupId = "svc_" + id;
        }

        public string Id { get { return IdValue; } }
        public string Group { get { return GroupValue; } }
        public string Name { get { return DisplayName; } }
        public string Description { get { return DescriptionValue; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return RiskyValue; } }
        public bool Recommended { get { return RecommendedValue; } }

        public string BackupId { get { return _backupId; } }

        public int CurrentStart
        {
            get { return RegHelper.GetServiceStart(ServiceName); }
        }

        public string CurrentStartText
        {
            get { return RegHelper.ServiceStartText(CurrentStart); }
        }

        public bool Exists
        {
            get { return CurrentStart >= 0; }
        }

        public bool IsApplied()
        {
            int cur = CurrentStart;
            if (cur < 0) return false;
            return cur == RegHelper.SvcDisabled;
        }

        public bool Apply()
        {
            if (!Exists) return false;
            _originalStart = CurrentStart;
            // 这里刻意写注册表，而不是调用 ServiceManager（SCM ChangeServiceConfig）：
            // 声明式优化项必须走 RegHelper 的备份链，才能把系统原 Start 值持久化下来，
            // 之后在任意时刻都能还原回真实原值；SCM 改法没有这层备份能力。
            // 「立即生效的服务启停/改启动类型」由服务管理页的 ServiceManager 承担，两者职责不同，
            // 不合并是设计选择而非疏漏。
            return RegHelper.SetServiceStart(ServiceName, RegHelper.SvcDisabled, _backupId);
        }

        public bool Revert()
        {
            // 还原优先级：持久化备份 > 会话内记录 > 兜底"手动"
            int target = RegHelper.GetServiceStartOriginal(_backupId, ServiceName);
            if (target < 0) target = _originalStart;
            if (target < 0 || target == RegHelper.SvcDisabled) target = RegHelper.SvcManual;
            return RegHelper.SetServiceStart(ServiceName, target);
        }
    }
    // ===================================================================
    // 命令式优化项（powercfg / fsutil 等）
    // ===================================================================

    public sealed class CommandTweak : ITweak
    {
        public string IdValue = "";
        public string GroupValue = "";
        public string NameValue = "";
        public string DescriptionValue = "";
        public bool RiskyValue;
        public bool RecommendedValue;

        public string EnableFile = "";
        public string EnableArgs = "";
        public string RevertFile = "";
        public string RevertArgs = "";

        /// <summary>状态探测：返回 true 表示已应用。为空时改用本工具记录的应用标记（见 IsApplied）。</summary>
        public Func<bool> Probe;

        /// <summary>
        /// 应用前捕获原始状态（例如当前电源计划 GUID），返回值会在还原时原样交回 RestoreOriginal。
        /// 命令式项不像 RegTweak 有注册表备份链，想让"还原"回到**你原来的状态**（而不是预设默认值）
        /// 就必须在这里先记下来。
        /// </summary>
        public Func<string> CaptureOriginal;

        /// <summary>用先前捕获的原始状态精确还原；返回是否成功。为空时退回 RevertFile/RevertArgs。</summary>
        public Func<string, bool> RestoreOriginal;

        // 命令式项的"已应用 + 原始状态"记录（HKCU，按项 id 一格）：
        // 程序重启后仍能判定状态并精确还原。
        private const string StateRoot = @"Software\GuyueBox\CmdState";

        public string Id { get { return IdValue; } }
        public string Group { get { return GroupValue; } }
        public string Name { get { return NameValue; } }
        public string Description { get { return DescriptionValue; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return RiskyValue; } }
        public bool Recommended { get { return RecommendedValue; } }

        public bool IsApplied()
        {
            if (Probe != null)
            {
                try { return Probe(); }
                catch { return false; }
            }
            // 没有探测器的项（dism / netsh / 部分 powercfg）：用本工具记录的应用标记判定。
            // 原先这里直接 return false —— 开关点开、命令执行成功，一刷新又被判成"未启用"，
            // 表现为"开关自己弹回去"，用户以为功能没生效。
            return GetState() != null;
        }

        /// <summary>读取记录（null = 没有记录）。空串表示"已应用但没有需要保留的原值"。</summary>
        private string GetState()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StateRoot))
                {
                    if (k == null) return null;
                    object v = k.GetValue(IdValue);
                    return v == null ? null : v.ToString();
                }
            }
            catch { return null; }
        }

        private void SetState(string original)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StateRoot))
                {
                    if (k != null) k.SetValue(IdValue, original ?? "", Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch { }
        }

        private void ClearState()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StateRoot, true))
                {
                    if (k != null) k.DeleteValue(IdValue, false);
                }
            }
            catch { }
        }

        public bool Apply()
        {
            if (string.IsNullOrEmpty(EnableFile)) return false;

            // 已应用就直接算成功，不再重复执行：命令式项重复 Apply 会用"应用后"的当前状态
            // 覆盖掉记录里的原值，之后还原就变成"还原成被优化后的值"（开关看着卡住）。
            // 现有 UI 入口都会跳过已应用项，这里是兜底（例如批量入口将来改动）。
            if (IsApplied()) return true;

            string captured = null;
            if (CaptureOriginal != null)
            {
                try { captured = CaptureOriginal(); }
                catch { }
            }

            // dism 命令在慢机器上可能需要 120 秒；其他命令 60 秒够用
            int timeout = EnableFile.IndexOf("dism", StringComparison.OrdinalIgnoreCase) >= 0 ? 120000 : 60000;
            bool ok = Shell.Run(EnableFile, EnableArgs, timeout, null, true).Ok;
            // 应用成功才记录；失败不写标记，状态自然不会误报"已应用"。
            // 已有记录时不覆盖：首份原值才是还原依据（与 RegHelper 备份链同一原则）。
            if (ok && GetState() == null) SetState(captured);
            return ok;
        }

        public bool Revert()
        {
            string captured = GetState();

            // 有原值就先按原值精确还原（例如回到你原来用的那个电源计划）
            if (RestoreOriginal != null && !string.IsNullOrEmpty(captured))
            {
                try
                {
                    if (RestoreOriginal(captured)) { ClearState(); return true; }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(RevertFile)) return false;
            int timeout = RevertFile.IndexOf("dism", StringComparison.OrdinalIgnoreCase) >= 0 ? 120000 : 60000;
            bool ok2 = Shell.Run(RevertFile, RevertArgs, timeout, null, true).Ok;
            if (ok2) ClearState();
            return ok2;
        }
    }
    /// <summary>
    /// 设备 MSI 中断模式：为 GPU / 网卡 / USB 控制器启用 Message Signaled Interrupts。
    /// MSI 中断比传统线路中断更快且不共享 IRQ，可消除共享中断导致的中断延迟尖峰。
    /// 存储/RAID 控制器刻意排除（蓝屏风险）。
    /// </summary>
    public sealed class MsiModeTweak : ITweak
    {
        private const string EnumBase = @"SYSTEM\CurrentControlSet\Enum";

        // 只对这三类设备启用：显卡 / 网卡 / USB 控制器（XHCI/EHCI）
        private static readonly string[] TargetClasses = new string[]
        {
            "{4d36e968-e325-11ce-bfc1-08002be10318}",
            "{4d36e972-e325-11ce-bfc1-08002be10318}",
            "{36fc9e60-c465-11cf-8056-444553540000}"
        };

        public string Id { get { return "msi_mode"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "设备 MSI 中断模式（GPU / 网卡 / USB）"; } }
        public string Description
        {
            get { return "为显卡、网卡与 USB 控制器启用 MSI（消息信号中断）——比传统线路中断更快、独占 IRQ，消除共享中断导致的延迟尖峰（msinfo32 冲突/共享中可验证）。需重启生效；部分老设备不支持 MSI 时无效果。存储控制器不在处理范围（有蓝屏风险）。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        /// <summary>枚举 PCI 总线下属于目标类别的设备 instance 路径（相对 EnumBase）。</summary>
        private static List<string> TargetInstances()
        {
            List<string> found = new List<string>();
            try
            {
                using (RegistryKey pci = Registry.LocalMachine.OpenSubKey(EnumBase + @"\PCI"))
                {
                    if (pci == null) return found;
                    foreach (string dev in pci.GetSubKeyNames())
                    {
                        using (RegistryKey insts = pci.OpenSubKey(dev))
                        {
                            if (insts == null) continue;
                            foreach (string inst in insts.GetSubKeyNames())
                            {
                                using (RegistryKey k = insts.OpenSubKey(inst))
                                {
                                    if (k == null) continue;
                                    object cls = k.GetValue("ClassGUID");
                                    if (cls == null) continue;
                                    string c = cls.ToString();
                                    for (int i = 0; i < TargetClasses.Length; i++)
                                    {
                                        if (string.Equals(c, TargetClasses[i], StringComparison.OrdinalIgnoreCase))
                                        {
                                            found.Add(@"PCI\" + dev + @"\" + inst);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return found;
        }

        private static string ParamPath(string instance)
        {
            return EnumBase + @"\" + instance + @"\Device Parameters";
        }

        public bool IsApplied()
        {
            List<string> targets = TargetInstances();
            if (targets.Count == 0) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                    ParamPath(targets[i]), "MSISupported");
                if (v == null) return false;
                try { if (Convert.ToInt32(v) != 1) return false; }
                catch { return false; }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> targets = TargetInstances();
            if (targets.Count == 0) return false;
            RegHelper.BeginBackup(Id);
            bool ok = true;
            for (int i = 0; i < targets.Count; i++)
            {
                ok &= RegHelper.SetValue(RegistryHive.LocalMachine, ParamPath(targets[i]),
                    "MSISupported", 1, RegistryValueKind.DWord, Id);
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }
    /// <summary>
    /// 处理器电源调度优化：解除核心停泊（停泊最少数=100%）并把性能检查间隔拉到 5000ms。
    /// 检查间隔拉满后 PpmPerfAction/PpmCheckRun 等 DPC 从每秒上百次降到接近 0。
    /// </summary>
    public sealed class BcdTweak : ITweak
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _apply;
        private readonly string[] _revert;
        private readonly string[] _mustHave;   // 输出中必须匹配（正则）
        private readonly string[] _mustNotHave; // 输出中必须不匹配
        private readonly bool _risky;

        public BcdTweak(string id, string name, string desc, string[] apply, string[] revert,
            string[] mustHave, string[] mustNotHave, bool risky)
        {
            _id = id; _name = name; _desc = desc;
            _apply = apply; _revert = revert;
            _mustHave = mustHave; _mustNotHave = mustNotHave; _risky = risky;
        }

        public string GroupOverride;

        public string Id { get { return _id; } }
        public string Group { get { return GroupOverride ?? TweakLibrary.GGame; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        public bool IsApplied()
        {
            Shell.Result r = Shell.Run("bcdedit.exe", "/enum {current}", 30000);
            string text = (r.All ?? "");
            if (text.Length == 0) return false;
            for (int i = 0; i < _mustHave.Length; i++)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, _mustHave[i],
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
            }
            for (int i = 0; i < _mustNotHave.Length; i++)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, _mustNotHave[i],
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        public bool Apply()
        {
            for (int i = 0; i < _apply.Length; i++)
            {
                if (!Shell.Run("bcdedit.exe", _apply[i], 30000, isChange: true).Ok) return false;
            }
            return true;
        }

        public bool Revert()
        {
            bool ok = true;
            for (int i = 0; i < _revert.Length; i++)
            {
                if (!Shell.Run("bcdedit.exe", _revert[i], 30000).Ok) ok = false;
            }
            return ok;
        }
    }
    /// <summary>
    /// Svchost 服务合并：把服务拆分阈值抬到物理内存总量以上，
    /// 所有共享服务并入少量 svchost 进程，减少进程数与上下文切换。
    /// 默认档按物理内存计算阈值，完全合并档（CreateFuseAll）直接写入 0xFFFFFFFF。
    /// </summary>
    public sealed class SvchostSplitTweak : ITweak
    {
        private const string Path = @"SYSTEM\CurrentControlSet\Control";
        private const string ValueName = "SvcHostSplitThresholdInKB";

        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly bool _risky;
        private readonly string _backupId;
        private readonly bool _fixed;   // true：固定阈值档（完全合并），false：按物理内存计算
        private readonly long _fixedKB;

        /// <summary>默认实例：阈值按物理内存总量计算（Id=svchost_merge）。</summary>
        public SvchostSplitTweak()
            : this("svchost_merge", "Svchost 服务合并",
                "把服务拆分阈值抬到物理内存总量，svchost 从上百个合并为十几个，降低内存占用与调度开销。",
                false, "svchost_merge", false, 0)
        {
        }

        private SvchostSplitTweak(string id, string name, string desc, bool risky,
            string backupId, bool fixedValue, long fixedKb)
        {
            _id = id;
            _name = name;
            _desc = desc;
            _risky = risky;
            _backupId = backupId;
            _fixed = fixedValue;
            _fixedKB = fixedKb;
        }

        /// <summary>完全合并档：阈值写 0xFFFFFFFF，全部共享服务并入同一 svchost 进程（与 svchost_merge 共用同一备份组）。</summary>
        public static SvchostSplitTweak CreateFuseAll()
        {
            return new SvchostSplitTweak("svc_fuse_all", "Svchost 完全合并",
                "把服务拆分阈值设为最大值 0xFFFFFFFF，全部共享服务并入同一个 svchost 进程，进一步减少进程数与上下文切换；与「Svchost 服务合并」共用同一备份组。",
                true, "svchost_merge", true, 0xFFFFFFFFL);
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GPerformance; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        private static long TotalKB()
        {
            return (long)SysInfo.GetMemory().TotalBytes / 1024;
        }

        /// <summary>目标阈值（KB）：固定档直接返回 0xFFFFFFFF；默认档按内存计算并钳制到 DWORD 上限，避免 ≥2TB 内存时溢出。</summary>
        private long TargetKB()
        {
            if (_fixed) return _fixedKB;
            return Math.Min(TotalKB(), (long)int.MaxValue);
        }

        /// <summary>写入值：DWORD 在 .NET 侧是 int，0xFFFFFFFF 需按无符号解释写入（int 侧为 -1）。</summary>
        private int WriteValue()
        {
            return unchecked((int)TargetKB());
        }

        /// <summary>读回当前阈值，按无符号 DWORD 解释（0..4294967295）；无值返回 -1。</summary>
        private static long ReadThreshold()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, Path, ValueName);
            if (v == null) return -1;
            try { return unchecked((uint)Convert.ToInt32(v)); }
            catch { return -1; }
        }

        public bool IsApplied()
        {
            long cur = ReadThreshold();
            if (cur < 0) return false;
            return cur >= TargetKB();
        }

        public bool Apply()
        {
            long kb = TargetKB();
            if (kb <= 0) return false;
            RegHelper.BeginBackup(_backupId);
            RegHelper.SetValue(RegistryHive.LocalMachine, Path, ValueName,
                WriteValue(), RegistryValueKind.DWord, _backupId);
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(_backupId);
        }
    }

    /// <summary>
    /// 外置命令输出的文本探测辅助。
    /// dism / netsh 的输出标签与状态词随系统语言本地化（中文系统为「状态 : 已启用」「已禁用」），
    /// 只匹配英文关键字会让 Probe 在中文系统上恒为 false（状态永远显示「未启用」）。
    /// 这里统一按「行」判定，中英关键字双匹配。
    /// </summary>
    internal static class ProbeText
    {
        /// <summary>找出包含任一关键字的行（不区分大小写）；找不到返回 null。</summary>
        public static string FindLine(string text, string keyA, string keyB)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (keyA != null && ln.IndexOf(keyA, StringComparison.OrdinalIgnoreCase) >= 0) return ln;
                if (keyB != null && ln.IndexOf(keyB, StringComparison.OrdinalIgnoreCase) >= 0) return ln;
            }
            return null;
        }

        /// <summary>该行是否表示「已启用」（英文 Enabled / 中文 已启用、已开启）。</summary>
        public static bool SaysOn(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return line.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("已启用", StringComparison.Ordinal) >= 0
                || line.IndexOf("已开启", StringComparison.Ordinal) >= 0;
        }

        /// <summary>该行是否表示「已禁用」（英文 Disabled / 中文 已禁用、已关闭）。</summary>
        public static bool SaysOff(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            return line.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("已禁用", StringComparison.Ordinal) >= 0
                || line.IndexOf("已关闭", StringComparison.Ordinal) >= 0;
        }
    }
}
