/* Core/Optimize/Tweaks.Privacy.cs — 「隐私与安全」分组优化项（Privacy()）。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Privacy()
        {
            List<ITweak> list = new List<ITweak>();

            var telemetry = RegTweak.Create("telemetry_off", GPrivacy,
                "关闭诊断遥测数据上报",
                "将 Windows 诊断数据级别设为最低（安全级别）。建议同时禁用 DiagTrack 服务。",
                adminOnly: true);
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));
            list.Add(telemetry);

            var adId = RegTweak.Create("ad_id_off", GPrivacy,
                "关闭广告 ID",
                "禁止应用使用你的广告标识符推送个性化广告。");
            adId.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0));
            list.Add(adId);

            var ceip = RegTweak.Create("ceip_off", GPrivacy,
                "退出客户体验改善计划",
                "停止向微软发送使用习惯与硬件配置统计信息。",
                adminOnly: true);
            ceip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            list.Add(ceip);

            var activity = RegTweak.Create("activity_feed_off", GPrivacy,
                "关闭活动历史记录上传",
                "不在本机收集活动历史，也不向云端同步时间线。");
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            list.Add(activity);

            var bing = RegTweak.Create("bing_search_off", GPrivacy,
                "关闭开始菜单联网搜索",
                "搜索框不再联网请求 Bing 结果，本地结果即输即出，也不会把输入内容发到云端。",
                adminOnly: true);
            bing.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1));
            list.Add(bing);

            var copilot = RegTweak.Create("copilot_off", GPrivacy,
                "关闭 Copilot 与任务栏图标",
                "通过策略禁用系统内置 AI 助手，减少常驻进程与后台联网。",
                adminOnly: true);
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            list.Add(copilot);

            var onedrive = RegTweak.Create("onedrive_off", GPrivacy,
                "禁用 OneDrive 文件同步",
                "阻止 OneDrive 随系统自启与后台同步占用资源。仍登录使用 OneDrive 的用户勿开。",
                adminOnly: true, risky: true);
            onedrive.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1));
            list.Add(onedrive);

            var feedback = RegTweak.Create("feedback_off", GPrivacy,
                "关闭 Windows 反馈与询问",
                "不再弹出「帮助我们改进 Windows」的反馈通知与频率询问，减少打扰。",
                adminOnly: false);
            feedback.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1));
            list.Add(feedback);

            var tailored = RegTweak.Create("tailored_experience_off", GPrivacy,
                "关闭定制体验",
                "停止利用诊断数据向你推送定制提示、技巧与广告（设置里的「定制体验」开关）。",
                adminOnly: false);
            tailored.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0));
            list.Add(tailored);

            var ink = RegTweak.Create("ink_telemetry_off", GPrivacy,
                "禁用输入个性化与墨迹遥测",
                "停止收集手写墨迹、输入历史与联系人样本用于云端个性化训练（触屏/手写笔用户关闭后不影响输入法本地功能）。",
                adminOnly: false);
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\InputPersonalization", "HarvestContacts", 0));
            list.Add(ink);

            var recall = RegTweak.Create("recall_off", GPrivacy,
                "禁用 Recall AI 快照",
                "策略级关闭 Windows Recall 的屏幕快照与 AI 分析，快照数据不会被保存。仅在 Windows 11 24H2 及以上版本生效——本机不满足该条件时应用会直接拒绝，状态保持「未启用」。",
                adminOnly: true, risky: true);
            recall.ApplicableWhen(new WindowsBuildCondition(26100));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            // 借鉴 optimizerDuck 的 DisableRecall：补全 Recall 的另外两个策略键
            recall.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", 1));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", 1));
            list.Add(recall);

            // —— 借鉴 optimizerDuck 的 AI 分类：Win11 24H2+ 的 Recall / Click To Do ——
            var clickToDo = RegTweak.Create("clicktodo_off", GPrivacy,
                "禁用 Click To Do AI 浮层",
                "策略级关闭 Windows 11 的 Click To Do 智能浮层（截图/选中即分析的 AI 叠加层），减少后台 AI 进程与打扰。" +
                "仅在 Windows 11 24H2 及以上生效——本机不满足时应用直接拒绝，状态保持「未启用」。",
                adminOnly: true);
            clickToDo.ApplicableWhen(new WindowsBuildCondition(26100));
            clickToDo.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1));
            clickToDo.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1));
            list.Add(clickToDo);

            var settingsAds = RegTweak.Create("settings365_ads_off", GPrivacy,
                "关闭设置里的 Microsoft 365 广告",
                "禁用设置应用内推广 Microsoft 365 / 消费版内容的横幅（DisableConsumerAccountStateContent=1），减少设置页干扰。",
                adminOnly: true);
            settingsAds.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableConsumerAccountStateContent", 1));
            list.Add(settingsAds);

            var wuPauseMax = RegTweak.Create("wu_pause_max", GPrivacy,
                "放宽 Windows 更新暂停上限",
                "把 Windows Update 可暂停天数上限设为最大值（FlightSettingsMaxPauseDays=0xFFFFFFFF），让你能一次性暂停更久的更新。" +
                "仍可随时手动检查更新。",
                adminOnly: true);
            wuPauseMax.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "FlightSettingsMaxPauseDays",
                unchecked((int)0xFFFFFFFF)));
            list.Add(wuPauseMax);

            var wuOff = RegTweak.Create("wu_off", GPerformance,
                "关闭 Windows 更新（谨慎）",
                "策略级禁用 Windows Update 连接与自动更新，并把相关服务转为手动/禁用。游戏机防更新干扰利器；长期不更新有安全风险，需更新时还原即可。",
                adminOnly: true, risky: true);
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableOSUpgrade", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 1));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 2));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsStore\WindowsUpdate", "AutoDownload", 2));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\wuauserv", "Start", 3));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc", "Start", 4));
            wuOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\UsoSvc", "Start", 4));
            list.Add(wuOff);

            var autorun = RegTweak.Create("no_drive_autorun", GPrivacy,
                "禁用所有驱动器的自动运行",
                "插入 U 盘/移动硬盘后不再自动执行其中的程序，可阻断最常见的 autorun 类脚本病毒；手动双击打开照常。与「关闭自动播放」互补：那条管弹窗选择，这条管自动执行本身。");
            autorun.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun", 255));
            list.Add(autorun);

            var lmCompat = RegTweak.Create("lm_compat_ntlmv2", GPrivacy,
                "拒绝 LM 与 NTLMv1 认证（谨慎）",
                "只接受 NTLMv2 响应，阻断已被证明可离线破解的弱认证协议。老式 NAS、网络打印机或部分域环境只支持 NTLMv1 时会连不上——遇到连不上再关掉本项即可。",
                adminOnly: true, risky: true);
            lmCompat.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel", 5));
            list.Add(lmCompat);

            var noLmHash = RegTweak.Create("no_lm_hash", GPrivacy,
                "不再存储 LM 口令哈希",
                "禁止在本地保存可被瞬间破解的 LM 口令哈希。下次修改登录密码后完全生效，对日常登录与共享访问没有影响。",
                adminOnly: true);
            noLmHash.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Lsa", "NoLMHash", 1));
            list.Add(noLmHash);

            var restrictAnon = RegTweak.Create("restrict_anonymous", GPrivacy,
                "限制匿名枚举账户与共享",
                "禁止匿名连接枚举本机账户名与共享列表，减少被局域网扫描工具探到的信息。个别老式 NAS 的「访客直连」可能受影响。",
                adminOnly: true);
            restrictAnon.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymous", 1));
            restrictAnon.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymousSAM", 1));
            list.Add(restrictAnon);

            var wsh = RegTweak.Create("wsh_off", GPrivacy,
                "禁用 Windows 脚本宿主（谨慎）",
                "禁止运行 .vbs / .js 脚本，可阻断最常见的一类脚本病毒与恶意宏载体。少数旧软件的安装/激活脚本依赖它，遇到脚本报错时关掉本项即可。",
                adminOnly: true, risky: true);
            wsh.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Script Host\Settings", "Enabled", 0));
            list.Add(wsh);

            return list;
        }
    }
}