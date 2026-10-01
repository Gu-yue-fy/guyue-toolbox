/* 文件说明：优化项库「补充注册表」组——MiscReg() + MiscReg2()，补齐尚未落地的注册表优化（系统调优、GPU 延迟渲染、DPC/DWM/显示合成补充、输入与多媒体调优）。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>
        /// 补充注册表优化项：逐条去重后落地为声明式 RegTweak
        /// （与「性能/游戏/隐私」各分组已覆盖的键不重复）。
        /// </summary>
        public static List<ITweak> MiscReg()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------------------------------------------------------
            // 1. 系统全局关机加速：fast_shutdown 已含 AutoEndTasks / WaitToKillAppTimeout /
            //    WaitToKillServiceTimeout / LowLevelHooksTimeout，此处只补缺失的 HungAppTimeout。
            // ---------------------------------------------------------------
            var hungKill = RegTweak.Create("shutdown_hung_timeout", GPerformance,
                "缩短程序无响应判定时间",
                "把无响应程序的等待阈值 HungAppTimeout 压到 2000ms，程序卡死后系统更快判定并结束它。与「加快关机与注销速度」互补：那一条已含 AutoEndTasks、WaitToKillAppTimeout 与 WaitToKillServiceTimeout，本项补齐其缺失的 HungAppTimeout。");
            hungKill.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Desktop", "HungAppTimeout", "2000"));
            list.Add(hungKill);

            // ---------------------------------------------------------------
            // 2. NTFS 存储流与调度低延迟：CacheSizeMB / PrefetchThreshold /
            //    EnablePriorityIO / EnableQpcBypass。PreemptTimeout 已由「缩短 GPU 抢占超时」覆盖，
            //    EnableLowLatencyVidmemAlloc 已由「显存低延迟分配模式」覆盖，此处不再重复。
            // ---------------------------------------------------------------
            const string StorageStream = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Storage\Streaming";
            var cros = RegTweak.Create("cros_storage_stream", GPerformance,
                "存储流缓存与调度低延迟",
                "把流式存储缓存 CacheSizeMB 提到 8MB、预测阈值 PrefetchThreshold 降到 2、开启优先级 I/O（EnablePriorityIO=1），并在图形调度器开启 QPC 旁路（EnableQpcBypass=1）绕过时钟换算，降低大量文件读写与调度时钟查询延迟。非游戏亦可受益，默认不推荐，需重启生效。",
                adminOnly: true, recommended: false);
            cros.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, StorageStream, "CacheSizeMB", 0x2000));
            cros.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, StorageStream, "PrefetchThreshold", 2));
            cros.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, StorageStream, "EnablePriorityIO", 1));
            cros.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "EnableQpcBypass", 1));
            list.Add(cros);

            // ---------------------------------------------------------------
            // 3. 内核 DPC / 手感低延迟：DpcQueueDepth / QuantumLength /
            //    HighResolutionTimers / DisableLowQosTimerResolution / TimeIncrement。
            //    ThreadDpcEnable 已由「内核低延迟精简」覆盖，CacheAwareScheduling 已由
            //    「Intel 调度与 TSX 修正」覆盖，此处跳过以免撞键。
            // ---------------------------------------------------------------
            const string Kernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            var dpcDeep = RegTweak.Create("kernel_dpc_deep", GGame,
                "内核 DPC 队列与调度量子深度（谨慎档）",
                "把 DPC 队列深度 DpcQueueDepth=1、调度量子 QuantumLength=1、高分辨率计时器 HighResolutionTimers=1、低 QoS 计时器分辨率禁用 DisableLowQosTimerResolution=1、计时增量 TimeIncrement=1 全部压到最小，缩短中断与调度延迟。属激进内核参数，异常时还原并重启即可。与「内核低延迟精简」「Intel 调度」互补（它们已含 ThreadDpcEnable 与 CacheAwareScheduling，此处不重复）。需重启生效。",
                adminOnly: true, risky: true);
            dpcDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DpcQueueDepth", 1));
            dpcDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "QuantumLength", 1));
            dpcDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "HighResolutionTimers", 1));
            dpcDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableLowQosTimerResolution", 1));
            dpcDeep.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "TimeIncrement", 1));
            list.Add(dpcDeep);

            // ---------------------------------------------------------------
            // 4. DWM 深度调优。
            //    已由 dwm_low_latency（UseImmediateFlips / MaxQueuedPresentBuffers）与
            //    dwm_deep_slim（DisableHologramCompositor / EnableDesktopOverlays /
            //    DisableProjectedShadows* / DisableDeviceBitmaps / InteractionOutputPredictionDisabled）
            //    覆盖的不再重复；仅补各处 .reg 独有的新键（调试/占位键与纯删除键跳过）。
            // ---------------------------------------------------------------
            const string DWM = @"SOFTWARE\Microsoft\Windows\DWM";
            var dwmExtra = RegTweak.Create("dwm_depth_extra", GGame,
                "DWM 深度调优补充（谨慎档）",
                "补齐合成器独有的深度键：并行模式策略 ParallelModePolicy、尺寸优化 EnableResizeOptimization、大矩形启用 EnableMegaRects、效果缓存 EnableEffectCaching、公共超集 EnableCommonSuperSets、WARP 绘制列表 UseHWDrawListEntriesOnWARP、重采样覆盖 ResampleModeOverride、低色 HighColor、动画归零 AnimationsShiftKey/AnimationAttributionEnabled、压平虚拟表面输入 FlattenVirtualSurfaceEffectInput、禁非绘制列表渲染 DisallowNonDrawListRendering、禁锁内存 DisableLockingMemory。与「DWM 深度精简」「DWM 立即翻转」互补，桌面特效进一步削减，异常时还原即可。",
                adminOnly: true, risky: true);
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "ParallelModePolicy", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "EnableResizeOptimization", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "EnableMegaRects", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "EnableEffectCaching", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "EnableCommonSuperSets", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "UseHWDrawListEntriesOnWARP", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "ResampleModeOverride", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "HighColor", 0));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "AnimationsShiftKey", 0));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "AnimationAttributionEnabled", 0));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "FlattenVirtualSurfaceEffectInput", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "DisallowNonDrawListRendering", 1));
            dwmExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "DisableLockingMemory", 1));
            list.Add(dwmExtra);

            // ---------------------------------------------------------------
            // 5. 鼠标延迟 / 键盘优化。
            //    MouseSpeed / MouseThreshold / MouseSensitivity / RawMouseThrottle 等已由
            //    mouse_accel_off / mouse_1to1 / mouse_feel_extra 覆盖，此处只补缺失键。
            //    注：reg 里的「SmoothMouse」是 DWORD（=0，关闭平滑），SmoothMouseX/YCurve
            //    （BINARY）已由「鼠标 1:1 原生移动」写入 MarkC 曲线，不重复。
            // ---------------------------------------------------------------
            var mouseExtra = RegTweak.Create("mouse_smooth_extra", GGame,
                "鼠标平滑与轨迹关闭",
                "关闭鼠标平滑 SmoothMouse=0、关闭指针轨迹 MouseTrails=0，指针移动路径更直接。SmoothMouseX/YCurve 曲线已由「鼠标 1:1 原生移动」管理，此处只补这两个缺失键。");
            mouseExtra.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SmoothMouse", 0));
            mouseExtra.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseTrails", "0"));
            list.Add(mouseExtra);

            // 键盘重复延迟.reg 里 KeyboardDelay=0 / KeyboardSpeed 已由「键盘响应调到最快」覆盖
            // （reg 的 KeyboardSpeed="500" 超出 0-31 合法范围，故沿用键盘项里合规的 31）。
            // 此处补键盘灵敏度.reg 里「筛选键」的弹跳/延时参数（Flags 与粘滞键项撞键，跳过）。
            const string KbdResp = @"Control Panel\Accessibility\Keyboard Response";
            var kbdExtra = RegTweak.Create("keyboard_response_extra", GGame,
                "键盘筛选键零延时",
                "把辅助功能「筛选键」的弹跳时间 BounceTime、接受前延时 DelayBeforeAcceptance、自动重复延迟 AutoRepeatDelay、自动重复速率 AutoRepeatRate 全部清零，键盘输入不再经过任何过滤延迟。主重复速率已由「键盘响应调到最快」管理（KeyboardDelay=0、KeyboardSpeed=31）。",
                adminOnly: false);
            kbdExtra.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser, KbdResp, "BounceTime", "0"));
            kbdExtra.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser, KbdResp, "DelayBeforeAcceptance", "0"));
            kbdExtra.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser, KbdResp, "AutoRepeatDelay", "0"));
            kbdExtra.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser, KbdResp, "AutoRepeatRate", "0"));
            list.Add(kbdExtra);

            // ---------------------------------------------------------------
            // 6. 系统优先级延迟.reg：NetworkThrottlingIndex / SystemResponsiveness / AlwaysOn /
            //    NoLazyMode 已分别由 mmcss_tuning 与 system_responsiveness_10 覆盖，
            //    这里只补缺失的 ExecuteQueueBoost。其它 .reg（临时文件分配策略/加快程序运行速度/
            //    网络加速/pci去除）为占位、仅还原或删除键、或改写 SubSystems 关键键，均无正向新键可落地，跳过。
            // ---------------------------------------------------------------
            const string MMCSS = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            var execBoost = RegTweak.Create("mmcss_execute_boost", GPerformance,
                "多媒体执行队列提升",
                "把 ExecuteQueueBoost 提到 0xFFFFFFFF，执行队列尽全力提升——其余键（NetworkThrottlingIndex、SystemResponsiveness、AlwaysOn、NoLazyMode）已由「多媒体调度（MMCSS）」与「MMCSS 后台配额」覆盖，此处只补该缺失键。",
                adminOnly: true);
            execBoost.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "ExecuteQueueBoost",
                unchecked((int)0xFFFFFFFF)));
            list.Add(execBoost);

            // ---------------------------------------------------------------
            // 7. 隐私补充：只补现有隐私/精简项没有的键。
            //    DODownloadMode=0 已由「关闭更新传递优化（P2P 上传）」经策略键覆盖，跳过并注明。
            // ---------------------------------------------------------------
            var privacySupp = RegTweak.Create("privacy_supplement", GPrivacy,
                "隐私补充",
                "补齐现有隐私项未覆盖的键：禁用锁屏摄像头、机器级广告标识、蓝牙广告、消息同步、生物识别、密码显示、WMDRM 联网、语言列表外发，关闭更新传递的系统级下载模式与网络位置感知（NlaSvc）的主动联网探测。传递优化的策略键 DODownloadMode=0 已由「关闭更新传递优化」覆盖，此处不重复。",
                adminOnly: true);
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenCamera", 1));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\PolicyManager\current\device\Bluetooth", "AllowAdvertising", 0));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Messaging", "AllowMessageSync", 0));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Biometrics", "Enabled", 0));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CredUI", "DisablePasswordReveal", 1));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\WMDRM", "DisableOnline", 1));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization", "SystemSettingsDownloadMode", 0));
            privacySupp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\NlaSvc\Parameters\Internet", "EnableActiveProbing", 0));
            list.Add(privacySupp);

            return list;
        }

        /// <summary>
        /// 补充注册表优化项（第二批）：N/A 卡两套 GPU 延迟渲染、DPC 延迟强化、
        /// DWM 帧延迟、显示合成、键盘/鼠标响应补充与多媒体优先级。
        /// 逐键去重：与 Game/Performance/Privacy 各分组及 MiscReg() 已覆盖的键不重复；
        /// 已覆盖键与冲突键在此放弃并注释原因。
        /// </summary>
        public static List<ITweak> MiscReg2()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------------------------------------------------------
            // （已删除）原「一、N 卡 GPU 延迟渲染」六组条目：gpu_dx_perf / nv_gpu_latency_core /
            // nv_gpu_render_fillrate / nv_gpu_display_sched / nv_gpu_hw_thread / nv_gpu_mem_firmware。
            //
            // 删除原因：它们写的是 OptimizeDXPerformance、EnableGPUFillRateBoost、
            // EnableGPUThreadOptimization、EnableGPUMemoryManagement、EnableGPUPerformanceMode 等
            // **Windows 与 NVIDIA 驱动都不存在、也不读取的键名**（社区流传的偏方）。
            // 写进去既无任何效果，又顶着「谨慎」标记让用户以为动了系统核心开关——
            // 属于典型的"无用项"，故整组移除。原文件已备份到项目外。
            //
            // 路径约定（保留给后续真实条目）：GPU 相关统一写 CurrentControlSet
            // （ControlSet001 是历史副本，写入不生效）。
            // ---------------------------------------------------------------

            // ---------------------------------------------------------------
            // 二、DPC 延迟强化（A 卡 / N 卡均适用，不做厂商护栏）。
            //     两键都在 Control\Power 下，属内核中断/内存访问微调，risky=true。
            // ---------------------------------------------------------------
            const string CtlPower = @"SYSTEM\CurrentControlSet\Control\Power";
            var dpcIntBoost = RegTweak.Create("dpc_int_mem_boost", GGame,
                "DPC 中断与内存访问强化（谨慎档）",
                "开启中断处理优化 EnableInterruptHandlingOptimization 与高效内存访问 EnableEfficientMemoryAccess。属内核电源/中断微调，异常时还原并重启即可。",
                adminOnly: true, risky: true);
            dpcIntBoost.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "EnableInterruptHandlingOptimization", 1));
            dpcIntBoost.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, CtlPower, "EnableEfficientMemoryAccess", 1));
            list.Add(dpcIntBoost);

            // ---------------------------------------------------------------
            // 三、内核 DPC 工作分组与遥测开关：
            //     GroupDpcWorkers / DpcWorkerGroup 属内核 DPC 工作分组，risky=true；
            //     CeipEnable 属遥测开关，独立成项（隐私类），risky=false。
            // ---------------------------------------------------------------
            const string Kernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            var dpcWorkers = RegTweak.Create("kernel_dpc_workers", GGame,
                "内核 DPC 工作线程分组（谨慎档）",
                "GroupDpcWorkers=1 / DpcWorkerGroup=1，让 DPC 中断按处理器组分组派发，减少中断密集场景下的调度开销。属激进内核参数，异常时还原并重启即可。",
                adminOnly: true, risky: true);
            dpcWorkers.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "GroupDpcWorkers", 1));
            dpcWorkers.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DpcWorkerGroup", 1));
            list.Add(dpcWorkers);

            var ceipOff = RegTweak.Create("ceip_kernel_off", GPrivacy,
                "关闭客户体验改善计划·内核域 (CEIP)",
                "把内核域 CeipEnable=0，禁用客户体验改善计划（SQM/CEIP）后台数据采集。与隐私分组的「退出客户体验改善计划」（SQMClient\\CEIPEnable）互补，此处仅补该内核域键，同名 Id 已让位以免撞键。",
                adminOnly: true);
            ceipOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "CeipEnable", 0));
            list.Add(ceipOff);

            // ---------------------------------------------------------------
            // 四、DWM 帧延迟与合成：
            //     UseImmediateFlips / MaxQueuedPresentBuffers 已由「DWM 立即翻转与最小排队缓冲」覆盖，
            //     ForceDirectDrawSync 已由系统精简分组覆盖，此处仅补 FrameLatency / DisableDWMComposition。
            // ---------------------------------------------------------------
            const string DWM = @"SOFTWARE\Microsoft\Windows\DWM";
            var dwmLatency = RegTweak.Create("dwm_frame_latency_extra", GGame,
                "DWM 帧延迟与合成补充（谨慎档）",
                "把合成器帧延迟 FrameLatency=2 并保持合成开启 DisableDWMComposition=0。UseImmediateFlips / MaxQueuedPresentBuffers 已由「DWM 立即翻转与最小排队缓冲」覆盖，ForceDirectDrawSync 已由系统精简分组覆盖，此处不重复。",
                adminOnly: true, risky: true);
            dwmLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "FrameLatency", 2));
            dwmLatency.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "DisableDWMComposition", 0));
            list.Add(dwmLatency);

            // ---------------------------------------------------------------
            // 五、显示合成与立即翻转补充：
            // ---------------------------------------------------------------
            var crosFlip = RegTweak.Create("cros_flip_synth", GGame,
                "显示合成与立即翻转补充（谨慎档）",
                "在图形调度器 Flip 键开启 EnableImmediateFlip，并在 DWM 开启合成向优化：DisableVSync=1 关垂直同步、FrameBufferSharpening=0 关帧缓冲锐化。其余相关键已由现有项覆盖，此处不重复。",
                adminOnly: true, risky: true);
            crosFlip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Flip", "EnableImmediateFlip", 1));
            crosFlip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "DisableVSync", 1));
            crosFlip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DWM, "FrameBufferSharpening", 0));
            list.Add(crosFlip);

            // ---------------------------------------------------------------
            // 六、鼠标响应补充
            // ---------------------------------------------------------------
            var cursorHw = RegTweak.Create("input_cursor_hw_extra", GGame,
                "输入补充·光标灵敏度与硬件按键",
                "把控制器光标灵敏度 CursorSensitivity 提到 0xFFFFFFFF（-1）、IR 遥控导航增量归零，并关闭硬件按键映射为虚拟键 HardwareButtonsAsVKeys=0。其余键已由「输入精简」覆盖，此处不重复。",
                adminOnly: true);
            cursorHw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\Input\Buttons", "HardwareButtonsAsVKeys", 0));
            cursorHw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\CursorSpeed", "CursorSensitivity",
                unchecked((int)0xFFFFFFFF)));
            cursorHw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\CursorSpeed", "IRRemoteNavigationDelta", 0));
            list.Add(cursorHw);

            // ---------------------------------------------------------------
            // 七、鼠标加速度系数归零
            // ---------------------------------------------------------------
            var mouseAccelZero = RegTweak.Create("mouse_accel_zero", GGame,
                "鼠标加速度系数归零",
                "把 MouseAccel / MouseAccel_Scale / MouseAccel_Max 三个加速度缩放系数归零，去除部分驱动/外壳额外的指针曲线缩放，配合「鼠标 1:1 原生移动」获得纯传感器位移。");
            mouseAccelZero.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseAccel", "0"));
            mouseAccelZero.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseAccel_Scale", "0"));
            mouseAccelZero.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseAccel_Max", "0"));
            list.Add(mouseAccelZero);

            // ---------------------------------------------------------------
            // 八、多媒体让渡优先级补充
            // ---------------------------------------------------------------
            const string MMCSS = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            var mmcssYield = RegTweak.Create("mmcss_yield_priority", GGame,
                "多媒体让渡优先级补充",
                "给 MMCSS 各任务补齐 Priority When Yielded：游戏/采集/分发/窗口管理等前台任务设为 19（0x13），播放/专业音频/显示后处理等后台任务设为 1。",
                adminOnly: true);
            string[] highYield = new string[] { "Games", "Capture", "Distribution", "Window Manager" };
            string[] lowYield = new string[] { "Playback", "Pro Audio", "DisplayPostProcessing" };
            for (int i = 0; i < highYield.Length; i++)
            {
                mmcssYield.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    MMCSS + @"\Tasks\" + highYield[i], "Priority When Yielded", 19));
            }
            for (int i = 0; i < lowYield.Length; i++)
            {
                mmcssYield.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    MMCSS + @"\Tasks\" + lowYield[i], "Priority When Yielded", 1));
            }
            list.Add(mmcssYield);

            return list;
        }
    }
}