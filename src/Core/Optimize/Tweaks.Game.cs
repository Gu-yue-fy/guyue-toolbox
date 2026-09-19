/* ============================================================
 * 文件说明：优化项库「Game」组的全部优化项声明。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
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
            // 注意：任务级值在 ...\Tasks\Games（非 SystemProfile\Games）
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "AlwaysOn", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Latency Sensitive", "True"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Clock Rate", 5000));
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
            // DWM性能优化：投影阴影/设备位图/输入预测
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableProjectedShadows", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableProjectedShadowsRendering", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "DisableDeviceBitmaps", 1));
            dwmDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "InteractionOutputPredictionDisabled", 1));
            list.Add(dwmDeep);

            // ---- 14.叠加补充 / 10.超目标优先 吸收：显卡呈现与固件 ----

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
                "EnableGpuFirmware=1 启用 GPU 固件调度（DSP，需 N 卡 560+ 驱动）、LOWLATENCY=1 与 D3PCLatency=1 降低显示链路延迟。该项风险较高，仅建议 N 卡用户尝试，出问题还原即可。",
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
            nvDeep.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("N"); });
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
            amdDeep.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("A"); });
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
            // Clock Rate 由「系统调度偏向游戏 (MMCSS)」一项写入（5000），此处不再重复写入：
            // 两项写同一个值会让后应用者与前者的备份/状态判定互相干扰，
            // 本项已用 SchedulerTimerResolution 控制计时分辨率，功能不缺失
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
                // 真实探测：DISM 查询保留存储状态（Disabled → 已应用）
                Shell.Result r = Shell.Run("dism.exe", "/Online /Get-ReservedStorageState", 60000);
                if (!r.Ok) return false;
                return r.All.IndexOf("State : Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       r.All.IndexOf("状态 : Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       r.All.IndexOf(": Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // Windows电源事件策略稳定：低延迟/游戏模式事件权重提到最高
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
            // 厂商适用性：仅 N/A 卡写入（Intel 核显 / 未知显卡拒绝，避免无效键）
            gpuPstate.ApplicableValue = delegate
            {
                string v = GpuLatencyTweak.DetectVendor();
                return v.Contains("N") || v.Contains("A");
            };
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
            // 三种优先级取值范围不同：CPU 1/6，IO 0-3，Page 0-5——按 CPU 值分别映射
            for (int r = 0; r < prioRoots.Length; r++)
            {
                for (int e = 0; e < prioTargets.Length; e++)
                {
                    string perfPath = prioRoots[r] + "\\" + prioTargets[e][0] + "\\PerfOptions";
                    int cpu = int.Parse(prioTargets[e][1], System.Globalization.CultureInfo.InvariantCulture);
                    int[] mapped = new int[]
                    {
                        cpu,                          // CpuPriorityClass 原值
                        cpu >= 4 ? 3 : 0,             // IoPriority 合法范围 0-3
                        cpu >= 4 ? 5 : 1              // PagePriority 合法范围 0-5
                    };
                    for (int v = 0; v < prioValues.Length; v++)
                    {
                        sysPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, perfPath,
                            prioValues[v], mapped[v]));
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
                @"Software\Microsoft\GameBar", "ShowStartupPanel", 0));
            gamebarTips.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\GameBar", "GamePanelStartupTipIndex", 3));
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
                // OnlyIfExists：不存在的声卡实例跳过，不制造幽灵设备键
                RegWrite w1 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "ConservationIdleTime", zero4);
                RegWrite w2 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "PerformanceIdleTime", zero4);
                RegWrite w3 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "IdlePowerState", zero4);
                w1.OnlyIfExists = true; w2.OnlyIfExists = true; w3.OnlyIfExists = true;
                audioIdle.Enable.Add(w1);
                audioIdle.Enable.Add(w2);
                audioIdle.Enable.Add(w3);
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

    }
}
