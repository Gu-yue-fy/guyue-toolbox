﻿/* ============================================================
 * 文件说明：计划任务：schtasks 列表读取与启停
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Text;

namespace GuyueBox.Core
{
    public sealed class ScheduledTask
    {
        public string Name = "";
        public string NextRun = "";
        /// <summary>CIM 状态枚举（Ready / Running / Disabled / Queued），语言无关。</summary>
        public string State = "";

        public bool Enabled
        {
            get { return !string.Equals(State, "Disabled", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// 状态的中文展示。
        /// 不再用 schtasks 的「状态」列：那一列随系统语言本地化（中文系统输出「已禁用/就绪」），
        /// 靠英文文本判断会在中文系统上整体失效。
        /// </summary>
        public string StatusText
        {
            get
            {
                switch ((State ?? "").Trim().ToLowerInvariant())
                {
                    case "ready": return "就绪";
                    case "running": return "正在运行";
                    case "disabled": return "已禁用";
                    case "queued": return "已排队";
                    case "": return "未知";
                    default: return State;
                }
            }
        }
    }
    public static class ScheduledTasks
    {
        /// <summary>
        /// 读取计划任务。
        /// 失败时把原因写入 error——页面据此区分"确实没有任务"与"读取失败"，并给出重试 / 提权提示；
        /// 失败时返回空列表，绝不把半截数据当成功。
        /// </summary>
        public static List<ScheduledTask> List(out string error)
        {
            error = "";
            List<ScheduledTask> list = new List<ScheduledTask>();

            // 不用 `schtasks /query /fo CSV`：它的「状态」列随系统语言本地化（中文系统为「已禁用/就绪」），
            // 匹配英文 "Disabled" 在中文系统上会失效——表现为「已禁用」恒为 0、已禁用任务无法重新启用。
            // 改用 Get-ScheduledTask 的 State（CIM 枚举，始终为英文）；「下次运行」来自 Get-ScheduledTaskInfo
            //（Get-ScheduledTask 自身不提供 NextRunTime）。一次调用取全量，任务名用 | 分隔（文件名不允许 |）。
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -Command \"$states = @{}; Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object { $states[$_.TaskPath + '|' + $_.TaskName] = $_.State }; Get-ScheduledTask -ErrorAction SilentlyContinue | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue | ForEach-Object { $k = $_.TaskPath + '|' + $_.TaskName; $n = ''; if ($_.NextRunTime) { $n = $_.NextRunTime.ToString('yyyy-MM-dd HH:mm') }; Write-Output ($k + '|' + $states[$k] + '|' + $n) }\"",
                120000);
            if (!r.Ok)
            {
                error = r.All.Trim();
                if (error.Length == 0) error = "Get-ScheduledTask 退出码 " + r.ExitCode;
                return list;
            }

            string[] lines = r.Output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                string[] f = line.Split('|');
                if (f.Length < 4) continue;

                string taskName = f[1].Trim();
                if (taskName.Length == 0) continue;

                ScheduledTask t = new ScheduledTask();
                t.Name = JoinTaskPath(f[0].Trim(), taskName);
                t.State = f[2].Trim();
                t.NextRun = f[3].Trim();
                list.Add(t);
            }

            list.Sort(delegate (ScheduledTask a, ScheduledTask b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// <summary>拼出 schtasks /set 需要的完整任务路径（\Microsoft\Windows\xxx 形式）。</summary>
        private static string JoinTaskPath(string path, string name)
        {
            if (string.IsNullOrEmpty(path)) path = "\\";
            if (!path.EndsWith("\\", StringComparison.Ordinal)) path += "\\";
            return (path + name).Replace("\\\\", "\\");
        }

        public static bool Set(ScheduledTask task, bool enable, out string error)
        {
            error = "";
            try
            {
                string verb = enable ? "/enable" : "/disable";
                Shell.Result r = Shell.Run("schtasks.exe",
                    "/change /tn \"" + task.Name + "\" " + verb, 20000, isChange: true);
                if (!r.Ok)
                {
                    error = (enable ? "启用失败：" : "禁用失败：") + r.All.Trim();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
