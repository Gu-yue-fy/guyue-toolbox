using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace SysToolbox.Core
{
    public interface ITweak
    {
        string Id { get; }
        string Group { get; }
        string Name { get; }
        string Description { get; }
        bool AdminOnly { get; }
        bool Risky { get; }
        bool Recommended { get; }
        bool IsApplied();
        bool Apply();
        bool Revert();
    }

    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================

    public sealed class RegWrite
    {
        public RegistryHive Hive = RegistryHive.CurrentUser;
        public string Path = "";
        public string Name = "";
        public RegistryValueKind Kind = RegistryValueKind.DWord;
        public object Value;

        /// <summary>为 true 时表示删除该值（回到系统默认）。</summary>
        public bool Delete;

        public static RegWrite Dword(RegistryHive hive, string path, string name, int value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.DWord;
            w.Value = value;
            return w;
        }

        public static RegWrite Str(RegistryHive hive, string path, string name, string value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.String;
            w.Value = value;
            return w;
        }

        public static RegWrite Binary(RegistryHive hive, string path, string name, byte[] value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.Binary;
            w.Value = value;
            return w;
        }

        public static RegWrite Remove(RegistryHive hive, string path, string name)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Delete = true;
            return w;
        }

        public bool MatchesCurrent()
        {
            if (Delete)
            {
                return RegHelper.GetValue(Hive, Path, Name) == null;
            }
            object cur = RegHelper.GetValue(Hive, Path, Name);
            if (cur == null) return false;
            try
            {
                if (Value is int)
                {
                    int a = Convert.ToInt32(cur, CultureInfo.InvariantCulture);
                    return a == (int)Value;
                }
                return string.Equals(cur.ToString(), Value == null ? "" : Value.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void Write(string backupId)
        {
            if (Delete)
            {
                RegHelper.DeleteValue(Hive, Path, Name, backupId);
                return;
            }

            object value = Value;
            RegistryValueKind kind = Kind;
            if (kind == RegistryValueKind.DWord && !(value is int))
            {
                int iv;
                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out iv)) value = iv;
            }
            RegHelper.SetValue(Hive, Path, Name, value, kind, backupId);
        }
    }

    /// <summary>
    /// 声明式优化项：Enable 为一组目标写入，Revert 为可选的手工还原动作
    /// （不指定时直接使用备份还原）。
    /// </summary>
    public sealed class RegTweak : ITweak
    {
        public string IdValue = "";
        public string GroupValue = "";
        public string NameValue = "";
        public string DescriptionValue = "";
        public bool AdminOnlyValue;
        public bool RiskyValue;
        public bool RecommendedValue;

        /// <summary>
        /// 同键多档项共享的备份 ID（如 Win32PrioritySeparation 三个档位）：
        /// 首次应用记录系统原值，各档还原都回到最初默认，切档互不污染。
        /// 为空时使用 IdValue。
        /// </summary>
        public string BackupIdValue = "";

        public List<RegWrite> Enable = new List<RegWrite>();
        public List<RegWrite> RevertWrites = new List<RegWrite>();

        /// <summary>还原时是否强制使用备份（默认 true）。</summary>
        public bool UseBackupOnRevert = true;

        /// <summary>
        /// 适用性检测（可选）：本机不满足生效条件时返回 false。
        /// 未达标的项「已启用」状态恒为否，应用会直接拒绝，避免写无效键造成"开了却没生效"。
        /// </summary>
        public Func<bool> ApplicableValue;

        private string BackupId
        {
            get { return string.IsNullOrEmpty(BackupIdValue) ? IdValue : BackupIdValue; }
        }

        public string Id { get { return IdValue; } }
        public string Group { get { return GroupValue; } }
        public string Name { get { return NameValue; } }
        public string Description { get { return DescriptionValue; } }
        public bool AdminOnly { get { return AdminOnlyValue; } }
        public bool Risky { get { return RiskyValue; } }
        public bool Recommended { get { return RecommendedValue; } }

        private bool Applicable()
        {
            try { return ApplicableValue == null || ApplicableValue(); }
            catch { return true; }
        }

        public bool IsApplied()
        {
            if (Enable.Count == 0) return false;
            if (!Applicable()) return false;
            // 全部写入项都与目标值匹配才算"已启用"（部分写入失败时如实显示未启用）
            for (int i = 0; i < Enable.Count; i++)
            {
                if (!Enable[i].MatchesCurrent()) return false;
            }
            return true;
        }

        public bool Apply()
        {
            if (!Applicable()) return false; // 本机不满足生效条件，不写无效键
            RegHelper.BeginBackup(BackupId);
            bool ok = true;
            for (int i = 0; i < Enable.Count; i++)
            {
                try { Enable[i].Write(BackupId); }
                catch { ok = false; }
            }
            return ok;
        }

        public bool Revert()
        {
            if (RevertWrites.Count > 0)
            {
                bool ok = true;
                for (int i = 0; i < RevertWrites.Count; i++)
                {
                    try { RevertWrites[i].Write(null); }
                    catch { ok = false; }
                }
                return ok;
            }

            if (UseBackupOnRevert)
            {
                return RegHelper.Restore(BackupId);
            }
            return false;
        }
    }

    /// <summary>
    /// 写入显卡设备类 {4d36e968} 全部实例（0000、0001…）的键值，
    /// 用于 dunu 系显卡类调优（固件调度/全速渲染等），全实例覆盖避免依赖具体索引。
    /// </summary>
    /// <summary>
    /// CPU 厂商检测：按处理器名称串判断（Intel / AMD）。
    /// 用于让 CPU 厂商专属优化项（如 Intel TSX/调度键）在无关机器上自动判定「不适用」。
    /// </summary>
    public static class CpuVendor
    {
        private static string _name;

        private static string Name()
        {
            if (_name != null) return _name;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                {
                    object n = k == null ? null : k.GetValue("ProcessorNameString");
                    _name = n == null ? "" : n.ToString();
                }
            }
            catch
            {
                _name = "";
            }
            return _name;
        }

        public static bool IsIntel
        {
            get { return Name().IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        public static bool IsAmd
        {
            get { return Name().IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       Name().IndexOf("ryzen", StringComparison.OrdinalIgnoreCase) >= 0; }
        }
    }

    public sealed class GpuInstanceTweak : ITweak
    {
        private const string GpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly KeyValuePair<string, object>[] _values;
        private bool _risky;
        private Func<bool> _applicable;

        public GpuInstanceTweak(string id, string name, string desc, KeyValuePair<string, object>[] values)
        {
            _id = id;
            _name = name;
            _desc = desc;
            _values = values;
        }

        /// <summary>挂厂商适用性检测：不满足时项不可用（状态恒「未启用」、应用直接拒绝）。</summary>
        public GpuInstanceTweak ApplicableWhen(Func<bool> check)
        {
            _applicable = check;
            return this;
        }

        private bool Applicable()
        {
            try { return _applicable == null || _applicable(); }
            catch { return true; }
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }
        public bool RiskyValue { get { return _risky; } set { _risky = value; } }

        public bool IsApplied()
        {
            if (!Applicable()) return false;
            List<string> instances = GpuInstances();
            if (instances.Count == 0) return false;
            foreach (string path in instances)
            {
                foreach (KeyValuePair<string, object> kv in _values)
                {
                    object v = RegHelper.GetValue(RegistryHive.LocalMachine, path, kv.Key);
                    if (v == null || Convert.ToInt32(v) != Convert.ToInt32(kv.Value)) return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            if (!Applicable()) return false; // 厂商不适用（如 A 卡跑 N 卡专属项），不写无效键
            List<string> instances = GpuInstances();
            if (instances.Count == 0) return false;
            RegHelper.BeginBackup(_id);
            bool ok = true;
            foreach (string path in instances)
            {
                foreach (KeyValuePair<string, object> kv in _values)
                {
                    ok &= RegHelper.SetValue(RegistryHive.LocalMachine, path, kv.Key,
                        Convert.ToInt32(kv.Value), RegistryValueKind.DWord, _id);
                }
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(_id);
        }

        private static List<string> GpuInstances()
        {
            List<string> result = new List<string>();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass))
                {
                    if (cls == null) return result;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (RegexCheck(sub)) result.Add(GpuClass + "\\" + sub);
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static bool RegexCheck(string name)
        {
            // 实例子键为 0000、0001 … 四位数字
            if (name.Length != 4) return false;
            foreach (char c in name)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }

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

    // ===================================================================
    // Nagle 禁用（低延迟网络）：需要写入每个 TCP/IP 接口，动态项
    // ===================================================================

    public sealed class NagleTweak : ITweak
    {
        private const string InterfacesPath =
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        public string Id { get { return "nagle_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "禁用 Nagle 算法（降低网络延迟）"; } }
        public string Description
        {
            get { return "对全部网卡接口禁用 Nagle 合包与延迟确认，竞技游戏实测可降低数毫秒网络延迟。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static List<string> InterfacePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(InterfacesPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        list.Add(InterfacesPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public bool IsApplied()
        {
            List<string> paths = InterfacePaths();
            if (paths.Count == 0) return false;
            for (int i = 0; i < paths.Count; i++)
            {
                object ack = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "TcpAckFrequency");
                object noDelay = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "TCPNoDelay");
                if (ack == null || noDelay == null) return false;
                try
                {
                    if (Convert.ToInt32(ack) != 1 || Convert.ToInt32(noDelay) != 1) return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> paths = InterfacePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(Id);
            bool ok = true;
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TcpAckFrequency", 1,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TCPNoDelay", 1,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TcpDelAckTicks", 0,
                        RegistryValueKind.DWord, Id);
                }
                catch
                {
                    ok = false;
                }
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>
    /// 显卡驱动 LTR 延迟参数（NVIDIA/AMD 低延迟注册表键）：
    /// 按 GPU 厂商把对应键集写入显卡实例子键，消除 PCIe LTR 与显示流水线的空闲等待。需重启生效。
    /// </summary>
    public sealed class GpuLatencyTweak : ITweak
    {
        private const string GpuClass =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static readonly string[] NvidiaKeys = new string[]
        {
            "D3PCLatency", "F1TransitionLatency", "LOWLATENCY", "Node3DLowLatency",
            "RMDeepL1EntryLatencyUsec", "RmGspcMaxFtuS", "RmGspcMinFtuS", "RmGspcPerioduS",
            "RMLpwrEiIdleThresholdUs", "RMLpwrGrIdleThresholdUs", "RMLpwrGrRgIdleThresholdUs",
            "RMLpwrMsIdleThresholdUs", "VRDirectFlipDPCDelayUs", "VRDirectFlipTimingMarginUs",
            "VRDirectJITFlipMsHybridFlipDelayUs", "vrrCursorMarginUs", "vrrDeflickerMarginUs",
            "vrrDeflickerMaxUs"
        };

        private static readonly string[] AmdKeys = new string[]
        {
            "LTRSnoopL1Latency", "LTRSnoopL0Latency", "LTRNoSnoopL1Latency", "LTRMaxNoSnoopLatency",
            "KMD_RpmComputeLatency", "DalUrgentLatencyNs", "memClockSwitchLatency",
            "PP_RTPMComputeF1Latency", "PP_DGBMMMaxTransitionLatencyUvd", "PP_DGBPMMaxTransitionLatencyGfx",
            "DalNBLatencyForUnderFlow", "BGM_LTRSnoopL1Latency", "BGM_LTRSnoopL0Latency",
            "BGM_LTRNoSnoopL1Latency", "BGM_LTRNoSnoopL0Latency", "BGM_LTRMaxSnoopLatencyValue",
            "BGM_LTRMaxNoSnoopLatencyValue"
        };

        public string Id { get { return "gpu_ltr_latency"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "显卡驱动 LTR 延迟参数（自动识别本机显卡）"; } }
        public string Description
        {
            get { return "把显卡驱动的空闲等待参数全部压到 1 微秒级，降低显示流水线延迟。自动识别本机显卡厂商：NVIDIA 写 D3PC/VRDirectFlip/VRR 裕量键集，AMD 写 LTR Snoop/显存切换延迟键集；Intel 核显或无法识别时不适用（应用会直接拒绝）。需重启生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        /// <summary>显卡厂商识别：N=NVIDIA，A=AMD，I=Intel，空=未识别。</summary>
        public static string DetectVendor()
        {
            for (int i = 0; i < 6; i++)
            {
                string desc = RegHelper.GetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i, "DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;
                string d = desc.ToLowerInvariant();
                if (d.Contains("nvidia") || d.Contains("geforce") || d.Contains("quadro")) return "N";
                if (d.Contains("amd") || d.Contains("radeon") || d.Contains("ati")) return "A";
                if (d.Contains("intel") || d.Contains("arc")) return "I";
            }
            return "";
        }

        private static void WriteSet(string backupId, string[] keys)
        {
            for (int i = 0; i < 4; i++)
            {
                foreach (string k in keys)
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i, k, 1,
                        RegistryValueKind.DWord, backupId);
                }
            }
            // PciLatencyTimerControl 用 0x20（N 卡惯例值）
            if (Array.IndexOf(keys, "D3PCLatency") >= 0)
            {
                RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\0000", "PciLatencyTimerControl", 32,
                    RegistryValueKind.DWord, backupId);
            }
        }

        public bool IsApplied()
        {
            string vendor = DetectVendor();
            if (vendor != "N" && vendor != "A") return false; // 本机显卡不在适用范围
            string[] keys = vendor == "A" ? AmdKeys : NvidiaKeys;
            string probe = keys[0];
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, GpuClass + "\\0000", probe);
            try { return v != null && Convert.ToInt32(v) == 1; }
            catch { return false; }
        }

        public bool Apply()
        {
            string vendor = DetectVendor();
            // 厂商不适用（Intel 核显 / 未知）时拒绝——绝不再把两套键全写
            if (vendor != "N" && vendor != "A") return false;
            RegHelper.BeginBackup(Id);
            if (vendor == "A") WriteSet(Id, AmdKeys);
            else WriteSet(Id, NvidiaKeys);
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>
    /// 关闭 NVIDIA 驱动遥测：注册表开关（NvControlPanel2 / nvlddmkm / FTS RID 三处）
    /// 加禁用 NvTm* 遥测计划任务（privacy.sexy 方案）。
    /// </summary>
    public sealed class NvTelemetryTweak : ITweak
    {
        public string Id { get { return "nv_telemetry_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "关闭 NVIDIA 驱动遥测"; } }
        public string Description
        {
            get { return "停止 GeForce 驱动向 NVIDIA 服务器发送使用数据（NvControlPanel2、nvlddmkm、FTS RID 三处开关），并禁用 NvTmRep/NvTmMon/NvTmRepOnLogon 遥测计划任务，减少后台网络与 CPU 活动。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static readonly string[][] RegKeys = new string[][]
        {
            new string[] { @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInOrOutPreference" },
            new string[] { @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup", "SendTelemetryData" },
            new string[] { @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID44231" },
            new string[] { @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID64640" },
            new string[] { @"SOFTWARE\NVIDIA Corporation\Global\FTS", "EnableRID66610" }
        };

        private static bool RunTaskScript(bool disable)
        {
            string cmd = disable
                ? "Get-ScheduledTask -TaskPath '\\' -TaskName 'NvTm*' -ErrorAction SilentlyContinue | Disable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null"
                : "Get-ScheduledTask -TaskPath '\\' -TaskName 'NvTm*' -ErrorAction SilentlyContinue | Enable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null";
            return Shell.Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"", 60000).Ok;
        }

        public bool IsApplied()
        {
            for (int i = 0; i < RegKeys.Length; i++)
            {
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, RegKeys[i][0], RegKeys[i][1]);
                if (v == null || Convert.ToInt32(v) != 0) return false;
            }
            return true;
        }

        public bool Apply()
        {
            RegHelper.BeginBackup(Id);
            bool ok = true;
            for (int i = 0; i < RegKeys.Length; i++)
            {
                ok &= RegHelper.SetValue(RegistryHive.LocalMachine, RegKeys[i][0], RegKeys[i][1], 0,
                    RegistryValueKind.DWord, Id);
            }
            // 计划任务禁用为尽力而为：NVCleanstall 精简驱动可能没有 NvTm* 任务
            RunTaskScript(true);
            return ok;
        }

        public bool Revert()
        {
            if (!RegHelper.Restore(Id)) return false;
            RunTaskScript(false);
            return true;
        }
    }

    /// <summary>
    /// 游戏 DSCP 46（EF 加速转发）QoS 标记。
    /// 为选定的游戏 exe 写入 QoS 策略（HKLM\...\QoS），出站包打 DSCP 46 标记，
    /// 支持按包优先级调度的路由器/运营商会优先转发游戏流量。
    /// </summary>
    public sealed class DscpTweak : ITweak
    {
        private const string QosKey = @"SOFTWARE\Policies\Microsoft\Windows\QoS";

        public string Id { get { return "dscp_game_marker"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "游戏网络 DSCP 优先标记 (QoS)"; } }
        public string Description
        {
            get { return "选择游戏 exe，为其出站网络包打 DSCP 46（EF 加速转发）标记——支持 QoS 调度的路由器/运营商会优先转发游戏流量，降低网络延迟抖动。同时禁用该游戏的 DX 无边框窗口化模式。仅对支持 DSCP 的网络环境生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private const string ApplyScript =
            "Add-Type -AssemblyName System.Windows.Forms\r\n" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog\r\n" +
            "$d.Filter = '游戏程序 (*.exe)|*.exe'\r\n" +
            "$d.Title = 'Select game executable'\r\n" +
            "if ($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 1 }\r\n" +
            "$n = [IO.Path]::GetFileName($d.FileName)\r\n" +
            "$k = 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\QoS\\' + $n\r\n" +
            "New-Item -Path $k -Force | Out-Null\r\n" +
            "Set-ItemProperty -Path $k -Name 'Application Name' -Value $n -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name 'Version' -Value '1.0' -Type String\r\n" +
            "foreach ($p in 'Protocol','Local Port','Local IP','Local IP Prefix Length','Remote Port','Remote IP','Remote IP Prefix Length') { Set-ItemProperty -Path $k -Name $p -Value '*' -Type String }\r\n" +
            "Set-ItemProperty -Path $k -Name 'DSCP Value' -Value '46' -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name 'Throttle Rate' -Value '-1' -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name \"Don't use NLA\" -Value '1' -Type String\r\n" +
            "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers' -Name $d.FileName -Value '~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE' -Type String\r\n";

        private const string RevertScript =
            "$base = 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\QoS'\r\n" +
            "if (Test-Path $base) { Get-ChildItem $base | ForEach-Object { $v = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).'DSCP Value'; if ($v -eq '46') { Remove-Item $_.PSPath -Force } } }\r\n" +
            "$lay = 'HKCU:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers'\r\n" +
            "if (Test-Path $lay) { $p = Get-ItemProperty $lay; $p.PSObject.Properties | Where-Object { $_.Value -like '*DISABLEDXMAXIMIZEDWINDOWEDMODE*' } | ForEach-Object { Remove-ItemProperty -Path $lay -Name $_.Name -ErrorAction SilentlyContinue } }\r\n";

        private static bool RunScript(string script)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "systoolbox_dscp.ps1");
                System.IO.File.WriteAllText(path, script,
                    new System.Text.UTF8Encoding(true));
                return Shell.Run("powershell.exe",
                    "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + path + "\"", 120000).Ok;
            }
            catch
            {
                return false;
            }
        }

        public bool IsApplied()
        {
            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(QosKey))
                {
                    if (baseKey == null) return false;
                    foreach (string sub in baseKey.GetSubKeyNames())
                    {
                        using (RegistryKey k = baseKey.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("DSCP Value");
                            object nla = k.GetValue("Don't use NLA");
                            if (v != null && v.ToString() == "46" &&
                                nla != null && nla.ToString() == "1") return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public bool Apply()
        {
            return RunScript(ApplyScript) && IsApplied();
        }

        public bool Revert()
        {
            return RunScript(RevertScript);
        }
    }

    /// <summary>
    /// 游戏进程高优先级（IFEO）：为选定的游戏 exe 写入 PerfOptions
    /// CpuPriorityClass=3（高）+ IoPriority=3，系统启动该游戏时自动提权。
    /// 黑白包「永劫无间优先级」方案的通用化。
    /// </summary>
    public sealed class GamePriorityTweak : ITweak
    {
        private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        public string Id { get { return "game_priority_ifeo"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "游戏进程高优先级 (IFEO)"; } }
        public string Description
        {
            get { return "选择游戏 exe，通过映像劫持选项让系统启动该游戏时自动赋予高 CPU 优先级与高 IO 优先级，后台任务不再抢占游戏时间片。选错程序也无妨，还原即可移除。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private const string ApplyScript =
            "Add-Type -AssemblyName System.Windows.Forms\r\n" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog\r\n" +
            "$d.Filter = '游戏程序 (*.exe)|*.exe'\r\n" +
            "$d.Title = 'Select game executable'\r\n" +
            "if ($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 1 }\r\n" +
            "$n = [IO.Path]::GetFileName($d.FileName)\r\n" +
            "$k = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\' + $n + '\\PerfOptions'\r\n" +
            "New-Item -Path $k -Force | Out-Null\r\n" +
            "Set-ItemProperty -Path $k -Name 'CpuPriorityClass' -Value 3 -Type DWord\r\n" +
            "Set-ItemProperty -Path $k -Name 'IoPriority' -Value 3 -Type DWord\r\n";

        private const string RevertScript =
            "$root = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options'\r\n" +
            "if (Test-Path $root) { Get-ChildItem $root | Where-Object { Test-Path ($_.PSPath + '\\PerfOptions') } | ForEach-Object { $p = Get-ItemProperty ($_.PSPath + '\\PerfOptions'); if ($p.CpuPriorityClass -eq 3 -and $p.IoPriority -eq 3) { Remove-Item ($_.PSPath + '\\PerfOptions') -Force } } }\r\n";

        private static bool RunScript(string script)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "systoolbox_gameprio.ps1");
                System.IO.File.WriteAllText(path, script, new System.Text.UTF8Encoding(true));
                return Shell.Run("powershell.exe",
                    "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + path + "\"", 120000).Ok;
            }
            catch
            {
                return false;
            }
        }

        public bool IsApplied()
        {
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(IfeoRoot))
                {
                    if (root == null) return false;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (!sub.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        using (RegistryKey k = root.OpenSubKey(sub + "\\PerfOptions"))
                        {
                            if (k == null) continue;
                            object cpu = k.GetValue("CpuPriorityClass");
                            object io = k.GetValue("IoPriority");
                            if (cpu != null && Convert.ToInt32(cpu) == 3) return true;
                            if (io != null && Convert.ToInt32(io) == 3) return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public bool Apply()
        {
            return RunScript(ApplyScript) && IsApplied();
        }

        public bool Revert()
        {
            return RunScript(RevertScript);
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
    public sealed class CoreParkingTweak : ITweak
    {
        private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        private const string CoreParkingMin = "0cc5b647-c1df-4637-891a-dec35c318583";
        private const string PerfCheckInterval = "4d2b0152-7d5c-498b-88e2-34345392a2c5";
        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public string Id { get { return "core_parking_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "解除核心停泊与电源调度优化"; } }
        public string Description
        {
            get { return "把「核心停泊最少数」设为 100%（所有核心保持唤醒），并把处理器性能检查间隔拉到 5000ms——默认间隔会让调度 DPC 每秒触发上百次。停泊核心重新上线需数十微秒，是游戏帧率尖峰的常见原因。笔记本功耗会略增。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string SettingPath(string settingGuid)
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            string scheme = v == null ? "" : v.ToString();
            if (string.IsNullOrEmpty(scheme)) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + SubProcessor + "\\" + settingGuid;
        }

        public bool IsApplied()
        {
            string park = SettingPath(CoreParkingMin);
            string interval = SettingPath(PerfCheckInterval);
            if (string.IsNullOrEmpty(park) || string.IsNullOrEmpty(interval)) return false;
            object ac = RegHelper.GetValue(RegistryHive.LocalMachine, park, "ACSettingIndex");
            object dc = RegHelper.GetValue(RegistryHive.LocalMachine, park, "DCSettingIndex");
            object iv = RegHelper.GetValue(RegistryHive.LocalMachine, interval, "ACSettingIndex");
            if (ac == null || dc == null || iv == null) return false;
            try
            {
                return Convert.ToInt32(ac) == 100 && Convert.ToInt32(dc) == 100 &&
                    Convert.ToInt32(iv) == 5000;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>电源设置子树由 Power 服务管理，写入必须走 powercfg；还原值取方案默认值。</summary>
        private static bool PowerSet(string settingGuid, int ac, int dc)
        {
            string sub = SubProcessor;
            bool ok = Shell.Run("powercfg.exe",
                "/setacvalueindex scheme_current " + sub + " " + settingGuid + " " + ac, 30000).Ok;
            ok &= Shell.Run("powercfg.exe",
                "/setdcvalueindex scheme_current " + sub + " " + settingGuid + " " + dc, 30000).Ok;
            ok &= Shell.Run("powercfg.exe", "/setactive scheme_current", 30000).Ok;
            return ok;
        }

        public bool Apply()
        {
            // 启动时核心全数唤醒（Juxic 电源方案：InitialUnparkCount 系列注册表键）
            RegHelper.BeginBackup(Id);
            string[] unparkKeys = new string[] { "InitialUnparkCount", "Class1InitialUnparkCount", "Class2InitialUnparkCount" };
            bool reg = true;
            for (int i = 0; i < unparkKeys.Length; i++)
            {
                reg &= RegHelper.SetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power", unparkKeys[i], 100,
                    RegistryValueKind.DWord, Id);
            }

            bool ok = PowerSet(CoreParkingMin, 100, 100);
            ok &= PowerSet(PerfCheckInterval, 5000, 5000);
            return ok && reg;
        }

        /// <summary>DefaultPowerSchemeValues\{方案GUID} 存放该方案的出厂默认（ACSettingIndex/DCSettingIndex）。</summary>
        private static int DefaultOf(string settingGuid, string which)
        {
            string scheme = SettingPath(CoreParkingMin);
            if (string.IsNullOrEmpty(scheme)) return -1;
            string[] parts = scheme.Split('\\');
            string schemeGuid = parts[parts.Length - 2];
            object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + SubProcessor + "\\" + settingGuid +
                @"\DefaultPowerSchemeValues\" + schemeGuid, which);
            try { return v == null ? -1 : Convert.ToInt32(v); }
            catch { return -1; }
        }

        public bool Revert()
        {
            // 先还原注册表备份（启动停泊数），再还原电源计划默认值
            RegHelper.Restore(Id);
            int parkAc = DefaultOf(CoreParkingMin, "ACSettingIndex");
            int parkDc = DefaultOf(CoreParkingMin, "DCSettingIndex");
            int ivAc = DefaultOf(PerfCheckInterval, "ACSettingIndex");
            int ivDc = DefaultOf(PerfCheckInterval, "DCSettingIndex");
            if (parkAc < 0 || parkDc < 0 || ivAc < 0 || ivDc < 0) return false;
            bool ok = PowerSet(CoreParkingMin, parkAc, parkDc);
            ok &= PowerSet(PerfCheckInterval, ivAc, ivDc);
            return ok;
        }
    }

    /// <summary>
    /// 禁用新式待机（Modern Standby / S0ix）：PlatformAoAcOverride=0 + CsEnabled=0。
    /// 台式游戏机可避免待机时的后台唤醒活动；笔记本若固件不支持传统 S3 会失去睡眠功能，故为谨慎项。
    /// </summary>
    public sealed class ModernStandbyOffTweak : ITweak
    {
        public string Id { get { return "modern_standby_off"; } }
        public string Group { get { return TweakLibrary.GPower; } }
        public string Name { get { return "禁用新式待机 (Modern Standby)"; } }
        public string Description
        {
            get { return "关闭 S0 低电量待机（Modern Standby）——该模式下系统待机时仍有后台网络与维护活动。禁用后台式机待机更纯净；笔记本需固件支持传统 S3 睡眠，否则将无法睡眠。需重启生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        public bool IsApplied()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride");
            return v != null && Convert.ToInt32(v) == 0;
        }

        public bool Apply()
        {
            RegHelper.BeginBackup(Id);
            bool ok = RegHelper.SetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "PlatformAoAcOverride", 0,
                RegistryValueKind.DWord, Id);
            ok &= RegHelper.SetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "CsEnabled", 0,
                RegistryValueKind.DWord, Id);
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>
    /// 禁用 CPU 空闲状态（C-State 强制 C0）：微软官方实时性能建议项之一。
    /// 消除核心进入/退出空闲的唤醒延迟，代价是待机温度与功耗明显上升；开启 SMT 时单线程可能下降。
    /// </summary>
    public sealed class CpuIdleTweak : ITweak
    {
        private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
        private const string IdleDisable = "5d76a2ca-e8c0-402f-a133-2158492d58ad";
        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public string Id { get { return "cpu_idle_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "禁用 CPU 空闲状态（C-State）"; } }
        public string Description
        {
            get { return "强制核心保持在 C0（不进入深度空闲），消除唤醒延迟——微软实时性能文档推荐项。代价：待机温度与功耗明显上升，笔记本续航变短；开启 SMT/超线程时单线程性能可能不升反降。默认建议保持关闭。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        private static string SettingPath()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            string scheme = v == null ? "" : v.ToString();
            if (string.IsNullOrEmpty(scheme)) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + SubProcessor + "\\" + IdleDisable;
        }

        public bool IsApplied()
        {
            string path = SettingPath();
            if (string.IsNullOrEmpty(path)) return false;
            object ac = RegHelper.GetValue(RegistryHive.LocalMachine, path, "ACSettingIndex");
            if (ac == null) return false;
            try { return Convert.ToInt32(ac) == 1; }
            catch { return false; }
        }

        private static bool PowerSet(int ac, int dc)
        {
            bool ok = Shell.Run("powercfg.exe",
                "/setacvalueindex scheme_current " + SubProcessor + " " + IdleDisable + " " + ac, 30000).Ok;
            ok &= Shell.Run("powercfg.exe",
                "/setdcvalueindex scheme_current " + SubProcessor + " " + IdleDisable + " " + dc, 30000).Ok;
            ok &= Shell.Run("powercfg.exe", "/setactive scheme_current", 30000).Ok;
            return ok;
        }

        public bool Apply()
        {
            return PowerSet(1, 1);
        }

        public bool Revert()
        {
            string scheme = SettingPath();
            if (string.IsNullOrEmpty(scheme)) return false;
            string[] parts = scheme.Split('\\');
            string schemeGuid = parts[parts.Length - 2];
            int ac = RegHelper.GetInt(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + SubProcessor + "\\" + IdleDisable +
                @"\DefaultPowerSchemeValues\" + schemeGuid, "ACSettingIndex", 0);
            int dc = RegHelper.GetInt(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + SubProcessor + "\\" + IdleDisable +
                @"\DefaultPowerSchemeValues\" + schemeGuid, "DCSettingIndex", 0);
            return PowerSet(ac, dc);
        }
    }

    // ===================================================================
    // 输入设备低延迟项（动态：电源计划 / USB 设备逐一处理）
    // ===================================================================

    /// <summary>
    /// 电源计划里的「USB 选择性暂停设置」禁用。
    /// 状态直接读当前活动方案的注册表值，应用/还原走 powercfg。
    /// </summary>
    public sealed class UsbSuspendTweak : ITweak
    {
        private const string UsbSubgroup = "2a737441-1930-4402-8d77-b2bebba308a3";
        private const string UsbSuspendSetting = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public string Id { get { return "usb_suspend_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "禁用电源计划 USB 选择性暂停"; } }
        public string Description
        {
            get { return "禁止系统为省电而挂起空闲的 USB 鼠标 / 键盘 / 手柄，消除唤醒与回报延迟。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string ActiveScheme()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                UserPowerSchemes, "ActivePowerScheme");
            return v == null ? "" : v.ToString();
        }

        private static string SchemeSettingPath()
        {
            string scheme = ActiveScheme();
            if (string.IsNullOrEmpty(scheme)) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + UsbSubgroup + "\\" + UsbSuspendSetting;
        }

        public bool IsApplied()
        {
            string path = SchemeSettingPath();
            if (string.IsNullOrEmpty(path)) return false;
            object ac = RegHelper.GetValue(RegistryHive.LocalMachine, path, "ACSettingIndex");
            object dc = RegHelper.GetValue(RegistryHive.LocalMachine, path, "DCSettingIndex");
            if (ac == null || dc == null) return false;
            try
            {
                return Convert.ToInt32(ac) == 0 && Convert.ToInt32(dc) == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool Apply()
        {
            if (string.IsNullOrEmpty(ActiveScheme())) return false;
            bool ok = Shell.Run("powercfg.exe",
                "-setacvalueindex scheme_current " + UsbSubgroup + " " + UsbSuspendSetting + " 0", 30000).Ok;
            ok &= Shell.Run("powercfg.exe",
                "-setdcvalueindex scheme_current " + UsbSubgroup + " " + UsbSuspendSetting + " 0", 30000).Ok;
            ok &= Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
            return ok;
        }

        public bool Revert()
        {
            if (string.IsNullOrEmpty(ActiveScheme())) return false;
            Shell.Run("powercfg.exe",
                "-setacvalueindex scheme_current " + UsbSubgroup + " " + UsbSuspendSetting + " 1", 30000);
            Shell.Run("powercfg.exe",
                "-setdcvalueindex scheme_current " + UsbSubgroup + " " + UsbSuspendSetting + " 1", 30000);
            return Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
        }
    }

    /// <summary>
    /// 磁盘链路电源管理（LPM）禁用：AHCI HIPM/DIPM、Adaptive、NVMe Idle Timeout。
    /// 磁盘进出低功耗状态的退出延迟是随机卡顿与加载尖峰的常见来源，SSD/HDD 均适用。
    /// 走 powercfg 电源计划写入（磁盘子组 0012ee47），还原读回方案默认值。
    /// </summary>
    public sealed class DiskLpmTweak : ITweak
    {
        private const string DiskSubgroup = "0012ee47-9041-4b5d-9b77-535fba8b1442";

        // {设置 GUID, 还原默认值未知时回退值}
        private static readonly string[][] Settings = new string[][]
        {
            new string[] { "dab60367-53fe-4fbc-825e-521d069d2456", "0" },  // AHCI Link Power Mgmt (HIPM/DIPM)，0=Active
            new string[] { "fc95af4d-40e7-4b6d-835a-56d131dbc80e", "0" },  // AHCI Link Power Mgmt - Adaptive（部分系统无此项，尽力而为）
            new string[] { "d639518a-e56d-4345-8af2-b9f32fb26196", "0" },  // NVMe Idle Timeout，0=不闲置断链
            new string[] { "fc7372b2-ab46-43fc-9b7d-4f9f3a9e7a71", "0" }   // NVMe 电源状态切换延迟容忍，0=不容忍
        };

        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public string Id { get { return "disk_lpm_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "磁盘链路节能禁用（AHCI / NVMe LPM）"; } }
        public string Description
        {
            get { return "禁止 SATA / NVMe 磁盘在空闲时进入低功耗链路状态（HIPM/DIPM、Adaptive、NVMe Idle Timeout）——磁盘唤醒延迟是游戏加载卡顿与掉帧尖峰的常见来源。对当前电源计划生效，还原时回写方案默认值；部分老主板无对应项时自动跳过。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string ActiveScheme()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            return v == null ? "" : v.ToString();
        }

        private static string SchemeSettingPath(string settingGuid)
        {
            string scheme = ActiveScheme();
            if (string.IsNullOrEmpty(scheme)) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + DiskSubgroup + "\\" + settingGuid;
        }

        /// <summary>本机是否支持某个电源设置（PowerSettings 下存在该 GUID 子键）。</summary>
        private static bool Supported(string settingGuid)
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + DiskSubgroup + "\\" + settingGuid,
                "Attributes");
            return v != null;
        }

        public bool IsApplied()
        {
            if (string.IsNullOrEmpty(ActiveScheme())) return false;
            bool any = false;
            for (int i = 0; i < Settings.Length; i++)
            {
                if (!Supported(Settings[i][0])) continue; // 本机没有该设置则跳过
                string path = SchemeSettingPath(Settings[i][0]);
                if (string.IsNullOrEmpty(path)) return false;
                object ac = RegHelper.GetValue(RegistryHive.LocalMachine, path, "ACSettingIndex");
                object dc = RegHelper.GetValue(RegistryHive.LocalMachine, path, "DCSettingIndex");
                if (ac == null || dc == null) return false;
                try
                {
                    if (Convert.ToInt32(ac) != 0 || Convert.ToInt32(dc) != 0) return false;
                }
                catch
                {
                    return false;
                }
                any = true;
            }
            return any;
        }

        public bool Apply()
        {
            if (string.IsNullOrEmpty(ActiveScheme())) return false;
            RegHelper.BeginBackup(Id);
            bool ok = false;
            for (int i = 0; i < Settings.Length; i++)
            {
                bool ac = Shell.Run("powercfg.exe",
                    "-setacvalueindex scheme_current " + DiskSubgroup + " " + Settings[i][0] + " 0", 30000).Ok;
                bool dc = Shell.Run("powercfg.exe",
                    "-setdcvalueindex scheme_current " + DiskSubgroup + " " + Settings[i][0] + " 0", 30000).Ok;
                if (ac && dc) ok = true; // 本机支持的至少一项写入成功即可
            }
            ok &= Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
            return ok;
        }

        public bool Revert()
        {
            if (string.IsNullOrEmpty(ActiveScheme())) return false;
            string scheme = ActiveScheme();
            for (int i = 0; i < Settings.Length; i++)
            {
                // 从方案的出厂默认值回写（无记录时回退 0）
                object def = RegHelper.GetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + DiskSubgroup +
                    "\\" + Settings[i][0] + @"\DefaultPowerSchemeValues\" + scheme, "ACSettingIndex");
                string val = def == null ? Settings[i][1] : Convert.ToInt32(def).ToString();
                Shell.Run("powercfg.exe",
                    "-setacvalueindex scheme_current " + DiskSubgroup + " " + Settings[i][0] + " " + val, 30000);
                Shell.Run("powercfg.exe",
                    "-setdcvalueindex scheme_current " + DiskSubgroup + " " + Settings[i][0] + " " + val, 30000);
            }
            return Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
        }
    }

    /// <summary>
    /// 逐个网卡（网络适配器类驱动子键）关闭省电特性：节能以太网、选择性暂停、
    /// 关机降速、电源节省模式等，消除竞技游戏中网卡省电导致的延迟抖动。
    /// </summary>
    public sealed class NicPowerTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>键名 → 写入值（网卡高级属性多为 REG_SZ；PnPCapabilities 为 DWORD）。</summary>
        private static readonly string[] StrKeys = new string[]
        {
            "*DeviceSleepOnDisconnect", "*EEE", "*ModernStandbyWoLMagicPacket", "*SelectiveSuspend",
            "*WakeOnMagicPacket", "*WakeOnPattern", "AutoPowerSaveModeEnabled", "EEELinkAdvertisement",
            "EeePhyEnable", "EnableGreenEthernet", "EnableModernStandby", "GigaLite",
            "PowerDownPll", "PowerSavingMode", "ReduceSpeedOnPowerDown", "S5WakeOnLan",
            "SavePowerNowEnabled", "ULPMode", "WakeOnLink", "WakeOnSlot", "WakeUpModeCap"
        };

        public string Id { get { return "nic_power_save_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "网卡省电全关（降低延迟抖动）"; } }
        public string Description
        {
            get { return "对每个网卡关闭节能以太网 (EEE)、选择性暂停、电源节省模式等省电特性。省电机制会让网卡间歇性降速，是 WiFi/有线游戏延迟抖动的常见原因。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        /// <summary>只取形如 0000 的驱动实例子键（跳过 Properties 等）。</summary>
        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NicClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(NicClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;
            for (int i = 0; i < paths.Count; i++)
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                {
                    if (k == null) continue;
                    foreach (string name in StrKeys)
                    {
                        object v = k.GetValue(name);
                        if (v != null && v.ToString() != "0") return false;
                    }
                    object pnp = k.GetValue("PnPCapabilities");
                    if (pnp != null)
                    {
                        try
                        {
                            if (Convert.ToInt32(pnp) != 24) return false;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(Id);
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    for (int n = 0; n < StrKeys.Length; n++)
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], StrKeys[n], "0",
                            RegistryValueKind.String, Id);
                    }
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "PnPCapabilities", 24,
                        RegistryValueKind.DWord, Id);
                }
                catch
                {
                }
            }
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// 逐个 USB 设备（鼠标 / 键盘 / 手柄 / 集线器）关闭「允许计算机关闭此设备以节约电源」。
    /// </summary>
    public sealed class UsbPowerTweak : ITweak
    {
        private const string UsbClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{36fc9e60-c465-11cf-8056-444553540000}";

        public string Id { get { return "usb_power_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "USB 设备省电全关（鼠标 / 手柄）"; } }
        public string Description
        {
            get { return "对每个 USB 输入设备关闭「允许计算机关闭此设备以节约电源」，避免竞技游戏中设备短暂离线或掉帧。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        /// <summary>只取形如 0000 的设备子键，跳过 Properties 等系统键。</summary>
        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(UsbClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(UsbClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;
            for (int i = 0; i < paths.Count; i++)
            {
                object enh = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "EnhancedPowerManagementEnabled");
                object sus = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "SelectiveSuspend");
                if (enh == null || sus == null) return false;
                try
                {
                    if (Convert.ToInt32(enh) != 0 || Convert.ToInt32(sus) != 0) return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(Id);
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "EnhancedPowerManagementEnabled", 0,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "SelectiveSuspend", 0,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "DeviceSelectiveSuspended", 0,
                        RegistryValueKind.DWord, Id);
                }
                catch
                {
                }
            }
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    // ===================================================================
    // 网卡高级属性（动态：每个驱动暴露的关键字逐一处理，与 NIC调整.bat 同思路）
    // ===================================================================

    public sealed class NicAdvancedTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _keys;      // REG_SZ 关键字
        private readonly string[] _dwordKeys; // REG_DWORD 关键字

        public NicAdvancedTweak(string id, string name, string desc, string[] keys, string[] dwordKeys)
        {
            _id = id;
            _name = name;
            _desc = desc;
            _keys = keys ?? new string[0];
            _dwordKeys = dwordKeys ?? new string[0];
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NicClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(NicClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private bool Matches(object current, string key)
        {
            bool isDword = Array.IndexOf(_dwordKeys, key) >= 0;
            try
            {
                if (isDword) return Convert.ToInt32(current) == 24;
                return string.Equals(current.ToString(), "0", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            int seen = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                {
                    if (k == null) continue;
                    CheckGroup(k.GetValueNames(), paths[i], ref seen);
                }
            }
            return seen > 0;
        }

        private void CheckGroup(string[] existing, string path, ref int seen)
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                if (Array.IndexOf(existing, _keys[i]) < 0) continue;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, path, _keys[i]);
                if (v == null || !Matches(v, _keys[i])) return; // 存在但未达标 → 未应用
                seen++;
            }
            for (int i = 0; i < _dwordKeys.Length; i++)
            {
                if (Array.IndexOf(existing, _dwordKeys[i]) < 0) continue;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, path, _dwordKeys[i]);
                if (v == null || !Matches(v, _dwordKeys[i])) return;
                seen++;
            }
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(_id);
            int written = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string[] existing;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                    {
                        if (k == null) continue;
                        existing = k.GetValueNames();
                    }
                }
                catch
                {
                    continue;
                }

                for (int v = 0; v < _keys.Length; v++)
                {
                    // 与原脚本一致：只覆盖驱动暴露（已存在）的关键字
                    if (Array.IndexOf(existing, _keys[v]) < 0) continue;
                    try
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], _keys[v], "0",
                            RegistryValueKind.String, _id);
                        written++;
                    }
                    catch
                    {
                    }
                }
                for (int v = 0; v < _dwordKeys.Length; v++)
                {
                    if (Array.IndexOf(existing, _dwordKeys[v]) < 0) continue;
                    try
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], _dwordKeys[v], 24,
                            RegistryValueKind.DWord, _id);
                        written++;
                    }
                    catch
                    {
                    }
                }
            }
            return written > 0;
        }

        public bool Revert()
        {
            return RegHelper.Restore(_id);
        }
    }

    /// <summary>
    /// netsh 类优化项：apply/revert 走命令，probe 解析 show 输出（中英双语，
    /// 通过 chcp 65001 强制 UTF-8 输出避免乱码）。
    /// </summary>
    public sealed class NetshTweak : ITweak
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _apply;
        private readonly string[] _revert;
        private readonly string _show;
        private readonly string[] _patterns; // 每条都必须在输出中匹配到
        private readonly bool _risky;

        public NetshTweak(string id, string name, string desc, string[] apply, string[] revert,
            string show, string[] patterns, bool risky)
        {
            _id = id; _name = name; _desc = desc;
            _apply = apply; _revert = revert; _show = show; _patterns = patterns; _risky = risky;
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        public bool IsApplied()
        {
            string text = RunNetsh(_show);
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < _patterns.Length; i++)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, _patterns[i],
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// 运行 netsh 并自适应解码输出：新系统 (Win11 24H2+) 重定向输出为 UTF-8，
        /// 旧系统为系统 ANSI（中文 GBK/936）。按原始字节先试严格 UTF-8，失败回退 GBK。
        /// </summary>
        private static string RunNetsh(string arguments)
        {
            try
            {
                using (System.Diagnostics.Process p = new System.Diagnostics.Process())
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = "netsh.exe";
                    psi.Arguments = arguments;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    p.StartInfo = psi;
                    p.Start();

                    byte[] stdout = ReadAll(p.StandardOutput.BaseStream);
                    byte[] stderr = ReadAll(p.StandardError.BaseStream);
                    p.WaitForExit(30000);

                    return DecodeAuto(stdout) + "\r\n" + DecodeAuto(stderr);
                }
            }
            catch
            {
                return "";
            }
        }

        private static byte[] ReadAll(System.IO.Stream stream)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                byte[] buf = new byte[8192];
                while (true)
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static string DecodeAuto(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            try
            {
                // 严格 UTF-8：出现非法序列即抛异常，走 GBK 回退
                return new System.Text.UTF8Encoding(false, true).GetString(data);
            }
            catch
            {
                try { return System.Text.Encoding.GetEncoding(936).GetString(data); }
                catch { return System.Text.Encoding.GetEncoding(0).GetString(data); } // OEM 代码页（netsh 输出实际编码）
            }
        }

        public bool Apply()
        {
            for (int i = 0; i < _apply.Length; i++)
            {
                Shell.Result r = Shell.Netsh(_apply[i]);
                if (!r.Ok) return false;
            }
            return true;
        }

        public bool Revert()
        {
            for (int i = 0; i < _revert.Length; i++)
            {
                Shell.Netsh(_revert[i]);
            }
            return true;
        }
    }

    /// <summary>
    /// 网络协议栈精简：对全部网卡禁用非必要协议绑定（LLDP / LLTDIO / IP Helper /
    /// 响应程序 / SMB 服务端与客户端），减少后台广播与协议处理。走 PowerShell NetAdapter Binding。
    /// </summary>
    public sealed class NetBindingTweak : ITweak
    {
        private const string ComponentIds = "ms_lldp,ms_lltdio,ms_implat,ms_rspndr,ms_server,ms_msclient,ms_netbt";
        private static readonly string[] IdArray = new string[]
        {
            "ms_lldp", "ms_lltdio", "ms_implat", "ms_rspndr", "ms_server", "ms_msclient", "ms_netbt"
        };

        public string Id { get { return "net_binding_slim"; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return "网络协议栈精简（禁用发现与共享协议）"; } }
        public string Description
        {
            get
            {
                return "对全部网卡禁用 LLDP / LLTDIO / IP-Helper / 响应程序 / 微软文件共享与客户端 / NetBIOS 绑定，"
                    + "减少后台广播流量与名称解析干扰。代价：无法局域网共享文件、部分网络发现功能失效。游戏机/单机环境适用。";
            }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        private static string Ps(string script)
        {
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -Command \"" + script + "\"", 60000);
            return r.All ?? "";
        }

        public bool IsApplied()
        {
            string text = Ps("(Get-NetAdapterBinding -Name '*' -ErrorAction SilentlyContinue | " +
                "Where-Object { $_.ComponentID -in @('" +
                string.Join("','", IdArray) + "') }) | " +
                "ForEach-Object { $_.ComponentID + '=' + $_.Enabled }");
            if (string.IsNullOrEmpty(text)) return false;

            int seen = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                for (int k = 0; k < IdArray.Length; k++)
                {
                    if (!line.StartsWith(IdArray[k] + "=", StringComparison.Ordinal)) continue;
                    seen++;
                    if (!line.EndsWith("=False", StringComparison.Ordinal)) return false;
                }
            }
            return seen > 0;
        }

        public bool Apply()
        {
            Ps("Disable-NetAdapterBinding -Name '*' -ComponentID " + ComponentIds +
                " -ErrorAction SilentlyContinue");
            return true; // 部分机器无对应绑定也算完成
        }

        public bool Revert()
        {
            Ps("Enable-NetAdapterBinding -Name '*' -ComponentID " + ComponentIds +
                " -ErrorAction SilentlyContinue");
            return true;
        }
    }

    /// <summary>
    /// 设备类中断优先级：按类 GUID 设置驱动 ISR/DPC 的 BasePriority / OverTargetPriority，
    /// 网络 / 键鼠最高、显卡次之、存储音频再次（与游戏系统包的分级表一致）。
    /// </summary>
    public sealed class DevicePriorityTweak : ITweak
    {
        private sealed class Entry
        {
            public string Guid;
            public int BasePriority;
            public int OverTarget; // -1 = 删除该值（用系统默认）

            public Entry(string guid, int basePriority, int overTarget)
            {
                Guid = guid;
                BasePriority = basePriority;
                OverTarget = overTarget;
            }
        }

        private static readonly Entry[] Entries = new Entry[]
        {
            new Entry("4d36e972-e325-11ce-bfc1-08002be10318", 200, -1),  // 网络适配器
            new Entry("4d36e96b-e325-11ce-bfc1-08002be10318", 200, 64),  // 键盘
            new Entry("4d36e96f-e325-11ce-bfc1-08002be10318", 200, 80),  // 鼠标
            new Entry("745a17a0-74d3-11d0-b6fe-00a0c90f57da", 200, -1),  // 人体学输入设备
            new Entry("4d36e968-e325-11ce-bfc1-08002be10318", 255, 96),  // 显示适配器
            new Entry("50127dc3-0f36-415e-a6cc-4cb3be910b65", 28, -1),   // 处理器
            new Entry("4d36e97d-e325-11ce-bfc1-08002be10318", 28, -1),   // 系统设备
            new Entry("4d36e967-e325-11ce-bfc1-08002be10318", 22, -1),   // 磁盘驱动器
            new Entry("4d36e97b-e325-11ce-bfc1-08002be10318", 22, -1),   // 存储控制器
            new Entry("4d36e96c-e325-11ce-bfc1-08002be10318", 18, -1),   // 声音视频游戏控制器
            new Entry("4d36e973-e325-11ce-bfc1-08002be10318", 18, -1),   // 媒体类
        };

        private const string ClassRoot = @"SYSTEM\CurrentControlSet\Control\Class";

        public string Id { get { return "device_priority"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "设备中断优先级分级（网/键鼠最高）"; } }
        public string Description
        {
            get { return "按设备类别设置驱动中断优先级：网络与键鼠最高、显卡次之、存储再次，让输入与网络中断永远优先被 CPU 处理。需重启生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string PathOf(Entry e)
        {
            return ClassRoot + "\\{" + e.Guid + "}";
        }

        private static bool BaseOk(object v, int target)
        {
            try { return v != null && Convert.ToInt32(v) == target; }
            catch { return false; }
        }

        public bool IsApplied()
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry e = Entries[i];
                object baseV = RegHelper.GetValue(RegistryHive.LocalMachine, PathOf(e), "BasePriority");
                if (!BaseOk(baseV, e.BasePriority)) return false;
                object overV = RegHelper.GetValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority");
                if (e.OverTarget < 0)
                {
                    if (overV != null) return false;
                }
                else
                {
                    if (!BaseOk(overV, e.OverTarget)) return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            RegHelper.BeginBackup(Id);
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry e = Entries[i];
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, PathOf(e), "BasePriority",
                        e.BasePriority, RegistryValueKind.DWord, Id);
                    if (e.OverTarget < 0)
                    {
                        RegHelper.DeleteValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority", Id);
                    }
                    else
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority",
                            e.OverTarget, RegistryValueKind.DWord, Id);
                    }
                }
                catch
                {
                }
            }

            // 显卡实例子键（0000-0009）：与类键同步拉高
            const string GpuClass = ClassRoot + "\\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i,
                        "BasePriority", 255, RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i,
                        "OverTargetPriority", 96, RegistryValueKind.DWord, Id);
                }
                catch
                {
                }
            }

            // 显卡链路（Control\Video\{GUID}\000x，GUID 每台机器不同，动态枚举）
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Video", false))
                {
                    if (root != null)
                    {
                        string[] guids = root.GetSubKeyNames();
                        for (int g = 0; g < guids.Length; g++)
                        {
                            using (RegistryKey dev = Registry.LocalMachine.OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g], false))
                            {
                                if (dev == null) continue;
                                string[] subs = dev.GetSubKeyNames();
                                for (int s = 0; s < subs.Length; s++)
                                {
                                    if (subs[s].Length != 4 || !char.IsDigit(subs[s][0])) continue;
                                    try
                                    {
                                        RegHelper.SetValue(RegistryHive.LocalMachine,
                                            @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g] + "\\" + subs[s],
                                            "BasePriority", 255, RegistryValueKind.DWord, Id);
                                        RegHelper.SetValue(RegistryHive.LocalMachine,
                                            @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g] + "\\" + subs[s],
                                            "OverTargetPriority", 96, RegistryValueKind.DWord, Id);
                                    }
                                    catch
                                    {
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
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>
    /// BCD 引导项优化：apply/revert 走 bcdedit，probe 解析 /enum 输出（键名与值均为 ASCII）。
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

    /// <summary>
    /// 设备省电全树清零：递归扫描 Enum / Class 全部设备实例，
    /// 把所有省电 / 选择性暂停 / 唤醒相关的值统一写 0（来自 HanFly 关闭驱动省电.bat 的思路）。
    /// </summary>
    public sealed class EnumPowerSweepTweak : ITweak
    {
        private const string EnumRoot = @"SYSTEM\CurrentControlSet\Enum";
        private const string ClassRoot = @"SYSTEM\CurrentControlSet\Control\Class";
        private const string LmPrefix = "HKEY_LOCAL_MACHINE\\";

        private static readonly string[] EnumValueNames = new string[]
        {
            "EnhancedPowerManagementEnabled", "AllowIdleIrpInD3", "EnableSelectiveSuspend",
            "DeviceSelectiveSuspended", "SelectiveSuspendEnabled", "SelectiveSuspendOn",
            "WaitWakeEnabled", "D3ColdSupported", "WdfDirectedPowerTransitionEnable",
            "EnableIdlePowerManagement", "IdleInWorkingState"
        };

        private static readonly string[] ClassValueNames = new string[]
        {
            "WakeEnabled", "WdkSelectiveSuspendEnable"
        };

        public string Id { get { return "device_power_sweep"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "设备省电全树清零（Enum / Class 扫描）"; } }
        public string Description
        {
            get { return "递归扫描全部设备实例，把省电、选择性暂停、等待唤醒等参数统一写 0，比逐项开关更彻底。需重启生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        private static string RelOf(string fullPath)
        {
            return fullPath.StartsWith(LmPrefix, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(LmPrefix.Length) : fullPath;
        }

        /// <summary>write=false 时探测（发现任一非 0 即返回 false）；write=true 时写 0（带备份）。</summary>
        private static bool Walk(string path, string[] names, bool write, ref int seen, int depth)
        {
            if (depth > 10) return true;
            RegistryKey key = null;
            try { key = Registry.LocalMachine.OpenSubKey(RelOf(path), write); }
            catch { return true; }
            if (key == null) return true;

            bool ok = true;
            try
            {
                string[] existing = key.GetValueNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (Array.IndexOf(existing, names[i]) < 0) continue;
                    if (!write)
                    {
                        try
                        {
                            if (Convert.ToInt32(key.GetValue(names[i])) != 0) return false;
                            seen++;
                        }
                        catch { return false; }
                    }
                    else
                    {
                        try
                        {
                            bool isZero = false;
                            try { isZero = Convert.ToInt32(key.GetValue(names[i])) == 0; }
                            catch { }
                            if (!isZero)
                            {
                                RegHelper.SetValue(RegistryHive.LocalMachine, path, names[i], 0,
                                    RegistryValueKind.DWord, "device_power_sweep");
                            }
                            seen++;
                        }
                        catch
                        {
                        }
                    }
                }

                string[] subs;
                try { subs = key.GetSubKeyNames(); }
                catch { return ok; }
                for (int i = 0; i < subs.Length && ok; i++)
                {
                    ok = Walk(path + "\\" + subs[i], names, write, ref seen, depth + 1);
                }
            }
            finally
            {
                key.Close();
            }
            return ok;
        }

        public bool IsApplied()
        {
            int seen = 0;
            bool ok = Walk(EnumRoot, EnumValueNames, false, ref seen, 0);
            if (!ok) return false;
            ok = Walk(ClassRoot, ClassValueNames, false, ref seen, 0);
            return ok && seen > 0;
        }

        public bool Apply()
        {
            RegHelper.BeginBackup(Id);
            int seen = 0;
            Walk(EnumRoot, EnumValueNames, true, ref seen, 0);
            Walk(ClassRoot, ClassValueNames, true, ref seen, 0);
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>电源计划 PCIe 链路状态电源管理（ASPM）禁用，与 UsbSuspendTweak 同模式。</summary>
    public sealed class PciAspmTweak : ITweak
    {
        private const string PciSubgroup = "501a4d13-42af-4429-9fd1-a8218c268e20";
        private const string AspmSetting = "ee12f906-d277-404b-b6da-e5fa1a576df5";
        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        public string Id { get { return "pci_aspm_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "禁用 PCIe 链路状态电源管理 (ASPM)"; } }
        public string Description
        {
            get { return "让 PCIe 设备（显卡 / 网卡 / 硬盘）链路始终全速，消除链路休眠唤醒引入的微延迟。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string SchemeSettingPath()
        {
            object scheme = RegHelper.GetValue(RegistryHive.LocalMachine,
                UserPowerSchemes, "ActivePowerScheme");
            if (scheme == null || string.IsNullOrEmpty(scheme.ToString())) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + PciSubgroup + "\\" + AspmSetting;
        }

        public bool IsApplied()
        {
            string path = SchemeSettingPath();
            if (string.IsNullOrEmpty(path)) return false;
            object ac = RegHelper.GetValue(RegistryHive.LocalMachine, path, "ACSettingIndex");
            object dc = RegHelper.GetValue(RegistryHive.LocalMachine, path, "DCSettingIndex");
            if (ac == null || dc == null) return false;
            try { return Convert.ToInt32(ac) == 0 && Convert.ToInt32(dc) == 0; }
            catch { return false; }
        }

        public bool Apply()
        {
            object scheme = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            if (scheme == null || string.IsNullOrEmpty(scheme.ToString())) return false;
            bool ok = Shell.Run("powercfg.exe",
                "-setacvalueindex scheme_current " + PciSubgroup + " " + AspmSetting + " 0", 30000).Ok;
            ok &= Shell.Run("powercfg.exe",
                "-setdcvalueindex scheme_current " + PciSubgroup + " " + AspmSetting + " 0", 30000).Ok;
            ok &= Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
            return ok;
        }

        public bool Revert()
        {
            object scheme = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            if (scheme == null || string.IsNullOrEmpty(scheme.ToString())) return false;
            Shell.Run("powercfg.exe",
                "-setacvalueindex scheme_current " + PciSubgroup + " " + AspmSetting + " 1", 30000);
            Shell.Run("powercfg.exe",
                "-setdcvalueindex scheme_current " + PciSubgroup + " " + AspmSetting + " 1", 30000);
            return Shell.Run("powercfg.exe", "-setactive scheme_current", 30000).Ok;
        }
    }

    // ===================================================================
    // 优化项库
    // ===================================================================

    public static partial class TweakLibrary
    {
        public const string GPerformance = "性能优化";
        public const string GAppearance = "外观与体验";
        public const string GPrivacy = "隐私与安全";
        public const string GServices = "系统服务";
        public const string GPower = "电源与启动";
        public const string GGame = "游戏优化";
        public const string GNetwork = "网络优化";
        public const string GSlim = "系统精简";
        public const string GExtreme = "极限性能";

        private const string CplDesktop = @"Control Panel\Desktop";
        private const string CplDWM = @"Control Panel\Desktop\WindowMetrics";
        private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ExplorerMain = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        private static List<ITweak> _allCache;
        private static readonly List<ITweakProvider> _providers = new List<ITweakProvider>();

        /// <summary>
        /// 注册扩展优化 Provider。须在首次访问 <see cref="All"/> 之前调用（重复注册同实例会被忽略）；
        /// 注册后清空缓存，Provider 提供的项自动进入优化中心、一键推荐与方案库。
        /// </summary>
        public static void RegisterProvider(ITweakProvider provider)
        {
            if (provider == null || _providers.Contains(provider)) return;
            _providers.Add(provider);
            _allCache = null;
        }

        /// <summary>全部优化项（内置分组 + 已注册 Provider；构建一次后缓存。调用方不得修改返回列表）。</summary>
        public static List<ITweak> All()
        {
            if (_allCache != null) return _allCache;
            List<ITweak> list = new List<ITweak>();
            list.AddRange(Game());
            list.AddRange(Network());
            list.AddRange(Performance());
            list.AddRange(Power());
            list.AddRange(Privacy());
            list.AddRange(Slim());
            list.AddRange(Appearance());
            list.AddRange(Services());
            list.AddRange(Extreme());
            for (int i = 0; i < _providers.Count; i++)
            {
                list.AddRange(_providers[i].Provide());
            }
            _allCache = list;
            return list;
        }

        // ---------------- 极限性能（安全让位：本组会削弱系统防护，仅供离线/专用游戏机） ----------------

        private static IEnumerable<ITweak> Extreme()
        {
            List<ITweak> list = new List<ITweak>();

            // 1. 内核漏洞利用缓解全关
            RegTweak mitigation = new RegTweak();
            mitigation.IdValue = "mitigation_off";
            mitigation.GroupValue = GExtreme;
            mitigation.NameValue = "关闭内核漏洞利用缓解";
            mitigation.DescriptionValue = "关闭 SEHOP、异常链校验、CFG 导出/XFG 抑制与用户态缓解策略，消除这些缓解措施在每个进程/每次调用上的开销。代价：内核漏洞利用防护大幅下降，请勿在此状态下浏览不可信网站或运行来路不明程序。需重启生效。";
            mitigation.AdminOnlyValue = true;
            mitigation.RiskyValue = true;
            const string Kernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            mitigation.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, Kernel,
                "MitigationOptions", "0000000000000000000000000000000000000000000000000000000000000000"));
            mitigation.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, Kernel,
                "MitigationAuditOptions", "0000000000000000000000000000000000000000000000000000000000000000"));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "KernelSEHOPEnabled", 0));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableExceptionChainValidation", 1));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableControlFlowGuardXfg", 1));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableControlFlowGuardExportSuppression", 1));
            list.Add(mitigation);

            // 2. 漏洞驱动黑名单关闭
            RegTweak vuln = new RegTweak();
            vuln.IdValue = "vuln_blocklist_off";
            vuln.GroupValue = GExtreme;
            vuln.NameValue = "关闭漏洞驱动黑名单";
            vuln.DescriptionValue = "微软维护的已知漏洞驱动（容易被利用读内核内存）黑名单不再强制拦截。部分底层工具（RWEverything 类、IMOD 修改）需要它才能加载驱动。代价：恶意驱动更容易被加载。需重启生效。";
            vuln.AdminOnlyValue = true;
            vuln.RiskyValue = true;
            vuln.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable", 0));
            list.Add(vuln);

            // 3. BCD 安全机制精简
            BcdTweak bcdSec = new BcdTweak("bcd_security_off",
                "BCD 安全机制精简（DEP / 完整性 / ELAM，谨慎）",
                "DEP 设为 AlwaysOff、关闭完整性检查与早期反恶意软件（ELAM）驱动、关闭隔离上下文与 TPM 启动熵。消除 DEP 检查与安全启动链的开销。代价：恶意代码防护大幅下降，仅建议离线/专用游戏机使用。重启生效。",
                new string[]
                {
                    "/set nx AlwaysOff",
                    "/set nointegritychecks yes",
                    "/set disableelamdrivers yes",
                    "/set isolatedcontext no",
                    "/set tpmbootentropy ForceDisable"
                },
                new string[]
                {
                    "/set nx OptIn",
                    "/deletevalue nointegritychecks",
                    "/deletevalue disableelamdrivers",
                    "/deletevalue isolatedcontext",
                    "/deletevalue tpmbootentropy"
                },
                new string[] { @"nx\s+AlwaysOff", @"nointegritychecks\s+Yes" },
                new string[] { @"nx\s+OptIn" },
                true);
            bcdSec.GroupOverride = GExtreme;
            list.Add(bcdSec);

            // 4. Defender 全家桶禁用
            RegTweak defender = new RegTweak();
            defender.IdValue = "defender_off";
            defender.GroupValue = GExtreme;
            defender.NameValue = "禁用 Windows Defender 全家桶";
            defender.DescriptionValue = "停止 Defender 防病毒、实时监控、网络检查、安全中心与 WdFilter/WdBoot 内核驱动，并关闭 SmartScreen 与示例上报。游戏加载与进程创建不再被实时扫描拖慢——代价：失去全部系统级病毒防护，请配合本工具「安全检查」页或第三方方案。需重启生效。";
            defender.AdminOnlyValue = true;
            defender.RiskyValue = true;
            string[] defServices = new string[] { "WinDefend", "WdNisSvc", "Sense", "wscsvc" };
            for (int i = 0; i < defServices.Length; i++)
            {
                defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + defServices[i], "Start", 4));
            }
            string[] defDrivers = new string[] { "WdFilter", "WdBoot", "WdNisDrv" };
            for (int i = 0; i < defDrivers.Length; i++)
            {
                defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + defDrivers[i], "Start", 4));
            }
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", 1));
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Defender\Spynet", "SpyNetReporting", 0));
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", 0));
            defender.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off"));
            list.Add(defender);

            // 5. UAC 完全关闭
            RegTweak uac = new RegTweak();
            uac.IdValue = "uac_off";
            uac.GroupValue = GExtreme;
            uac.NameValue = "关闭用户账户控制 (UAC)";
            uac.DescriptionValue = "EnableLUA=0 完全关闭 UAC（不再弹提权确认、不再拆分令牌），部分老游戏与工具在非拆分令牌下表现更好。代价：所有程序默认以完整管理员权限运行。需重启生效。";
            uac.AdminOnlyValue = true;
            uac.RiskyValue = true;
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0));
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0));
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0));
            list.Add(uac);

            // 6. GPU 超时检测关闭（TDR）
            RegTweak tdr = new RegTweak();
            tdr.IdValue = "tdr_off";
            tdr.GroupValue = GExtreme;
            tdr.NameValue = "关闭 GPU 超时检测与恢复 (TDR)";
            tdr.DescriptionValue = "TdrLevel=0：显卡驱动不再因单帧计算超时被系统重置，超长编译的 shader 与重度负载不再触发黑屏闪退。代价：显卡真死机时系统不会自动恢复（直接黑屏，只能重启）。需重启生效。";
            tdr.AdminOnlyValue = true;
            tdr.RiskyValue = true;
            tdr.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrLevel", 0));
            list.Add(tdr);

            // 8. Defender 删除级（备份服务键后 sc delete）
            list.Add(new DefenderRemoveTweak());

            // 9. 防火墙全禁
            RegTweak fw = new RegTweak();
            fw.IdValue = "firewall_off";
            fw.GroupValue = GExtreme;
            fw.NameValue = "禁用 Windows 防火墙（全 Profile）";
            fw.DescriptionValue = "关闭域/专用/公用三个 Profile 的防火墙，并禁用防火墙服务（mpssvc）——省去每个网络连接的过滤规则匹配开销。代价：入站出站全部不设防，仅建议有路由器 NAT 保护的家用环境使用。还原时自动回写原状态。";
            fw.AdminOnlyValue = true;
            fw.RiskyValue = true;
            string[] profiles = new string[] { "Domain Profile", "Standard Profile", "Public Profile" };
            for (int i = 0; i < profiles.Length; i++)
            {
                fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + profiles[i], "EnableFirewall", 0));
                fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profiles[i],
                    "EnableFirewall", 0));
            }
            fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mpssvc", "Start", 4));
            list.Add(fw);

            // 10. GPU 中断优先级提升（动态查询 IRQ）
            list.Add(new GpuIrqPriorityTweak());

            return list;
        }

        /// <summary>
        /// Defender 删除级：关闭篡改保护 → 禁用计划任务 → 备份并删除 Defender 服务与内核驱动注册表键 →
        /// 移除安全中心 UWP。还原时从备份重建服务键（完整恢复，无需系统更新）。
        /// </summary>
        public sealed class DefenderRemoveTweak : ITweak
        {
            private static readonly string[] ServiceNames = new string[]
            {
                "WinDefend", "WdNisSvc", "Sense", "WdFilter", "WdBoot", "WdNisDrv"
            };
            private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";
            private const string BackupRoot = @"SOFTWARE\SysToolbox\Backup\defender_remove\Services";
            private const string TasksPath = @"\Microsoft\Windows\Windows Defender\";

            public string Id { get { return "defender_remove"; } }
            public string Group { get { return TweakLibrary.GExtreme; } }
            public string Name { get { return "删除 Windows Defender（服务 / 驱动 / 任务 / UI）"; } }
            public string Description
            {
                get { return "删除级卸载：先关篡改保护，再禁用全部 Defender 计划任务，备份后删除 4 个服务与 3 个内核驱动的注册表键，并移除安全中心 UWP——进程创建、文件读写完全不再经过任何扫描钩子（比禁用更彻底，残留为 0）。还原时从备份完整重建服务键。建议先做系统备份；需要杀毒时还原本项即可。"; }
            }
            public bool AdminOnly { get { return true; } }
            public bool Risky { get { return true; } }
            public bool Recommended { get { return false; } }

            private static void CopyKey(Microsoft.Win32.RegistryKey src, Microsoft.Win32.RegistryKey dst)
            {
                foreach (string name in src.GetValueNames())
                {
                    try { dst.SetValue(name, src.GetValue(name, null), src.GetValueKind(name)); }
                    catch { }
                }
                foreach (string sub in src.GetSubKeyNames())
                {
                    try
                    {
                        using (Microsoft.Win32.RegistryKey s = src.OpenSubKey(sub))
                        using (Microsoft.Win32.RegistryKey d = dst.CreateSubKey(sub))
                        {
                            CopyKey(s, d);
                        }
                    }
                    catch { }
                }
            }

            private static void BackupService(string name)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey src = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(ServicesRoot + "\\" + name, false))
                    {
                        if (src == null) return; // 已删除，无需备份
                        using (Microsoft.Win32.RegistryKey dst = Microsoft.Win32.Registry.LocalMachine
                            .CreateSubKey(BackupRoot + "\\" + name))
                        {
                            CopyKey(src, dst);
                        }
                    }
                }
                catch { }
            }

            private static void RestoreService(string name)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey src = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(BackupRoot + "\\" + name, false))
                    {
                        if (src == null) return;
                        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(ServicesRoot + "\\" + name, false);
                        using (Microsoft.Win32.RegistryKey dst = Microsoft.Win32.Registry.LocalMachine
                            .CreateSubKey(ServicesRoot + "\\" + name))
                        {
                            CopyKey(src, dst);
                        }
                    }
                }
                catch { }
            }

            private static void RunTasks(bool disable)
            {
                string[] tasks = new string[]
                {
                    "Scheduled Scan", "Cache Maintenance", "Cleanup", "Verification"
                };
                for (int i = 0; i < tasks.Length; i++)
                {
                    string verb = disable ? "/disable" : "/enable";
                    Shell.Run("schtasks.exe", "/Change /TN \"" + TasksPath + tasks[i] + "\" " + verb, 30000);
                }
            }

            public bool IsApplied()
            {
                // 主服务键已不存在即视为已删除
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(ServicesRoot + "\\" + ServiceNames[0], false))
                {
                    return k == null;
                }
            }

            public bool Apply()
            {
                // 前置：关闭篡改保护
                try
                {
                    Microsoft.Win32.Registry.SetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Features",
                        "TamperProtection", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }

                RunTasks(true);

                bool ok = true;
                for (int i = 0; i < ServiceNames.Length; i++)
                {
                    BackupService(ServiceNames[i]);
                    Shell.Result r = Shell.Run("sc.exe", "delete " + ServiceNames[i], 30000);
                    if (!r.Ok) ok = false;
                }

                // 移除安全中心 UWP（尽力而为）
                Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Get-AppxPackage *SecHealthUI* | Remove-AppxPackage\"", 120000);

                return ok;
            }

            public bool Revert()
            {
                bool ok = true;
                for (int i = 0; i < ServiceNames.Length; i++)
                {
                    RestoreService(ServiceNames[i]);
                    // 确认键回来了
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(ServicesRoot + "\\" + ServiceNames[i], false))
                    {
                        if (k == null) ok = false;
                    }
                }
                RunTasks(false);

                // 安全中心 UWP 重新注册（尽力而为）
                Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Get-AppxPackage -AllUsers *SecHealthUI* | ForEach-Object { Add-AppxPackage -Register ($_.InstallLocation + '\\AppxManifest.xml') -DisableDevelopmentMode }\"",
                    120000);
                return ok;
            }
        }

        /// <summary>
        /// GPU 中断优先级：动态查询显卡当前 IRQ 号，把对应 IRQnPriority 设为 High，
        /// 显卡中断优先于其他设备被 CPU 处理（单 GPU 平台收益最明显）。
        /// </summary>
        public sealed class GpuIrqPriorityTweak : ITweak
        {
            public string Id { get { return "gpu_irq_priority"; } }
            public string Group { get { return TweakLibrary.GExtreme; } }
            public string Name { get { return "显卡中断优先级提升"; } }
            public string Description
            {
                get { return "自动查询显卡占用的 IRQ 号，把该中断的处理优先级提到 High，渲染中断插队其他设备。多 GPU 或共享 IRQ 平台收益有限。需重启生效。"; }
            }
            public bool AdminOnly { get { return true; } }
            public bool Risky { get { return true; } }
            public bool Recommended { get { return false; } }

            private static string GpuIrq()
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"(gwmi -q 'select * from Win32_PnPAllocatedResource' | Where-Object {$_.Dependent -like '*Win32_IRQResource*' -and $_.Antecedent} | Where-Object {$_.Dependent -match 'Display'} | Select-Object -First 1).Antecedent.Split('=')[-1]\"",
                    60000);
                string s = (r.All ?? "").Trim();
                if (s.Length == 0 || !char.IsDigit(s[0])) return "";
                return s;
            }

            private static string KeyPath(string irq)
            {
                return @"SYSTEM\CurrentControlSet\Control\PriorityControl";
            }

            public bool IsApplied()
            {
                string irq = GpuIrq();
                if (irq.Length == 0) return false;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", "IRQ" + irq + "Priority");
                try { return v != null && Convert.ToInt32(v) == 1; }
                catch { return false; }
            }

            public bool Apply()
            {
                string irq = GpuIrq();
                if (irq.Length == 0) return false;
                RegHelper.BeginBackup(Id);
                return RegHelper.SetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", "IRQ" + irq + "Priority", 1,
                    RegistryValueKind.DWord, Id);
            }

            public bool Revert()
            {
                return RegHelper.Restore(Id);
            }
        }

        // ---------------- 系统精简（砍掉普通用户用不到的后台组件） ----------------

        private static IEnumerable<ITweak> Slim()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak consumer = new RegTweak();
            consumer.IdValue = "consumer_content_off";
            consumer.GroupValue = GSlim;
            consumer.NameValue = "关闭推送的建议与预装内容";
            consumer.DescriptionValue = "关闭开始菜单建议、锁屏聚焦广告、静默安装的推广应用（含任务栏资讯兴趣流）与「Windows 体验」弹窗——砍掉 ContentDeliveryManager 的全部推送通道（原「阻止静默安装推荐应用」「关闭系统推广与任务栏资讯」已并入本项）。";
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "ContentDeliveryAllowed", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "OemPreInstalledAppsEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0));
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353698Enabled", 0));
            // 任务栏资讯兴趣流（原「关闭系统推广与任务栏资讯」的独有键，该项已并入）
            consumer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarEnabled", 0));
            list.Add(consumer);

            RegTweak copilot = new RegTweak();
            copilot.IdValue = "taskbar_bloat_off";
            copilot.GroupValue = GSlim;
            copilot.NameValue = "移除任务栏 Copilot / 小组件 / 聊天";
            copilot.DescriptionValue = "隐藏 Win11 任务栏上的 Copilot 按钮、小组件与聊天入口，砍掉对应后台进程的常驻加载，任务栏更干净。";
            copilot.AdminOnlyValue = true;
            // TurnOffWindowsCopilot 策略键由 copilot_off 负责，此处只管任务栏入口
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0));
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0));
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarChat", 0));
            list.Add(copilot);

            RegTweak cortana = new RegTweak();
            cortana.IdValue = "cortana_off";
            cortana.GroupValue = GSlim;
            cortana.NameValue = "禁用 Cortana 语音助手";
            cortana.DescriptionValue = "国内环境基本用不到 Cortana，禁用后不再随系统常驻。需要语音助手时还原即可。";
            cortana.AdminOnlyValue = true;
            cortana.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0));
            list.Add(cortana);

            RegTweak dod = new RegTweak();
            dod.IdValue = "delivery_optimization_off";
            dod.GroupValue = GSlim;
            dod.NameValue = "关闭更新传递优化（P2P 上传）";
            dod.DescriptionValue = "传递优化会把已下载的更新分片上传给局域网/互联网其他电脑，白白占用上传带宽与磁盘 I/O。关闭后更新只从微软源下载。";
            dod.AdminOnlyValue = true;
            dod.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0));
            list.Add(dod);

            RegTweak firstLogon = new RegTweak();
            firstLogon.IdValue = "first_logon_anim_off";
            firstLogon.GroupValue = GSlim;
            firstLogon.NameValue = "跳过首次登录动画";
            firstLogon.DescriptionValue = "新账户/大版本更新后的首次登录「嗨，正在为你准备」全屏动画与引导页直接跳过，进桌面更快。";
            firstLogon.AdminOnlyValue = true;
            firstLogon.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableFirstLogonAnimation", 0));
            list.Add(firstLogon);

            RegTweak ioCount = new RegTweak();
            ioCount.IdValue = "io_counting_off";
            ioCount.GroupValue = GSlim;
            ioCount.NameValue = "关闭 I/O 操作计数";
            ioCount.DescriptionValue = "内核不再为每次文件读写维护计数器（任务管理器等工具的 I/O 列将显示为 0），高 I/O 场景减少一点内核开销。";
            ioCount.AdminOnlyValue = true;
            ioCount.RiskyValue = true;
            ioCount.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\I/O System", "CountOperations", 0));
            list.Add(ioCount);

            RegTweak wpbt = new RegTweak();
            wpbt.IdValue = "wpbt_off";
            wpbt.GroupValue = GSlim;
            wpbt.NameValue = "禁用 WPBT 固件启动执行";
            wpbt.DescriptionValue = "WPBT 是主板固件里的 ACPI 表，允许 OEM 在每次开机时静默执行程序（常见于预装推广软件）。禁用后固件无法再借它塞私货（Atlas OS 官方方案）。少数商务机型依赖它做反盗，异常时还原即可。";
            wpbt.AdminOnlyValue = true;
            wpbt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", 1));
            list.Add(wpbt);

            RegTweak crash = new RegTweak();
            crash.IdValue = "crash_control_qol";
            crash.GroupValue = GSlim;
            crash.NameValue = "蓝屏体验优化（不自动重启）";
            crash.DescriptionValue = "蓝屏时停留在错误画面显示详细参数（方便拍照查错），不再自动重启，也不再生成绝大多数人不会看的转储文件。需要保留崩溃转储做调试的话请勿开启。";
            crash.AdminOnlyValue = true;
            crash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0));
            crash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "CrashDumpEnabled", 0));
            crash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "LogEvent", 0));
            crash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CrashControl", "DisplayParameters", 1));
            crash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CrashControl\StorageTelemetry", "DeviceDumpEnabled", 0));
            list.Add(crash);

            RegTweak wer = new RegTweak();
            wer.IdValue = "error_reporting_off";
            wer.GroupValue = GSlim;
            wer.NameValue = "禁用 Windows 错误报告";
            wer.DescriptionValue = "程序崩溃后不再生成并发送 Watson 错误报告（Fault bucket / 上传队列），省去报告进程启动与磁盘写入，也避免崩溃后多等几秒。";
            wer.AdminOnlyValue = true;
            wer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1));
            wer.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1));
            list.Add(wer);

            RegTweak iris = new RegTweak();
            iris.IdValue = "start_recommended_off";
            iris.GroupValue = GSlim;
            iris.NameValue = "隐藏开始菜单推荐区";
            iris.DescriptionValue = "去掉 Win11 开始菜单下方「推荐的项目」（最近文件与推广内容），菜单更紧凑，也不再持续扫描最近活动。";
            iris.AdminOnlyValue = false;
            iris.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer", "Start_IrisRecommendations", 0));
            iris.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_IrisRecommendations", 0));
            list.Add(iris);

            return list;
        }

        // ---------------- 网络优化（吸收自社区游戏网络调整方案） ----------------

        private static IEnumerable<ITweak> Performance()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak menuAnim = new RegTweak();
            menuAnim.IdValue = "menu_anim";
            menuAnim.GroupValue = GPerformance;
            menuAnim.NameValue = "关闭窗口与菜单动画";
            menuAnim.DescriptionValue = "关闭任务栏动画、菜单淡入淡出和窗口最小化动画，并把菜单弹出延迟清零（原「悬停弹出加速」已并入本项）。界面响应更干脆，不影响游戏帧数。";
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDesktop, "MenuShowDelay", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDWM, "MinAnimate", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "TaskbarAnimations", 0));
            list.Add(menuAnim);

            RegTweak vfx = new RegTweak();
            vfx.IdValue = "visual_fx";
            vfx.GroupValue = GPerformance;
            vfx.NameValue = "视觉效果调整为最佳性能";
            vfx.DescriptionValue = "关闭窗口阴影、透明效果与动画，优先保证系统流畅度。桌面观感会变朴素；游戏帧数不受影响，纯手感向。";
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerMain, "VisualFXSetting", 2));
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0));
            list.Add(vfx);

            // Win32PrioritySeparation 三档（同键多档，共享备份 ID，可互相切换、都还原到系统默认）
            RegTweak fore = new RegTweak();
            fore.IdValue = "win32_priority_game";
            fore.BackupIdValue = "win32_priority";
            fore.GroupValue = GPerformance;
            fore.NameValue = "CPU 调度：游戏推荐档 (38)";
            fore.DescriptionValue = "Win32PrioritySeparation=0x26：短量子 + 可变量子 + 高前台提升。前台游戏获得明显优先权，输入响应与帧间隔更平滑。社区最广泛推荐的游戏值。重启后生效。";
            fore.AdminOnlyValue = true;
            fore.RecommendedValue = true;
            fore.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38));
            list.Add(fore);

            RegTweak foreMulti = new RegTweak();
            foreMulti.IdValue = "win32_priority_multi";
            foreMulti.BackupIdValue = "win32_priority";
            foreMulti.GroupValue = GPerformance;
            foreMulti.NameValue = "CPU 调度：多开后台档 (24)";
            foreMulti.DescriptionValue = "Win32PrioritySeparation=0x18：长量子 + 固定 + 无前台提升。所有进程公平分配 CPU，适合挂机下载、渲染、开服务器等多开场景。重启后生效。";
            foreMulti.AdminOnlyValue = true;
            foreMulti.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 24));
            list.Add(foreMulti);

            RegTweak foreDefault = new RegTweak();
            foreDefault.IdValue = "win32_priority_default";
            foreDefault.BackupIdValue = "win32_priority";
            foreDefault.GroupValue = GPerformance;
            foreDefault.NameValue = "CPU 调度：系统默认档 (2)";
            foreDefault.DescriptionValue = "Win32PrioritySeparation=2：恢复 Windows 默认，由系统按「处理器计划」设置自动解析。不折腾时的稳妥选择。重启后生效。";
            foreDefault.AdminOnlyValue = true;
            foreDefault.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2));
            list.Add(foreDefault);

            // SystemResponsiveness（MMCSS 后台保留配额）
            // 注意：该配额最低有效值为 10（设 0 不会生效为 0%，系统按默认处理）——
            // 早期的「极致档 (0)」是无效项，已按实际生效边界移除。
            RegTweak resp10 = new RegTweak();
            resp10.IdValue = "system_responsiveness_10";
            resp10.BackupIdValue = "system_responsiveness";
            resp10.GroupValue = GGame;
            resp10.NameValue = "MMCSS 后台配额：游戏推荐档 (10)";
            resp10.DescriptionValue = "SystemResponsiveness=10：后台保留配额从默认 20% 减到 10%（该配额的最低有效值就是 10，无法设为 0），把更多调度余量让给前台游戏，兼顾后台基本流畅。多数玩家的推荐值。";
            resp10.AdminOnlyValue = true;
            resp10.RecommendedValue = true;
            resp10.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10));
            list.Add(resp10);

            RegTweak ntfs = new RegTweak();
            ntfs.IdValue = "ntfs_lastaccess";
            ntfs.GroupValue = GPerformance;
            ntfs.NameValue = "NTFS 访问加速（关闭访问时间与 8.3 短名）";
            ntfs.DescriptionValue = "关闭 NTFS 上次访问时间记录并禁用 8.3 短文件名生成，减少磁盘无谓 I/O 与目录枚举开销（老旧 16 位程序可能依赖短名）。重启后生效。";
            ntfs.AdminOnlyValue = true;
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1));
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1));
            list.Add(ntfs);

            RegTweak startupDelay = new RegTweak();
            startupDelay.IdValue = "startup_delay_zero";
            startupDelay.GroupValue = GPerformance;
            startupDelay.NameValue = "取消开机启动项延迟";
            startupDelay.DescriptionValue = "Windows 默认在开机后依次延迟启动应用，清零后启动项立即加载，进入游戏桌面更快就绪。";
            startupDelay.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0));
            list.Add(startupDelay);

            RegTweak fastShutdown = new RegTweak();
            fastShutdown.IdValue = "fast_shutdown";
            fastShutdown.GroupValue = GPerformance;
            fastShutdown.NameValue = "加快关机与注销速度";
            fastShutdown.DescriptionValue = "缩短系统等待应用退出的超时并自动结束无响应程序。未保存的工作可能丢失，请先保存再关机。";
            fastShutdown.AdminOnlyValue = true;
            fastShutdown.RiskyValue = true;
            fastShutdown.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                CplDesktop, "AutoEndTasks", 1));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "WaitToKillAppTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "LowLevelHooksTimeout", "1000"));
            fastShutdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0));
            list.Add(fastShutdown);

            RegTweak hover = new RegTweak();
            hover.IdValue = "mouse_hover_fast";
            hover.GroupValue = GPerformance;
            hover.NameValue = "鼠标悬停提示加速";
            hover.DescriptionValue = "悬停缩略图与提示信息的等待时间从 400ms 降到 10ms，浏览文件更跟手。";
            hover.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseHoverTime", "10"));
            list.Add(hover);

            RegTweak ntfsGaming = new RegTweak();
            ntfsGaming.IdValue = "ntfs_gaming";
            ntfsGaming.GroupValue = GPerformance;
            ntfsGaming.NameValue = "NTFS 内存与 MFT 优化";
            ntfsGaming.DescriptionValue = "加大文件系统元数据缓存（NtfsMemoryUsage=2）并扩大 MFT 保留区，大量小文件读写（游戏库）更顺。需重启生效。";
            ntfsGaming.AdminOnlyValue = true;
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 2));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMftZoneReservation", 4));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsQuotaNotifyRate", 36000));
            list.Add(ntfsGaming);

            RegTweak maintOff = new RegTweak();
            maintOff.IdValue = "maintenance_off";
            maintOff.GroupValue = GPerformance;
            maintOff.NameValue = "禁用系统自动维护与容错堆";
            maintOff.DescriptionValue = "关闭后台自动维护计划与容错堆 (FTH)，消除空闲时段的自动扫描/修复活动。";
            maintOff.AdminOnlyValue = true;
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "WakeUp", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\ScheduledDiagnostics", "EnabledExecution", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\EnergyEstimation\TaggedEnergy", "DisableTaggedEnergyLogging", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\EnergyEstimation\TaggedEnergy", "TelemetryMaxApplication", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "SleepStudyDisabled", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Reliability", "TimeStampInterval", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\FTH", "Enabled", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\NvCache", "OptimizeBootAndResume", 1));
            list.Add(maintOff);

            list.Add(new SvchostSplitTweak());

            CommandTweak memCompress = new CommandTweak();
            memCompress.IdValue = "mem_compression_off";
            memCompress.GroupValue = GPerformance;
            memCompress.NameValue = "禁用内存压缩";
            memCompress.DescriptionValue = "关闭系统的内存压缩存储，省下压缩/解压的 CPU 开销（代价是相同内容占更多物理内存）。16GB 以上内存推荐。";
            memCompress.EnableFile = "powershell.exe";
            memCompress.EnableArgs = "-NoProfile -Command \"Disable-MMAgent -MemoryCompression\"";
            memCompress.RevertFile = "powershell.exe";
            memCompress.RevertArgs = "-NoProfile -Command \"Enable-MMAgent -MemoryCompression\"";
            memCompress.Probe = delegate
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Write-Output ((Get-MMAgent).MemoryCompression)\"", 60000);
                string text = r.All ?? "";
                return text.IndexOf("False", StringComparison.Ordinal) >= 0;
            };
            list.Add(memCompress);

            return list;
        }

        // ---------------- 游戏与硬件 ----------------

        private static IEnumerable<ITweak> Game()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak gameMode = new RegTweak();
            gameMode.IdValue = "game_mode";
            gameMode.GroupValue = GGame;
            gameMode.NameValue = "启用游戏模式";
            gameMode.DescriptionValue = "让 Windows 在运行游戏时优先分配 CPU 与 GPU 资源，抑制后台更新与通知。对全屏游戏收益最明显；与「关闭游戏栏」搭配使用无冲突。";
            gameMode.AdminOnlyValue = true;
            gameMode.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", 1));
            list.Add(gameMode);

            RegTweak gameBar = new RegTweak();
            gameBar.IdValue = "game_bar_off";
            gameBar.GroupValue = GGame;
            gameBar.NameValue = "关闭 Xbox 游戏栏";
            gameBar.DescriptionValue = "禁用 Win+G 游戏栏覆盖层，避免游戏时弹出 ms-gamingoverlay 等干扰窗口。";
            gameBar.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0));
            list.Add(gameBar);

            RegTweak gameDvr = new RegTweak();
            gameDvr.IdValue = "game_dvr_off";
            gameDvr.GroupValue = GGame;
            gameDvr.NameValue = "关闭游戏后台录制 (Game DVR)";
            gameDvr.DescriptionValue = "停止后台录制，降低显存占用与磁盘写入；使用其它录制软件（如 OBS）时建议关闭。";
            gameDvr.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_Enabled", 0));
            gameDvr.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
            list.Add(gameDvr);

            RegTweak hags = new RegTweak();
            hags.IdValue = "hags_on";
            hags.GroupValue = GGame;
            hags.NameValue = "启用硬件加速 GPU 调度";
            hags.DescriptionValue = "让 GPU 直接管理显存调度，降低延迟、提升帧稳定性。需要重启后生效。";
            hags.AdminOnlyValue = true;
            hags.RiskyValue = true;
            hags.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2));
            list.Add(hags);

            RegTweak mouseAccel = new RegTweak();
            mouseAccel.IdValue = "mouse_accel_off";
            mouseAccel.GroupValue = GGame;
            mouseAccel.NameValue = "关闭鼠标指针加速度";
            mouseAccel.DescriptionValue = "关闭“提高指针精确度”，让鼠标移动与物理位移 1:1，FPS 游戏瞄准更稳定。";
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseSpeed", 0));
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseThreshold1", 0));
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseThreshold2", 0));
            list.Add(mouseAccel);

            // —— 以下为低延迟游戏系统的安全向键值项 ——

            RegTweak mmcss = new RegTweak();
            mmcss.IdValue = "mmcss_gaming";
            mmcss.GroupValue = GGame;
            mmcss.NameValue = "系统调度偏向游戏 (MMCSS)";
            mmcss.DescriptionValue = "关闭网络节流并把游戏线程的调度优先级调高（SystemResponsiveness 由「MMCSS 后台配额」档位项单独控制）。";
            mmcss.AdminOnlyValue = true;
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "GPU Priority", 8));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "Priority", 6));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "Scheduling Category", "High"));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "SFIO Priority", "High"));
            // 游戏任务低延迟三件套（NoLazyMode / Latency Sensitive / Clock Rate）
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "AlwaysOn", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "Latency Sensitive", "True"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Games", "Clock Rate", 5000));
            list.Add(mmcss);

            RegTweak dynTicks = new RegTweak();
            dynTicks.IdValue = "dynamic_ticks_off";
            dynTicks.GroupValue = GGame;
            dynTicks.NameValue = "禁用动态时钟 (Dynamic Ticks)";
            dynTicks.DescriptionValue = "让内核时钟始终全速跳动，配合高精度定时器消除 tick 省电导致的微卡顿。需重启生效。";
            dynTicks.AdminOnlyValue = true;
            dynTicks.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "DisableDynamicTicks", 1));
            list.Add(dynTicks);

            RegTweak d3dLow = new RegTweak();
            d3dLow.IdValue = "d3d_low_latency";
            d3dLow.GroupValue = GGame;
            d3dLow.NameValue = "全局低帧延迟并强制关闭垂直同步（旧 3D 游戏）";
            d3dLow.DescriptionValue = "把 Direct3D 全局最大帧延迟设为 1、PresentInterval=0，降低旧 D3D 游戏的输入延迟。代价：画面可能撕裂，需要垂直同步的游戏勿开。";
            d3dLow.AdminOnlyValue = true;
            d3dLow.RiskyValue = true;
            d3dLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "MaxFrameLatency", 1));
            d3dLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "PresentInterval", 0));
            list.Add(d3dLow);

            RegTweak dwmLow = new RegTweak();
            dwmLow.IdValue = "dwm_low_latency";
            dwmLow.GroupValue = GGame;
            dwmLow.NameValue = "DWM 立即翻转与最小排队缓冲";
            dwmLow.DescriptionValue = "让桌面合成器立即翻转到屏幕并把排队缓冲降为 1，减少合成环节引入的一帧延迟。";
            dwmLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\DWM", "UseImmediateFlips", 1));
            dwmLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\DWM", "MaxQueuedPresentBuffers", 1));
            list.Add(dwmLow);

            RegTweak preempt = new RegTweak();
            preempt.IdValue = "gpu_preempt_short";
            preempt.GroupValue = GGame;
            preempt.NameValue = "缩短 GPU 抢占超时";
            preempt.DescriptionValue = "把图形调度器的抢占超时从 8 降到 1，GPU 被单个负载长时间占用时更快让位，全屏游戏后台响应更及时。";
            preempt.AdminOnlyValue = true;
            preempt.RiskyValue = true;
            preempt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "PreemptTimeout", 1));
            list.Add(preempt);

            RegTweak meltdown = new RegTweak();
            meltdown.IdValue = "meltdown_off";
            meltdown.GroupValue = GGame;
            meltdown.NameValue = "关闭 Meltdown / Spectre 缓解";
            meltdown.DescriptionValue = "关闭 CPU 侧信道漏洞缓解，游戏帧数与内存性能常见提升 5~15%，需重启生效。⚠ 安全代价极大：内核数据保护被解除，仅建议完全离线或纯游戏机使用，联网办公机勿开。";
            meltdown.AdminOnlyValue = true;
            meltdown.RiskyValue = true;
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettings", 1));
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 3));
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverrideMask", 3));
            list.Add(meltdown);

            RegTweak mpoOff = new RegTweak();
            mpoOff.IdValue = "mpo_off";
            mpoOff.GroupValue = GGame;
            mpoOff.NameValue = "禁用 MPO（多平面叠加）";
            mpoOff.DescriptionValue = "官方级修复：解决部分显卡驱动下全屏游戏闪烁、掉帧、鼠标卡顿的问题（OverlayTestMode=5），NVIDIA 曾在官方说明中推荐。";
            mpoOff.AdminOnlyValue = true;
            mpoOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5));
            list.Add(mpoOff);

            RegTweak powerThrottle = new RegTweak();
            powerThrottle.IdValue = "power_throttling_off";
            powerThrottle.GroupValue = GGame;
            powerThrottle.NameValue = "关闭电源节流 (EcoQoS)";
            powerThrottle.DescriptionValue = "禁止系统把后台线程调度到能效核或降频执行（PowerThrottlingOff=1），带鱼屏/大小核 CPU 上后台任务不再干扰游戏线程。";
            powerThrottle.AdminOnlyValue = true;
            powerThrottle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1));
            // 根键双保险（Juxic 电源方案：部分版本读 Control\Power 下的同名值）
            powerThrottle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "PowerThrottlingOff", 1));
            list.Add(powerThrottle);

            RegTweak cpuQuota = new RegTweak();
            cpuQuota.IdValue = "cpu_quota_off";
            cpuQuota.GroupValue = GGame;
            cpuQuota.NameValue = "关闭 CPU 配额节流";
            cpuQuota.DescriptionValue = "禁用系统对后台进程的 CPU 配额限制（Quota System），游戏帧生成不再被调度器限流。";
            cpuQuota.AdminOnlyValue = true;
            cpuQuota.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Quota System", "EnableCpuQuota", 0));
            list.Add(cpuQuota);

            RegTweak driverUpdate = new RegTweak();
            driverUpdate.IdValue = "driver_update_off";
            driverUpdate.GroupValue = GGame;
            driverUpdate.NameValue = "禁止 Windows 更新自动装驱动";
            driverUpdate.DescriptionValue = "阻止 Windows Update 自动下载安装驱动，防止显卡/主板驱动被悄悄替换。驱动请从官方渠道手动安装。";
            driverUpdate.AdminOnlyValue = true;
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DriverSearching", "DriverUpdateWizardWuSearchEnabled", 0));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1));
            list.Add(driverUpdate);

            RegTweak pcaOff = new RegTweak();
            pcaOff.IdValue = "pca_off";
            pcaOff.GroupValue = GGame;
            pcaOff.NameValue = "关闭程序兼容性助手";
            pcaOff.DescriptionValue = "停止 PCA 在后台检测与提示程序兼容性问题，减少游戏时的干扰与后台扫描。";
            pcaOff.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisablePCA", 1));
            list.Add(pcaOff);

            RegTweak dwmDeep = new RegTweak();
            dwmDeep.IdValue = "dwm_deep_slim";
            dwmDeep.GroupValue = GGame;
            dwmDeep.NameValue = "DWM 深度精简（谨慎）";
            dwmDeep.DescriptionValue = "关闭全息合成器、桌面叠加与交互输出预测，为游戏让出合成器资源。部分桌面特效会减少。";
            dwmDeep.AdminOnlyValue = true;
            dwmDeep.RiskyValue = true;
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableHologramCompositor", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "EnableDesktopOverlays", 0));
            // dunu DWM性能优化：投影阴影/设备位图/输入预测
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableProjectedShadows", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableProjectedShadowsRendering", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableDeviceBitmaps", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "InteractionOutputPredictionDisabled", 1));
            list.Add(dwmDeep);

            // ---- dunu 14.叠加补充 / 10.超目标优先 吸收：显卡呈现与固件 ----

            RegTweak gpuFlip = new RegTweak();
            gpuFlip.IdValue = "gpu_flip_reporting";
            gpuFlip.GroupValue = GGame;
            gpuFlip.NameValue = "显卡 Flip 呈现优化";
            gpuFlip.DescriptionValue = "启用 Flip 折叠与立即翻转完成报告（enableRS2FlipCollapse / enableRS2ImmediateFlipCompletionReporting / Flip\\EnableImmediateFlip），减少全屏游戏帧呈现的排队等待。写入全部显卡设备实例。";
            gpuFlip.AdminOnlyValue = true;
            gpuFlip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Flip", "EnableImmediateFlip", 1));
            list.Add(gpuFlip);

            GpuInstanceTweak gpuFw = new GpuInstanceTweak(
                "gpu_firmware_dsp",
                "显卡固件调度与链路低延迟（谨慎）",
                "EnableGpuFirmware=1 启用 GPU 固件调度（DSP，需 N 卡 560+ 驱动）、LOWLATENCY=1 与 D3PCLatency=1 降低显示链路延迟。dunu 作者标注有风险，仅建议 N 卡用户尝试，出问题还原即可。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("EnableGpuFirmware", 1),
                    new KeyValuePair<string, object>("LOWLATENCY", 1),
                    new KeyValuePair<string, object>("D3PCLatency", 1)
                });
            gpuFw.RiskyValue = true;
            list.Add(gpuFw);

            GpuInstanceTweak gpuUncached = new GpuInstanceTweak(
                "gpu_uncached_memory",
                "显卡全速渲染（非缓存访问，谨慎）",
                "EnableUncachedMemoryAccess=1 与 ForceWriteCombining=1 让显存写入走非缓存通道，部分游戏帧时间更稳；也可能与个别驱动不合。出问题还原即可。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("EnableUncachedMemoryAccess", 1),
                    new KeyValuePair<string, object>("ForceWriteCombining", 1)
                });
            gpuUncached.RiskyValue = true;
            list.Add(gpuUncached);

            // （dwm_present_buffers 已并入 dwm_low_latency：同键 MaxQueuedPresentBuffers=1 重复项已移除）

            RegTweak mouseFeel = new RegTweak();
            mouseFeel.IdValue = "mouse_feel_extra";
            mouseFeel.GroupValue = GGame;
            mouseFeel.NameValue = "关闭原始输入节流";
            mouseFeel.DescriptionValue = "关闭 Win11 原始输入节流（RawMouseThrottle），高回报率鼠标移动更跟手。去抖（DebounceTime）由「鼠标驱动响应修正」统一管理。";
            mouseFeel.AdminOnlyValue = true;
            mouseFeel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "RawMouseThrottleEnabled", 0));
            mouseFeel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "RawMouseThrottleForced", 0));
            list.Add(mouseFeel);

            // ---- 黑白 吸收：N/A 卡链路延迟深度、输入精简、USB 低延迟、游戏进程优先级 ----

            GpuInstanceTweak nvDeep = new GpuInstanceTweak(
                "nvidia_latency_deep",
                "N 卡链路延迟深度（仅 NVIDIA，谨慎）",
                "N 卡驱动级延迟键：RMDeepLlEntryLatencyUsec、Node3DLowLatency、PciLatencyTimerControl、VRDirectFlip 时序余量、vrr 游标/消抖余量、RmGpsPsEnablePerCpuCoreDpc 等全部压到最小值。黑白包 N 卡导入方案。仅 NVIDIA 显卡可应用——A 卡/Intel 机器上本项自动判定为不适用。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("D3PCLatency", 1),
                    new KeyValuePair<string, object>("LOWLATENCY", 1),
                    new KeyValuePair<string, object>("Node3DLowLatency", 1),
                    new KeyValuePair<string, object>("PciLatencyTimerControl", 0x20),
                    new KeyValuePair<string, object>("RMDeepL1EntryLatencyUsec", 1),
                    new KeyValuePair<string, object>("RmGspcMaxFtuS", 1),
                    new KeyValuePair<string, object>("RmGspcMinFtuS", 1),
                    new KeyValuePair<string, object>("RmGspcPerioduS", 1),
                    new KeyValuePair<string, object>("RMLpwrEiIdleThresholdUs", 1),
                    new KeyValuePair<string, object>("RMLpwrGrIdleThresholdUs", 1),
                    new KeyValuePair<string, object>("RMLpwrGrRgIdleThresholdUs", 1),
                    new KeyValuePair<string, object>("RMLpwrMsIdleThresholdUs", 1),
                    new KeyValuePair<string, object>("VRDirectFlipDPCDelayUs", 1),
                    new KeyValuePair<string, object>("VRDirectFlipTimingMarginUs", 1),
                    new KeyValuePair<string, object>("VRDirectJITFlipMsHybridFlipDelayUs", 1),
                    new KeyValuePair<string, object>("vrrCursorMarginUs", 1),
                    new KeyValuePair<string, object>("vrrDeflickerMarginUs", 1),
                    new KeyValuePair<string, object>("vrrDeflickerMaxUs", 1),
                    new KeyValuePair<string, object>("RmGpsPsEnablePerCpuCoreDpc", 1)
                });
            nvDeep.RiskyValue = true;
            nvDeep.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor() == "N"; });
            list.Add(nvDeep);

            GpuInstanceTweak amdDeep = new GpuInstanceTweak(
                "amd_latency_deep",
                "A 卡链路延迟深度（谨慎）",
                "A 卡驱动级延迟键：LTR 收发路径延迟、显存时钟切换延迟、计算/图形空闲阈值、欠流 NB 延迟等全部压到最小值。黑白包 A 卡导入方案，仅 A 卡用户建议尝试。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("LTRSnoopL1Latency", 1),
                    new KeyValuePair<string, object>("LTRSnoopL0Latency", 1),
                    new KeyValuePair<string, object>("LTRNoSnoopL1Latency", 1),
                    new KeyValuePair<string, object>("LTRMaxNoSnoopLatency", 1),
                    new KeyValuePair<string, object>("KMD_RpmComputeLatency", 1),
                    new KeyValuePair<string, object>("DalUrgentLatencyNs", 1),
                    new KeyValuePair<string, object>("memClockSwitchLatency", 1),
                    new KeyValuePair<string, object>("PP_RTPMComputeF1Latency", 1),
                    new KeyValuePair<string, object>("PP_DGBMMMaxTransitionLatencyUvd", 1),
                    new KeyValuePair<string, object>("PP_DGBPMMaxTransitionLatencyGfx", 1),
                    new KeyValuePair<string, object>("DalNBLatencyForUnderFlow", 1),
                    new KeyValuePair<string, object>("BGM_LTRSnoopL1Latency", 1),
                    new KeyValuePair<string, object>("BGM_LTRSnoopL0Latency", 1),
                    new KeyValuePair<string, object>("BGM_LTRNoSnoopL1Latency", 1),
                    new KeyValuePair<string, object>("BGM_LTRNoSnoopL0Latency", 1),
                    new KeyValuePair<string, object>("BGM_LTRMaxSnoopLatencyValue", 1),
                    new KeyValuePair<string, object>("BGM_LTRMaxNoSnoopLatencyValue", 1)
                });
            amdDeep.RiskyValue = true;
            amdDeep.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor() == "A"; });
            list.Add(amdDeep);

            RegTweak inputPrecision = new RegTweak();
            inputPrecision.IdValue = "input_precision";
            inputPrecision.GroupValue = GGame;
            inputPrecision.NameValue = "输入精简（光标抑制/磁吸/触控可视化）";
            inputPrecision.DescriptionValue = "关闭系统光标抑制补偿（EnableCursorSuppression，社区公认游戏手感项）、指针磁吸、触控死区跳转与可视化特效，鼠标键盘输入路径更直接。";
            inputPrecision.AdminOnlyValue = true;
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableCursorSuppression", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "TreatAbsolutePointerAsAbsolute", 1));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "TreatAbsoluteAsRelative", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\input\TIPC", "Enabled", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\CursorSpeed", "CursorUpdateInterval", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\CursorMagnetism", "MagnetismDelayInMilliseconds", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\CursorMagnetism", "MagnetismUpdateIntervalInMilliseconds", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Cursors", "CursorDeadzoneJumpingSetting", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Cursors", "ContactVisualization", 0));
            inputPrecision.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Cursors", "GestureVisualization", 0));
            list.Add(inputPrecision);

            RegTweak usbLowLatency = new RegTweak();
            usbLowLatency.IdValue = "usb_low_latency";
            usbLowLatency.GroupValue = GGame;
            usbLowLatency.NameValue = "USB 控制器强制低延迟";
            usbLowLatency.DescriptionValue = "USBXHCI 强制低延迟模式、异步调度启用、传输缓冲扩到 4MB、集线器空闲超时归零——键鼠等 USB 设备的中断处理更快（黑白包 usb.bat 方案）。";
            usbLowLatency.AdminOnlyValue = true;
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters", "ForceLowLatency", 1));
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters", "AsynchronousScheduleEnable", 1));
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters", "MaxTransferSize", 4194304));
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters", "ForceHCResetOnResume", 1));
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\usbhub\HubG", "IdleTimeout", 0));
            usbLowLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBSTOR", "TransferBufferLength", 4194304));
            list.Add(usbLowLatency);

            // —— 内核低延迟精简（取自社区内核方案中有据可查的键） ——
            RegTweak kernelLow = new RegTweak();
            kernelLow.IdValue = "kernel_low_latency";
            kernelLow.GroupValue = GGame;
            kernelLow.NameValue = "内核低延迟精简（DPC / 计时器 / 缓解）";
            kernelLow.DescriptionValue = "全局化定时器精度请求、禁用 DPC 节流与计时器合并、关闭 I/O 计数与资源管理器 DEP，为游戏让出内核开销。需重启生效。";
            kernelLow.AdminOnlyValue = true;
            kernelLow.RiskyValue = true;
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "ThreadDpcEnable", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "CoalescingTimerInterval", 0));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "IdealDpcRate", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "MaximumDpcQueueDepth", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "MinimumDpcRate", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "UnlimitDpcQueue", 1));
            // DPC 看门狗与队列深度（HanFly DPC.bat / 内核.reg 中的真实有效键）
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DpcWatchdogProfileOffset", 0));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DpcTimeout", 0));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DpcWatchdogPeriod", 0));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DpcCumulativeSoftTimeout", 0));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "MaxDynamicTickDuration", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DistributeTimers", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "SerializeTimerExpiration", 0));
            // 内存与磁盘计数开销（Juxic 内核方案中的真实有效键）
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePageCombining", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\I/O System", "DisableDiskCounters", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer", "NoDataExecutionPrevention", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "MaximumSharedReadyQueueSize", 1));
            kernelLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Classpnp", "NVMeDisablePerfThrottling", 1));
            list.Add(kernelLow);

            // Intel 专属调度键（从 kernel_low_latency 拆出，仅 Intel CPU 可应用）
            RegTweak intelSched = new RegTweak();
            intelSched.IdValue = "intel_scheduling";
            intelSched.GroupValue = GGame;
            intelSched.NameValue = "Intel 调度与 TSX 修正（仅 Intel CPU）";
            intelSched.DescriptionValue = "CacheAwareScheduling=15 启用按缓存亲和的调度（混合架构/多缓存拓扑下减少跨缓存迁移），DisableTsx=0 恢复被社区脚本误关的 TSX 指令支持。仅 Intel 处理器有效——AMD 机器上本项自动判定为不适用。需重启生效。";
            intelSched.AdminOnlyValue = true;
            intelSched.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DisableTsx", 0));
            intelSched.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "CacheAwareScheduling", 15));
            intelSched.ApplicableValue = delegate { return CpuVendor.IsIntel; };
            list.Add(intelSched);

            RegTweak mmcssDeep = new RegTweak();
            mmcssDeep.IdValue = "mmcss_deep";
            mmcssDeep.GroupValue = GGame;
            mmcssDeep.NameValue = "MMCSS 深度调度（谨慎）";
            mmcssDeep.DescriptionValue = "按低延迟模板重排全部多媒体任务：音频/播放降为 Low，采集/分发/窗口管理升为 High，并把调度器计时分辨率锁到 5ms。";
            mmcssDeep.AdminOnlyValue = true;
            mmcssDeep.RiskyValue = true;
            const string MM = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "SchedulerTimerResolution", 5000));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "SchedulerPeriod", 100000));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "MaxThreadsPerProcess", 128));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "MaxThreadsTotal", 65535));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "IdleDetectionCycles", 5));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM, "LatencyIndicatorEnabled", 0));
            mmcssDeep.Enable.Add(RegWrite.Remove(RegistryHive.LocalMachine, MM, "LazyModeTimeout"));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Games", "Priority", 8));
            // （"Priority When Yielded" 无微软文档依据且社区流传值 19 超出 MMCSS 1-8 合法范围，不写入）
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Games", "BackgroundPriority", 8));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Games", "Clock Rate", 5002));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Audio", "Priority", 1));
            mmcssDeep.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MM + @"\Tasks\Audio", "Scheduling Category", "Low"));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Playback", "Priority", 1));
            mmcssDeep.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MM + @"\Tasks\Playback", "Scheduling Category", "Low"));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Pro Audio", "Priority", 1));
            mmcssDeep.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MM + @"\Tasks\Pro Audio", "Scheduling Category", "Low"));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Capture", "Priority", 8));
            mmcssDeep.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MM + @"\Tasks\Capture", "Scheduling Category", "High"));
            mmcssDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MM + @"\Tasks\Window Manager", "Priority", 8));
            mmcssDeep.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MM + @"\Tasks\Window Manager", "Scheduling Category", "High"));
            list.Add(mmcssDeep);

            list.Add(new PciAspmTweak());

            list.Add(new GpuLatencyTweak());

            list.Add(new CoreParkingTweak());

            list.Add(new MsiModeTweak());

            list.Add(new DiskLpmTweak());

            list.Add(new CpuIdleTweak());

            list.Add(new DscpTweak());

            CommandTweak reserved = new CommandTweak();
            reserved.IdValue = "reserved_storage_off";
            reserved.GroupValue = GPerformance;
            reserved.NameValue = "禁用系统保留存储（释放约 7GB）";
            reserved.DescriptionValue = "Windows 会预留约 7GB 磁盘空间保障更新成功率，禁用后释放该空间（更新时临时文件按需占用）。可随时还原。";
            reserved.EnableFile = "dism.exe";
            reserved.EnableArgs = "/Online /Set-ReservedStorageState /State:Disabled";
            reserved.RevertFile = "dism.exe";
            reserved.RevertArgs = "/Online /Set-ReservedStorageState /State:Enabled";
            reserved.Probe = delegate
            {
                return false; // 交由命令执行时 DISM 校验（已禁用会提示无需更改）
            };
            list.Add(reserved);

            RegTweak storageSense = new RegTweak();
            storageSense.IdValue = "storage_sense_off";
            storageSense.GroupValue = GPerformance;
            storageSense.NameValue = "禁用存储感知自动清理";
            storageSense.DescriptionValue = "存储感知会在后台定期扫描并删除临时文件与回收站内容，扫描期间占用磁盘 I/O 与 CPU。禁用后由本工具的垃圾清理功能按需手动清理。";
            storageSense.AdminOnlyValue = false;
            storageSense.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0));
            list.Add(storageSense);

            list.Add(new DevicePriorityTweak());

            RegTweak inputBuffer = new RegTweak();
            inputBuffer.IdValue = "input_buffer";
            inputBuffer.GroupValue = GGame;
            inputBuffer.NameValue = "输入缓冲优化（鼠标 8 / 键盘 16）";
            inputBuffer.DescriptionValue = "缩小键盘与鼠标类驱动的数据队列，输入包更快被处理（缓冲越小延迟越低，极重负载下理论可能丢包）。需重启生效。";
            inputBuffer.AdminOnlyValue = true;
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 10));
            // 单键盘设备标准配置：关闭端口多路复用，减少一层转发
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "ConnectMultiplePorts", 0));
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 8));
            // 关键驱动线程优先级拉满（社区竞技方案：mouclass/kbdclass/N卡/DXGKrnl/Tcpip/NDIS/USB）
            string[] prioServices = new string[]
            {
                "mouclass", "kbdclass", "nvlddmkm", "DXGKrnl", "Tcpip", "NDIS", "Usbxhci", "USBHUB3"
            };
            for (int i = 0; i < prioServices.Length; i++)
            {
                inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + prioServices[i] + @"\Parameters", "ThreadPriority", 31));
            }
            list.Add(inputBuffer);

            RegTweak usbPowerAll = new RegTweak();
            usbPowerAll.IdValue = "usb_power_off_all";
            usbPowerAll.GroupValue = GGame;
            usbPowerAll.NameValue = "USB 全链路省电禁用（控制器 / 集线器 / HID）";
            usbPowerAll.DescriptionValue = "关闭 XHCI 控制器中断调节与空闲断电、USB 选择性暂停、HID 空闲等待、音频设备省电与 D1-D3 延迟，鼠标键盘手柄全程在线。需重启生效。";
            usbPowerAll.AdminOnlyValue = true;
            const string USBX = @"SYSTEM\CurrentControlSet\Services\USBXHCI\Parameters";
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Enum\USB", "AllowIdleIrpInD3", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Enum\USB", "EnhancedPowerManagementEnabled", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, USBX, "InterruptModeration", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, USBX, "DisableIdlePowerDown", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, USBX, "EnableIdlePowerManagement", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, USBX, "CompletionInterval", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, USBX, "DisableSelectiveSuspend", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USB\Parameters", "DisableSelectiveSuspend", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USB\Parameters", "DisablePowerManagement", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USB\Parameters", "IdleEnable", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\usbhub\Parameters", "DisableSelectiveSuspendForInteractive", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\usbhub\Parameters", "DisableDeviceSelectiveSuspend", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\usbhub\hubg", "DisableOnSoftRemove", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "IdleWaitTime", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "DeviceIdleEnabled", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\HidUsb\Parameters", "SelectiveSuspendEnabled", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "IdleTimeout", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "IdleTimeout", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBHUB3\Parameters", "SelectiveSuspendEnabled", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USBEHCI\Parameters", "EnableSelectiveSuspend", 0));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\USB Audio\Parameters", "DisableIdlePowerManagement", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\usbaudio\Parameters", "DisableIdlePowerManagement", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\usbflags", "fid_D1Latency", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\usbflags", "fid_D2Latency", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\usbflags", "fid_D3Latency", 1));
            usbPowerAll.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\EnhancedStorageDevices", "TCGSecurityActivationDisabled", 1));
            list.Add(usbPowerAll);

            RegTweak powerLatency = new RegTweak();
            powerLatency.IdValue = "power_latency_off";
            powerLatency.GroupValue = GGame;
            powerLatency.NameValue = "电源延迟容忍归零";
            powerLatency.DescriptionValue = "把电源管理器的各类延迟容忍与退出延迟全部压到最小，系统不为省电而等待。需重启生效。";
            powerLatency.AdminOnlyValue = true;
            const string PW = @"SYSTEM\CurrentControlSet\Control\Power";
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "ExitLatency", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "ExitLatencyCheckEnabled", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "Latency", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyToleranceDefault", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyToleranceFSVP", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyToleranceIdleResiliency", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyTolerancePerfOverride", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyToleranceScreenOffIR", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "LatencyToleranceVSyncEnabled", 0));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "MfBufferingThreshold", 0));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "QosManagesIdleProcessors", 0));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "DisableSensorWatchdog", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, PW, "RtlCapabilityCheckLatency", 1));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                PW + @"\Policy\Settings\Misc", "DeviceIdlePolicy", 0));
            // 图形电源延迟容忍（Stop-Tolerating-High-DPC 方案：DX 空闲转换全部压到 1ms 级，见下方 gpKeys 循环）
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                PW + @"\Profile\Events\{54533251-82be-4824-96c1-47b60b740d00}\{0DA965DC-8FCF-4c0b-8EFE-8DD5E7BC959A}\{7E01ADEF-81E6-4e1b-8075-56F373584694}",
                "TimeLimitInSeconds", 2));
            // dunu Windows电源事件策略稳定：低延迟/游戏模式事件权重提到最高
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                PW + @"\Profile\Events\{54533251-82be-4824-96c1-47b60b740d00}\{0DA965DC-8FCF-4c0b-8EFE-8DD5E7BC959A}",
                "Pri", 0x28));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                PW + @"\Profile\Events\{54533251-82be-4824-96c1-47b60b740d00}\{D4140C81-EBBA-4e60-8561-6918290359CD}",
                "Pri", 0x20));
            powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                PW + @"\Policy\Settings\Power", "SleepReliabilityDetailedDiagnostics", 0));
            // GraphicsDrivers\Power 延迟表（社区 DPC/ISR 延迟优化标准方案，22 键全部归 1）
            const string GP = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Power";
            string[] gpKeys = new string[]
            {
                "DefaultD3TransitionLatencyActivelyUsed", "DefaultD3TransitionLatencyIdleLongTime",
                "DefaultD3TransitionLatencyIdleMonitorOff", "DefaultD3TransitionLatencyIdleNoContext",
                "DefaultD3TransitionLatencyIdleShortTime", "DefaultD3TransitionLatencyIdleVeryLongTime",
                "DefaultLatencyToleranceIdle0", "DefaultLatencyToleranceIdle0MonitorOff",
                "DefaultLatencyToleranceIdle1", "DefaultLatencyToleranceIdle1MonitorOff",
                "DefaultLatencyToleranceMemory", "DefaultLatencyToleranceNoContext",
                "DefaultLatencyToleranceNoContextMonitorOff", "DefaultLatencyToleranceOther",
                "DefaultLatencyToleranceTimerPeriod", "DefaultMemoryRefreshLatencyToleranceActivelyUsed",
                "DefaultMemoryRefreshLatencyToleranceMonitorOff", "DefaultMemoryRefreshLatencyToleranceNoContext",
                "Latency", "MaxIAverageGraphicsLatencyInOneBucket", "MiracastPerfTrackGraphicsLatency",
                "MonitorLatencyTolerance", "MonitorRefreshLatencyTolerance", "TransitionLatency"
            };
            for (int i = 0; i < gpKeys.Length; i++)
            {
                powerLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GP, gpKeys[i], 1));
            }
            list.Add(powerLatency);

            RegTweak gpuPstate = new RegTweak();
            gpuPstate.IdValue = "gpu_max_pstate";
            gpuPstate.GroupValue = GGame;
            gpuPstate.NameValue = "显卡维持最高性能状态（N/A 卡）";
            gpuPstate.DescriptionValue = "N 卡禁用动态 Pstate（锁 P0，待机功耗上升），A 卡禁用 Sclk DeepSleep。消除核心降频回升造成的偶发卡顿。需重启生效。";
            gpuPstate.AdminOnlyValue = true;
            gpuPstate.RiskyValue = true;
            string gpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 3; i++)
            {
                gpuPstate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    gpuClass + @"\000" + i.ToString(), "DisableDynamicPstate", 1));
            }
            gpuPstate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                gpuClass + @"\0000", "PP_SclkDeepSleepDisable", 1));
            list.Add(gpuPstate);

            list.Add(new BcdTweak("bcd_timer",
                "BCD 计时器与平台时钟优化（需重启）",
                "关闭平台时钟与平台 tick、禁用动态 tick、TSC 同步策略设为默认——与「高精度定时器」开关配合使用效果最佳。写入 BCD，重启后生效。",
                new string[]
                {
                    "/set useplatformclock no",
                    "/set useplatformtick no",
                    "/set disabledynamictick yes",
                    "/set tscsyncpolicy default",
                    "/set uselegacyapicmode no"
                },
                new string[]
                {
                    "/deletevalue useplatformclock",
                    "/deletevalue useplatformtick",
                    "/deletevalue disabledynamictick",
                    "/deletevalue tscsyncpolicy",
                    "/deletevalue uselegacyapicmode"
                },
                new string[] { @"disabledynamictick\s+Yes" },
                new string[] { @"useplatformclock\s+Yes" },
                false));

            RegTweak sysPrio = new RegTweak();
            sysPrio.IdValue = "sys_proc_priority";
            sysPrio.GroupValue = GGame;
            sysPrio.NameValue = "系统进程优先级重排（DWM/CSRSS 高，服务低）";
            sysPrio.DescriptionValue = "用 IFEO 把桌面窗口管理与 CSRSS 提到高优先、LSASS 与服务宿主降为空闲级，让 CPU 让位给前台游戏。需重启生效。";
            sysPrio.AdminOnlyValue = true;
            string[] prioRoots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"
            };
            string[][] prioTargets = new string[][]
            {
                new string[] { "dwm.exe", "6" },
                new string[] { "csrss.exe", "6" },
                new string[] { "lsass.exe", "1" },
                new string[] { "svchost.exe", "1" }
            };
            string[] prioValues = new string[] { "CpuPriorityClass", "IoPriority", "PagePriority" };
            for (int r = 0; r < prioRoots.Length; r++)
            {
                for (int e = 0; e < prioTargets.Length; e++)
                {
                    string perfPath = prioRoots[r] + "\\" + prioTargets[e][0] + "\\PerfOptions";
                    for (int v = 0; v < prioValues.Length; v++)
                    {
                        sysPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, perfPath,
                            prioValues[v], int.Parse(prioTargets[e][1], System.Globalization.CultureInfo.InvariantCulture)));
                    }
                }
            }
            sysPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "ConvertibleSlateMode", 0));
            list.Add(sysPrio);

            list.Add(new EnumPowerSweepTweak());

            CommandTweak wmiPower = new CommandTweak();
            wmiPower.IdValue = "wmi_device_power_off";
            wmiPower.GroupValue = GGame;
            wmiPower.NameValue = "设备管理器省电全关（WMI 扫描）";
            wmiPower.DescriptionValue = "遍历全部设备的「允许计算机关闭此设备以节约电源」开关并关闭，覆盖设备管理器电源管理页的每一项。";
            wmiPower.EnableFile = "powershell.exe";
            wmiPower.EnableArgs = "-NoProfile -Command \"Get-WmiObject MSPower_DeviceEnable -Namespace root\\wmi -ErrorAction SilentlyContinue | ForEach-Object { $_.Enable = $false; $_.psbase.Put() }\"";
            wmiPower.RevertFile = "powershell.exe";
            wmiPower.RevertArgs = "-NoProfile -Command \"Get-WmiObject MSPower_DeviceEnable -Namespace root\\wmi -ErrorAction SilentlyContinue | ForEach-Object { $_.Enable = $true; $_.psbase.Put() }\"";
            wmiPower.Probe = delegate
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"$bad=0; Get-WmiObject MSPower_DeviceEnable -Namespace root\\wmi -ErrorAction SilentlyContinue | ForEach-Object { if ($_.Enable -ne $false) { $bad++ } }; Write-Output ('BAD=' + $bad)\"",
                    60000);
                string text = r.All ?? "";
                return text.IndexOf("BAD=0", StringComparison.Ordinal) >= 0;
            };
            list.Add(wmiPower);

            list.Add(new BcdTweak("bcd_virt_off",
                "BCD 关闭虚拟化底座（Hyper-V / VBS，谨慎）",
                "关闭 Hypervisor 启动、VBS 与 IOMMU 虚拟化，消除虚拟化层对游戏的性能开销（与关闭内核隔离配套）。重启生效。使用 Hyper-V / WSA / 部分反作弊的虚拟化功能勿开。",
                new string[]
                {
                    "/set hypervisorlaunchtype off",
                    "/set vsmlaunchtype off",
                    "/set hypervisoriommupolicy disable",
                    "/set isolatedcontext no",
                    "/set vm no"
                },
                new string[]
                {
                    "/deletevalue hypervisorlaunchtype",
                    "/deletevalue vsmlaunchtype",
                    "/deletevalue hypervisoriommupolicy",
                    "/deletevalue isolatedcontext",
                    "/deletevalue vm"
                },
                new string[] { @"hypervisorlaunchtype\s+Off" },
                new string[0],
                true));

            RegTweak fso = new RegTweak();
            fso.IdValue = "fso_off";
            fso.GroupValue = GGame;
            fso.NameValue = "关闭全屏优化 (FSO)";
            fso.DescriptionValue = "让游戏独占全屏而不是边框化合成，配合全屏游戏可降低一帧以上的显示延迟。";
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2));
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1));
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0));
            list.Add(fso);

            RegTweak bgApps = new RegTweak();
            bgApps.IdValue = "bgapps_off";
            bgApps.GroupValue = GGame;
            bgApps.NameValue = "禁止 UWP 应用后台运行";
            bgApps.DescriptionValue = "策略级 + 用户级双重阻断：后台 UWP 应用不再占用网络与 CPU。副作用：邮件/天气等磁贴停止自动刷新。";
            bgApps.AdminOnlyValue = true;
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2));
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1));
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0));
            list.Add(bgApps);

            list.Add(new NagleTweak());

            RegTweak keyboard = new RegTweak();
            keyboard.IdValue = "keyboard_gaming";
            keyboard.GroupValue = GGame;
            keyboard.NameValue = "键盘响应调到最快";
            keyboard.DescriptionValue = "按键重复延迟设为最短、重复速度设为最快，游戏内选单与打字跟手。";
            keyboard.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Keyboard", "KeyboardDelay", 0));
            keyboard.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Keyboard", "KeyboardSpeed", 31));
            list.Add(keyboard);

            RegTweak sticky = new RegTweak();
            sticky.IdValue = "sticky_keys_off";
            sticky.GroupValue = GGame;
            sticky.NameValue = "关闭粘滞键 / 筛选键弹窗";
            sticky.DescriptionValue = "游戏中连按 Shift 或长按键时不再弹出辅助功能询问窗口打断操作。";
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\StickyKeys", "Flags", "506"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\ToggleKeys", "Flags", "58"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\Keyboard Response", "Flags", "122"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\MouseKeys", "Flags", "0"));
            list.Add(sticky);

            RegTweak notify = new RegTweak();
            notify.IdValue = "notify_off";
            notify.GroupValue = GGame;
            notify.NameValue = "关闭所有通知弹窗";
            notify.DescriptionValue = "全局禁止应用推送横幅通知（含专注助手的横幅），全屏游戏时不再被弹窗切出或分心。系统更新的提醒也不受影响（由更新策略单独控制）。";
            notify.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0));
            list.Add(notify);

            RegTweak gamebarTips = new RegTweak();
            gamebarTips.IdValue = "gamebar_tips_off";
            gamebarTips.GroupValue = GGame;
            gamebarTips.NameValue = "关闭游戏栏启动面板与提示";
            gamebarTips.DescriptionValue = "不再弹出游戏栏欢迎面板与新手提示，减少干扰。";
            gamebarTips.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\GameBar", "ShowStartupPanel", 0));
            gamebarTips.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\GameBar", "GamePanelStartupTipIndex", 3));
            list.Add(gamebarTips);

            RegTweak storeUpdate = new RegTweak();
            storeUpdate.IdValue = "store_autoupdate_off";
            storeUpdate.GroupValue = GGame;
            storeUpdate.NameValue = "禁止微软商店自动更新";
            storeUpdate.DescriptionValue = "阻止商店应用在游戏时悄悄下载更新抢带宽与磁盘。需要更新时手动打开商店即可。";
            storeUpdate.AdminOnlyValue = true;
            storeUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2));
            list.Add(storeUpdate);

            RegTweak prefetcher = new RegTweak();
            prefetcher.IdValue = "prefetcher_off";
            prefetcher.GroupValue = GGame;
            prefetcher.NameValue = "关闭预读取 Prefetcher";
            prefetcher.DescriptionValue = "停止系统预读加速机制，减少开机与游戏时的后台磁盘活动。固态硬盘适用；机械硬盘用户勿开。";
            prefetcher.AdminOnlyValue = true;
            prefetcher.RiskyValue = true;
            prefetcher.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 0));
            list.Add(prefetcher);

            RegTweak hvci = new RegTweak();
            hvci.IdValue = "hvci_off";
            hvci.GroupValue = GGame;
            hvci.NameValue = "关闭内核隔离 / 内存完整性 (HVCI)";
            hvci.DescriptionValue = "关闭基于虚拟化的安全（VBS）后常见可提升 3~8% 帧数，需重启生效。代价是降低对内核级攻击的防护，安全性要求高的环境勿开。";
            hvci.AdminOnlyValue = true;
            hvci.RiskyValue = true;
            hvci.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0));
            list.Add(hvci);

            RegTweak paging = new RegTweak();
            paging.IdValue = "paging_executive";
            paging.GroupValue = GGame;
            paging.NameValue = "内核常驻内存 (DisablePagingExecutive)";
            paging.DescriptionValue = "禁止系统把内核代码与驱动换出到页面文件，降低偶发性卡顿尖峰，需重启生效。内存 16GB 以上推荐。";
            paging.AdminOnlyValue = true;
            paging.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1));
            list.Add(paging);

            RegTweak mouse1to1 = new RegTweak();
            mouse1to1.IdValue = "mouse_1to1";
            mouse1to1.GroupValue = GGame;
            mouse1to1.NameValue = "鼠标 1:1 原生移动（第 6 格）";
            mouse1to1.DescriptionValue = "把指针速度固定在第 6 格（无缩放插值），配合「关闭鼠标加速度」获得传感器原生计数——非第 6 格时指针按比例丢帧/加速，瞄准会漂移。游戏内灵敏度不受影响。";
            mouse1to1.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseSensitivity", "10"));
            // MarkC 标准 1:1 修正曲线：消除 Windows 默认 X 曲线的非线性加速段
            mouse1to1.Enable.Add(RegWrite.Binary(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SmoothMouseXCurve",
                new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00 }));
            mouse1to1.Enable.Add(RegWrite.Binary(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SmoothMouseYCurve",
                new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00 }));
            list.Add(mouse1to1);

            RegTweak vidAlloc = new RegTweak();
            vidAlloc.IdValue = "vidmem_alloc_low";
            vidAlloc.GroupValue = GGame;
            vidAlloc.NameValue = "显存低延迟分配模式";
            vidAlloc.DescriptionValue = "EnableLowLatencyVidmemAlloc=1，让驱动优先走低延迟显存分配路径，减少游戏加载纹理时的卡顿尖峰。需重启生效。";
            vidAlloc.AdminOnlyValue = true;
            vidAlloc.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "EnableLowLatencyVidmemAlloc", 1));
            list.Add(vidAlloc);

            list.Add(new NvTelemetryTweak());

            RegTweak gpuEnergy = new RegTweak();
            gpuEnergy.IdValue = "gpu_energy_drv_off";
            gpuEnergy.GroupValue = GGame;
            gpuEnergy.NameValue = "禁用 GPU 能耗统计驱动";
            gpuEnergy.DescriptionValue = "GpuEnergyDrv 只为任务管理器的 GPU 能耗列提供数据，禁用后省去每次显存查询的开销，不影响游戏与显卡功能。";
            gpuEnergy.AdminOnlyValue = true;
            gpuEnergy.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\GpuEnergyDrv", "Start", 4));
            list.Add(gpuEnergy);

            list.Add(new NicPowerTweak());

            RegTweak audioIdle = new RegTweak();
            audioIdle.IdValue = "audio_idle_off";
            audioIdle.GroupValue = GGame;
            audioIdle.NameValue = "声卡省电禁用（消除音频延迟/爆音）";
            audioIdle.DescriptionValue = "对声卡设备关闭空闲节电（ConservationIdleTime/PerformanceIdleTime/IdlePowerState 清零），避免游戏语音与音效因声卡休眠产生延迟或杂音。";
            audioIdle.AdminOnlyValue = true;
            const string AudioClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}";
            byte[] zero4 = new byte[] { 0, 0, 0, 0 };
            for (int i = 0; i < 10; i++)
            {
                string sub = AudioClass + "\\000" + i + "\\PowerSettings";
                audioIdle.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine, sub, "ConservationIdleTime", zero4));
                audioIdle.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine, sub, "PerformanceIdleTime", zero4));
                audioIdle.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine, sub, "IdlePowerState", zero4));
            }
            list.Add(audioIdle);

            RegTweak mouseFix = new RegTweak();
            mouseFix.IdValue = "mouse_fixes";
            mouseFix.GroupValue = GGame;
            mouseFix.NameValue = "鼠标驱动响应修正";
            mouseFix.DescriptionValue = "关闭鼠标驱动去抖延迟（DebounceTime=0），让高回报率鼠标的移动数据原样送达游戏。绝对/相对指针转换处理由「输入精简」统一管理。";
            mouseFix.AdminOnlyValue = true;
            mouseFix.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "DebounceTime", 0));
            list.Add(mouseFix);

            list.Add(new UsbSuspendTweak());
            list.Add(new UsbPowerTweak());

            RegTweak snapOff = new RegTweak();
            snapOff.IdValue = "mouse_snap_off";
            snapOff.GroupValue = GGame;
            snapOff.NameValue = "关闭指针自动吸附默认按钮";
            snapOff.DescriptionValue = "禁用「自动将指针移动到对话框默认按钮」，防止指针在弹窗出现时被强制移位。";
            snapOff.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SnapToDefaultButton", "0"));
            list.Add(snapOff);

            RegTweak nexus = new RegTweak();
            nexus.IdValue = "nexus_off";
            nexus.GroupValue = GGame;
            nexus.NameValue = "手柄 Xbox 键不再唤出游戏栏";
            nexus.DescriptionValue = "游戏中长按手柄 Xbox 键不再弹出游戏栏覆盖层，避免误按切屏卡顿。";
            nexus.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0));
            list.Add(nexus);

            return list;
        }

        // ---------------- 外观 ----------------

        private static IEnumerable<ITweak> Appearance()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak ext = new RegTweak();
            ext.IdValue = "show_file_ext";
            ext.GroupValue = GAppearance;
            ext.NameValue = "显示文件扩展名";
            ext.DescriptionValue = "在资源管理器中显示 .exe、.txt 等扩展名，便于识别文件真实类型。";
            ext.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "HideFileExt", 0));
            list.Add(ext);

            RegTweak hidden = new RegTweak();
            hidden.IdValue = "show_hidden_files";
            hidden.GroupValue = GAppearance;
            hidden.NameValue = "显示隐藏文件";
            hidden.DescriptionValue = "在资源管理器中显示隐藏文件和文件夹。";
            hidden.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Hidden", 1));
            list.Add(hidden);

            RegTweak shake = new RegTweak();
            shake.IdValue = "disable_shake";
            shake.GroupValue = GAppearance;
            shake.NameValue = "禁用 Aero Shake 窗口最小化";
            shake.DescriptionValue = "防止误拖动鼠标导致其它窗口全部最小化。";
            shake.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "DisallowShaking", 1));
            list.Add(shake);

            RegTweak recent = new RegTweak();
            recent.IdValue = "no_recent_docs";
            recent.GroupValue = GAppearance;
            recent.NameValue = "关闭'最近使用的文件'记录";
            recent.DescriptionValue = "不再记录最近打开的文件与常用文件夹，同时保护隐私。";
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackDocs", 0));
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackProgs", 0));
            list.Add(recent);

            RegTweak classicMenu = new RegTweak();
            classicMenu.IdValue = "win11_classic_menu";
            classicMenu.GroupValue = GAppearance;
            classicMenu.NameValue = "恢复 Windows 10 经典右键菜单 (Win11)";
            classicMenu.DescriptionValue = "在 Windows 11 上跳过'显示更多选项'，直接展开完整右键菜单。仅对 Win11 有效。";
            classicMenu.RiskyValue = true;
            classicMenu.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""));
            list.Add(classicMenu);

            RegTweak taskbarAl = new RegTweak();
            taskbarAl.IdValue = "taskbar_left";
            taskbarAl.GroupValue = GAppearance;
            taskbarAl.NameValue = "开始菜单与任务栏左对齐";
            taskbarAl.DescriptionValue = "把 Win11 默认居中的任务栏图标与开始菜单恢复为 Windows 10 风格的左对齐，纯外观偏好。";
            taskbarAl.AdminOnlyValue = false;
            taskbarAl.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1));
            list.Add(taskbarAl);

            return list;
        }

        // ---------------- 隐私 ----------------

        private static IEnumerable<ITweak> Privacy()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak telemetry = new RegTweak();
            telemetry.IdValue = "telemetry_off";
            telemetry.GroupValue = GPrivacy;
            telemetry.NameValue = "关闭诊断遥测数据上报";
            telemetry.DescriptionValue = "将 Windows 诊断数据级别设为最低（安全级别）。建议同时禁用 DiagTrack 服务。";
            telemetry.AdminOnlyValue = true;
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));
            list.Add(telemetry);

            RegTweak adId = new RegTweak();
            adId.IdValue = "ad_id_off";
            adId.GroupValue = GPrivacy;
            adId.NameValue = "关闭广告 ID";
            adId.DescriptionValue = "禁止应用使用你的广告标识符推送个性化广告。";
            adId.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0));
            list.Add(adId);

            RegTweak ceip = new RegTweak();
            ceip.IdValue = "ceip_off";
            ceip.GroupValue = GPrivacy;
            ceip.NameValue = "退出客户体验改善计划";
            ceip.DescriptionValue = "停止向微软发送使用习惯与硬件配置统计信息。";
            ceip.AdminOnlyValue = true;
            ceip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            list.Add(ceip);

            RegTweak activity = new RegTweak();
            activity.IdValue = "activity_feed_off";
            activity.GroupValue = GPrivacy;
            activity.NameValue = "关闭活动历史记录上传";
            activity.DescriptionValue = "不在本机收集活动历史，也不向云端同步时间线。";
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            list.Add(activity);

            RegTweak autoInst = new RegTweak();
            // 「阻止静默安装推荐应用」「关闭系统推广与任务栏资讯」已并入 consumer_content_off（键集全被覆盖）

            RegTweak bing = new RegTweak();
            bing.IdValue = "bing_search_off";
            bing.GroupValue = GPrivacy;
            bing.NameValue = "关闭开始菜单联网搜索";
            bing.DescriptionValue = "搜索框不再联网请求 Bing 结果，本地结果即输即出，也不会把输入内容发到云端。";
            bing.AdminOnlyValue = true;
            bing.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1));
            list.Add(bing);

            RegTweak copilot = new RegTweak();
            copilot.IdValue = "copilot_off";
            copilot.GroupValue = GPrivacy;
            copilot.NameValue = "关闭 Copilot 与任务栏图标";
            copilot.DescriptionValue = "通过策略禁用系统内置 AI 助手，减少常驻进程与后台联网。";
            copilot.AdminOnlyValue = true;
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            list.Add(copilot);

            RegTweak onedrive = new RegTweak();
            onedrive.IdValue = "onedrive_off";
            onedrive.GroupValue = GPrivacy;
            onedrive.NameValue = "禁用 OneDrive 文件同步";
            onedrive.DescriptionValue = "阻止 OneDrive 随系统自启与后台同步占用资源。仍登录使用 OneDrive 的用户勿开。";
            onedrive.AdminOnlyValue = true;
            onedrive.RiskyValue = true;
            onedrive.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1));
            list.Add(onedrive);

            RegTweak feedback = new RegTweak();
            feedback.IdValue = "feedback_off";
            feedback.GroupValue = GPrivacy;
            feedback.NameValue = "关闭 Windows 反馈与询问";
            feedback.DescriptionValue = "不再弹出「帮助我们改进 Windows」的反馈通知与频率询问，减少打扰。";
            feedback.AdminOnlyValue = false;
            feedback.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1));
            list.Add(feedback);

            RegTweak tailored = new RegTweak();
            tailored.IdValue = "tailored_experience_off";
            tailored.GroupValue = GPrivacy;
            tailored.NameValue = "关闭定制体验";
            tailored.DescriptionValue = "停止利用诊断数据向你推送定制提示、技巧与广告（设置里的「定制体验」开关）。";
            tailored.AdminOnlyValue = false;
            tailored.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0));
            list.Add(tailored);

            RegTweak ink = new RegTweak();
            ink.IdValue = "ink_telemetry_off";
            ink.GroupValue = GPrivacy;
            ink.NameValue = "禁用输入个性化与墨迹遥测";
            ink.DescriptionValue = "停止收集手写墨迹、输入历史与联系人样本用于云端个性化训练（触屏/手写笔用户关闭后不影响输入法本地功能）。";
            ink.AdminOnlyValue = false;
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\InputPersonalization", "HarvestContacts", 0));
            list.Add(ink);

            RegTweak recall = new RegTweak();
            recall.IdValue = "recall_off";
            recall.GroupValue = GPrivacy;
            recall.NameValue = "禁用 Recall AI 快照";
            recall.DescriptionValue = "策略级关闭 Windows Recall 的屏幕快照与 AI 分析，快照数据不会被保存；同时禁用相关预下载。仅在 Windows 11 24H2 及以上版本生效——本机不满足该条件时应用会直接拒绝，状态保持「未启用」。";
            recall.AdminOnlyValue = true;
            recall.RiskyValue = true;
            recall.ApplicableValue = delegate
            {
                // Windows 11 24H2 = 内部版本 26100 及以上
                return Environment.OSVersion.Version.Build >= 26100;
            };
            recall.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            list.Add(recall);

            RegTweak wuOff = new RegTweak();
            wuOff.IdValue = "wu_off";
            wuOff.GroupValue = GPerformance;
            wuOff.NameValue = "关闭 Windows 更新（谨慎）";
            wuOff.DescriptionValue = "策略级禁用 Windows Update 连接与自动更新，并把相关服务转为手动/禁用。游戏机防更新干扰利器；长期不更新有安全风险，需更新时还原即可。";
            wuOff.AdminOnlyValue = true;
            wuOff.RiskyValue = true;
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableOSUpgrade", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 2));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsStore\WindowsUpdate", "AutoDownload", 2));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 3));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc", "Start", 4));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\UsoSvc", "Start", 4));
            list.Add(wuOff);

            return list;
        }

        // ---------------- 服务 ----------------

        private static IEnumerable<ITweak> Services()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(MakeService("DiagTrack", "连接用户体验和遥测",
                "向微软上报诊断与使用数据。普通用户完全可以禁用。", true, false));

            list.Add(MakeService("dmwappushservice", "WAP 推送消息路由服务",
                "设备管理与推送消息路由，家用环境不需要。", true, false));

            list.Add(MakeService("SysMain", "SysMain (超级预读取)",
                "预加载常用程序以加速启动。在固态硬盘上收益有限，禁用可释放内存。", true, false));

            list.Add(MakeService("WSearch", "Windows Search 索引",
                "为文件内容建立搜索索引。禁用后搜索会变慢，但可显著降低磁盘占用。", true, true));

            list.Add(MakeService("RemoteRegistry", "远程注册表",
                "允许远程修改本机注册表，存在安全风险，建议禁用。", true, false));

            list.Add(MakeService("Fax", "传真服务",
                "传真功能，几乎无人使用。", false, false));

            list.Add(MakeService("MapsBroker", "下载地图管理器",
                "为地图应用在后台下载数据。", false, false));

            list.Add(MakeService("WMPNetworkSvc", "WMP 网络共享服务",
                "共享 Windows Media Player 媒体库。", false, false));

            list.Add(MakeService("XblAuthManager", "Xbox 身份验证管理器",
                "Xbox 账号与成就同步，不使用 Xbox 时可禁用。", false, false));

            list.Add(MakeService("XblGameSave", "Xbox 游戏保存",
                "Xbox 游戏存档同步，不使用 Xbox 时可禁用。", false, false));

            list.Add(MakeService("XboxNetApiSvc", "Xbox Live 网络服务",
                "Xbox Live 网络连接，不使用 Xbox 时可禁用。", false, false));

            list.Add(MakeService("XboxGipSvc", "Xbox 访问服务",
                "Xbox 配件驱动，无 Xbox 手柄时可禁用。", false, false));

            list.Add(MakeService("RetailDemo", "零售演示服务",
                "商店演示环境用，家用电脑可禁用。", false, false));

            list.Add(MakeService("PeerDistSvc", "分支缓存 (传递优化后台)",
                "P2P 内容缓存，家用网络可禁用。", false, false));

            list.Add(MakeService("lmhosts", "TCP/IP NetBIOS 助手",
                "旧式 NetBIOS 名称解析，现代网络可禁用。", false, false));

            list.Add(MakeService("PcaSvc", "程序兼容性助手",
                "在后台监测程序兼容性问题并弹提示，游戏时偶尔打扰。禁用不影响程序运行。", true, false));
            list.Add(MakeService("TrkWks", "分布式链接跟踪",
                "维护 NTFS 文件间的链接引用，普通单机用户几乎用不到。", true, false));
            list.Add(MakeService("WbioSrvc", "Windows 生物识别",
                "指纹/人脸登录服务。不用 Windows Hello 指纹或人脸解锁可禁用。", true, false));
            list.Add(MakeService("PhoneSvc", "电话服务",
                "管理设备电话号码信息，配合调制解调器使用，现代电脑基本无用。", true, false));
            list.Add(MakeService("SCardSvr", "智能卡服务",
                "智能卡读卡器支持。没有银行 U 盾/加密狗需求可禁用。", true, false));
            list.Add(MakeService("SCPolicySvc", "智能卡删除证书策略",
                "智能卡相关策略服务，与智能卡服务一起禁用。", true, false));
            list.Add(MakeService("SEMgrSvc", "支付和 NFC/SE 管理器",
                "NFC 移动支付相关，绝大多数台式机没有 NFC 硬件。", true, false));
            list.Add(MakeService("WpcMonSvc", "家长控制",
                "Microsoft 家庭安全家长控制，成年人环境可禁用。", true, false));
            list.Add(MakeService("SmsRouter", "Microsoft 短信路由",
                "接收/转发系统短信通知，配合手机网络模块，PC 上无用。", true, false));
            list.Add(MakeService("DoSvc", "传递优化",
                "Windows 更新 P2P 共享，禁用可停止占用上行带宽。", false, false));

            // 诊断策略系列（工具包服务清单的增量：常驻 CPU 的诊断后台）
            list.Add(MakeService("DPS", "诊断策略服务",
                "为诊断场景收集事件并执行策略评估，常驻 CPU/内存。禁用后系统内置的疑难解答（诊断向导）不可用。", true, false));
            list.Add(MakeService("diagsvc", "诊断服务执行",
                "按需执行诊断计划任务（自动疑难解答）。不用系统自带排错可禁用。", false, false));
            list.Add(MakeService("WdiServiceHost", "诊断服务主机",
                "承载诊断组件的后台进程，偶发 CPU 占用尖峰。禁用后自动诊断不可用。", false, false));
            list.Add(MakeService("WdiSystemHost", "诊断系统主机",
                "承载硬件级诊断探测。禁用后硬件自动诊断不可用。", false, false));
            list.Add(MakeService("DusmSvc", "数据使用量",
                "统计每个应用的网络用量（设置里的数据使用量页面），持续跟踪网络流量。", false, false));
            list.Add(MakeService("DsmSvc", "设备安装管理器",
                "按需联网检索设备驱动与元数据。驱动已装齐后可禁用（新设备将无法自动装驱动）。", false, false));
            list.Add(MakeService("wercplsupport", "问题报告与解决方案",
                "为「问题报告与解决」控制面板拉取错误报告数据。", false, false));

            // 内置老旧驱动禁用（来自游戏系统包，按值照搬）
            RegTweak legacyDrivers = new RegTweak();
            legacyDrivers.IdValue = "legacy_drivers_off";
            legacyDrivers.GroupValue = GServices;
            legacyDrivers.NameValue = "禁用老旧内置驱动（谨慎）";
            legacyDrivers.DescriptionValue = "禁用 1394 / 软驱 / 光驱 / CDFS / UDFS / Ndu 等现代游戏机用不到的驱动，减少内核加载项。用到光驱或 IEEE1394 设备勿开。需重启生效。";
            legacyDrivers.AdminOnlyValue = true;
            legacyDrivers.RiskyValue = true;
            string[] legacy = new string[]
            {
                "1394ohci", "AcpiPmi", "Beep", "bowser", "cdfs", "cdrom", "CSC", "dam",
                "fdc", "flpydisk", "HidIr", "Ndu", "scfilter", "sfloppy", "udfs"
            };
            for (int i = 0; i < legacy.Length; i++)
            {
                legacyDrivers.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + legacy[i], "Start", 4));
            }
            list.Add(legacyDrivers);

            return list;
        }

        private static ServiceTweak MakeService(string name, string display, string desc,
            bool recommended, bool risky)
        {
            ServiceTweak t = new ServiceTweak("svc_" + name);
            t.ServiceName = name;
            t.DisplayName = display;
            t.DescriptionValue = desc;
            t.RecommendedValue = recommended;
            t.RiskyValue = risky;
            return t;
        }

        // ---------------- 电源 ----------------

        private static IEnumerable<ITweak> Power()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(new ModernStandbyOffTweak());

            CommandTweak hibernate = new CommandTweak();
            hibernate.IdValue = "hibernate_off";
            hibernate.GroupValue = GPower;
            hibernate.NameValue = "关闭休眠功能";
            hibernate.DescriptionValue = "删除 hiberfil.sys，可释放与内存等大的磁盘空间（常见 4~32 GB）。" +
                "关闭后'快速启动'也会一并失效。";
            hibernate.RiskyValue = true;
            hibernate.EnableFile = "powercfg.exe";
            hibernate.EnableArgs = "/hibernate off";
            hibernate.RevertFile = "powercfg.exe";
            hibernate.RevertArgs = "/hibernate on";
            hibernate.Probe = delegate
            {
                return RegHelper.GetInt(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1) == 0;
            };
            list.Add(hibernate);

            CommandTweak highPerf = new CommandTweak();
            highPerf.IdValue = "power_high";
            highPerf.GroupValue = GPower;
            highPerf.NameValue = "启用高性能电源计划";
            highPerf.DescriptionValue = "锁定到高性能电源方案，CPU 不再为省电而降频。笔记本续航会下降。";
            highPerf.RiskyValue = true;
            highPerf.EnableFile = "powercfg.exe";
            highPerf.EnableArgs = "/setactive " + SchemeHighPerformance;
            highPerf.RevertFile = "powercfg.exe";
            highPerf.RevertArgs = "/setactive " + SchemeBalanced;
            highPerf.Probe = delegate
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/getactivescheme", 15000);
                return r.All.IndexOf(SchemeHighPerformance, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            list.Add(highPerf);

            CommandTweak fastBoot = new CommandTweak();
            fastBoot.IdValue = "disable_fast_startup";
            fastBoot.GroupValue = GPower;
            fastBoot.NameValue = "关闭快速启动";
            fastBoot.DescriptionValue = "避免快速启动导致的部分驱动异常、双系统时间错误等问题。";
            fastBoot.RiskyValue = true;
            fastBoot.EnableFile = "powercfg.exe";
            fastBoot.EnableArgs = "/hibernate off";
            fastBoot.Probe = delegate
            {
                return RegHelper.GetInt(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1) == 0;
            };
            list.Add(fastBoot);

            CommandTweak ultimate = new CommandTweak();
            ultimate.IdValue = "ultimate_perf";
            ultimate.GroupValue = GPower;
            ultimate.NameValue = "启用终极性能电源计划";
            ultimate.DescriptionValue = "终极性能计划移除节能节流与小核调度延迟，适合追求极限性能的高端台式机（笔记本/家用慎用）。";
            ultimate.RiskyValue = true;
            ultimate.EnableFile = "cmd.exe";
            ultimate.EnableArgs = "/c powercfg /duplicatescheme " + SchemeUltimate + " && powercfg /setactive " + SchemeUltimate;
            ultimate.RevertFile = "powercfg.exe";
            ultimate.RevertArgs = "/setactive " + SchemeBalanced;
            ultimate.Probe = delegate
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/getactivescheme", 15000);
                return r.All.IndexOf(SchemeUltimate, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            list.Add(ultimate);

            return list;
        }

        public const string SchemeHighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string SchemeBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string SchemeUltimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        /// <summary>一键优化时使用的推荐项 id 集合。</summary>
        private static IEnumerable<ITweak> Network()
        {
            List<ITweak> list = new List<ITweak>();

            // 「网卡节能全关」已由游戏优化组的 nic_power_save_off 覆盖（键集为其子集），不再重复提供

            list.Add(new NicAdvancedTweak("nic_latency_off",
                "关闭网卡合并与流控（RSC / 包合并 / 流控）",
                "关闭接收段合并 (RSC)、数据包合并 (Packet Coalescing) 与流量控制，减少网卡缓冲合包带来的延迟，是竞技游戏网络的经典优化。",
                new string[] { "*RSCIPv4", "*PacketCoalescing", "*FlowControl" },
                new string[0]));

            list.Add(new NetshTweak("rsc_global_off",
                "关闭全局接收段合并 (RSC)",
                "全局禁用 TCP 接收段合并，与逐网卡 RSC 关闭配合，降低下载与游戏并存时的延迟。重启后仍保持。",
                new string[] { "int tcp set global rsc=disabled" },
                new string[] { "int tcp set global rsc=enabled" },
                "int tcp show global",
                new string[] { @"(接收段合并状态|Receive Segment Coalescing State)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("ecn_off",
                "关闭 TCP ECN 功能",
                "禁用显式拥塞通知，部分游戏服务器/路由对其兼容不佳，关闭后网络行为更传统稳定。",
                new string[] { "int tcp set global ecncapability=disabled" },
                new string[] { "int tcp set global ecncapability=enabled" },
                "int tcp show global",
                new string[] { @"(ECN 功能|ECN Capability)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("tcp_timestamps_off",
                "关闭 TCP 时间戳",
                "禁用 RFC 1323 时间戳，减少每个包 12 字节头部开销与协议处理，低延迟场景更干净。",
                new string[] { "int tcp set global timestamps=disabled" },
                new string[] { "int tcp set global timestamps=enabled" },
                "int tcp show global",
                new string[] { @"(RFC 1323 时间戳|RFC 1323 Timestamps)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("tcp_heuristics_off",
                "关闭 TCP 窗口缩放启发式",
                "禁用按比例改变窗口的试探算法，避免回程流量干扰时窗口被意外缩小，竞技网络常用。",
                new string[] { "int tcp set heuristics disabled" },
                new string[] { "int tcp set heuristics enabled" },
                "int tcp show heuristics",
                new string[] { @"(窗口缩放启发|Window Scaling heuristics)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("tcp_fast_retrans",
                "加速 TCP 重传（SYN×3 / 初始 RTO 1s）",
                "把 SYN 重传次数降为 3、初始重传超时降为 1000ms，连接建立失败更快暴露、重连更快。",
                new string[] { "int tcp set global maxsynretransmissions=3", "int tcp set global initialrto=1000" },
                new string[] { "int tcp set global maxsynretransmissions=2", "int tcp set global initialrto=3000" },
                "int tcp show global",
                new string[]
                {
                    @"(最大 SYN 重新传输次数|Maximum SYN Retransmissions)\s*:\s*3",
                    @"(初始 RTO|Initial RTO)\s*:\s*1000"
                },
                false));

            list.Add(new NetshTweak("ipv6_tunnel_off",
                "禁用 IPv6 隧道（Teredo / ISATAP / 6to4）",
                "关闭三个过渡隧道技术，减少后台隧道探测与地址协商对网络的干扰。不影响原生 IPv6 上网。",
                new string[]
                {
                    "interface teredo set state disabled",
                    "int isatap set state disabled",
                    "int ipv6 6to4 set state disabled"
                },
                new string[]
                {
                    "interface teredo set state default",
                    "int isatap set state default",
                    "int ipv6 6to4 set state default"
                },
                "interface teredo show state",
                new string[] { @"(类型|Type|状态|State)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetBindingTweak());

            list.Add(new NetshTweak("bbr2_on",
                "启用 BBRv2 拥塞控制 (Win11 22H2+)",
                "把 TCP 拥塞控制切换为 Google BBRv2，弱网/跨境环境下延迟更稳。需 Win11 22H2 及以上；不支持的模板会失败。",
                new string[]
                {
                    "int tcp set supplemental template=internet congestionprovider=bbr2",
                    "int tcp set supplemental template=internetcustom congestionprovider=bbr2",
                    "int tcp set supplemental template=datacenter congestionprovider=bbr2",
                    "int tcp set supplemental template=datacentercustom congestionprovider=bbr2",
                    "int tcp set supplemental template=compat congestionprovider=bbr2"
                },
                new string[]
                {
                    "int tcp set supplemental template=internet congestionprovider=default",
                    "int tcp set supplemental template=internetcustom congestionprovider=default",
                    "int tcp set supplemental template=datacenter congestionprovider=default",
                    "int tcp set supplemental template=datacentercustom congestionprovider=default",
                    "int tcp set supplemental template=compat congestionprovider=default"
                },
                "int tcp show supplemental",
                new string[] { @"bbr2" },
                true));

            RegTweak ipv6Off = new RegTweak();
            ipv6Off.IdValue = "ipv6_off";
            ipv6Off.GroupValue = GNetwork;
            ipv6Off.NameValue = "完全禁用 IPv6";
            ipv6Off.DescriptionValue = "设 DisabledComponents=0xFF，彻底关闭 IPv6 协议栈。纯 IPv4 环境可减少干扰；使用 IPv6 宽带/内网发现的环境勿开。需重启生效。";
            ipv6Off.AdminOnlyValue = true;
            ipv6Off.RiskyValue = true;
            ipv6Off.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 255));
            list.Add(ipv6Off);

            RegTweak dnsPrio = new RegTweak();
            dnsPrio.IdValue = "dns_priority";
            dnsPrio.GroupValue = GNetwork;
            dnsPrio.NameValue = "DNS 解析与 TCP 连接优化";
            dnsPrio.DescriptionValue = "调整名称解析顺序（hosts > 本地缓存 > DNS > NetBIOS），并扩大 TCP 连接表容量（MaxUserPort/MaxFreeTcbs）与缩短 TIME_WAIT 回收（TcpTimedWaitDelay=30），多连接场景不易卡顿。";
            dnsPrio.AdminOnlyValue = true;
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "Class", 8));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 6));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "HostsPriority", 5));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority", 4));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "NetbtPriority", 7));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort", 65534));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxFreeTcbs", 20000));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay", 30));
            // DNS 缓存强化 + TCP 保活（Juxic 网络方案的增量部分）
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheEntryTtlLimit", 86400));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NetFailureCacheTime", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveTime", 60000));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveInterval", 1000));
            list.Add(dnsPrio);

            RegTweak smb = new RegTweak();
            smb.IdValue = "lanman_smb_tuning";
            smb.GroupValue = GNetwork;
            smb.NameValue = "SMB 文件共享吞吐调优";
            smb.DescriptionValue = "加大 SMB 客户端（Lanman Workstation）的命令队列、收集计数与线程数，局域网拷贝与跨机读写吞吐更饱满。不用局域网共享的环境无感，可随时还原。";
            smb.AdminOnlyValue = true;
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCmds", 100));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCollectionCount", 32));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxThreads", 30));
            list.Add(smb);

            return list;
        }

        public static List<string> RecommendedIds()
        {
            return new List<string>(new string[]
            {
                "menu_anim", "visual_fx", "startup_delay_zero",
                "show_file_ext", "disable_shake",
                "ad_id_off", "copilot_off", "bing_search_off",
                "svc_DiagTrack", "svc_dmwappushservice", "svc_RemoteRegistry"
            });
        }
    }
}
