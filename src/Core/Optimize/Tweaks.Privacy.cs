/* ============================================================
 * 文件说明：优化项库「Privacy」组的全部优化项声明。
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
        private static IEnumerable<ITweak> Privacy()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak telemetry = new RegTweak();
            telemetry.IdValue = "telemetry_off";
            telemetry.GroupValue = GPrivacy;
            telemetry.NameValue = "关闭诊断遥测数据上报";
            telemetry.DescriptionValue = "将 Windows 诊断数据级别设为最低（安全级别）。建议同时禁用 DiagTrack 服务。";
            telemetry.AdminOnlyValue = true;
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0));
            telemetry.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));
            list.Add(telemetry);

            RegTweak adId = new RegTweak();
            adId.IdValue = "ad_id_off";
            adId.GroupValue = GPrivacy;
            adId.NameValue = "关闭广告 ID";
            adId.DescriptionValue = "禁止应用使用你的广告标识符推送个性化广告。";
            adId.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0));
            list.Add(adId);

            RegTweak ceip = new RegTweak();
            ceip.IdValue = "ceip_off";
            ceip.GroupValue = GPrivacy;
            ceip.NameValue = "退出客户体验改善计划";
            ceip.DescriptionValue = "停止向微软发送使用习惯与硬件配置统计信息。";
            ceip.AdminOnlyValue = true;
            ceip.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\SQMClient\Windows", "CEIPEnable", 0));
            list.Add(ceip);

            RegTweak activity = new RegTweak();
            activity.IdValue = "activity_feed_off";
            activity.GroupValue = GPrivacy;
            activity.NameValue = "关闭活动历史记录上传";
            activity.DescriptionValue = "不在本机收集活动历史，也不向云端同步时间线。";
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0));
            activity.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));
            list.Add(activity);

            RegTweak autoInst = new RegTweak();
            // 「阻止静默安装推荐应用」「关闭系统推广与任务栏资讯」已并入 consumer_content_off（键集全被覆盖）

            RegTweak bing = new RegTweak();
            bing.IdValue = "bing_search_off";
            bing.GroupValue = GPrivacy;
            bing.NameValue = "关闭开始菜单联网搜索";
            bing.DescriptionValue = "搜索框不再联网请求 Bing 结果，本地结果即输即出，也不会把输入内容发到云端。";
            bing.AdminOnlyValue = true;
            bing.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1));
            list.Add(bing);

            RegTweak copilot = new RegTweak();
            copilot.IdValue = "copilot_off";
            copilot.GroupValue = GPrivacy;
            copilot.NameValue = "关闭 Copilot 与任务栏图标";
            copilot.DescriptionValue = "通过策略禁用系统内置 AI 助手，减少常驻进程与后台联网。";
            copilot.AdminOnlyValue = true;
            copilot.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            list.Add(copilot);

            RegTweak onedrive = new RegTweak();
            onedrive.IdValue = "onedrive_off";
            onedrive.GroupValue = GPrivacy;
            onedrive.NameValue = "禁用 OneDrive 文件同步";
            onedrive.DescriptionValue = "阻止 OneDrive 随系统自启与后台同步占用资源。仍登录使用 OneDrive 的用户勿开。";
            onedrive.AdminOnlyValue = true;
            onedrive.RiskyValue = true;
            onedrive.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", 1));
            list.Add(onedrive);

            RegTweak feedback = new RegTweak();
            feedback.IdValue = "feedback_off";
            feedback.GroupValue = GPrivacy;
            feedback.NameValue = "关闭 Windows 反馈与询问";
            feedback.DescriptionValue = "不再弹出「帮助我们改进 Windows」的反馈通知与频率询问，减少打扰。";
            feedback.AdminOnlyValue = false;
            feedback.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1));
            list.Add(feedback);

            RegTweak tailored = new RegTweak();
            tailored.IdValue = "tailored_experience_off";
            tailored.GroupValue = GPrivacy;
            tailored.NameValue = "关闭定制体验";
            tailored.DescriptionValue = "停止利用诊断数据向你推送定制提示、技巧与广告（设置里的「定制体验」开关）。";
            tailored.AdminOnlyValue = false;
            tailored.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0));
            list.Add(tailored);

            RegTweak ink = new RegTweak();
            ink.IdValue = "ink_telemetry_off";
            ink.GroupValue = GPrivacy;
            ink.NameValue = "禁用输入个性化与墨迹遥测";
            ink.DescriptionValue = "停止收集手写墨迹、输入历史与联系人样本用于云端个性化训练（触屏/手写笔用户关闭后不影响输入法本地功能）。";
            ink.AdminOnlyValue = false;
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1));
            ink.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\InputPersonalization", "HarvestContacts", 0));
            list.Add(ink);

            RegTweak recall = new RegTweak();
            recall.IdValue = "recall_off";
            recall.GroupValue = GPrivacy;
            recall.NameValue = "禁用 Recall AI 快照";
            recall.DescriptionValue = "策略级关闭 Windows Recall 的屏幕快照与 AI 分析，快照数据不会被保存；同时禁用相关预下载。仅在 Windows 11 24H2 及以上版本生效——本机不满足该条件时应用会直接拒绝，状态保持「未启用」。";
            recall.AdminOnlyValue = true;
            recall.RiskyValue = true;
            recall.ApplicableValue = delegate
            {
                // Windows 11 24H2 = 内部版本 26100 及以上
                return Environment.OSVersion.Version.Build >= 26100;
            };
            recall.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            recall.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));
            list.Add(recall);

            RegTweak wuOff = new RegTweak();
            wuOff.IdValue = "wu_off";
            wuOff.GroupValue = GPerformance;
            wuOff.NameValue = "关闭 Windows 更新（谨慎）";
            wuOff.DescriptionValue = "策略级禁用 Windows Update 连接与自动更新，并把相关服务转为手动/禁用。游戏机防更新干扰利器；长期不更新有安全风险，需更新时还原即可。";
            wuOff.AdminOnlyValue = true;
            wuOff.RiskyValue = true;
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

            return list;
        }

        // ---------------- 服务 ----------------

    }
}
