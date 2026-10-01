/* 文件说明：优化项库「Game」组——游戏模式、多媒体调度（MMCSS）、DWM 呈现与系统节流。 */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>
        /// Game 组：优化项最多的一组，按主题拆成 6 段顺序追加。
        /// 子方法的调用顺序 = 界面里的展示顺序，调整顺序会改变列表排列，勿随手调整。
        /// </summary>
        private static IEnumerable<ITweak> Game()
        {
            List<ITweak> list = new List<ITweak>();

            GameModeAndScheduling(list);        // 段 1：游戏模式、HUD/录制、调度与时钟
            GamePresentationAndThrottling(list);// 段 2：DWM 呈现、缓解与节流
            GameKernelAndInput(list);           // 段 3：内核、输入与 USB 深度精简
            GameStorageAndDevicePower(list);    // 段 4：存储、设备电源与进程优先级
            GameFullscreenAndSystemNoise(list); // 段 5：全屏优化与系统干扰
            GameIsolationAndPeripherals(list);  // 段 6：隔离、内核内存与输入外设

            return list;
        }

        /// <summary>段 1：游戏模式、Xbox 游戏栏/录制、硬件加速 GPU 调度、鼠标加速、MMCSS 调度、动态时钟、低帧延迟。</summary>
        private static void GameModeAndScheduling(List<ITweak> list)
        {
            var gameMode = RegTweak.Create("game_mode", GGame,
                "启用游戏模式",
                "让 Windows 在运行游戏时优先分配 CPU 与 GPU 资源，抑制后台更新与通知。对全屏游戏收益最明显；与「关闭游戏栏」搭配使用无冲突。",
                adminOnly: true);
            gameMode.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", 1));
            list.Add(gameMode);

            var gameBar = RegTweak.Create("game_bar_off", GGame,
                "关闭 Xbox 游戏栏",
                "禁用 Win+G 游戏栏覆盖层，避免游戏时弹出 ms-gamingoverlay 等干扰窗口。");
            gameBar.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0));
            list.Add(gameBar);

            var gameDvr = RegTweak.Create("game_dvr_off", GGame,
                "关闭游戏后台录制 (Game DVR)",
                "停止后台录制，降低显存占用与磁盘写入；使用其它录制软件（如 OBS）时建议关闭。");
            gameDvr.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_Enabled", 0));
            gameDvr.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
            list.Add(gameDvr);

            var hags = RegTweak.Create("hags_on", GGame,
                "启用硬件加速 GPU 调度",
                "让 GPU 直接管理显存调度，降低延迟、提升帧稳定性。需重启生效。",
                adminOnly: true, risky: true);
            hags.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2));
            list.Add(hags);

            var mouseAccel = RegTweak.Create("mouse_accel_off", GGame,
                "关闭鼠标指针加速度",
                "关闭“提高指针精确度”，让鼠标移动与物理位移 1:1，FPS 游戏瞄准更稳定。");
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseSpeed", 0));
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseThreshold1", 0));
            mouseAccel.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseThreshold2", 0));
            list.Add(mouseAccel);

            // —— 以下为低延迟游戏系统的安全向键值项 ——

            var mmcss = RegTweak.Create("mmcss_tuning", GGame,
                "多媒体调度（MMCSS）",
                "关闭网络节流（NetworkThrottlingIndex=0xFFFFFFFF），游戏任务调度拉满（GPU Priority=8/Priority=6/Scheduling Category=High/SFIO Priority=High），音频任务升到高优先级与高 SFIO；并按低延迟模板调整 MMCSS 调度器参数与各多媒体任务优先级，整体压低多媒体与网络延迟。需重启生效。（系统响应度 SystemResponsiveness 由独立的「系统响应度」项管理，避免备份冲突）",
                adminOnly: true, recommended: true);
            const string MMCSS = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            // 网络节流关闭：DWORD 0xFFFFFFFF（等价 -1）
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)));
            // 全局低延迟 + 调度器计时参数
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "AlwaysOn", 1));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "SchedulerTimerResolution", 5000));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "SchedulerPeriod", 100000));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "MaxThreadsPerProcess", 128));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "MaxThreadsTotal", 65535));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "IdleDetectionCycles", 5));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS, "LatencyIndicatorEnabled", 0));
            mmcss.Enable.Add(RegWrite.Remove(RegistryHive.LocalMachine, MMCSS, "LazyModeTimeout"));
            // 游戏任务：高优先级——注意路径在 Tasks\Games 下（非 SystemProfile\Games）
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "GPU Priority", 8));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "Priority", 6));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "Scheduling Category", "High"));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "SFIO Priority", "High"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "BackgroundPriority", 8));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "NoLazyMode", 1));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "Latency Sensitive", "True"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Games", "Clock Rate", 5000));
            // 音频任务：高优先级 + 高 SFIO，消除音频延迟 / 爆音
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Audio", "Priority", 6));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Audio", "SFIO Priority", "High"));
            // 播放 / 专业音频降为 Low：把时间片让给游戏与交互
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Playback", "Priority", 1));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Playback", "Scheduling Category", "Low"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Pro Audio", "Priority", 1));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Pro Audio", "Scheduling Category", "Low"));
            // 采集 / 分发 / 窗口管理升为 High：游戏直播与窗口交互更跟手
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Capture", "Priority", 8));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Capture", "Scheduling Category", "High"));
            mmcss.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Window Manager", "Priority", 8));
            mmcss.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine, MMCSS + @"\Tasks\Window Manager", "Scheduling Category", "High"));
            list.Add(mmcss);

            var dynTicks = RegTweak.Create("dynamic_ticks_off", GGame,
                "禁用动态时钟 (Dynamic Ticks)",
                "让内核时钟始终全速跳动，配合「计时器分辨率」消除 tick 省电导致的微卡顿。需重启生效。",
                adminOnly: true);
            dynTicks.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "DisableDynamicTicks", 1));
            list.Add(dynTicks);

            var d3dLow = RegTweak.Create("d3d_low_latency", GGame,
                "全局低帧延迟并强制关闭垂直同步（旧 3D 游戏）",
                "把 Direct3D 全局最大帧延迟设为 1、PresentInterval=0，降低旧 D3D 游戏的输入延迟。代价：画面可能撕裂，需要垂直同步的游戏勿开。",
                adminOnly: true, risky: true);
            d3dLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "MaxFrameLatency", 1));
            d3dLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "PresentInterval", 0));
            list.Add(d3dLow);
        }

        /// <summary>段 2：DWM 立即翻转、GPU 抢占超时、Meltdown/Spectre 缓解、MPO、EcoQoS 与 CPU 配额节流、驱动更新与兼容性助手。</summary>
        private static void GamePresentationAndThrottling(List<ITweak> list)
        {
            var dwmLow = RegTweak.Create("dwm_low_latency", GGame,
                "DWM 立即翻转与最小排队缓冲",
                "让桌面合成器立即翻转到屏幕并把排队缓冲降为 1，减少合成环节引入的一帧延迟。");
            dwmLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\DWM", "UseImmediateFlips", 1));
            dwmLow.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\DWM", "MaxQueuedPresentBuffers", 1));
            list.Add(dwmLow);

            var preempt = RegTweak.Create("gpu_preempt_short", GGame,
                "缩短 GPU 抢占超时",
                "把图形调度器的抢占超时从 8 降到 1，GPU 被单个负载长时间占用时更快让位，全屏游戏后台响应更及时。",
                adminOnly: true, risky: true);
            preempt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "PreemptTimeout", 1));
            list.Add(preempt);

            var meltdown = RegTweak.Create("meltdown_off", GGame,
                "关闭 Meltdown / Spectre 缓解",
                "关闭 CPU 侧信道漏洞缓解，游戏帧数与内存性能常见提升 5~15%，需重启生效。⚠ 安全代价极大：内核数据保护被解除，仅建议完全离线或纯游戏机使用，联网办公机勿开。",
                adminOnly: true, risky: true);
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettings", 1));
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride", 3));
            meltdown.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverrideMask", 3));
            list.Add(meltdown);

            var mpoOff = RegTweak.Create("mpo_off", GGame,
                "禁用 MPO（多平面叠加）",
                "官方级修复：解决部分显卡驱动下全屏游戏闪烁、掉帧、鼠标卡顿的问题（OverlayTestMode=5），NVIDIA 曾在官方说明中推荐。",
                adminOnly: true);
            mpoOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5));
            list.Add(mpoOff);

            var powerThrottle = RegTweak.Create("power_throttling_off", GGame,
                "关闭电源节流 (EcoQoS)",
                "禁止系统把后台线程调度到能效核或降频执行（PowerThrottlingOff=1），带鱼屏/大小核 CPU 上后台任务不再干扰游戏线程。",
                adminOnly: true);
            powerThrottle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1));
            // 根键双保险（部分系统版本读 Control\Power 下的同名值）
            powerThrottle.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "PowerThrottlingOff", 1));
            list.Add(powerThrottle);

            var cpuQuota = RegTweak.Create("cpu_quota_off", GGame,
                "关闭 CPU 配额节流",
                "禁用系统对后台进程的 CPU 配额限制（Quota System），游戏帧生成不再被调度器限流。",
                adminOnly: true);
            cpuQuota.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Quota System", "EnableCpuQuota", 0));
            list.Add(cpuQuota);

            var driverUpdate = RegTweak.Create("driver_update_off", GGame,
                "禁止 Windows 更新自动装驱动",
                "阻止 Windows Update 自动下载安装驱动，防止显卡/主板驱动被悄悄替换。驱动请从官方渠道手动安装。",
                adminOnly: true);
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DriverSearching", "DriverUpdateWizardWuSearchEnabled", 0));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1));
            driverUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata", "PreventDeviceMetadataFromNetwork", 1));
            list.Add(driverUpdate);

            var pcaOff = RegTweak.Create("pca_off", GGame,
                "关闭程序兼容性助手",
                "停止 PCA 在后台检测与提示程序兼容性问题，减少游戏时的干扰与后台扫描。");
            pcaOff.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisablePCA", 1));
            list.Add(pcaOff);
        }
    }
}
