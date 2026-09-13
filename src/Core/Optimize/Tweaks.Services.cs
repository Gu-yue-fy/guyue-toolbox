/* ============================================================
 * 文件说明：优化项库「Services」组的全部优化项声明。
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
        private static IEnumerable<ITweak> Services()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(MakeService("DiagTrack", "连接用户体验和遥测",
                "向微软上报诊断与使用数据。普通用户完全可以禁用。", true, false));

            list.Add(MakeService("dmwappushservice", "WAP 推送消息路由服务",
                "设备管理与推送消息路由，家用环境不需要。", true, false));

            list.Add(MakeService("SysMain", "SysMain (超级预读取)",
                "预加载常用程序以加速启动。在固态硬盘上收益有限，禁用可释放内存。", true, false));

            list.Add(MakeService("WSearch", "Windows Search 索引",
                "为文件内容建立搜索索引。禁用后搜索会变慢，但可显著降低磁盘占用。", true, true));

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

            // 内置老旧驱动禁用（来自游戏系统包，按值照搬）
            RegTweak legacyDrivers = new RegTweak();
            legacyDrivers.IdValue = "legacy_drivers_off";
            legacyDrivers.GroupValue = GServices;
            legacyDrivers.NameValue = "禁用老旧内置驱动（谨慎）";
            legacyDrivers.DescriptionValue = "禁用 1394 / 软驱 / 光驱 / CDFS / UDFS / Ndu 等现代游戏机用不到的驱动，减少内核加载项。用到光驱或 IEEE1394 设备勿开。需重启生效。";
            legacyDrivers.AdminOnlyValue = true;
            legacyDrivers.RiskyValue = true;
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

        // ---------------- 电源 ----------------

    }
}
