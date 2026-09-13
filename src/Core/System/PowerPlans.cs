using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GuyueBox.Core
{
    public sealed class PowerPlan
    {
        public string Guid = "";
        public string Name = "";
        public bool Active;
    }

    public static class PowerPlans
    {
        public const string SchemeUltimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public static List<PowerPlan> List()
        {
            List<PowerPlan> list = new List<PowerPlan>();
            Shell.Result r = Shell.Run("powercfg.exe", "/list", 20000);
            if (!r.Ok) return list;

            string[] lines = r.All.Replace("\r\n", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int gi = line.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
                if (gi < 0) continue;

                string rest = line.Substring(gi + 5);
                Match m = Regex.Match(rest, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
                if (!m.Success) continue;

                PowerPlan p = new PowerPlan();
                p.Guid = m.Value;
                Match n = Regex.Match(rest, @"\(([^)]*)\)");
                p.Name = n.Success ? n.Groups[1].Value.Trim() : p.Guid;
                p.Active = line.EndsWith("*", StringComparison.Ordinal);
                list.Add(p);
            }

            list.Sort(delegate (PowerPlan a, PowerPlan b)
            {
                if (a.Active != b.Active) return a.Active ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        public static bool SetActive(string guid, out string error)
        {
            error = "";
            try
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/setactive " + guid, 20000);
                if (!r.Ok) { error = r.All.Trim(); return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>确保存在“终极性能”计划（不存在则克隆高性能），并激活它。</summary>
        public static bool EnsureUltimate(out string error)
        {
            error = "";
            try
            {
                string target = SchemeUltimate; // 预置了终极性能的系统直接激活官方 GUID
                Shell.Result dup = Shell.Run("powercfg.exe", "/duplicatescheme " + SchemeUltimate, 20000);
                if (dup.Ok)
                {
                    Match m = Regex.Match(dup.All, @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
                    if (m.Success) target = m.Value; // 克隆成功：激活克隆出的新实例（原代码丢弃了新 GUID）
                }
                Shell.Result act = Shell.Run("powercfg.exe", "/setactive " + target, 20000);
                if (!act.Ok) { error = act.All.Trim(); return false; }
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
