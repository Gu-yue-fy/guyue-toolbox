/* 文件说明：优化方案文件（*.stprofile）的读写与校验。界面导入/导出与命令行 --apply 共用同一条解析。 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>方案文档的解析结果：合法 Id 列表 + 非致命警告（app/版本不符等，调用方决定如何提示）。</summary>
    public sealed class ProfileDocument
    {
        public List<string> Ids = new List<string>();
        public string Saved = "";
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 方案文件格式：{"app":"GuyueBox","version":1,"saved":"yyyy-MM-dd HH:mm","ids":["id1","id2",…]}
    /// ids 为优化项 Id（与 TweakLibrary 一致）。此前解析散在 UI 层用正则从文本里抽字符串：
    /// app/版本不校验、ids 含转义或非法字符会被静默读坏——现在收口到这里。
    /// </summary>
    public static class ProfileFile
    {
        public const string AppTag = "GuyueBox";
        public const int Version = 1;

        /// <summary>读方案文件。error 非空表示文件不可用（返回 null）；否则返回文档与警告列表。</summary>
        public static ProfileDocument Read(string path, out string error)
        {
            error = null;

            string text;
            try
            {
                text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                return null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "文件是空的。";
                return null;
            }

            object parsed;
            try
            {
                parsed = Json.Parse(text);
            }
            catch (Exception ex)
            {
                error = "不是合法的 JSON：" + ex.Message;
                return null;
            }

            Dictionary<string, object> root = Json.AsObject(parsed);
            if (root == null)
            {
                error = "根节点必须是对象。";
                return null;
            }

            ProfileDocument doc = new ProfileDocument();

            string app = Json.Str(root, "app", AppTag);
            if (!string.Equals(app, AppTag, StringComparison.OrdinalIgnoreCase))
            {
                doc.Warnings.Add("文件声明由 \"" + app + "\" 导出（本工具为 " + AppTag + "），将按 Id 尽力匹配。");
            }

            int ver = 0;
            object vraw = Json.Get(root, "version");
            if (vraw is double) ver = (int)(double)vraw;
            else int.TryParse(Convert.ToString(vraw, CultureInfo.InvariantCulture), out ver);
            if (ver > Version)
            {
                doc.Warnings.Add("文件版本 v" + ver + " 比本程序支持的 v" + Version + " 新，按现有格式尽力读取。");
            }

            doc.Saved = Json.Str(root, "saved", "");

            List<object> arr = Json.AsArray(Json.Get(root, "ids"));
            if (arr == null || arr.Count == 0)
            {
                error = "文件里没有 ids（没有任何优化项）。";
                return null;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arr.Count; i++)
            {
                string id = arr[i] as string;
                if (string.IsNullOrEmpty(id) && arr[i] != null)
                {
                    id = Convert.ToString(arr[i], CultureInfo.InvariantCulture);
                }
                id = (id ?? "").Trim();
                if (id.Length == 0)
                {
                    doc.Warnings.Add("第 " + (i + 1) + " 个 Id 为空，已跳过。");
                    continue;
                }
                if (id.Length > 128 || id.IndexOf('"') >= 0 || id.IndexOf('\\') >= 0 ||
                    id.IndexOf('\n') >= 0 || id.IndexOf('\r') >= 0)
                {
                    doc.Warnings.Add("第 " + (i + 1) + " 个 Id 含非法字符或过长，已跳过。");
                    continue;
                }
                if (!seen.Add(id)) continue;   // 重复 Id 只留一个
                doc.Ids.Add(id);
            }

            if (doc.Ids.Count == 0)
            {
                error = "ids 里没有合法的优化项 Id。";
                return null;
            }
            return doc;
        }

        /// <summary>写方案文件。返回 null=成功，否则为错误消息。Id 里的特殊字符会转义。</summary>
        public static string Write(string path, IEnumerable<string> ids)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"app\":\"").Append(AppTag).Append("\",\"version\":").Append(Version);
            sb.Append(",\"saved\":\"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\",\"ids\":[");
            bool first = true;
            foreach (string id in ids)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append((id ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            sb.Append("]}");

            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
