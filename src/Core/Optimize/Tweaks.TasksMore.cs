/* 文件说明：优化项库「任务调度」补充批量项：12 组共 45 个系统计划任务。
   内容：冗杂系统计划任务的批量关闭脚本。
   与 Tweaks.Tasks.cs 的 Tasks() 分工：Tasks() 覆盖 16 个核心任务；本文件补全遥测、
   诊断、错误报告、地图定位、商店/语言组件、磁盘清理等剩余可开关批量项。
   照 ScheduledTaskTweak 现有 API：每条为「目录 + 任务名」完整路径，批量禁用/启用。 */

using System.Collections.Generic;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>任务调度补充批量项：12 组覆盖 45 个系统计划任务（遥测/诊断/错误上报/维护类）。</summary>
        public static List<ITweak> TasksMore()
        {
            List<ITweak> list = new List<ITweak>();

            // 遥测：Windows 飞行（Flighting）与功能配置上报。
            list.Add(new ScheduledTaskTweak(
                "tasks_flighting_off",
                GSlim,
                "禁用遥测飞行与功能配置任务",
                "关闭 Windows 飞行（Flighting）与功能配置上报：刷新缓存、引导使用数据上报、使用数据刷新、功能协调及使用数据收发 6 个后台任务，减少遥测数据外发。",
                new string[]
                {
                    @"\Microsoft\Windows\Flighting\OneSettings\RefreshCache",
                    @"\Microsoft\Windows\Flighting\FeatureConfig\BootstrapUsageDataReporting",
                    @"\Microsoft\Windows\Flighting\FeatureConfig\UsageDataFlushing",
                    @"\Microsoft\Windows\Flighting\FeatureConfig\ReconcileFeatures",
                    @"\Microsoft\Windows\Flighting\FeatureConfig\UsageDataReporting",
                    @"\Microsoft\Windows\Flighting\FeatureConfig\UsageDataReceiver"
                },
                false));

            // 诊断：疑难解答、磁盘诊断与磁盘占用数据收集。
            list.Add(new ScheduledTaskTweak(
                "tasks_diagnostics_off",
                GSlim,
                "禁用诊断与磁盘数据收集任务",
                "关闭推荐疑难解答扫描、计划诊断、磁盘诊断解析与磁盘占用诊断 4 个数据收集任务，抑制空闲时的后台诊断活动。",
                new string[]
                {
                    @"\Microsoft\Windows\Diagnosis\RecommendedTroubleshootingScanner",
                    @"\Microsoft\Windows\Diagnosis\Scheduled",
                    @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticResolver",
                    @"\Microsoft\Windows\DiskFootprint\Diagnostics"
                },
                false));

            // 错误报告：可靠性分析组件与后台数据上报。
            list.Add(new ScheduledTaskTweak(
                "tasks_reliability_off",
                GSlim,
                "禁用可靠性分析与数据上报任务",
                "关闭可靠性分析组件（RAC）、应用列表备份与移动宽带元数据解析 3 个任务，减少错误报告与后台数据上报。",
                new string[]
                {
                    @"\Microsoft\Windows\RAC\RacTask",
                    @"\Microsoft\Windows\AppListBackup\Backup",
                    @"\Microsoft\Windows\Mobile Broadband Accounts\MNO Metadata Parser"
                },
                false));

            // 诊断：内存诊断与电源效率分析。
            list.Add(new ScheduledTaskTweak(
                "tasks_memdiag_off",
                GSlim,
                "禁用内存与电源效率诊断任务",
                "关闭完整内存诊断、内存诊断事件处理与电源效率分析 3 个诊断任务，避免空闲时被内存/功耗检测唤醒。",
                new string[]
                {
                    @"\Microsoft\Windows\MemoryDiagnostic\RunFullMemoryDiagnostic",
                    @"\Microsoft\Windows\MemoryDiagnostic\ProcessMemoryDiagnosticEvents",
                    @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem"
                },
                false));

            // 定位/地图广告相关后台任务。
            list.Add(new ScheduledTaskTweak(
                "tasks_location_maps_off",
                GSlim,
                "禁用地图与位置服务任务",
                "关闭地图更新、地图通知、位置通知与位置操作对话框 4 个任务，停用地图与定位相关后台活动。",
                new string[]
                {
                    @"\Microsoft\Windows\Maps\MapsToastTask",
                    @"\Microsoft\Windows\Maps\MapsUpdateTask",
                    @"\Microsoft\Windows\Location\Notifications",
                    @"\Microsoft\Windows\Location\WindowsActionDialog"
                },
                false));

            // 应用遥测与后台注册维护。
            list.Add(new ScheduledTaskTweak(
                "tasks_app_telemetry_off",
                GSlim,
                "禁用应用遥测与后台注册维护任务",
                "关闭应用 URI 校验（每日/安装时）与后台任务注册维护 3 个任务，减少应用遥测与后台维护开销。",
                new string[]
                {
                    @"\Microsoft\Windows\ApplicationData\appuriverifierdaily",
                    @"\Microsoft\Windows\ApplicationData\appuriverifierinstall",
                    @"\Microsoft\Windows\BrokerInfrastructure\BgTaskRegistrationMaintenanceTask"
                },
                false));

            // 设置同步与语言设置同步。
            list.Add(new ScheduledTaskTweak(
                "tasks_sync_lang_off",
                GSlim,
                "禁用设置同步与语言设置同步任务",
                "关闭设置同步备份、网络状态变化同步与语言设置同步 3 个任务（含登录同步残留），减少账户同步后台活动。",
                new string[]
                {
                    @"\Microsoft\Windows\SettingSync\BackupTask",
                    @"\Microsoft\Windows\SettingSync\NetworkStateChangeTask",
                    @"\Microsoft\Windows\International\Synchronize Language Settings"
                },
                false));

            // 空维护扫描与版本到期提醒。
            list.Add(new ScheduledTaskTweak(
                "tasks_maint_scan_off",
                GSlim,
                "禁用维护扫描与升级提醒任务",
                "关闭系统评分（WinSAT）与系统到期提醒（EOSNotify/EOSNotify2）共 3 个任务，去除空闲维护扫描与版本到期提示。",
                new string[]
                {
                    @"\Microsoft\Windows\Maintenance\WinSAT",
                    @"\Microsoft\Windows\Setup\EOSNotify",
                    @"\Microsoft\Windows\Setup\EOSNotify2"
                },
                false));

            // 语言组件安装/卸载（涉及语言包更新，标记为谨慎）。
            list.Add(new ScheduledTaskTweak(
                "tasks_lang_components_off",
                GSlim,
                "禁用语言组件安装与清理任务",
                "关闭语言组件安装、资源协调、卸载与语言包移除 4 个任务。注意：可能影响后续语言包/输入法组件的自动安装与卸载。",
                new string[]
                {
                    @"\Microsoft\Windows\LanguageComponentsInstaller\Installation",
                    @"\Microsoft\Windows\LanguageComponentsInstaller\ReconcileLanguageResources",
                    @"\Microsoft\Windows\LanguageComponentsInstaller\Uninstallation",
                    @"\Microsoft\Windows\MUI\LPRemove"
                },
                true));

            // 商店许可与推送安装（涉及商店功能，标记为谨慎）。
            list.Add(new ScheduledTaskTweak(
                "tasks_store_off",
                GSlim,
                "禁用商店许可校验与推送安装任务",
                "关闭商店许可校验、违规应用列表刷新、推送安装注册与登录检查 4 个任务。注意：可能影响 Microsoft Store 应用的许可与自动安装。",
                new string[]
                {
                    @"\Microsoft\Windows\WS\License Validation",
                    @"\Microsoft\Windows\WS\WSRefreshBannedAppsListTask",
                    @"\Microsoft\Windows\PushToInstall\Registration",
                    @"\Microsoft\Windows\PushToInstall\LoginCheck"
                },
                true));

            // 磁盘清理与存储感知（自动维护，标记为谨慎）。
            list.Add(new ScheduledTaskTweak(
                "tasks_diskcleanup_off",
                GSlim,
                "禁用磁盘清理与存储感知任务",
                "关闭存储感知、静默磁盘清理、临时状态清理、数据服务清理与互联网缓存 5 个自动清理任务。注意：停用后系统不再自动清理临时与缓存文件，需手动清理。",
                new string[]
                {
                    @"\Microsoft\Windows\DiskFootprint\StorageSense",
                    @"\Microsoft\Windows\DiskCleanup\SilentCleanup",
                    @"\Microsoft\Windows\ApplicationData\CleanupTemporaryState",
                    @"\Microsoft\Windows\ApplicationData\DsSvcCleanup",
                    @"\Microsoft\Windows\Wininet\CacheTask"
                },
                true));

            // 安装残留清理与主动磁盘检查（自动维护，标记为谨慎）。
            list.Add(new ScheduledTaskTweak(
                "tasks_setupcleanup_off",
                GSlim,
                "禁用安装残留清理与主动磁盘检查任务",
                "关闭安装清理、演示内容离线清理与主动磁盘检查 3 个维护任务。注意：停用主动磁盘检查后，Chkdsk 不再定时预扫描。",
                new string[]
                {
                    @"\Microsoft\Windows\Setup\SetupCleanupTask",
                    @"\Microsoft\Windows\RetailDemo\CleanupOfflineContent",
                    @"\Microsoft\Windows\Chkdsk\ProactiveScan"
                },
                true));

            return list;
        }
    }
}