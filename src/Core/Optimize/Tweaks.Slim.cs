/* ============================================================
 * 文件说明：优化项库「Slim」组的全部优化项声明。
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

            // ---------------- 系统精简（第二批：冗余组件与服务） ----------------

            RegTweak uar = new RegTweak();
            uar.IdValue = "steps_recorder_off";
            uar.GroupValue = GSlim;
            uar.NameValue = "禁用步骤记录器与遥测收集";
            uar.DescriptionValue = "关闭「步骤记录器」（Problem Steps Recorder）与相关的应用兼容性遥测收集——多数用户从未使用，禁用后少一个常驻采集组件。";
            uar.AdminOnlyValue = true;
            uar.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableUAR", 1));
            list.Add(uar);

            RegTweak smb1 = new RegTweak();
            smb1.IdValue = "smb1_off";
            smb1.GroupValue = GSlim;
            smb1.NameValue = "禁用 SMB1 老协议组件";
            smb1.DescriptionValue = "SMBv1 是存在严重漏洞（如 WannaCry 传播途径）的过时文件共享协议，Win10/11 默认已弃用。禁用后无法访问 2008 以前的极老 NAS/共享设备，其余场景更安全。";
            smb1.AdminOnlyValue = true;
            smb1.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1", 0));
            list.Add(smb1);

            RegTweak phoneLink = new RegTweak();
            phoneLink.IdValue = "phone_link_off";
            phoneLink.GroupValue = GSlim;
            phoneLink.NameValue = "禁用手机连接（手机链接）";
            phoneLink.DescriptionValue = "「手机连接」用于 Android/iPhone 与电脑联动，不使用时其服务与后台进程纯属常驻。禁用后应用无法配对手机，还原即可恢复。";
            phoneLink.AdminOnlyValue = true;
            phoneLink.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\YourPhoneService", "Start", 4));
            phoneLink.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableMmx", 0));
            list.Add(phoneLink);

            RegTweak frameServer = new RegTweak();
            frameServer.IdValue = "frame_server_off";
            frameServer.GroupValue = GSlim;
            frameServer.NameValue = "禁用摄像头帧服务器";
            frameServer.DescriptionValue = "「Windows 相机帧服务器」为多应用同时共享摄像头提供服务，只用一个相机应用（如 QQ/微信/Teams 之一）时可以禁用；使用摄像头录制的应用请还原本项。";
            frameServer.AdminOnlyValue = true;
            frameServer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\FrameServer", "Start", 4));
            list.Add(frameServer);

            RegTweak mapsUpdate = new RegTweak();
            mapsUpdate.IdValue = "maps_update_off";
            mapsUpdate.GroupValue = GSlim;
            mapsUpdate.NameValue = "禁用地图自动更新";
            mapsUpdate.DescriptionValue = "「地图」应用会在后台自动下载离线地图数据，占用磁盘与流量。不用 Windows 自带地图的直接禁用（配合系统服务里的「地图代理」效果更完整）。";
            mapsUpdate.AdminOnlyValue = true;
            mapsUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\Maps", "AutoUpdateEnabled", 0));
            list.Add(mapsUpdate);

            RegTweak xboxNet = new RegTweak();
            xboxNet.IdValue = "xbox_net_services_off";
            xboxNet.GroupValue = GSlim;
            xboxNet.NameValue = "禁用 Xbox 网络服务";
            xboxNet.DescriptionValue = "关闭 Xbox 游戏存档同步与联机网络服务（配合系统服务里的「Xbox 身份验证管理器」覆盖全套）。不玩微软商店版联机游戏的可禁用；Steam/本地游戏不受影响。";
            xboxNet.AdminOnlyValue = true;
            xboxNet.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\XblGameSave", "Start", 4));
            xboxNet.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\XboxNetApiSvc", "Start", 4));
            xboxNet.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\XboxGipSvc", "Start", 4));
            list.Add(xboxNet);

            RegTweak edgeBoost = new RegTweak();
            edgeBoost.IdValue = "edge_startup_boost_off";
            edgeBoost.GroupValue = GSlim;
            edgeBoost.NameValue = "禁用 Edge 后台预启动";
            edgeBoost.DescriptionValue = "Edge 默认开机预启动并常驻后台（哪怕你从未打开它），只为加快自身启动。禁用后 Edge 启动略慢约零点几秒，但开机更干净、内存占用更少。";
            edgeBoost.AdminOnlyValue = true;
            edgeBoost.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0));
            edgeBoost.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0));
            list.Add(edgeBoost);

            // ---------------- 精调系列（吸收自社区游戏优化素材，与库内已有项冲突时以本库为准） ----------------

            // GPU 抢占禁用：GPU 被多应用共享时会抢占造成输入延迟；单游戏场景收益明显
            RegTweak gpuPreempt = new RegTweak();
            gpuPreempt.IdValue = "gpu_preemption_off";
            gpuPreempt.GroupValue = GExtreme;
            gpuPreempt.NameValue = "禁用 GPU 抢占";
            gpuPreempt.DescriptionValue = "禁止 GPU 在多个应用间切换抢占，降低游戏输入延迟与帧生成波动。多任务（边游戏边直播/录制）时可能画面卡顿——直播用户请勿开启。";
            gpuPreempt.RiskyValue = true;
            gpuPreempt.AdminOnlyValue = true;
            gpuPreempt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", "EnablePreemption", 0));
            list.Add(gpuPreempt);

            // D3D 预渲染帧：驱动提前渲染的帧数压到 1，降低输入延迟（帧率可能略降）
            RegTweak d3dPreRender = new RegTweak();
            d3dPreRender.IdValue = "d3d_prerender_1";
            d3dPreRender.GroupValue = GGame;
            d3dPreRender.NameValue = "Direct3D 预渲染帧数 = 1";
            d3dPreRender.DescriptionValue = "把驱动提前渲染的帧数压到 1，画面更跟手、输入延迟更低；对 CPU 较弱的机器帧率可能小幅下降。";
            d3dPreRender.AdminOnlyValue = true;
            d3dPreRender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\WOW6432Node\Microsoft\Direct3D", "MaxPreRenderedFrames", 1));
            list.Add(d3dPreRender);

            // DWM 帧队列：排队缓冲键（MaxQueuedPresentBuffers）库内「DWM 呈现模式」已覆盖，仅补 ForceDirectDrawSync
            RegTweak dwmQueue = new RegTweak();
            dwmQueue.IdValue = "dwm_directdraw_sync_off";
            dwmQueue.GroupValue = GGame;
            dwmQueue.NameValue = "关闭 DWM 强制 DirectDraw 同步";
            dwmQueue.DescriptionValue = "关闭合成器的强制 DirectDraw 同步路径，画面呈现更直接。与「DWM 呈现模式」搭配使用效果更完整。";
            dwmQueue.AdminOnlyValue = true;
            dwmQueue.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\DWM", "ForceDirectDrawSync", 0));
            list.Add(dwmQueue);

            // 中断转向禁用：中断统一交由 BSP 处理，部分平台 DPC 延迟更稳定
            RegTweak intSteer = new RegTweak();
            intSteer.IdValue = "interrupt_steering_off";
            intSteer.GroupValue = GExtreme;
            intSteer.NameValue = "禁用内核中断转向";
            intSteer.DescriptionValue = "让设备中断不再分散到各 CPU 核心，部分平台 DPC 延迟更平稳。多核弱 CPU 上可能反而变差——逐台实测决定。";
            intSteer.RiskyValue = true;
            intSteer.AdminOnlyValue = true;
            intSteer.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "InterruptSteeringDisabled", 1));
            list.Add(intSteer);

            // 分离大型缓存
            RegTweak splitCache = new RegTweak();
            splitCache.IdValue = "split_large_caches";
            splitCache.GroupValue = GExtreme;
            splitCache.NameValue = "分离内核大型缓存";
            splitCache.DescriptionValue = "把内核的大型缓存按核心分离，减少核心间缓存争抢，特定工作负载下延迟更低。属于实验性内核参数，异常时还原即可。";
            splitCache.RiskyValue = true;
            splitCache.AdminOnlyValue = true;
            splitCache.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "SplitLargeCaches", 1));
            list.Add(splitCache);

            // 电源事件处理器禁用
            RegTweak eventProc = new RegTweak();
            eventProc.IdValue = "power_event_processor_off";
            eventProc.GroupValue = GPower;
            eventProc.NameValue = "禁用电源事件处理器";
            eventProc.DescriptionValue = "关闭电源计划的动态事件处理（亮度/热区等响应），减少电源子系统唤醒。台式机收益更明显；笔记本可能影响亮度自动调节。";
            eventProc.RiskyValue = true;
            eventProc.AdminOnlyValue = true;
            eventProc.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power", "EventProcessorEnabled", 0));
            list.Add(eventProc);

            // 核心进程大页面：dwm/csrss/explorer/services/svchost/ntoskrnl 启用大页
            RegTweak largePages = new RegTweak();
            largePages.IdValue = "large_pages_core_procs";
            largePages.GroupValue = GExtreme;
            largePages.NameValue = "核心进程启用大页面";
            largePages.DescriptionValue = "让 dwm/csrss/explorer/services/svchost 等核心进程用大内存页（减少 TLB miss）。需重启生效，内存占用小幅上升；需 16GB 以上内存更稳妥。";
            largePages.RiskyValue = true;
            largePages.AdminOnlyValue = true;
            string[] coreProcs = new string[] { "audiodg.exe", "csrss.exe", "dwm.exe", "explorer.exe", "ntoskrnl.exe", "rundll32.exe", "services.exe", "svchost.exe" };
            for (int i = 0; i < coreProcs.Length; i++)
            {
                largePages.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + coreProcs[i],
                    "UseLargePages", 1));
            }
            list.Add(largePages);

            // N 卡：每 CPU 核心 DPC（5 处键）
            RegTweak nvDpc = new RegTweak();
            nvDpc.IdValue = "nv_per_cpu_core_dpc";
            nvDpc.GroupValue = GGame;
            nvDpc.NameValue = "N 卡：每 CPU 核心 DPC 处理";
            nvDpc.DescriptionValue = "让显卡中断的 DPC 在每个 CPU 核心上独立处理，配合「MSI 中断模式」可显著降低 DPC 延迟。仅 NVIDIA 显卡适用。";
            nvDpc.AdminOnlyValue = true;
            nvDpc.ApplicableValue = delegate { return GpuLatencyTweak.DetectVendor().Contains("N"); };
            string[] dpcPaths = new string[]
            {
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Power",
                @"SYSTEM\CurrentControlSet\Services\nvlddmkm",
                @"SYSTEM\CurrentControlSet\Services\nvlddmkm\NVAPI",
                @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak"
            };
            for (int i = 0; i < dpcPaths.Length; i++)
            {
                nvDpc.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, dpcPaths[i],
                    "RmGpsPsEnablePerCpuCoreDpc", 1));
            }
            list.Add(nvDpc);

            // N 卡遥测：库内 Gpu.cs 已有 nv_telemetry_off（GpuInstanceTweak 版），此处不再重复——冲突以本库既有实现为准

            // HDCP 禁用（主显卡；RMHdcpKeyglobZero 为 NVIDIA 专属键，A/Intel 卡自动隐藏）
            RegTweak hdcp = new RegTweak();
            hdcp.IdValue = "hdcp_off";
            hdcp.GroupValue = GGame;
            hdcp.NameValue = "禁用显卡 HDCP 加密";
            hdcp.DescriptionValue = "关闭显示输出的 HDCP 内容加密握手，部分游戏/串流场景首帧延迟更低。需要播放受版权保护的高清内容（蓝光/流媒体 App）的用户请勿开启。作用于主显卡（GPU 0），仅 NVIDIA 显卡适用。";
            hdcp.AdminOnlyValue = true;
            hdcp.ApplicableValue = delegate { return GpuLatencyTweak.DetectVendor().Contains("N"); };
            hdcp.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000",
                "RMHdcpKeyglobZero", 1));
            list.Add(hdcp);

            // ---------------- 系统精简（第三批：SapphireOS 素材，冲突以本库既有项为准） ----------------

            RegTweak onlineTips = new RegTweak();
            onlineTips.IdValue = "online_tips_off";
            onlineTips.GroupValue = GPrivacy;
            onlineTips.NameValue = "关闭系统在线小贴士";
            onlineTips.DescriptionValue = "设置首页不再弹出「获取建议/提示」类在线内容推送。";
            onlineTips.AdminOnlyValue = true;
            onlineTips.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "AllowOnlineTips", 0));
            list.Add(onlineTips);

            RegTweak typingInsights = new RegTweak();
            typingInsights.IdValue = "typing_insights_off";
            typingInsights.GroupValue = GPrivacy;
            typingInsights.NameValue = "关闭输入洞察";
            typingInsights.DescriptionValue = "停止记录你的输入习惯用于「键入见解」功能（触摸键盘词库个性化），输入历史不再留存云端。";
            typingInsights.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\input\Settings", "InsightsEnabled", 0));
            list.Add(typingInsights);

            // CEIP：库内已有 ceip_off，此处不再重复——冲突以本库既有实现为准

            RegTweak psTelemetry = new RegTweak();
            psTelemetry.IdValue = "powershell_telemetry_off";
            psTelemetry.GroupValue = GPrivacy;
            psTelemetry.NameValue = "关闭 PowerShell 遥测";
            psTelemetry.DescriptionValue = "设置 POWERSHELL_TELEMETRY_OPTOUT 环境变量，PowerShell 不再上报使用数据。";
            psTelemetry.AdminOnlyValue = true;
            psTelemetry.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "POWERSHELL_TELEMETRY_OPTOUT", "1"));
            list.Add(psTelemetry);

            RegTweak cloudNotify = new RegTweak();
            cloudNotify.IdValue = "cloud_notifications_off";
            cloudNotify.GroupValue = GPrivacy;
            cloudNotify.NameValue = "禁用推送通知联网同步";
            cloudNotify.DescriptionValue = "应用推送通知不再经微软云通道转发（WNS），通知全部走本地，减少后台联网。个别依赖云推送的应用通知可能延迟。";
            cloudNotify.AdminOnlyValue = true;
            cloudNotify.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\PushNotifications", "NoCloudApplicationNotification", 1));
            list.Add(cloudNotify);

            // IFEO 拦截遥测进程：CompatTelRunner（兼容性遥测）/AggregatorHost/CrossDeviceResume 启动即被终结
            RegTweak ifeoBlock = new RegTweak();
            ifeoBlock.IdValue = "ifeo_telemetry_block";
            ifeoBlock.GroupValue = GPrivacy;
            ifeoBlock.NameValue = "拦截遥测进程启动";
            ifeoBlock.DescriptionValue = "用 IFEO 调试器机制让 CompatTelRunner（兼容性遥测）、AggregatorHost、CrossDeviceResume 三个遥测进程一启动就被终结。与「服务管理」里的 DiagTrack 禁用互补。";
            ifeoBlock.RiskyValue = true;
            ifeoBlock.AdminOnlyValue = true;
            string[] blockedProcs = new string[] { "CompatTelRunner.exe", "AggregatorHost.exe", "CrossDeviceResume.exe" };
            for (int i = 0; i < blockedProcs.Length; i++)
            {
                ifeoBlock.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + blockedProcs[i],
                    "debugger", "%WINDIR%\\System32\\taskkill.exe"));
            }
            list.Add(ifeoBlock);

            // 后台进程优先级调优：把常驻后台服务的 CPU/IO 优先级压低，让前台游戏拿满资源
            RegTweak bgPriority = new RegTweak();
            bgPriority.IdValue = "ifeo_bg_priority";
            bgPriority.GroupValue = GExtreme;
            bgPriority.NameValue = "后台进程优先级调优";
            bgPriority.DescriptionValue = "通过 IFEO 把 ctfmon（输入法）、SearchIndexer（索引）、fontdrvhost（字体）、sihost、sppsvc（授权）、csrss 等后台进程的 CPU/IO 优先级压低，前台应用资源更充裕。属于激进调度调整，异常时还原。";
            bgPriority.RiskyValue = true;
            bgPriority.AdminOnlyValue = true;
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\ctfmon.exe\PerfOptions", "CpuPriorityClass", 5));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\SearchIndexer.exe\PerfOptions", "CpuPriorityClass", 5));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\fontdrvhost.exe\PerfOptions", "CpuPriorityClass", 1));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\fontdrvhost.exe\PerfOptions", "IoPriority", 0));
            // lsass.exe 不再在此写入：它与「系统进程优先级重排」项写同一个值（均为 1），
            // 重复写入会让两个项的备份归属与"已启用"判定互相干扰，保留在该专项即可
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sihost.exe\PerfOptions", "CpuPriorityClass", 1));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sihost.exe\PerfOptions", "IoPriority", 0));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sppsvc.exe\PerfOptions", "CpuPriorityClass", 1));
            bgPriority.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sppsvc.exe\PerfOptions", "IoPriority", 0));
            list.Add(bgPriority);

            // Superfetch 事件日志通道关闭
            RegTweak sfLog = new RegTweak();
            sfLog.IdValue = "superfetch_evtlog_off";
            sfLog.GroupValue = GPerformance;
            sfLog.NameValue = "关闭 SysMain 事件日志通道";
            sfLog.DescriptionValue = "SysMain（预读取）的三个事件日志通道不再记录，减少日志写入；配合「服务管理」里的 SysMain 调整效果更完整。";
            sfLog.AdminOnlyValue = true;
            string[] sfChannels = new string[]
            {
                @"Microsoft-Windows-Superfetch/Main",
                @"Microsoft-Windows-Superfetch/PfApLog",
                @"Microsoft-Windows-Superfetch/StoreLog"
            };
            for (int i = 0; i < sfChannels.Length; i++)
            {
                sfLog.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Channels\" + sfChannels[i], "Enable", 0));
            }
            list.Add(sfLog);

            // 文件夹类型不探测：打开文件夹时不逐文件解析类型
            RegTweak folderType = new RegTweak();
            folderType.IdValue = "folder_type_fast";
            folderType.GroupValue = GPerformance;
            folderType.NameValue = "文件夹打开不再逐文件探测";
            folderType.DescriptionValue = "让资源管理器打开文件夹时按通用模板显示，不再逐个解析文件来猜测文件夹类型——包含大量文件的目录打开速度明显加快。";
            folderType.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell",
                "FolderType", "NotSpecified"));
            list.Add(folderType);

            // 任务栏右键直接结束任务
            RegTweak taskbarEnd = new RegTweak();
            taskbarEnd.IdValue = "taskbar_end_task";
            taskbarEnd.GroupValue = GAppearance;
            taskbarEnd.NameValue = "任务栏右键显示「结束任务」";
            taskbarEnd.DescriptionValue = "Win11 任务栏图标右键菜单直接提供「结束任务」，程序卡死时不用再开任务管理器。";
            taskbarEnd.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings",
                "TaskbarEndTask", 1));
            list.Add(taskbarEnd);

            // 自动播放关
            RegTweak autoplay = new RegTweak();
            autoplay.IdValue = "autoplay_off";
            autoplay.GroupValue = GPerformance;
            autoplay.NameValue = "关闭自动播放";
            autoplay.DescriptionValue = "插入 U 盘/光盘不再自动运行内容（防autorun病毒 + 少一次弹窗）。";
            autoplay.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1));
            list.Add(autoplay);

            // 定时器合并扩展禁用（Windows 键，REG_BINARY 全零）
            RegTweak timerCoalescing = new RegTweak();
            timerCoalescing.IdValue = "timer_coalescing_windows_off";
            timerCoalescing.GroupValue = GExtreme;
            timerCoalescing.NameValue = "禁用 Windows 层计时器合并";
            timerCoalescing.DescriptionValue = "在 Windows 层彻底关闭定时器合并（与「内核层 CoalescingTimer」互补），定时唤醒更精确、抖动更低。待机功耗略增。";
            timerCoalescing.RiskyValue = true;
            timerCoalescing.AdminOnlyValue = true;
            timerCoalescing.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "TimerCoalescing", new byte[32]));
            timerCoalescing.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "RITdemonTimerPowerSaveElapse", 0));
            timerCoalescing.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "RITdemonTimerPowerSaveCoalescing", 0));
            list.Add(timerCoalescing);

            // 附件区域标记
            RegTweak zoneInfo = new RegTweak();
            zoneInfo.IdValue = "save_zone_info_off";
            zoneInfo.GroupValue = GPerformance;
            zoneInfo.NameValue = "不再标记下载文件来源";
            zoneInfo.DescriptionValue = "下载/复制的文件不再写入「来自网络」的区域标记，打开时少一次安全确认。从不可信来源下载文件时请自行留意。";
            zoneInfo.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments", "SaveZoneInformation", 1));
            // 桌面堆日志关
            RegTweak deskHeap = new RegTweak();
            deskHeap.IdValue = "desktop_heap_log_off";
            deskHeap.GroupValue = GPerformance;
            deskHeap.NameValue = "关闭桌面堆诊断日志";
            deskHeap.DescriptionValue = "关闭桌面堆（Desktop Heap）的诊断日志记录，减少无用的系统日志写入。影响极小，纯白赚。";
            deskHeap.AdminOnlyValue = true;
            deskHeap.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "DesktopHeapLogging", 0));
            list.Add(deskHeap);

            return list;
        }

        // ---------------- 网络优化（吸收自社区游戏网络调整方案） ----------------

    }
}
