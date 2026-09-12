using System;
using System.Collections.Generic;
using System.Text;

namespace SysToolbox.Core
{
    public sealed class ScheduledTask
    {
        public string Name = "";
        public string NextRun = "";
        public string Status = "";
        public bool Enabled
        {
            get { return !string.Equals(Status, "Disabled", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public static class ScheduledTasks
    {
        public static List<ScheduledTask> List()
        {
            List<ScheduledTask> list = new List<ScheduledTask>();
            Shell.Result r = Shell.Run("schtasks.exe", "/query /fo CSV /nh", 60000);
            if (!r.Ok) return list;

            string[] lines = r.Output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string[] f;
                if (!ParseCsv(line, out f) || f.Length < 3) continue;

                ScheduledTask t = new ScheduledTask();
                t.Name = f[0].Trim();
                t.NextRun = f[1].Trim();
                t.Status = f[2].Trim();
                if (t.Name.Length == 0) continue;
                list.Add(t);
            }

            list.Sort(delegate (ScheduledTask a, ScheduledTask b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        public static bool Set(ScheduledTask task, bool enable, out string error)
        {
            error = "";
            try
            {
                string verb = enable ? "/enable" : "/disable";
                Shell.Result r = Shell.Run("schtasks.exe",
                    "/change /tn \"" + task.Name + "\" " + verb, 20000);
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

        private static bool ParseCsv(string line, out string[] fields)
        {
            fields = new string[0];
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
}
