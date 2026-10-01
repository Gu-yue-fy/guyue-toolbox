/* 文件说明：优化项库「补充批次」——服务、遥测策略、调度优先级、网络参数与系统键值类优化项。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>
        /// 补充批次：服务禁用、遥测策略、调度优先级、网络参数与系统键值四组。
        /// 与既有 340 余项逐键去重：已覆盖键不再重复，仅落地未覆盖的注册表键 / 服务 / 命令。
        /// </summary>
        public static List<ITweak> Supplement()
        {
            List<ITweak> list = new List<ITweak>();

            // ===================================================================
            // 一、服务与引导
            // ===================================================================

            // 新增服务（未被服务页既有项覆盖的驱动与服务）
            list.Add(MakeSvc("svc_ucpd", "UCPD", "UCPD 用户连接体验驱动",
                "UCPD 是 Windows「已连接的用户体验」相关组件，家用环境可安全禁用。"));
            list.Add(MakeSvc("svc_netbt", "NetBT", "NetBIOS over TCP/IP 驱动（谨慎）",
                "禁用 NetBT 内核驱动，进一步停止 NetBIOS 名称服务与旧式共享发现（与「禁用 NetBIOS over TCP/IP」接口级设置互补）。依赖旧式名称解析的局域网设备可能受影响。", true));

            // SvcHostSplitDisable：禁用服务宿主拆分（按服务逐一设置，排除 Xbox 系）
            CommandTweak svcSplit = new CommandTweak();
            svcSplit.IdValue = "svchost_split_disable";
            svcSplit.GroupValue = GPerformance;
            svcSplit.NameValue = "禁用服务宿主拆分";
            svcSplit.DescriptionValue =
                "把每个非 Xbox 服务的 SvcHostSplitDisable 置 1，将共享服务并入更少的 svchost 进程，减少进程数与上下文切换（与「Svchost 服务合并」互补，走另一条开关）。";
            svcSplit.RecommendedValue = true;
            svcSplit.EnableFile = "powershell.exe";
            svcSplit.EnableArgs = "-NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services' | Where-Object { $_.PSChildName -notmatch 'Xbl|Xbox' } | ForEach-Object { if ($null -ne (Get-ItemProperty -Path $_.PSPath -Name Start -EA SilentlyContinue)) { Set-ItemProperty -Path $_.PSPath -Name SvcHostSplitDisable -Type DWord -Value 1 -Force } }\"";
            svcSplit.RevertFile = "powershell.exe";
            svcSplit.RevertArgs = "-NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services' | Where-Object { $_.PSChildName -notmatch 'Xbl|Xbox' } | ForEach-Object { Remove-ItemProperty -Path $_.PSPath -Name SvcHostSplitDisable -Force -EA SilentlyContinue }\"";
            svcSplit.Probe = delegate
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"$v = (Get-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Dnscache' -Name SvcHostSplitDisable -EA SilentlyContinue).SvcHostSplitDisable; Write-Output ('SVH=' + $v)\"",
                    60000);
                string text = r.All ?? "";
                return text.IndexOf("SVH=1", StringComparison.Ordinal) >= 0;
            };
            list.Add(svcSplit);

            // BCD 传统引导菜单
            BcdTweak bootMenu = new BcdTweak("bootmenu_legacy",
                "使用传统引导菜单（BCD）",
                "把引导菜单切换为传统（legacy）模式：开机选择操作系统时不再进入较慢的图形界面，双系统切换更快。属启动引导改动，谨慎使用。",
                new string[] { "/set bootmenupolicy legacy" },
                new string[] { "/set bootmenupolicy standard" },
                new string[] { @"bootmenupolicy\s+Legacy" },
                new string[0], true);
            bootMenu.GroupOverride = GPower;
            list.Add(bootMenu);

            // ===================================================================
            // 二、遥测与策略
            // ===================================================================

            list.Add(MakeSvc("svc_tcpipreg", "tcpipreg", "TCP/IP 注册表兼容型接口服务",
                "供旧式 TCP/IP 兼容接口查询使用（实验性）。现代程序几乎不依赖，可安全禁用。"));
            list.Add(MakeSvc("svc_wecsvc", "Wecsvc", "Windows 事件收集器",
                "从远程计算机收集并转发事件日志，普通单机用户用不到，可禁用。", true));

            // 遥测商业数据管线（DataCollection 策略键，未被 telemetry_off 覆盖）
            var telPipe = RegTweak.Create("telemetry_pipeline_off", GPrivacy,
                "关闭商业数据管线与遥测附属",
                "补全隐私项未覆盖的数据收集策略：关商业数据管线、设备名上报、企业认证代理、Edge 浏览数据、遥测选择通知与设置项、诊断日志/转储限制、预览构建上报等 9 个 DataCollection 策略键。AllowTelemetry 已由「关闭诊断遥测数据上报」负责，此处不重复。",
                adminOnly: true, recommended: true);
            const string DataColl = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "AllowCommercialDataPipeline", 0));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "AllowDeviceNameInTelemetry", 0));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "DisableEnterpriseAuthProxy", 1));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "MicrosoftEdgeDataOptIn", 0));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "DisableTelemetryOptInChangeNotification", 1));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "DisableTelemetryOptInSettingsUx", 1));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "LimitDiagnosticLogCollection", 1));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "LimitDumpCollection", 1));
            telPipe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, DataColl, "AllowBuildPreview", 0));
            list.Add(telPipe);

            // 飞行与实验功能（EnableConfigFlighting / AllowExperimentation）
            var flighting = RegTweak.Create("flighting_off", GPrivacy,
                "关闭 Windows 飞行与实验功能",
                "关闭预发布功能配置（EnableConfigFlighting=0）与系统级实验（AllowExperimentation=0），停止系统按微软 A/B 实验推送功能与后台联网探测。",
                adminOnly: true, recommended: true);
            flighting.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\PreviewBuilds", "EnableConfigFlighting", 0));
            flighting.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\PolicyManager\current\device\System", "AllowExperimentation", 0));
            list.Add(flighting);

            // WMI 自动记录器（SQMLogger / SetupPlatformTel）
            var autologger = RegTweak.Create("autologger_wmi_off", GPrivacy,
                "关闭 WMI 自动记录器（SQM / 安装遥测）",
                "把 WMI 自动记录器 SQMLogger 与 SetupPlatformTel 的 Start 置 0，停用 SQM 客户体验遥测与安装平台遥测的自动采集会话。",
                adminOnly: true, recommended: true);
            autologger.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\WMI\Autologger\SQMLogger", "Start", 0));
            autologger.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\WMI\Autologger\SetupPlatformTel", "Start", 0));
            list.Add(autologger);

            // CEIP 附加（策略层 SQMClient / App-V / IE / Messenger）
            var ceipExtra = RegTweak.Create("ceip_policy_off", GPrivacy,
                "退出客户体验改善计划·策略层",
                "补全策略层的 CEIP 退出：策略域 SQMClient、App-V、Internet Explorer、Windows Messenger 四处 CEIPEnable 全部关闭。SQMClient\\Windows 机器域已由「退出客户体验改善计划」覆盖，此处不重复。",
                adminOnly: true, recommended: true);
            ceipExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            ceipExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\AppV\CEIP", "CEIPEnable", 0));
            ceipExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Internet Explorer\SQM", "DisableCustomerImprovementProgram", 1));
            ceipExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Messenger\Client", "CEIP", 2));
            list.Add(ceipExtra);

            // AppCompat 附加（DisableEngine / SbEnable，其余已覆盖）
            var appCompatExtra = RegTweak.Create("appcompat_engine_off", GPrivacy,
                "关闭应用兼容性引擎与 SwitchBack",
                "关闭应用兼容性引擎（DisableEngine=1）与 SwitchBack 引擎（SbEnable=1）。DisablePCA / DisableUAR / AITEnable / DisableInventory 已由现有项覆盖，此处仅补这两个策略键。",
                adminOnly: true, recommended: true);
            appCompatExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableEngine", 1));
            appCompatExtra.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "SbEnable", 1));
            list.Add(appCompatExtra);

            // RSoP 日志关闭
            var rsop = RegTweak.Create("rsop_logging_off", GPerformance,
                "关闭 RSoP 组策略日志",
                "关闭组策略结果集（RSoP）日志记录，减少开机与策略刷新时的磁盘 I/O。",
                adminOnly: true, recommended: true);
            rsop.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "RSoPLogging", 0));
            list.Add(rsop);

            // 电源菜单显示「睡眠」项
            var sleepOpt = RegTweak.Create("sleep_option_show", GPower,
                "电源菜单显示「睡眠」项",
                "在开始菜单电源选项里显示「睡眠」条目。默认隐藏睡眠项时开启本项即可找回。",
                adminOnly: true, recommended: true);
            sleepOpt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FlyoutMenuSettings", "ShowSleepOption", 1));
            list.Add(sleepOpt);

            // ===================================================================
            // 三、调度优先级
            // ===================================================================

            // mmcss：时间关键优先级（TimeCriticalPriority / LowLatencyPriority）
            var timeCrit = RegTweak.Create("mmcss_time_critical", GGame,
                "时间关键 / 低延迟优先级",
                "把 SystemResponsiveness 的时间关键优先级与低延迟优先级置 1，让请求实时调度的任务（游戏/音频）获得更高的调度响应。SystemResponsiveness 配额本身已由「MMCSS 后台配额」管理，此处仅补这两个优先级开关。",
                adminOnly: true, recommended: true);
            timeCrit.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\SystemResponsiveness", "TimeCriticalPriority", 1));
            timeCrit.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\SystemResponsiveness", "LowLatencyPriority", 1));
            list.Add(timeCrit);

            // ===================================================================
            // 四、网络与系统键值
            // ===================================================================

            list.Add(MakeSvc("svc_xbgm", "xbgm", "Xbox 游戏监控服务",
                "监视 Xbox 游戏进程并提供游戏媒体元数据。不玩微软商店版游戏可禁用。"));
            list.Add(MakeSvc("svc_gamingservices", "GamingServices", "游戏服务（GamingServices）",
                "微软商店游戏平台的底层服务，用于 Xbox 游戏安装/授权。不玩商店版游戏可禁用。"));
            list.Add(MakeSvc("svc_gamingservicesnet", "GamingServicesNet", "游戏服务网络（GamingServicesNet）",
                "游戏服务的联网组件，与 GamingServices 一起禁用。"));

            // 应用兼容遥测·步骤记录器策略（DisableUAR 已覆盖，此处仅 DisableStepsRecorder）
            var stepsRec = RegTweak.Create("steps_recorder_policy", GPrivacy,
                "禁用步骤记录器（AppCompat 策略）",
                "通过 AppCompat 策略禁用步骤记录器录制（DisableStepsRecorder=1）。UAR 遥测已由「禁用步骤记录器与遥测收集」覆盖，此处补步骤录制器本身的策略开关。",
                adminOnly: true, recommended: true);
            stepsRec.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableStepsRecorder", 1));
            list.Add(stepsRec);

            // 相机策略关闭
            var webcam = RegTweak.Create("webcam_policy_off", GPrivacy,
                "策略级禁用摄像头访问",
                "通过 Camera 策略关闭应用对摄像头的访问（AllowCamera=0）。注意：会统一关闭商店应用读写摄像头；桌面程序与驱动不受影响。",
                adminOnly: true, recommended: true);
            webcam.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Camera", "AllowCamera", 0));
            list.Add(webcam);

            // Diagtrack 自动记录器
            var diagAutologger = RegTweak.Create("diagtrack_autologger_off", GPrivacy,
                "关闭 Diagtrack 诊断自动记录器",
                "把 WMI AutoLogger-Diagtrack-Listener 的 Start 置 0，停用 DiagTrack 诊断监听会话的自动采集。",
                adminOnly: true, recommended: true);
            diagAutologger.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\WMI\AutoLogger\AutoLogger-Diagtrack-Listener", "Start", 0));
            list.Add(diagAutologger);

            // Wifi Sense
            var wifiSense = RegTweak.Create("wifi_sense_oem_off", GPrivacy,
                "关闭 Wi-Fi 感知自动连接",
                "关闭 Wi-Fi 感知的 OEM 自动连接（AutoConnectAllowedOEM=0），避免设备未经许可连入运营商热点。",
                adminOnly: true, recommended: true);
            wifiSense.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config", "AutoConnectAllowedOEM", 0));
            list.Add(wifiSense);

            // TCP/IP 参数组（DefaultTTL / TcpMaxDupAcks / TcpAutotuningLevel / Tcp1323Opts）
            var ttl = RegTweak.Create("tcp_ttl_64", GNetwork,
                "TCP 生命周期 TTL = 64",
                "把 IP 包默认 TTL 写为 64（DefaultTTL=64）。多数在线游戏与网络环境的推荐值，长链路下丢包定位更规范。需重启生效。",
                adminOnly: true, recommended: true);
            ttl.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "DefaultTTL", 64));
            list.Add(ttl);

            var dupAcks = RegTweak.Create("tcp_dupacks", GNetwork,
                "TCP 最大重复确认 = 2",
                "把 TcpMaxDupAcks 写为 2，重传触发更快，丢包恢复更及时，弱网联机更顺。需重启生效。",
                adminOnly: true, recommended: true);
            dupAcks.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpMaxDupAcks", 2));
            list.Add(dupAcks);

            // TCP 接收窗口自动调优：netsh 是该开关的正式控制面（注册表 TcpAutotuningLevel
            // 并非可靠路径，旧版直接写注册表可能无效），故改为 CommandTweak + netsh，探测也走 netsh。
            CommandTweak autoTuning = new CommandTweak();
            autoTuning.IdValue = "tcp_autotuning_off";
            autoTuning.GroupValue = GNetwork;
            autoTuning.NameValue = "禁用 TCP 接收窗口自动调优";
            autoTuning.DescriptionValue =
                "netsh 把接收窗口自动调优设为 disabled，获得确定性延迟与固定接收窗口。"
                + "与「拥塞控制优化」互补。弱网/高带宽长延迟链路建议保持 normal，追求固定低延迟再启用。";
            // 这条曾被标为「推荐」：一键推荐会直接关掉接收窗口自动调优 ——
            // 而现代 Windows 的自动调优正是为高带宽/高延迟链路动态放大窗口，
            // 关掉后吞吐会明显下降（属负优化）。改为「谨慎」：不再进推荐集，
            // 并且按谨慎项走二次确认 + 强制系统还原点兜底。
            autoTuning.RecommendedValue = false;
            autoTuning.RiskyValue = true;
            autoTuning.EnableFile = "netsh.exe";
            autoTuning.EnableArgs = "int tcp set global autotuninglevel=disabled";
            autoTuning.RevertFile = "netsh.exe";
            autoTuning.RevertArgs = "int tcp set global autotuninglevel=normal";
            autoTuning.Probe = delegate
            {
                Shell.Result r = Shell.Run("netsh.exe", "int tcp show global", 30000);
                // 行标签随系统语言本地化（英文 "Receive Window Auto-Tuning Level" / 中文「接收窗口自动调优级别」），
                // 只匹配英文标签会在中文系统上恒判「未启用」
                string line = ProbeText.FindLine(r.All, "tuning", "调优");
                return ProbeText.SaysOff(line);   // 值一般为英文 disabled，兼容本地化「已禁用」
            };
            list.Add(autoTuning);

            var tcp1323 = RegTweak.Create("tcp1323_window_scale", GNetwork,
                "启用 RFC1323 窗口缩放（时间戳保持关闭）",
                "把 Tcp1323Opts 写为 1。注意：1 = 仅启用 RFC1323 窗口缩放，时间戳保持关闭，"
                + "与「关闭 TCP 时间戳」方向一致、可安全叠加（若要同时开时间戳需改为 3）。适合高带宽长延迟链路。需重启生效。",
                adminOnly: true, recommended: true);
            tcp1323.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Tcp1323Opts", 1));
            list.Add(tcp1323);

            // OOBE 隐私体验
            var oobe = RegTweak.Create("oobe_privacy_off", GPrivacy,
                "跳过 OOBE 隐私体验页",
                "通过 OOBE 策略关闭隐私体验引导（DisablePrivacyExperience=1），首次登录不再弹出隐私设置引导页。",
                adminOnly: true, recommended: true);
            oobe.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\OOBE", "DisablePrivacyExperience", 1));
            list.Add(oobe);

            // S0 现代待机期间断网
            var s0Net = RegTweak.Create("modern_standby_net_off", GPower,
                "现代待机期间断网（S0 联网策略）",
                "把 S0 Modern Standby 待机期间的网络连接策略写为断开（AC/DC 均为 0），消除待机后台联网。仅对支持 S0 的机型有意义，不影响待机本身。",
                adminOnly: true, recommended: true);
            s0Net.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9", "ACSettingIndex", 0));
            s0Net.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\f15576e8-98b7-4186-b944-eafa664402d9", "DCSettingIndex", 0));
            list.Add(s0Net);

            // LMHOSTS 查询关闭
            var lmhosts = RegTweak.Create("lmhosts_off", GNetwork,
                "禁用 LMHOSTS 名称解析查询",
                "把 NetBT 的 EnableLMHOSTS 写为 0，停止基于 LMHOSTS 文件的 NetBIOS 名称解析，减少名称解析等待。",
                adminOnly: true, recommended: true);
            lmhosts.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHOSTS", 0));
            list.Add(lmhosts);

            // IoPageLockLimit
            var ioLock = RegTweak.Create("io_page_lock_limit", GPerformance,
                "I/O 页锁定上限（约 960MB）",
                "把 IoPageLockLimit 写为 983040，为文件系统 I/O 保留更大的锁定缓冲，大文件读写更饱满。需重启生效。",
                adminOnly: true, recommended: true);
            ioLock.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "IoPageLockLimit", 983040));
            list.Add(ioLock);

            // NTFS EFS 加密关闭
            var ntfsEnc = RegTweak.Create("ntfs_encryption_off", GPerformance,
                "禁用 NTFS 文件加密 (EFS)",
                "把 NtfsDisableEncryption 写为 1，禁止新建 EFS 加密文件。已加密文件与密钥不受影响；不使用 EFS 的家庭用户可关闭。",
                adminOnly: true, recommended: true);
            ntfsEnc.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableEncryption", 1));
            list.Add(ntfsEnc);

            // 可变刷新率关闭
            var vrr = RegTweak.Create("vrr_off", GGame,
                "关闭可变刷新率 (VRR / FreeSync / G-Sync)",
                "把 GraphicsDrivers 的 VRROptimizeEnable 写为 0，关闭可变刷新率优化。部分显示驱动兼容问题或画面撕裂时可用。",
                adminOnly: true, recommended: true);
            vrr.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "VRROptimizeEnable", 0));
            list.Add(vrr);

            // DNS 缓存优化（MaxCacheTtl 等未覆盖键）
            var dnsCache = RegTweak.Create("dns_cache_optimize", GNetwork,
                "DNS 缓存生命周期优化",
                "把 MaxCacheTtl 设为 900 秒、负缓存 SOA 时间与服务器优先级时间归零。MaxNegativeCacheTtl / NetFailureCacheTime 已由「DNS 解析与 TCP 连接优化」覆盖，此处仅补遗漏键。需重启生效。",
                adminOnly: true, recommended: true);
            dnsCache.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheTtl", 900));
            dnsCache.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeSOACacheTime", 0));
            dnsCache.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "ServerPriorityTimeLimit", 0));
            list.Add(dnsCache);

            // 云内容关闭
            var cloudContent = RegTweak.Create("cloud_content_off", GPrivacy,
                "关闭 Windows 云内容与聚焦",
                "通过 CloudContent 策略关闭消费者功能、锁屏/桌面聚焦（Spotlight）与软着陆推送（DisableWindowsConsumerFeatures / DisableWindowsSpotlightFeatures / DisableSoftLanding）。",
                adminOnly: true, recommended: true);
            cloudContent.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1));
            cloudContent.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsSpotlightFeatures", 1));
            cloudContent.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableSoftLanding", 1));
            list.Add(cloudContent);

            // 设置同步关闭
            var settingSync = RegTweak.Create("setting_sync_off", GPrivacy,
                "关闭设置同步",
                "通过 SettingSync 策略关闭设置同步及其用户覆盖（DisableSettingSync / DisableSettingSyncUserOverride），停止把系统设置同步到微软账户。",
                adminOnly: true, recommended: true);
            settingSync.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 1));
            settingSync.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSyncUserOverride", 1));
            list.Add(settingSync);

            // AFD 收发窗口
            var afd = RegTweak.Create("afd_windows", GNetwork,
                "AFD 收发窗口（65KB）",
                "把 AFD 的默认接收/发送窗口写为 65535（DefaultReceiveWindow / DefaultSendWindow），为个别走 AFD 通道的联网应用提供固定窗口，减少小窗口导致的吞吐瓶颈。需重启生效。",
                adminOnly: true, recommended: true);
            afd.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\AFD\Parameters", "DefaultReceiveWindow", 65535));
            afd.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\AFD\Parameters", "DefaultSendWindow", 65535));
            list.Add(afd);

            // AMD 抗延迟（EnableUlps，仅 AMD）
            var amdAntiLag = RegTweak.Create("amd_ulps_off", GGame,
                "A 卡禁用 ULPS 节能（仅 AMD）",
                "把主显卡的 EnableUlps 写为 0，关闭 AMD 超低功耗状态（ULPS），消除显卡进出节能态导致的微卡顿。PP_SclkDeepSleepDisable 已由「显卡维持最高性能状态」覆盖，此处仅补 ULPS 开关；仅 AMD 显卡适用。",
                adminOnly: true, recommended: true, applicable: delegate
                {
                    return GpuLatencyTweak.DetectVendor().Contains("A");
                });
            amdAntiLag.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000",
                "EnableUlps", 0));
            list.Add(amdAntiLag);

            // VBS（基于虚拟化的安全）——高风险
            var vbs = RegTweak.Create("vbs_off", GGame,
                "关闭基于虚拟化的安全 (VBS，谨慎)",
                "关闭 VBS（EnableVirtualizationBasedSecurity=0，机器策略与控制集双写）并把 LsaCfgFlags 归 0，解除虚拟化安全层对游戏帧数的拖累——官方文档确认可提升游戏性能。HVCI 内存完整性已由「关闭内核隔离」单独管理。代价：降低对内核级攻击的防护，需重启生效。",
                adminOnly: true, risky: true);
            vbs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0));
            vbs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard", "EnableVirtualizationBasedSecurity", 0));
            vbs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Lsa", "LsaCfgFlags", 0));
            list.Add(vbs);

            // Defender 实时保护（高风险，策略值；与 defender_off 互补）
            var defenderRt = RegTweak.Create("defender_rt_policy_off", GExtreme,
                "关闭 Defender 实时监控（策略，谨慎）",
                "把 Defender 实时保护策略 DisableRealtimeMonitoring 写为 1，临时关闭实时扫描钩子。注意：受到系统篡改保护（Tamper Protection）时会被系统拒绝写入。与「禁用 Windows Defender 全家桶」互补（那条直接关服务/驱动）。",
                adminOnly: true, risky: true);
            defenderRt.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", 1));
            list.Add(defenderRt);

            return list;
        }

        /// <summary>构造带别名前缀的服务禁用项（推荐=非谨慎，谨慎项 risky=true）。</summary>
        private static ServiceTweak MakeSvc(string id, string name, string display, string desc)
        {
            return MakeSvc(id, name, display, desc, false);
        }

        private static ServiceTweak MakeSvc(string id, string name, string display, string desc, bool risky)
        {
            ServiceTweak t = new ServiceTweak(id);
            t.ServiceName = name;
            t.DisplayName = display;
            t.DescriptionValue = desc;
            t.RecommendedValue = !risky;
            t.RiskyValue = risky;
            return t;
        }
    }
}