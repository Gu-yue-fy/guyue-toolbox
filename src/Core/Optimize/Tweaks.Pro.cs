/* 文件说明：优化项库「补充批次 Pro」——既有项尚未覆盖的残余高价值键，逐键查重后落地。
 *           含两个设备级扫描项（网卡兼容性能键、DMA 重映射关闭），全部走 Engine 备份链可还原。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>
        /// 补充批次（Pro）：逐键查重后仅落地既有项未覆盖的高价值键——
        /// 1. 内核工作线程池扩容（微软文档值 Additional*WorkerThreads）；
        /// 2. Smart App Control 关闭（拖慢应用加载且上报数据）；
        /// 3. NVMe 空闲电源关闭（stornvme IdlePowerMode + 存储 Modern Standby D3）；
        /// 4. 电源遥测计量关闭（CPU 利用率估算/能耗估算/睡眠研究统计）；
        /// 5. OOBE 附属应用阻装（DevHome/Outlook 的 UScheduler 闸门）；
        /// 6. DMA 重映射关闭（逐驱动扫描）；
        /// 7. 网卡兼容性能键（AutoDisableGigabit/ApCompatMode/Sips/DMACoalescing）；
        /// 8. 传统游戏组件启用（DirectPlay + LegacyComponents）。
        /// </summary>
        public static List<ITweak> Pro()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------------------------------------------------------
            // 1. 内核工作线程池扩容：
            //    AdditionalCriticalWorkerThreads / AdditionalDelayedWorkerThreads 是微软文档记载的
            //    内核执行体工作线程增补值（默认 0），写 32 可让关键/延迟工作队列在重负载下
            //    （游戏读盘、驱动 DPC 密集时）不排队等待。需重启生效。
            // ---------------------------------------------------------------
            var workerThreads = RegTweak.Create("worker_threads_boost", GPerformance,
                "内核工作线程池扩容（+32）",
                "给内核执行体增补 32 个关键工作线程与 32 个延迟工作线程"
                + "（AdditionalCriticalWorkerThreads / AdditionalDelayedWorkerThreads，默认 0）。"
                + "驱动 DPC 与磁盘 I/O 密集时（游戏加载、后台解压）系统线程不再排队，响应更稳。"
                + "需重启生效。值过大无额外收益，32 为社区实测稳定档。",
                adminOnly: true, recommended: true);
            workerThreads.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Executive", "AdditionalCriticalWorkerThreads", 0x20));
            workerThreads.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Executive", "AdditionalDelayedWorkerThreads", 0x20));
            list.Add(workerThreads);

            // ---------------------------------------------------------------
            // 2. Smart App Control 关闭：
            //    VerifiedAndReputablePolicyState=0。SAC 开启时每个新启动的应用都要联网送微软云端
            //    预测评估，直接拖慢应用启动并产生上行数据；游戏加载器/修改器常被误杀。
            //    Windows 11 新装/重置后默认开启且设置页藏得很深，注册表是唯一可靠的关闭途径。
            // ---------------------------------------------------------------
            var sacOff = RegTweak.Create("smart_app_control_off", GSlim,
                "关闭智能应用控制 (Smart App Control)",
                "把代码完整性策略 VerifiedAndReputablePolicyState 置 0，关闭"
                + " Smart App Control。SAC 会把每个新启动的程序送微软云端做信誉评估：拖慢应用与游戏启动、"
                + "产生上行遥测，单机与网吧环境普遍建议关闭。关闭后 SmartScreen 照常工作。需重启生效。",
                adminOnly: true, recommended: true);
            sacOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", 0));
            list.Add(sacOff);

            // ---------------------------------------------------------------
            // 3. NVMe 空闲电源关闭：
            //    stornvme IdlePowerMode=0 让 NVMe 控制器永不进入低功耗态（进游戏/切地图时
            //    不再等待盘从省电唤醒）；StorageD3InModernStandby=0 禁止存储设备在待机时进 D3。
            //    台式机收益明显；笔记本会略微增加待机功耗。
            // ---------------------------------------------------------------
            var nvmeIdle = RegTweak.Create("nvme_idle_power_off", GGame,
                "NVMe 空闲电源关闭（零唤醒延迟）",
                "把 stornvme 驱动的 IdlePowerMode 置 0——NVMe 固态盘"
                + " 不再进入低功耗休眠，进游戏、切地图、加载资源时不再出现「盘唤醒」造成的 0.1~0.5 秒"
                + " 卡顿尖峰；同时禁止存储设备在待机时进入 D3（StorageD3InModernStandby=0）。"
                + " 台式机推荐；笔记本开启会增加少量待机功耗。仅 NVMe 设备有效，需重启生效。",
                adminOnly: true, recommended: true);
            nvmeIdle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IdlePowerMode", 0));
            nvmeIdle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Storage", "StorageD3InModernStandby", 0));
            list.Add(nvmeIdle);

            // ---------------------------------------------------------------
            // 4. 电源遥测计量关闭：
            //    关掉电源管理器内部的一组「估算/统计」开关——CPU 实际利用率估算、能耗估算、
            //    睡眠研究 (SleepStudy) 统计、热度遥测上报。这些统计服务于电池报告与待机诊断，
            //    桌式机与游戏机常年用不到，关闭可减掉一串周期性采样线程与事件日志。
            // ---------------------------------------------------------------
            var powerTel = RegTweak.Create("power_telemetry_off", GPower,
                "电源遥测与能耗统计关闭",
                "关闭电源管理器的内部计量：CPU 实际利用率估算"
                + " (PerfCalculateActualUtilization)、能耗估算 (EnergyEstimationEnabled)、睡眠研究统计"
                + " (SleepstudyAccountingEnabled)、热度遥测等级 (ThermalTelemetryVerbosity) 与 Fx 计费"
                + " 统计 (FxAccountingTelemetryDisabled)。这些服务于电池报告/待机诊断，台式机与游戏机"
                + " 用不到，关掉减少周期采样与事件日志写入。笔记本用户如需查看「电池使用情况」报告请保持关闭状态的本项。",
                adminOnly: true, recommended: true);
            const string CtlPower = @"SYSTEM\CurrentControlSet\Control\Power";
            powerTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "PerfCalculateActualUtilization", 0));
            powerTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "EnergyEstimationEnabled", 0));
            powerTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "SleepstudyAccountingEnabled", 0));
            powerTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "ThermalTelemetryVerbosity", 0));
            powerTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "FxAccountingTelemetryDisabled", 0));
            list.Add(powerTel);

            // ---------------------------------------------------------------
            // 5. OOBE 附属应用阻装：
            //    Windows 11 每次大更新后会借「OOBE/更新调度器」自动补装 DevHome、新版 Outlook
            //    等附属应用。把 UScheduler 的 workCompleted 写 1（任务已完成）让调度器跳过补装。
            //    键只写在已存在的调度器键上（OnlyIfExists），旧系统/已清理系统自动视为不适用。
            // ---------------------------------------------------------------
            var oobeBlock = RegTweak.Create("oobe_update_block", GSlim,
                "阻止更新自动补装 DevHome / Outlook",
                "Windows 11 大版本更新后会通过 OOBE 更新调度器自动补装"
                + " DevHub(DevHome) 与新版 Outlook 两个附属应用。把 UScheduler_Oobe 与 UScheduler 下"
                + " 对应任务的 workCompleted 置 1（标记已完成），更新后不再静默补装。"
                + " 仅对系统里存在这些调度器键的 Windows 11 生效；旧系统上本项保持未启用属正常。",
                adminOnly: true, recommended: true);
            oobeBlock.Enable.Add(OobeDone(@"UScheduler_Oobe\DevHomeUpdate"));
            oobeBlock.Enable.Add(OobeDone(@"UScheduler_Oobe\OutlookUpdate"));
            oobeBlock.Enable.Add(OobeDone(@"UScheduler\DevHomeUpdate"));
            oobeBlock.Enable.Add(OobeDone(@"UScheduler\OutlookUpdate"));
            list.Add(oobeBlock);

            // ---------------------------------------------------------------
            // 6. 网卡兼容性能键（五键里 ReduceSpeedOnPowerDown
            //    已由「网卡省电全关」覆盖，此处补齐其余四键）：
            //    AutoDisableGigabit=0 不自动降千兆、ApCompatMode=0 高性能模式、SipsEnabled=0
            //    关联机功耗节流、DMACoalescing=0 关 DMA 合并（Intel 官方文档承认会增加延迟）。
            // ---------------------------------------------------------------
            list.Add(new NicCompatTweak());

            // ---------------------------------------------------------------
            // 7. DMA 重映射关闭：
            //    逐驱动扫描 DmaRemappingCompatible 置 0，关闭内核 DMA 重映射（设备直通内存）。
            //    属安全换性能项：重映射开启时外设 DMA 要经 IOMMU 转换，增加一致性开销。
            // ---------------------------------------------------------------
            list.Add(new DmaRemapTweak());

            // ---------------------------------------------------------------
            // 8. 传统游戏组件启用：
            //    DirectPlay + LegacyComponents 是 2008 年前老游戏（红警2、帝国时代、浩方/游侠联机）
            //    的依赖组件，现代系统默认未装。DISM 启用后老游戏联机与保护模式不再报错。
            // ---------------------------------------------------------------
            CommandTweak legacy = new CommandTweak();
            legacy.IdValue = "legacy_gaming_features";
            legacy.GroupValue = GGame;
            legacy.NameValue = "启用传统游戏组件 (DirectPlay / LegacyComponents)";
            legacy.DescriptionValue =
                "用 DISM 启用 DirectPlay 与 LegacyComponents 两个"
                + " 可选功能——红警2、帝国时代、FPS 经典老游戏的局域网联机与兼容模式依赖它们，"
                + " 现代系统默认未安装。执行需要约 30 秒，完成后无需重启立即生效；"
                + " 不玩老游戏可随时停用（关闭这两个功能）。";
            legacy.RecommendedValue = false;
            legacy.EnableFile = "cmd.exe";
            legacy.EnableArgs = "/c dism /online /enable-feature /featurename:DirectPlay /norestart " +
                "&& dism /online /enable-feature /featurename:LegacyComponents /norestart";
            legacy.RevertFile = "cmd.exe";
            legacy.RevertArgs = "/c dism /online /disable-feature /featurename:DirectPlay /norestart " +
                "&& dism /online /disable-feature /featurename:LegacyComponents /norestart";
            legacy.Probe = delegate
            {
                Shell.Result r = Shell.Run("dism.exe",
                    "/online /get-featureinfo /featurename:DirectPlay", 90000);
                // dism 输出本地化：英文「State : Enabled」/ 中文「状态 : 已启用」，
                // 只看英文 "Enabled" 会在中文系统上恒判「未启用」
                string line = ProbeText.FindLine(r.All, "State", "状态");
                return ProbeText.SaysOn(line) && !ProbeText.SaysOff(line);
            };
            list.Add(legacy);

            return list;
        }

        /// <summary>构造 OOBE 调度器闸门写入：只写已存在的调度器键（不凭空造键）。</summary>
        private static RegWrite OobeDone(string subKey)
        {
            RegWrite w = RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\" + subKey, "workCompleted", 1);
            w.OnlyIfExists = true;
            return w;
        }

        /// <summary>
        /// 硬件 / 网络进阶项：
        /// - AMD 显卡节能与温控全关（KMD 系列 Chill/Boost/DeLag/USU + 温控节流 + DRM/DMA 电源门控），仅 A 卡适用。
        /// 去重说明：网卡省电关键字全集已被 nic_power_save_off（NicPowerTweak）逐字覆盖，本组不再重复新增
        /// nic_power_full；Nvidia RMHdcpKeyglobZero 已由 hdcp_off 覆盖、P-State 锁已由 gpu_max_pstate 的
        /// DisableDynamicPstate 覆盖；全局 Tcpip 参数（MaxSynRetransmissions/InitialRto/MaxUserPort/
        /// TcpTimedWaitDelay 等）已分别由 tcp_fast_retrans 与 dns_priority 覆盖，均不重复。
        /// </summary>
        public static List<ITweak> HardwareNet()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------- AMD 显卡节能 / 温控全关（仅 A 卡） ----------------
            const string GpuInstance =
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

            var amdPower = RegTweak.Create("amd_power_thermal_off", GGame,
                "A 卡节能与温控全关（仅 AMD）",
                "关闭 AMD 显卡的 Chill 省电、Radeon Boost、Anti-Lag、用户设置套用 (USU) 等 KMD 开关，" +
                "并禁用显存温控自动节流 (PP_ThermalAutoThrottlingEnable)、启用 DRM/DMA 电源门控关闭 " +
                "(DisableDrmdmaPowerGating=1) 与带外复制关闭 (DisableDMACopy=1)，消除核心节流/降频导致的帧率波动。"
                + "副作用：关闭温控节流后满载温度可能更高、待机功耗上升。仅 AMD 显卡可应用（非 A 卡本项恒为未启用），需重启生效。",
                adminOnly: true, risky: true);
            amdPower.ApplicableWhen(new GpuVendorCondition("A"));
            // 主显卡实例子键（0000）
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "KMD_ChillEnabled", 0));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "KMD_RadeonBoostEnabled", 0));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "KMD_DeLagEnabled", 0));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "KMD_USUEnable", 0));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "PP_ThermalAutoThrottlingEnable", 0));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "DisableDrmdmaPowerGating", 1));
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, GpuInstance, "DisableDMACopy", 1));
            // 服务级 Chill 开关（amdwddmg）
            amdPower.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\amdwddmg", "ChillEnabled", 0));
            list.Add(amdPower);

            return list;
        }
    }

    /// <summary>
    /// 网卡兼容性能键：对每个网卡实例写入四项有据可查的性能键——
    /// AutoDisableGigabit=0（电池模式不自动降千兆）、ApCompatMode=0（高性能模式）、
    /// SipsEnabled=0（关闭关联机功耗节流）、DMACoalescing=0（关闭 DMA 合并，
    /// Intel 官方文档确认启用会增加传输延迟）。实例枚举与「网卡省电全关」同一套
    /// 4 位数字子键规则，只写已存在的网卡实例键，凭空不造键。
    /// </summary>
    public sealed class NicCompatTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>键名 → 目标值。网卡高级关键字均为 REG_SZ。</summary>
        private static readonly string[] Keys = new string[]
        {
            "AutoDisableGigabit", "ApCompatMode", "SipsEnabled", "DMACoalescing"
        };

        public string Id { get { return "nic_compat_perf"; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return "网卡兼容性能键"; } }
        public string Description
        {
            get
            {
                return "对每个网卡实例写入四项有据可查的性能键：不自动降千兆"
                    + " (AutoDisableGigabit=0)、高性能兼容模式 (ApCompatMode=0)、关闭关联机功耗节流 (SipsEnabled=0)、"
                    + " 关闭 DMA 合并 (DMACoalescing=0，Intel 官方文档确认其增加传输延迟)。"
                    + " 与「网卡省电全关」互补（那组管节能开关，本组管性能/兼容键），需重启或重插网卡生效。";
            }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return true; } }

        /// <summary>只取形如 0000 的驱动实例子键（跳过 Properties 等），与 NicPowerTweak 同规则。</summary>
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
                    for (int j = 0; j < Keys.Length; j++)
                    {
                        object v = k.GetValue(Keys[j]);
                        if (v != null && v.ToString() != "0") return false;
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
            bool ok = true;
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = 0; j < Keys.Length; j++)
                {
                    ok &= RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], Keys[j],
                        "0", RegistryValueKind.String, Id);
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
    /// DMA 重映射关闭：扫描 HKLM\SYSTEM\CurrentControlSet\Services 下所有
    /// 已声明 DmaRemappingCompatible 的驱动，逐个备份后置 0。内核不再对这些设备做
    /// IOMMU 地址重映射，设备直读直写物理内存，省掉一层转换开销。
    /// 属「安全换性能」：重映射是防外设攻击 DMA 的缓解措施，仅在可信设备环境下开启。
    /// </summary>
    public sealed class DmaRemapTweak : ITweak
    {
        private const string ServicesBase = @"SYSTEM\CurrentControlSet\Services";

        public string Id { get { return "dma_remapping_off"; } }
        public string Group { get { return TweakLibrary.GExtreme; } }
        public string Name { get { return "关闭驱动 DMA 重映射（逐驱动）"; } }
        public string Description
        {
            get
            {
                return "扫描所有声明了 DmaRemappingCompatible 的驱动，"
                    + "逐个备份后置 0——内核不再对它们做 IOMMU DMA 地址重映射，外设直读直写内存省掉一层转换。"
                    + "属安全换性能：DMA 重映射是防恶意外设攻击的缓解措施，仅在可信设备（无来路不明采集卡/扩展坞）"
                    + "的环境下建议开启本项。使用雷电采集卡的直播用户请勿开启。需重启生效，还原即恢复重映射。";
            }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        /// <summary>收集当前 DmaRemappingCompatible 非 0 的驱动服务名。</summary>
        private static List<string> ServicesWithRemap()
        {
            List<string> found = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(ServicesBase, false))
                {
                    if (root == null) return found;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        using (RegistryKey k = root.OpenSubKey(subs[i], false))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("DmaRemappingCompatible");
                            if (v == null) continue;
                            int n;
                            try { n = Convert.ToInt32(v); }
                            catch { continue; }
                            if (n != 0) found.Add(subs[i]);
                        }
                    }
                }
            }
            catch
            {
            }
            return found;
        }

        public bool IsApplied()
        {
            return ServicesWithRemap().Count == 0;
        }

        public bool Apply()
        {
            List<string> targets = ServicesWithRemap();
            if (targets.Count == 0) return false;
            RegHelper.BeginBackup(Id);
            bool ok = true;
            for (int i = 0; i < targets.Count; i++)
            {
                ok &= RegHelper.SetValue(RegistryHive.LocalMachine,
                    ServicesBase + "\\" + targets[i], "DmaRemappingCompatible",
                    0, RegistryValueKind.DWord, Id);
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }
}
