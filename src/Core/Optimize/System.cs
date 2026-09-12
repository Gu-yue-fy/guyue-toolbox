using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
            return RegHelper.SetServiceStart(ServiceName, RegHelper.SvcDisabled);
        }

        public bool Revert()
        {
            int target = _originalStart;
            if (target < 0)
            {
                // 会话内没有记录时使用合理默认值
                target = RegHelper.SvcManual;
            }
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

        /// <summary>状态探测：返回 true 表示已应用。</summary>
        public Func<bool> Probe;

        public string Id { get { return IdValue; } }
        public string Group { get { return GroupValue; } }
        public string Name { get { return NameValue; } }
        public string Description { get { return DescriptionValue; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return RiskyValue; } }
        public bool Recommended { get { return RecommendedValue; } }

        public bool IsApplied()
        {
            if (Probe == null) return false;
            try { return Probe(); }
            catch { return false; }
        }

        public bool Apply()
        {
            if (string.IsNullOrEmpty(EnableFile)) return false;
            return Shell.Run(EnableFile, EnableArgs, 60000).Ok;
        }

        public bool Revert()
        {
            if (string.IsNullOrEmpty(RevertFile)) return false;
            return Shell.Run(RevertFile, RevertArgs, 60000).Ok;
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
                if (!Shell.Run("bcdedit.exe", _apply[i], 30000).Ok) return false;
            }
            return true;
        }

        public bool Revert()
        {
            for (int i = 0; i < _revert.Length; i++)
            {
                Shell.Run("bcdedit.exe", _revert[i], 30000);
            }
            return true;
        }
    }

    /// <summary>
    /// Svchost 服务合并：把服务拆分阈值抬到物理内存总量以上，
    /// 所有共享服务并入少量 svchost 进程，减少进程数与上下文切换。
    /// </summary>
    public sealed class SvchostSplitTweak : ITweak
    {
        private const string Path = @"SYSTEM\CurrentControlSet\Control";

        public string Id { get { return "svchost_merge"; } }
        public string Group { get { return TweakLibrary.GPerformance; } }
        public string Name { get { return "Svchost 服务合并"; } }
        public string Description
        {
            get { return "把服务拆分阈值抬到物理内存总量，svchost 从上百个合并为十几个，降低内存占用与调度开销。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static long TotalKB()
        {
            return (long)SysInfo.GetMemory().TotalBytes / 1024;
        }

        public bool IsApplied()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, Path, "SvcHostSplitThresholdInKB");
            if (v == null) return false;
            try { return Convert.ToInt64(v) >= TotalKB(); }
            catch { return false; }
        }

        public bool Apply()
        {
            long kb = TotalKB();
            if (kb <= 0) return false;
            RegHelper.BeginBackup(Id);
            RegHelper.SetValue(RegistryHive.LocalMachine, Path, "SvcHostSplitThresholdInKB",
                (int)Math.Min(kb, int.MaxValue), RegistryValueKind.DWord, Id);
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }
}
