/* ============================================================
 * 文件说明：优化项库「任务调度」批量项。
 *           16 个系统计划任务合并为 4 个批量开关，状态判定走 Get-ScheduledTask 的
 *           .NET 枚举（Disabled/Ready），与系统显示语言无关；查询结果带短 TTL 缓存，
 *           避免优化中心探测时为每项各起一次 PowerShell。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>
    /// 计划任务批量开关：Apply 禁用清单内全部任务，Revert 全部启用。
    /// 本机不存在的任务视为「无需处理」（目标状态已满足），不计失败。
    /// </summary>
    public sealed class ScheduledTaskTweak : ITweak
    {
        private readonly string _id;
        private readonly string _group;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _taskPaths;
        private readonly bool _risky;

        public ScheduledTaskTweak(string id, string group, string name, string desc,
            string[] taskPaths, bool risky)
        {
            _id = id;
            _group = group;
            _name = name;
            _desc = desc;
            _taskPaths = taskPaths ?? new string[0];
            _risky = risky;
        }

        public string Id { get { return _id; } }
        public string Group { get { return _group; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        public bool IsApplied()
        {
            if (_taskPaths.Length == 0) return false;
            for (int i = 0; i < _taskPaths.Length; i++)
            {
                int st = TaskStateCache.Get(_taskPaths[i]);
                if (st < 0) continue;        // 任务不存在 / 无法读取
                if (st == 1) return false;   // 仍有任务处于启用状态
            }
            return true; // 全部已禁用，或本机根本没有这些任务
        }

        public bool Apply()
        {
            if (_taskPaths.Length == 0) return false;
            bool ok = SetEnabled(false);
            TaskStateCache.Invalidate();
            return ok;
        }

        public bool Revert()
        {
            if (_taskPaths.Length == 0) return false;
            bool ok = SetEnabled(true);
            TaskStateCache.Invalidate();
            return ok;
        }

        private bool SetEnabled(bool enable)
        {
            string script = BuildScript(_taskPaths, enable);
            Shell.Result r = Shell.Run("powershell.exe", "-NoProfile -Command \"" + script + "\"", 90000);
            if (!r.Ok) return false;

            // 解析 "OK=n;FAIL=n;MISS=n"
            string all = r.All ?? "";
            return Field(all, "FAIL") == 0;
        }

        private static int Field(string text, string key)
        {
            int at = text.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            at += key.Length + 1;
            int end = text.IndexOf(';', at);
            string num = end < 0 ? text.Substring(at) : text.Substring(at, end - at);
            int v;
            if (int.TryParse(num.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return -1;
        }

        private static string BuildScript(string[] taskPaths, bool enable)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("$ok=0;$fail=0;$miss=0;");
            sb.Append("foreach($p in @(");
            for (int i = 0; i < taskPaths.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\'').Append(taskPaths[i].Replace("'", "''")).Append('\'');
            }
            sb.Append(")){");
            sb.Append("$i=$p.LastIndexOf('\\');$tp=$p.Substring(0,$i+1);$tn=$p.Substring($i+1);");
            sb.Append("$t=Get-ScheduledTask -TaskPath $tp -TaskName $tn -ErrorAction SilentlyContinue;");
            sb.Append("if(-not $t){$miss++;continue};");
            sb.Append("try{");
            sb.Append(enable ? "Enable-ScheduledTask" : "Disable-ScheduledTask");
            sb.Append(" -TaskPath $tp -TaskName $tn -ErrorAction Stop | Out-Null;$ok++}");
            sb.Append("catch{$fail++}");
            sb.Append("};");
            sb.Append("'OK='+$ok+';FAIL='+$fail+';MISS='+$miss");
            return sb.ToString();
        }
    }

    /// <summary>
    /// 计划任务状态缓存：一次 PowerShell 拉全量任务状态（TaskPath+TaskName+State），
    /// 供所有 ScheduledTaskTweak 共享，避免每项各起一次进程。TTL 15 秒。
    /// </summary>
    internal static class TaskStateCache
    {
        private const int TtlSeconds = 15;
        private static readonly object Gate = new object();
        private static Dictionary<string, int> _states;
        private static DateTime _stamp = DateTime.MinValue;

        public static void Invalidate()
        {
            lock (Gate)
            {
                _states = null;
                _stamp = DateTime.MinValue;
            }
        }

        /// <summary>1=启用 0=禁用 -1=不存在或未知。</summary>
        public static int Get(string fullPath)
        {
            string key = Normalize(fullPath);
            Dictionary<string, int> map = Snapshot();
            int v;
            if (map != null && map.TryGetValue(key, out v)) return v;
            return -1;
        }

        private static Dictionary<string, int> Snapshot()
        {
            lock (Gate)
            {
                if (_states != null &&
                    (DateTime.UtcNow - _stamp).TotalSeconds < TtlSeconds)
                {
                    return _states;
                }
            }

            Dictionary<string, int> fresh = Query();
            lock (Gate)
            {
                _states = fresh;
                _stamp = DateTime.UtcNow;
                return _states;
            }
        }

        private static Dictionary<string, int> Query()
        {
            Dictionary<string, int> map =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Get-ScheduledTask | Select-Object TaskPath,TaskName,State | ConvertTo-Csv -NoTypeInformation\"",
                    60000);
                if (!r.Ok) return map;

                string[] lines = (r.Output ?? "").Split(new string[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("\"TaskPath\"", StringComparison.OrdinalIgnoreCase)) continue; // 列头
                    string[] f;
                    if (!SplitCsv(line, out f) || f.Length < 3) continue;

                    string path = f[0].Trim();
                    string taskName = f[1].Trim();
                    string state = f[2].Trim();
                    if (taskName.Length == 0) continue;

                    string full = Normalize(path + (path.EndsWith("\\") || path.Length == 0 ? "" : "\\") + taskName);
                    int enabled = string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    map[full] = enabled;
                }
            }
            catch
            {
            }
            return map;
        }

        private static string Normalize(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            string s = fullPath.Trim();
            if (!s.StartsWith("\\")) s = "\\" + s;
            s = s.Replace("\\\\", "\\");
            return s.ToLowerInvariant();
        }

        private static bool SplitCsv(string line, out string[] fields)
        {
            List<string> outFields = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { outFields.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }
            outFields.Add(cur.ToString());
            fields = outFields.ToArray();
            return true;
        }
    }

    public static partial class TweakLibrary
    {
        /// <summary>任务调度批量项：4 组覆盖 16 个系统计划任务。</summary>
        private static IEnumerable<ITweak> Tasks()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(new ScheduledTaskTweak(
                "tasks_telemetry_off",
                GSlim,
                "禁用遥测与诊断计划任务",
                "关闭兼容性评估、客户体验改善计划、磁盘诊断数据收集与自动代理检测 4 个后台任务，减少空闲时的磁盘与 CPU 唤醒。",
                new string[]
                {
                    @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                    @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                    @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                    @"\Microsoft\Windows\Autochk\Proxy"
                },
                false));

            list.Add(new ScheduledTaskTweak(
                "tasks_update_off",
                GSlim,
                "禁用更新与商店自动计划任务",
                "关闭 Windows 更新扫描、更新策略启动、商店应用自动更新与云体验主机任务。注意：这是任务层禁用，不等于关闭 Windows 更新服务。",
                new string[]
                {
                    @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
                    @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
                    @"\Microsoft\Windows\WindowsUpdate\Automatic App Update",
                    @"\Microsoft\Windows\CloudExperienceHost\CreateObjectTask"
                },
                false));

            list.Add(new ScheduledTaskTweak(
                "tasks_sync_remote_off",
                GSlim,
                "禁用同步、备份通知与远程协助计划任务",
                "关闭同步中心计划同步、设置同步后台上传、备份配置通知与远程协助任务（远程协助任务禁用后无法被他人发起远程连接）。",
                new string[]
                {
                    @"\Microsoft\Windows\SyncCenter\SyncCenterScheduledSync",
                    @"\Microsoft\Windows\SettingSync\BackgroundUploadTask",
                    @"\Microsoft\Windows\WindowsBackup\ConfigNotification",
                    @"\Microsoft\Windows\RemoteAssistance\RemoteAssistanceTask"
                },
                true));

            list.Add(new ScheduledTaskTweak(
                "tasks_maintenance_xbox_off",
                GSlim,
                "禁用组件维护与 Xbox 计划任务",
                "关闭组件清理（StartComponentCleanup）、磁盘碎片整理计划，以及 Xbox 游戏保存相关任务（含登录触发）。",
                new string[]
                {
                    @"\Microsoft\Windows\Servicing\StartComponentCleanup",
                    @"\Microsoft\Windows\Defrag\ScheduledDefrag",
                    @"\Microsoft\XblGameSave\XblGameSaveTask",
                    @"\Microsoft\XblGameSave\XblGameSaveTaskLogon"
                },
                false));

            return list;
        }
    }
}
