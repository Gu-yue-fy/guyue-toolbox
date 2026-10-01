/* 文件说明：优化项库「Game」组——DWM 深度精简、内核低延迟、Intel 调度、输入缓冲与设备电源。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {

        /// <summary>段 3：DWM 深度精简、显卡固件与链路延迟、原始输入与输入精简、USB 控制器低延迟、内核低延迟、Intel 调度。</summary>
        private static void GameKernelAndInput(List<ITweak> list)
        {
            var dwmDeep = RegTweak.Create("dwm_deep_slim", GGame,
                "DWM 深度精简（谨慎）",
                "关闭全息合成器、桌面叠加与交互输出预测，为游戏让出合成器资源。部分桌面特效会减少。",
                adminOnly: true, risky: true);
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

            var mouseFeel = RegTweak.Create("mouse_feel_extra", GGame,
                "关闭原始输入节流",
                "关闭 Win11 原始输入节流（RawMouseThrottle），高回报率鼠标移动更跟手。去抖（DebounceTime）由「鼠标驱动响应修正」统一管理。",
                adminOnly: true);
            mouseFeel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "RawMouseThrottleEnabled", 0));
            mouseFeel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "RawMouseThrottleForced", 0));
            list.Add(mouseFeel);

            // ---- 黑白 吸收：N/A 卡链路延迟深度、输入精简、USB 低延迟、游戏进程优先级 ----

            GpuInstanceTweak nvDeep = new GpuInstanceTweak(
                "nvidia_latency_deep",
                "N 卡链路延迟深度（仅 NVIDIA，谨慎）",
                "N 卡驱动级延迟键：RMDeepLlEntryLatencyUsec、Node3DLowLatency、PciLatencyTimerControl、" +
                "VRDirectFlip 时序余量、vrr 游标/消抖余量、RmGpsPsEnablePerCpuCoreDpc 等全部压到最小值。" +
                "黑白包 N 卡导入方案。仅 NVIDIA 显卡可应用——A 卡/Intel 机器上本项自动判定为不适用。",
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

            // —— 借鉴 optimizerDuck 的显卡厂商专属功耗/时钟门控（各自仅对应厂商生效） ——

            GpuInstanceTweak amdPower = new GpuInstanceTweak(
                "amd_power_gating",
                "A 卡功耗与时钟门控（仅 AMD）",
                "关闭 AMD 显卡动态降功耗与时钟门控（DisablePowerGating=1、PP_GPUPowerDownEnabled=0），" +
                "并禁用 UVD/VCE 视频时钟门控（DisableVCEPowerGating=1、DisableVceClockGating=1、EnableUvdClockGating=0），" +
                "让显卡始终满状态运行，消除视频/游戏里的偶发升降频卡顿。注意 DisableDynamicPstate（锁 P0）已由「显卡维持最高性能状态」覆盖，此处不再重复。" +
                "待机功耗上升。仅 AMD 显卡适用。需重启生效。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("DisablePowerGating", 1),
                    new KeyValuePair<string, object>("PP_GPUPowerDownEnabled", 0),
                    new KeyValuePair<string, object>("DisableVCEPowerGating", 1),
                    new KeyValuePair<string, object>("DisableVceClockGating", 1),
                    new KeyValuePair<string, object>("EnableUvdClockGating", 0)
                });
            amdPower.RiskyValue = true;
            amdPower.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("A"); });
            list.Add(amdPower);

            GpuInstanceTweak amdAspm = new GpuInstanceTweak(
                "amd_aspm_off",
                "A 卡关闭 PCIe ASPM 节能（仅 AMD）",
                "禁用 AMD 显卡的 PCIe ASPM L0s/L1 节能（EnableAspmL0s=0、EnableAspmL1=0），" +
                "降低显卡与主板间链路进出节能态的延迟，对高刷新率/VR 更稳。仅 AMD 显卡适用。需重启生效。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("EnableAspmL0s", 0),
                    new KeyValuePair<string, object>("EnableAspmL1", 0)
                });
            amdAspm.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("A"); });
            list.Add(amdAspm);

            GpuInstanceTweak nvAsync = new GpuInstanceTweak(
                "nvidia_async_pstate",
                "N 卡关闭异步 P-state（仅 NVIDIA）",
                "DisableASyncPstates=1：关闭 N 卡异步电源状态切换，避免游戏负载突变时 P-state 抖动带来的偶发卡顿。" +
                "仅 NVIDIA 显卡适用。需重启生效。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("DisableASyncPstates", 1)
                });
            nvAsync.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("N"); });
            list.Add(nvAsync);

            GpuInstanceTweak intelFlip = new GpuInstanceTweak(
                "intel_async_flip",
                "Intel 核显关闭异步翻转（仅 Intel）",
                "Display1_DisableAsyncFlips=1：关闭 Intel 核显的异步翻转，降低显示流水线延迟，桌面/视频更跟手。仅 Intel 核显适用。需重启生效。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("Display1_DisableAsyncFlips", 1)
                });
            intelFlip.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("I"); });
            list.Add(intelFlip);

            GpuInstanceTweak intelVsync = new GpuInstanceTweak(
                "intel_adaptive_vsync",
                "Intel 核显关闭自适应垂直同步（仅 Intel）",
                "AdaptiveVsyncEnable=0：关闭 Intel 核显自适应垂直同步，避免插帧/节流造成偶发延迟。仅 Intel 核显适用。需重启生效。",
                new KeyValuePair<string, object>[] {
                    new KeyValuePair<string, object>("AdaptiveVsyncEnable", 0)
                });
            intelVsync.ApplicableWhen(delegate { return GpuLatencyTweak.DetectVendor().Contains("I"); });
            list.Add(intelVsync);

            // —— 借鉴 optimizerDuck：USB 控制器意外断电恢复抑制 ——
            var usbRecovery = RegTweak.Create("usb_recovery_off", GGame,
                "关闭 USB 意外断电恢复尝试",
                "AutomaticSurpriseRemoval\\AttemptRecoveryFromUsbPowerDrain=0：禁用 USB 控制器在意外断电后尝试恢复，" +
                "减少外设因供电抖动造成的反复重连/中断。需重启生效。",
                adminOnly: true);
            usbRecovery.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\USB\AutomaticSurpriseRemoval", "AttemptRecoveryFromUsbPowerDrain", 0));
            list.Add(usbRecovery);

            var inputPrecision = RegTweak.Create("input_precision", GGame,
                "输入精简（光标抑制/磁吸/触控可视化）",
                "关闭系统光标抑制补偿（EnableCursorSuppression，社区公认游戏手感项）、指针磁吸、触控死区跳转与可视化特效，鼠标键盘输入路径更直接。",
                adminOnly: true);
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

            var usbLowLatency = RegTweak.Create("usb_low_latency", GGame,
                "USB 控制器强制低延迟",
                "USBXHCI 强制低延迟模式、异步调度启用、传输缓冲扩到 4MB、集线器空闲超时归零——键鼠等 USB 设备的中断处理更快（黑白包 usb.bat 方案）。",
                adminOnly: true);
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
            var kernelLow = RegTweak.Create("kernel_low_latency", GGame,
                "内核低延迟精简（DPC / 计时器 / 缓解）",
                "全局化定时器精度请求、禁用 DPC 节流与计时器合并、关闭 I/O 计数与资源管理器 DEP，为游戏让出内核开销。需重启生效。",
                adminOnly: true, risky: true);
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
            // DPC 看门狗与队列深度
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
            // 内存与磁盘计数开销
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
            var intelSched = RegTweak.Create("intel_scheduling", GGame,
                "Intel 调度与 TSX 修正（仅 Intel CPU）",
                "CacheAwareScheduling=15 启用按缓存亲和的调度（混合架构/多缓存拓扑下减少跨缓存迁移），DisableTsx=0 恢复被社区脚本误关的 TSX 指令支持。仅 Intel 处理器有效——AMD 机器上本项自动判定为不适用。需重启生效。",
                adminOnly: true);
            intelSched.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "DisableTsx", 0));
            intelSched.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "CacheAwareScheduling", 15));
            intelSched.ApplicableWhen(new CpuVendorCondition("Intel"));
            list.Add(intelSched);

            list.Add(new PciAspmTweak());

            list.Add(new GpuLatencyTweak());

            list.Add(new CoreParkingTweak());

            list.Add(new MsiModeTweak());

            list.Add(new DiskLpmTweak());

            list.Add(new CpuIdleTweak());

            list.Add(new DscpTweak());
        }

        /// <summary>段 4：保留存储与存储感知、输入缓冲、USB 全链路省电禁用、电源延迟容忍归零、N/A 卡最高性能、系统进程优先级重排、设备省电全关。</summary>
        private static void GameStorageAndDevicePower(List<ITweak> list)
        {
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
                // 真实探测：DISM 查询保留存储状态（Disabled → 已应用）。
                // 输出会本地化（中文为「已保留存储状态 : 已禁用」），故按行做中英双匹配
                Shell.Result r = Shell.Run("dism.exe", "/Online /Get-ReservedStorageState", 60000);
                if (!r.Ok) return false;
                string line = ProbeText.FindLine(r.All, "Reserved", "保留存储");
                return ProbeText.SaysOff(line);
            };
            list.Add(reserved);

            var storageSense = RegTweak.Create("storage_sense_off", GPerformance,
                "禁用存储感知自动清理",
                "存储感知会在后台定期扫描并删除临时文件与回收站内容，扫描期间占用磁盘 I/O 与 CPU。禁用后由本工具的垃圾清理功能按需手动清理。",
                adminOnly: false);
            storageSense.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0));
            list.Add(storageSense);

            list.Add(new DevicePriorityTweak());

            var inputBuffer = RegTweak.Create("input_buffer", GGame,
                "键鼠缓冲队列（低延迟）",
                "缩小键盘与鼠标类驱动的数据队列（键盘 16 / 鼠标 8，键盘最多并发服务端口 8），输入包更快被处理，降低键鼠响应延迟。需重启生效。",
                adminOnly: true);
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 16));
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "MaximumPortsServiced", 8));
            inputBuffer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 8));
            list.Add(inputBuffer);

            var usbPowerAll = RegTweak.Create("usb_power_off_all", GGame,
                "USB 全链路省电禁用（控制器 / 集线器 / HID）",
                "关闭 XHCI 控制器中断调节与空闲断电、USB 选择性暂停、HID 空闲等待、音频设备省电与 D1-D3 延迟，鼠标键盘手柄全程在线。需重启生效。",
                adminOnly: true);
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

            var powerLatency = RegTweak.Create("power_latency_off", GGame,
                "电源延迟容忍归零",
                "把电源管理器的各类延迟容忍与退出延迟全部压到最小，系统不为省电而等待。需重启生效。",
                adminOnly: true);
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

            var gpuPstate = RegTweak.Create("gpu_max_pstate", GGame,
                "显卡维持最高性能状态（N/A 卡）",
                "N 卡禁用动态 Pstate（锁 P0，待机功耗上升），A 卡禁用 Sclk DeepSleep。消除核心降频回升造成的偶发卡顿。需重启生效。",
                adminOnly: true, risky: true);
            // 厂商适用性：仅 N/A 卡写入（Intel 核显 / 未知显卡拒绝，避免无效键）
            gpuPstate.ApplicableWhen(new GpuVendorCondition("N,A"));
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
                "关闭平台时钟与平台 tick、禁用动态 tick、TSC 同步策略设为默认——与「计时器分辨率」开关配合使用效果最佳。写入 BCD，需重启生效。",
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

            var sysPrio = RegTweak.Create("sys_proc_priority", GGame,
                "系统进程优先级重排（DWM/CSRSS 高，服务低）",
                "用 IFEO 把桌面窗口管理与 CSRSS 提到高优先、LSASS 与服务宿主降为空闲级，让 CPU 让位给前台游戏。需重启生效。",
                adminOnly: true);
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
                "关闭 Hypervisor 启动、VBS 与 IOMMU 虚拟化，消除虚拟化层对游戏的性能开销（与关闭内核隔离配套）。需重启生效。使用 Hyper-V / WSA / 部分反作弊的虚拟化功能勿开。",
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
        }
    }
}
