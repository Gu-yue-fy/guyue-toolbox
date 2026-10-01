﻿/* ============================================================
 * 文件说明：电源计划枚举/激活/终极性能确保（方案 GUID 的唯一来源）
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

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
        // 电源方案 GUID 的唯一来源：电源计划页与优化项目录共用同一份常量，
        // 避免同一串 GUID 在两处硬编码（写错一处就会出现"点了没反应"）
        public const string SchemeHighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string SchemeBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        /// <summary>「终极性能」：默认隐藏，需 /duplicatescheme 或直接 /setactive 才生效。</summary>
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
                Shell.Result r = Shell.Run("powercfg.exe", "/setactive " + guid, 20000, isChange: true);
                if (!r.Ok) { error = r.All.Trim(); return false; }
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
