using System.Collections.Generic;
using GuyueBox.UI.Commands;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 系统概览页的全部命令。按钮只负责触发，操作逻辑与 owner 页面解耦：
    /// 每个命令一个注册条目，命令号「dashboard.动作」，便于查找、排查与自动化。
    /// </summary>
    public static class DashboardCommands
    {
        public const string HealthCheck = "dashboard.health_check";
        public const string Refresh = "dashboard.refresh";
        public const string ExportReport = "dashboard.export_report";
        public const string ReleaseMemory = "dashboard.release_memory";

        /// <summary>注册本页全部命令（幂等）。</summary>
        public static void RegisterAll(DashboardView view)
        {
            CommandHub.RegisterAll(Build(view));
        }

        private static IEnumerable<AppCommand> Build(DashboardView view)
        {
            List<AppCommand> list = new List<AppCommand>();

            AppCommand check = new AppCommand();
            check.Id = HealthCheck;
            check.Title = "一键体检";
            check.Execute = delegate (ViewBase owner) { view.OnHealthCheck(null, null); };
            list.Add(check);

            AppCommand refresh = new AppCommand();
            refresh.Id = Refresh;
            refresh.Title = "重新检测";
            refresh.Execute = delegate (ViewBase owner) { view.OnRefreshClick(null, null); };
            list.Add(refresh);

            AppCommand export = new AppCommand();
            export.Id = ExportReport;
            export.Title = "导出报告";
            export.Execute = delegate (ViewBase owner) { view.OnExportClick(null, null); };
            list.Add(export);

            AppCommand memory = new AppCommand();
            memory.Id = ReleaseMemory;
            memory.Title = "整理内存";
            memory.Execute = delegate (ViewBase owner) { view.OnReleaseMemory(null, null); };
            list.Add(memory);

            return list;
        }
    }
}
