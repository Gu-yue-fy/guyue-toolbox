﻿/* ============================================================
 * 文件说明：单行 CSV 解析（双引号包裹与 "" 转义）
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System.Collections.Generic;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>
    /// 单行 CSV 解析：支持双引号包裹、"" 转义与字段内逗号。
    /// 系统计划任务页、优化项任务状态缓存、设备管理页此前各写了一份逐字符相同的实现，
    /// 现只保留这一份。
    /// </summary>
    internal static class Csv
    {
        /// <summary>解析一行 CSV，返回字段数组。行内无分隔符时返回单元素数组。</summary>
        public static string[] SplitLine(string line)
        {
            if (line == null) return new string[0];

            List<string> fields = new List<string>();
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
                    else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
                    else cur.Append(c);
                }
            }

            fields.Add(cur.ToString());
            return fields.ToArray();
        }
    }
}
