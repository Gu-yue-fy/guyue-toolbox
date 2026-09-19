/* ============================================================
 * 文件说明：极简 JSON 只读解析器（object / array / string / number / bool / null，含 \u 转义）。
 *           用途：读取外部优化包清单（packs\*.json），不依赖任何第三方库。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GuyueBox.Core
{
    internal static class Json
    {
        /// <summary>
        /// 嵌套深度上限。递归下降解析器必须限深：损坏/恶意的清单文件可在 1 MB 内构造
        /// 极深嵌套，导致栈溢出——而 StackOverflowException 在 .NET 中不可捕获，
        /// 会直接终止进程，try/catch 兜不住。
        /// </summary>
        private const int MaxDepth = 64;

        /// <summary>解析 JSON 文本，返回 Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。</summary>
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON 内容为空");
            int i = 0;
            object v = ParseValue(text, ref i, 0);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 尾部有多余内容（位置 " + i + "）");
            return v;
        }

        public static Dictionary<string, object> AsObject(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> AsArray(object o)
        {
            return o as List<object>;
        }

        public static object Get(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v)) return null;
            return v;
        }

        public static string Str(Dictionary<string, object> o, string key, string def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            string s = v as string;
            if (s != null) return s;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            if (v is bool) return (bool)v;
            string s = v as string;
            if (s != null)
            {
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return def;
        }

        // ---------------- 内部实现 ----------------

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF')
                {
                    i++;
                    continue;
                }
                break;
            }
        }

        private static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth)
                throw new FormatException("JSON 嵌套层级过深（上限 " + MaxDepth + "）");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length ||
                string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
            {
                throw new FormatException("JSON 无效字面量（位置 " + i + "）");
            }
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            Dictionary<string, object> map =
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("JSON 对象缺少键（位置 " + i + "）");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("JSON 对象缺少冒号（位置 " + i + "）");
                i++;
                map[key] = ParseValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return map; }
                throw new FormatException("JSON 对象缺少逗号或右花括号（位置 " + i + "）");
            }
        }

        private static List<object> ParseArray(string s, ref int i, int depth)
        {
            List<object> list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("JSON 数组缺少逗号或右方括号（位置 " + i + "）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // 左引号
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out code))
                        {
                            throw new FormatException("JSON \\u 转义非法（位置 " + i + "）");
                        }
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new FormatException("JSON 非法转义 \\" + e);
                }
            }
            throw new FormatException("JSON 字符串未闭合");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    i++;
                    continue;
                }
                break;
            }
            if (i == start) throw new FormatException("JSON 无效值（位置 " + start + "）");
            string num = s.Substring(start, i - start);
            double d;
            if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("JSON 数字非法：" + num);
        }
    }
}
