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
        /// <summary>检测本机显卡厂商：返回全部命中厂商的集合串（如双显卡笔记本返回 "IN"），
        /// 调用方用 Contains('N')/Contains('A') 判断，避免"第一个命中"漏掉独立显卡。</summary>
        public static string DetectVendor()
        {
            string found = "";
            for (int i = 0; i < 6; i++)
            {
                string desc = RegHelper.GetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i, "DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;
                string d = desc.ToLowerInvariant();
                if ((d.Contains("nvidia") || d.Contains("geforce") || d.Contains("quadro")) && !found.Contains("N")) found += "N";
                if ((d.Contains("amd") || d.Contains("radeon") || d.Contains("ati")) && !found.Contains("A")) found += "A";
                if ((d.Contains("intel") || d.Contains("arc")) && !found.Contains("I")) found += "I";
            }
            return found;
        }

        private static bool WriteSet(string backupId, string[] keys)
        {
            bool all = true;
            for (int i = 0; i < 4; i++)
            {
                foreach (string k in keys)
                {
                    // 设备实例键专用写入：不存在的实例跳过，不制造幽灵键
                    if (!RegHelper.SetValueOnExisting(RegistryHive.LocalMachine, GpuClass + "\\000" + i, k, 1,
                        RegistryValueKind.DWord, backupId)) all = false;
                }
            }
            // PciLatencyTimerControl 用 0x20（N 卡惯例值）
            if (Array.IndexOf(keys, "D3PCLatency") >= 0)
            {
                if (!RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\0000", "PciLatencyTimerControl", 32,
                    RegistryValueKind.DWord, backupId)) all = false;
            }
            return all;
        }

        public bool IsApplied()
        {
            string vendor = DetectVendor();
            bool hasN = vendor.Contains("N"), hasA = vendor.Contains("A");
            if (!hasN && !hasA) return false; // 本机显卡不在适用范围
            string[] keys = hasA ? AmdKeys : NvidiaKeys;
            string probe = keys[0];
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, GpuClass + "\\0000", probe);
            try { return v != null && Convert.ToInt32(v) == 1; }
            catch { return false; }
        }

        public bool Apply()
        {
            string vendor = DetectVendor();
            // 多厂商集合语义：双显卡笔记本 N/A 两套都写（各自命中即写）
            bool hasN = vendor.Contains("N"), hasA = vendor.Contains("A");
            if (!hasN && !hasA) return false; // Intel 核显 / 未知显卡拒绝
            RegHelper.BeginBackup(Id);
            bool ok = true;
            if (hasN) ok &= WriteSet(Id, NvidiaKeys);
            if (hasA) ok &= WriteSet(Id, AmdKeys);
            return ok;
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
}
