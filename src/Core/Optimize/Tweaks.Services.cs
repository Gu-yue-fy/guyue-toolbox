/* Core/Optimize/Tweaks.Services.cs — 「系统服务」分组优化项：Services() + ServicesMore()。 */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Services()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(MakeService("DiagTrack", "连接用户体验和遥测",
                "向微软上报诊断与使用数据。普通用户完全可以禁用。", true, false));

            list.Add(MakeService("dmwappushservice", "WAP 推送消息路由服务",
                "设备管理与推送消息路由，家用环境不需要。", true, false));

            list.Add(MakeService("SysMain", "SysMain (超级预读取)",
                "预加载常用程序以加速启动。在固态硬盘上收益有限，禁用可释放内存。", true, false));

            // 禁用索引是真实取舍（搜索会变慢），标为有风险即可，不进「一键推荐」
            list.Add(MakeService("WSearch", "Windows Search 索引",
                "为文件内容建立搜索索引。禁用后搜索会变慢，但可显著降低磁盘占用。", false, true));

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

            list.Add(MakeService("Spooler", "打印后台处理程序",
                "管理打印队列。没有打印机/扫描仪时可禁用；禁用后无法打印（也会影响部分「打印为 PDF」功能）。", false, true));
            list.Add(MakeService("bthserv", "蓝牙支持服务",
                "蓝牙设备支持。使用蓝牙耳机/键鼠/手柄时请勿禁用。", false, true));
            list.Add(MakeService("WerSvc", "Windows 错误报告服务",
                "程序崩溃后收集并上报错误报告。禁用可减少崩溃后的磁盘写入与后台上报。", true, false));

            // 内置老旧驱动禁用（来自游戏系统包，按值照搬）
            var legacyDrivers = RegTweak.Create("legacy_drivers_off", GServices,
                "禁用老旧内置驱动（谨慎）",
                "禁用 1394 / 软驱 / 光驱 / CDFS / UDFS / Ndu 等现代游戏机用不到的驱动，减少内核加载项。用到光驱或 IEEE1394 设备勿开。需重启生效。",
                adminOnly: true, risky: true);
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

        /// <summary>
        /// 服务禁用补充清单：围绕遥测/诊断/钱包/传感/网络共享/VR/Hyper-V 等「安全可禁用」项补齐，
        /// 与 Services() 中已有 35 项去重（DiagTrack、WSearch、SysMain、Spooler、bthserv 等不再重复）。
        /// </summary>
        public static List<ITweak> ServicesMore()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------- 遥测 / 诊断 / 错误报告 ----------
            list.Add(MakeService("diagnosticshub.standardcollector.service", "诊断中心标准收集器服务",
                "收集并上报系统诊断数据，属于遥测体系，普通用户可禁用。", true, false));
            list.Add(MakeService("wisvc", "Windows 会员计划服务",
                "加入 Windows Insider 预览计划的组件，普通用户用不到，可禁用。", true, false));
            list.Add(MakeService("Telemetry", "Intel 遥测服务",
                "Intel 平台的遥测数据收集驱动，无实际功能，可禁用。", true, false));
            list.Add(MakeService("DsSvc", "数据共享服务",
                "向微软同步设备与使用数据，属于遥测体系，可禁用。", true, false));
            list.Add(MakeService("TroubleshootingSvc", "推荐疑难解答服务",
                "为设置里的推荐疑难解答提供后台支持。不用系统自带排错可禁用。", true, false));
            list.Add(MakeService("GraphicsPerfSvc", "图形性能监视服务",
                "向任务管理器提供 GPU 性能计数器。不看 GPU 图表可禁用。", false, false));

            // ---------- 钱包 / 智能卡 ----------
            list.Add(MakeService("WalletService", "钱包服务",
                "Windows 钱包（银行卡/支付）后台服务，不使用可禁用。", true, false));
            list.Add(MakeService("ScDeviceEnum", "智能卡设备枚举服务",
                "枚举智能卡读卡器，与智能卡服务一起禁用。", true, false));

            // ---------- 传感 ----------
            list.Add(MakeService("SensorDataService", "传感器数据服务",
                "为光/温度等传感器提供数据。台式机无传感器可禁用；平板/笔记本慎用。", false, false));
            list.Add(MakeService("SensorService", "传感器服务",
                "管理系统传感器通断。台式机可禁用，笔记本/平板务必保留。", false, false));
            list.Add(MakeService("SensrSvc", "传感器监视服务",
                "监视环境传感器。台式机可禁用。", false, false));

            // ---------- Phone / TouchKeyboard ----------
            list.Add(MakeService("TabletInputService", "触摸键盘和手写面板服务",
                "触屏键盘与手写笔服务。非触摸屏设备可禁用。", false, false));

            // ---------- 次要网络共享 / UPnP / 移动热点 ----------
            list.Add(MakeService("upnphost", "UPnP 设备主机",
                "通用即插即用（UPnP）设备发现。家用环境基本用不到，可禁用。", true, false));
            list.Add(MakeService("SSDPSRV", "SSDP 发现服务",
                "网络设备 SSDP 发现协议，配合 UPnP 使用，可禁用。", true, false));
            list.Add(MakeService("icssvc", "Windows 移动热点服务",
                "把电脑变成 Wi-Fi 热点。不用移动热点功能可禁用。", true, false));
            list.Add(MakeService("SharedAccess", "Internet 连接共享（ICS）",
                "互联网连接共享与移动热点底层服务。禁用后热点/ICS/WSL2 网络不可用，请谨慎。", false, true));
            list.Add(MakeService("fdPHost", "功能发现提供程序主机",
                "网络功能发现组件，家用环境可禁用。", false, false));
            list.Add(MakeService("FDResPub", "功能发现资源发布",
                "向网络发布本机功能资源，家用环境可禁用。", false, false));
            list.Add(MakeService("NetTcpPortSharing", "Net.Tcp 端口共享服务",
                "供 WCF 服务共享 TCP 端口，普通用户用不到，可禁用。", true, false));
            list.Add(MakeService("AJRouter", "AllJoyn 路由器服务",
                "AllJoyn 智能家居/IoT 协议，基本已淘汰，可禁用。", true, false));

            // ---------- 混合现实 / VR ----------
            list.Add(MakeService("MixedRealityOpenXRSvc", "Windows Mixed Reality OpenXR 服务",
                "MR/VR 头显 OpenXR 运行时，不用 VR 可禁用。", true, false));
            list.Add(MakeService("perceptionsimulation", "Windows 感知模拟服务",
                "混合现实空间感知模拟组件，不用 VR 可禁用。", true, false));
            list.Add(MakeService("spectrum", "Windows 感知服务",
                "混合现实设备感知服务，不用 VR 可禁用。", true, false));
            list.Add(MakeService("SharedRealitySvc", "Windows Mixed Reality 空间数据服务",
                "MR 空间数据服务，不用 VR 可禁用。", true, false));

            // ---------- Edge 更新 ----------
            list.Add(MakeService("MicrosoftEdgeElevationService", "Microsoft Edge 提权服务",
                "供 Edge 在需要时提升权限安装更新。不用 Edge 或追求精简可禁用。", false, false));
            list.Add(MakeService("edgeupdate", "Microsoft Edge 更新服务",
                "自动更新 Edge 浏览器。用其他浏览器可禁用（Edge 将停止自动更新）。", false, false));
            list.Add(MakeService("edgeupdatem", "Microsoft Edge 更新服务（后台）",
                "Edge 后台更新组件，与 edgeupdate 一起禁用。", false, false));

            // ---------- 字体缓存 ----------
            list.Add(MakeService("FontCache", "Windows 字体缓存服务",
                "缓存字体加速渲染。禁用后字体首载略慢，可降低资源占用。", false, false));
            list.Add(MakeService("FontCache3.0.0.0", "WPF 字体缓存 3.0.0.0",
                "WPF 应用字体缓存，与字体缓存服务一起禁用。", false, false));

            // ---------- 联系人 / 用户数据 ----------
            list.Add(MakeService("PimIndexMaintenanceSvc", "联系人数据索引维护服务",
                "维护联系人/通讯录索引。不用系统联系人功能可禁用。", false, false));
            list.Add(MakeService("UserDataSvc", "用户数据存取服务",
                "为应用提供联系人等用户数据存取。不用可禁用。", false, false));
            list.Add(MakeService("UnistoreSvc", "用户数据存储服务",
                "存储联系人等用户数据。不用系统通讯录可禁用。", false, false));

            // ---------- 剪贴板 / 便携设备 ----------
            list.Add(MakeService("cbdhsvc", "剪贴板用户服务",
                "跨设备/历史剪贴板。禁用后 Win+V 剪贴板历史不可用，普通复制粘贴不受影响。", false, false));
            list.Add(MakeService("WPDBusEnum", "便携设备枚举器服务",
                "枚举手机/相机等便携设备。不用 MTP 连手机可禁用。", false, false));

            // ---------- 其它 ----------
            list.Add(MakeService("BcastDVRUserService", "游戏录制和广播用户服务",
                "Xbox 游戏录制与直播底层的用户服务。不录制/直播游戏可禁用。", false, false));
            list.Add(MakeService("autotimesvc", "自动时区更新程序",
                "随网络自动更新时区。不常跨时区旅行可禁用。", false, false));
            list.Add(MakeService("DisplayEnhancementService", "显示增强服务",
                "显示增强（亮度/色彩）组件。台式机可禁用，笔记本 HDR 慎用。", false, false));
            list.Add(MakeService("ShellHWDetection", "Shell 硬件检测服务",
                "检测硬件插入并触发自动播放。关闭自动播放后可禁用。", false, false));
            list.Add(MakeService("AppVClient", "Microsoft App-V 客户端",
                "企业应用虚拟化客户端，家庭用户可禁用。", true, false));
            list.Add(MakeService("rdyboost", "ReadyBoost",
                "用 U 盘做缓存加速的老功能，固态硬盘时代已无意义，可禁用。", true, false));
            list.Add(MakeService("OneSyncSvc", "同步主机服务",
                "同步邮件/日历/联系人及账号设置。断开微软账户同步可禁用。", false, false));
            list.Add(MakeService("MessagingService", "消息服务",
                "系统消息推送组件，与短信路由同族，PC 上无用，可禁用。", true, false));
            list.Add(MakeService("lfsvc", "地理位置服务",
                "为应用提供定位。不用定位功能可禁用。", false, false));

            // ---------- Hyper-V 集成服务（未使用 Hyper-V 可禁用） ----------
            list.Add(MakeService("vmicguestinterface", "Hyper-V 来宾服务接口",
                "Hyper-V 虚拟机集成组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmicheartbeat", "Hyper-V 心跳服务",
                "Hyper-V 心跳组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmickvpexchange", "Hyper-V 数据交换服务",
                "Hyper-V 数据交换组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmicrdv", "Hyper-V 远程桌面虚拟化服务",
                "Hyper-V 远程桌面虚拟化组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmicshutdown", "Hyper-V 来宾关机服务",
                "Hyper-V 来宾关机组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmictimesync", "Hyper-V 时间同步服务",
                "Hyper-V 时间同步组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmicvmsession", "Hyper-V PowerShell Direct 服务",
                "Hyper-V PowerShell Direct 组件，未使用 Hyper-V 可禁用。", false, false));
            list.Add(MakeService("vmicvss", "Hyper-V 卷影复制服务",
                "Hyper-V 卷影复制请求组件，未使用 Hyper-V 可禁用。", false, false));

            return list;
        }
    }
}