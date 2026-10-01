/* 文件说明：优化项库「杂项」组——音频优化、外观与体验、隐私授权、体验增强补充。 */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        // ==================== 音频优化 ====================

        private const string AudioKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";
        private const string AudioDuckingKey = @"SOFTWARE\Microsoft\Multimedia\Audio";

        /// <summary>音频优化组：关闭系统音效处理链，降低音频链路延迟与中断。</summary>
        private static IEnumerable<ITweak> Audio()
        {
            List<ITweak> list = new List<ITweak>();

            var enh = RegTweak.Create("audio_enhancements_off", GAudio,
                "禁用系统音频增强",
                "关闭 Windows 音频效果处理（均衡器/响度等系统级增强），减少音频链路上的额外处理，降低延迟与爆音概率。应用后若某些音效软件失效，关闭本项即可还原。");
            enh.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioKey, "DisableSystemEffects", 1));
            list.Add(enh);

            var spatial = RegTweak.Create("audio_spatial_off", GAudio,
                "禁用空间音效",
                "关闭 Windows Sonic / 杜比全景声等空间音频处理。空间音效会引入额外混音与延迟，竞技类游戏建议关闭；观影时可按需重新开启。");
            spatial.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioKey, "EnableSpatialAudio", 0));
            list.Add(spatial);

            var delayed = RegTweak.Create("audio_service_delayed_start_off", GAudio,
                "音频服务不延迟启动",
                "把 Audiosrv 音频服务的延迟自动启动改为随系统启动，避免登录后音频设备需要等待数秒才可用（对开机即用音频/直播场景更友好）。",
                adminOnly: true);
            delayed.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Audiosrv", "DelayedAutoStart", 0));
            list.Add(delayed);

            var ducking = RegTweak.Create("audio_comm_ducking_off", GAudio,
                "关闭通信自动降低音量",
                "关闭「检测到通信活动时自动降低其他声音音量」（UserDuckingPreference=3），避免游戏/音乐声音被无端压低。");
            ducking.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioDuckingKey, "UserDuckingPreference", 3));
            list.Add(ducking);

            return list;
        }

        // ==================== 外观与体验 ====================

        private static IEnumerable<ITweak> Appearance()
        {
            List<ITweak> list = new List<ITweak>();

            var ext = RegTweak.Create("show_file_ext", GAppearance,
                "显示文件扩展名",
                "在资源管理器中显示 .exe、.txt 等扩展名，便于识别文件真实类型。");
            ext.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "HideFileExt", 0));
            list.Add(ext);

            var hidden = RegTweak.Create("show_hidden_files", GAppearance,
                "显示隐藏文件",
                "在资源管理器中显示隐藏文件和文件夹。");
            hidden.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Hidden", 1));
            list.Add(hidden);

            var shake = RegTweak.Create("disable_shake", GAppearance,
                "禁用 Aero Shake 窗口最小化",
                "防止误拖动鼠标导致其它窗口全部最小化。");
            shake.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "DisallowShaking", 1));
            list.Add(shake);

            var recent = RegTweak.Create("no_recent_docs", GAppearance,
                "关闭'最近使用的文件'记录",
                "不再记录最近打开的文件与常用文件夹，同时保护隐私。");
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackDocs", 0));
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackProgs", 0));
            list.Add(recent);

            var classicMenu = RegTweak.Create("win11_classic_menu", GAppearance,
                "恢复 Windows 10 经典右键菜单 (Win11)",
                "在 Windows 11 上跳过'显示更多选项'，直接展开完整右键菜单。仅对 Win11 有效。",
                risky: true);
            classicMenu.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""));
            list.Add(classicMenu);

            var taskbarAl = RegTweak.Create("taskbar_left", GAppearance,
                "开始菜单与任务栏左对齐",
                "把 Win11 默认居中的任务栏图标与开始菜单恢复为 Windows 10 风格的左对齐，纯外观偏好。",
                adminOnly: false);
            taskbarAl.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1));
            list.Add(taskbarAl);

            var syncNotice = RegTweak.Create("sync_provider_notice_off", GAppearance,
                "关闭同步服务通知横幅",
                "资源管理器顶部不再显示 OneDrive 等同步提供程序的提示横幅，少一处打扰（不影响同步本身）。");
            syncNotice.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "ShowSyncProviderNotifications", 0));
            list.Add(syncNotice);

            // 借鉴 optimizerDuck 的 HideRecommendedSection：Win11 开始菜单「推荐」区
            var hideRecommended = RegTweak.Create("win11_hide_recommended", GAppearance,
                "隐藏开始菜单'推荐'区域 (Win11)",
                "通过策略隐藏 Windows 11 开始菜单里的「推荐」项目区，界面更干净。仅对 Win11 有效（不影响已固定/已安装）。",
                adminOnly: false);
            hideRecommended.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\Explorer", "HideRecommendedSection", 1));
            list.Add(hideRecommended);

            var inkWorkspace = RegTweak.Create("ink_workspace_off", GAppearance,
                "关闭 Windows Ink 工作区",
                "移除任务栏的笔菜单与 Windows Ink 工作区入口，减少常驻项。手写笔本身的书写、压感与笔势不受影响。");
            inkWorkspace.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\PenWorkspace", "AllowWindowsInkWorkspace", 0));
            list.Add(inkWorkspace);

            var iconsOnly = RegTweak.Create("icons_only", GAppearance,
                "图标替代缩略图（禁文件预览）",
                "资源管理器不再生成视频/图片缩略图与预览窗格内容（IconsOnly=1），打开大量媒体文件的目录时渲染更快、延迟更低；如需恢复，在文件夹选项勾选「显示文件缩略图」即可。");
            iconsOnly.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "IconsOnly", 1));
            list.Add(iconsOnly);

            return list;
        }

        // ==================== 隐私授权 ====================

        private const string ConsentRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        /// <summary>ConsentStore 的 14 个隐私类别。</summary>
        private static readonly string[] ConsentKeys = new string[]
        {
            "radios",                    // 无线电（蓝牙/Wi-Fi 开关控制）
            "chat",                      // 短信 / 聊天
            "email",                     // 邮箱
            "appointments",              // 日历
            "contacts",                  // 联系人
            "webcam",                    // 相机
            "phoneCall",                 // 拨号
            "phoneCallHistory",          // 通话记录
            "trustedDevices",            // 受信任设备
            "activity",                  // 运动传感器
            "userDataTasks",             // 任务
            "userNotificationListener",  // 通知访问
            "userAccountInformation",    // 账户信息
            "appDiagnostics"             // 诊断数据
        };

        /// <summary>隐私授权批量项：拒绝应用访问隐私设备与数据。</summary>
        private static IEnumerable<ITweak> Consent()
        {
            List<ITweak> list = new List<ITweak>();

            var deny = RegTweak.Create("consent_store_deny", GPrivacy,
                "拒绝应用访问隐私设备与数据（14 类）",
                "把「隐私和安全性 → 应用权限」中的 14 类权限统一设为「拒绝」：" +
                "无线电、聊天、邮箱、日历、联系人、相机、拨号、通话记录、受信任设备、" +
                "运动传感器、任务、通知访问、账户信息、诊断数据。\r\n" +
                "注意：这会影响依赖这些权限的商店应用（如相机 App、邮件、日历无法读取相应数据），" +
                "但不影响桌面程序与设备驱动。关闭本项即按备份逐条还原。",
                risky: true);

            for (int i = 0; i < ConsentKeys.Length; i++)
            {
                deny.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                    ConsentRoot + "\\" + ConsentKeys[i], "Value", "Deny"));
            }

            list.Add(deny);
            return list;
        }

        // ==================== 体验增强补充 ====================

        /// <summary>
        /// 体验增强补充项：只做「让系统更好用」的界面与交互设置，
        /// 不涉及 Defender / UAC / 更新 / 驱动等高风险面。
        /// </summary>
        private static IEnumerable<ITweak> Extras()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------- 资源管理器 ----------------

            var launchTo = RegTweak.Create("explorer_launch_this_pc", GAppearance,
                "资源管理器打开「此电脑」",
                "默认进入「此电脑」而不是「快速访问」，打开新窗口直接看到磁盘列表，找文件少一步。仅影响资源管理器初始视图，不改变任何文件。",
                recommended: true);
            launchTo.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "LaunchTo", 1));
            list.Add(launchTo);

            var navPane = RegTweak.Create("explorer_navpane_expand", GAppearance,
                "导航窗格展开到当前文件夹",
                "左侧目录树自动展开并定位到当前所在文件夹，跳转层级一目了然，不用手动一路点开。");
            navPane.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "NavPaneExpandToCurrentFolder", 1));
            list.Add(navPane);

            var checkSelect = RegTweak.Create("explorer_checkbox_select", GAppearance,
                "文件显示复选框（方便多选）",
                "鼠标移到文件上即显示复选框，批量选取文件不必按住 Ctrl。习惯框选/快捷键的人可不开。");
            checkSelect.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "AutoCheckSelect", 1));
            list.Add(checkSelect);

            var separateProcess = RegTweak.Create("explorer_separate_process", GAppearance,
                "资源管理器独立进程",
                "每个资源管理器窗口运行在独立进程中：某个窗口卡死或崩溃时，不会再带走桌面任务栏和其他窗口。资源占用略增。");
            separateProcess.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "SeparateProcess", 1));
            list.Add(separateProcess);

            // ---------------- 任务栏与托盘 ----------------

            var glom = RegTweak.Create("taskbar_combine_never", GAppearance,
                "任务栏按钮从不合并",
                "同类窗口在任务栏上各自独立显示（并显示窗口标题），不再挤成一个图标。开很多同类型窗口时更好辨认，任务栏会变长。");
            glom.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "TaskbarGlomLevel", 2));
            list.Add(glom);

            var searchBox = RegTweak.Create("taskbar_search_hide", GAppearance,
                "隐藏任务栏搜索框",
                "去掉任务栏的搜索框/搜索图标，任务栏更清爽。仍可用 Win+S 或开始菜单直接输入搜索，功能不受影响。");
            searchBox.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "SearchboxTaskbarMode", 0));
            list.Add(searchBox);

            var trayIcons = RegTweak.Create("tray_show_all_icons", GAppearance,
                "托盘区始终显示所有图标",
                "关闭系统对不常用托盘图标的自动折叠，所有后台程序图标常驻可见，不用再点展开箭头。托盘图标多时会占较多任务栏空间。");
            trayIcons.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerMain, "EnableAutoTray", 0));
            list.Add(trayIcons);

            // ---------------- 系统提示 ----------------

            var scoobe = RegTweak.Create("scoobe_off", GAppearance,
                "关闭「欢迎体验」提示",
                "关闭登录后弹出的「欢迎体验 / 让我们完成设置」类引导卡片，不再被反复提醒。只影响提示本身，不影响任何功能。");
            scoobe.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0));
            list.Add(scoobe);

            return list;
        }
    }
}
