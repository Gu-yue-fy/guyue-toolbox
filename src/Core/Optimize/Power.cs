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
}
