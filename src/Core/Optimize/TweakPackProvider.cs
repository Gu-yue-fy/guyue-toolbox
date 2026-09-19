/* ============================================================
 * 文件说明：外部优化包 Provider：扫描 packs 目录下的 *.json 清单，把声明式条目物化为 RegTweak，
 *           自动接入优化中心 / 一键推荐 / 方案库（无需改动既有代码）。
 *           机制：清单声明（JSON）→ 运行时操作物化。
 *           装载位置：<程序目录>\packs\*.json 与 %LOCALAPPDATA%\GuyueBox\packs\*.json
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 优化包清单格式（JSON）：
    /// {
    ///   "pack": "包名", "version": "1.0", "author": "...", "enabled": true,
    ///   "items": [
    ///     { "id": "pack_x", "group": "系统精简", "name": "标题", "desc": "说明",
    ///       "admin": false, "risky": false, "recommended": false, "enabled": true,
    ///       "writes": [ { "hive": "HKLM", "path": "SOFTWARE\\X", "name": "Y",
    ///                     "kind": "dword|qword|string|expand|multistring|binary|delete",
    ///                     "value": 1 } ] } ]
    /// }
    /// </summary>
    public sealed class TweakPackProvider : ITweakProvider
    {
        /// <summary>未指定 group 时的默认分组（该分组只在优化中心「全部」分类下出现）。</summary>
        public const string GPack = "扩展优化包";

        private const string PackFolderName = "packs";
        private const int MaxFileBytes = 1024 * 1024; // 单个清单最大 1 MB
        private const int MaxItemsPerPack = 200;      // 单个包最多 200 项

        private static readonly Regex IdPattern =
            new Regex("^[A-Za-z0-9_]{1,64}$", RegexOptions.Compiled);

        private readonly List<ITweak> _items = new List<ITweak>();

        /// <summary>最近一次装载的非致命问题（文件损坏 / 条目非法 / 包被禁用等），供界面提示。</summary>
        public static readonly List<string> Problems = new List<string>();

        public TweakPackProvider()
        {
            Load();
        }

        public IEnumerable<ITweak> Provide()
        {
            return new List<ITweak>(_items);
        }

        // ---------------- 目录 ----------------

        /// <summary>程序目录下的 packs 文件夹（不存在时自动创建，便于用户投放清单）。</summary>
        public static string AppPackFolder()
        {
            try
            {
                string dir = Path.Combine(AppDirectory(), PackFolderName);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>用户目录下的 packs 文件夹（%LOCALAPPDATA%\GuyueBox\packs）。</summary>
        public static string UserPackFolder()
        {
            try
            {
                string dir = Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GuyueBox"), PackFolderName);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return null;
            }
        }

        private static string AppDirectory()
        {
            try
            {
                string loc = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
            }
            catch
            {
            }
            return Environment.CurrentDirectory;
        }

        // ---------------- 装载 ----------------

        private void Load()
        {
            Problems.Clear();

            List<string> dirs = new List<string>();
            string a = AppPackFolder();
            if (!string.IsNullOrEmpty(a)) dirs.Add(a);
            string u = UserPackFolder();
            if (!string.IsNullOrEmpty(u) && !string.Equals(u, a, StringComparison.OrdinalIgnoreCase))
                dirs.Add(u);

            for (int d = 0; d < dirs.Count; d++)
            {
                string[] files;
                try { files = Directory.GetFiles(dirs[d], "*.json"); }
                catch { continue; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int f = 0; f < files.Length; f++) LoadFile(files[f]);
            }
        }

        private void LoadFile(string path)
        {
            string fileName;
            try { fileName = Path.GetFileName(path); }
            catch { fileName = path; }

            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Length > MaxFileBytes)
                {
                    Problems.Add(fileName + "：清单超过 1 MB，已跳过。");
                    return;
                }
            }
            catch
            {
            }

            string text;
            try { text = File.ReadAllText(path, System.Text.Encoding.UTF8); }
            catch (Exception ex)
            {
                Problems.Add(fileName + "：读取失败（" + ex.Message + "）。");
                return;
            }

            Dictionary<string, object> root;
            try { root = Json.AsObject(Json.Parse(text)); }
            catch (Exception ex)
            {
                Problems.Add(fileName + "：JSON 解析失败（" + ex.Message + "）。");
                return;
            }
            if (root == null)
            {
                Problems.Add(fileName + "：清单根节点必须是对象。");
                return;
            }

            string packName = Json.Str(root, "pack", fileName);
            if (!Json.Bool(root, "enabled", true))
            {
                Problems.Add(packName + "：包已禁用（enabled=false），未装载。");
                return;
            }

            List<object> items = Json.AsArray(Json.Get(root, "items"));
            if (items == null || items.Count == 0)
            {
                Problems.Add(packName + "：items 缺省或为空，未装载。");
                return;
            }

            int limit = items.Count;
            if (limit > MaxItemsPerPack)
            {
                Problems.Add(packName + "：条目数 " + items.Count + " 超过上限 " + MaxItemsPerPack +
                    "，仅装载前 " + MaxItemsPerPack + " 项。");
                limit = MaxItemsPerPack;
            }

            for (int i = 0; i < limit; i++)
            {
                ITweak t = BuildTweak(Json.AsObject(items[i]), packName, i);
                if (t == null) continue;
                _items.Add(t);
            }
        }

        private ITweak BuildTweak(Dictionary<string, object> o, string packName, int index)
        {
            if (o == null)
            {
                Problems.Add(packName + "：第 " + (index + 1) + " 项不是对象，已跳过。");
                return null;
            }
            if (!Json.Bool(o, "enabled", true)) return null; // 单项禁用：静默跳过

            string id = Json.Str(o, "id", "");
            string name = Json.Str(o, "name", "");
            string group = Json.Str(o, "group", GPack);
            if (string.IsNullOrEmpty(group)) group = GPack;

            if (!IdPattern.IsMatch(id))
            {
                Problems.Add(packName + "：第 " + (index + 1) +
                    " 项的 id 非法（需 1-64 位字母/数字/下划线），已跳过。");
                return null;
            }
            if (string.IsNullOrEmpty(name))
            {
                Problems.Add(packName + "：" + id + " 缺少 name，已跳过。");
                return null;
            }

            List<object> arr = Json.AsArray(Json.Get(o, "writes"));
            if (arr == null || arr.Count == 0)
            {
                Problems.Add(packName + "：" + id + " 没有 writes，已跳过。");
                return null;
            }

            List<RegWrite> writes = new List<RegWrite>();
            for (int i = 0; i < arr.Count; i++)
            {
                RegWrite w = BuildWrite(Json.AsObject(arr[i]), packName, id, i);
                if (w != null) writes.Add(w);
            }
            if (writes.Count == 0)
            {
                Problems.Add(packName + "：" + id + " 的 writes 全部非法，已跳过。");
                return null;
            }

            RegTweak t = new RegTweak();
            t.IdValue = id;
            t.GroupValue = group;
            t.NameValue = name;
            t.DescriptionValue = Json.Str(o, "desc", "（来自优化包 " + packName + "）");
            t.AdminOnlyValue = Json.Bool(o, "admin", false);
            t.RiskyValue = Json.Bool(o, "risky", false);
            t.RecommendedValue = Json.Bool(o, "recommended", false);
            for (int i = 0; i < writes.Count; i++) t.Enable.Add(writes[i]);
            return t;
        }

        private RegWrite BuildWrite(Dictionary<string, object> o, string packName, string id, int index)
        {
            if (o == null)
            {
                Problems.Add(packName + "：" + id + " 第 " + (index + 1) + " 条写入不是对象，已跳过。");
                return null;
            }

            RegistryHive hive;
            if (!TryParseHive(Json.Str(o, "hive", "HKCU"), out hive))
            {
                Problems.Add(packName + "：" + id + " 第 " + (index + 1) + " 条写入的 hive 非法，已跳过。");
                return null;
            }

            string path = Json.Str(o, "path", "");
            string valueName = Json.Str(o, "name", "");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(valueName))
            {
                Problems.Add(packName + "：" + id + " 第 " + (index + 1) +
                    " 条写入缺少 path/name，已跳过。");
                return null;
            }

            string kind = Json.Str(o, "kind", "dword").ToLowerInvariant();
            if (kind == "delete") return RegWrite.Remove(hive, path, valueName);

            object raw = Json.Get(o, "value");

            switch (kind)
            {
                case "dword":
                case "int":
                    {
                        int iv;
                        if (!TryToInt(raw, out iv))
                        {
                            Problems.Add(packName + "：" + id + " 第 " + (index + 1) +
                                " 条写入的 value 不是整数，已跳过。");
                            return null;
                        }
                        return RegWrite.Dword(hive, path, valueName, iv);
                    }
                case "qword":
                case "long":
                    {
                        long lv;
                        if (!TryToLong(raw, out lv))
                        {
                            Problems.Add(packName + "：" + id + " 第 " + (index + 1) +
                                " 条写入的 value 不是整数，已跳过。");
                            return null;
                        }
                        RegWrite w = new RegWrite();
                        w.Hive = hive; w.Path = path; w.Name = valueName;
                        w.Kind = RegistryValueKind.QWord; w.Value = lv;
                        return w;
                    }
                case "string":
                case "sz":
                    return RegWrite.Str(hive, path, valueName, ToStr(raw));
                case "expand":
                case "expandstring":
                    {
                        RegWrite w = new RegWrite();
                        w.Hive = hive; w.Path = path; w.Name = valueName;
                        w.Kind = RegistryValueKind.ExpandString; w.Value = ToStr(raw);
                        return w;
                    }
                case "multistring":
                case "multisz":
                    {
                        List<string> vals = new List<string>();
                        List<object> parts = Json.AsArray(raw);
                        if (parts != null)
                        {
                            for (int i = 0; i < parts.Count; i++) vals.Add(ToStr(parts[i]));
                        }
                        else
                        {
                            string s = ToStr(raw);
                            if (s.Length > 0) vals.AddRange(s.Split('|'));
                        }
                        RegWrite w = new RegWrite();
                        w.Hive = hive; w.Path = path; w.Name = valueName;
                        w.Kind = RegistryValueKind.MultiString; w.Value = vals.ToArray();
                        return w;
                    }
                case "binary":
                case "bin":
                    {
                        try
                        {
                            return RegWrite.Binary(hive, path, valueName,
                                Convert.FromBase64String(ToStr(raw)));
                        }
                        catch
                        {
                            Problems.Add(packName + "：" + id + " 第 " + (index + 1) +
                                " 条写入的 base64 非法，已跳过。");
                            return null;
                        }
                    }
                default:
                    Problems.Add(packName + "：" + id + " 第 " + (index + 1) + " 条写入的 kind \"" +
                        kind + "\" 未知，已跳过。");
                    return null;
            }
        }

        private static bool TryParseHive(string s, out RegistryHive hive)
        {
            hive = RegistryHive.CurrentUser;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToUpperInvariant())
            {
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    hive = RegistryHive.CurrentUser; return true;
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    hive = RegistryHive.LocalMachine; return true;
                case "HKCR":
                case "HKEY_CLASSES_ROOT":
                    hive = RegistryHive.ClassesRoot; return true;
                case "HKU":
                case "HKEY_USERS":
                    hive = RegistryHive.Users; return true;
                case "HKCC":
                case "HKEY_CURRENT_CONFIG":
                    hive = RegistryHive.CurrentConfig; return true;
                default:
                    return false;
            }
        }

        private static bool TryToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null) return false;
            if (raw is double)
            {
                double d = (double)raw;
                if (d != Math.Floor(d) || d < int.MinValue || d > uint.MaxValue) return false;
                value = unchecked((int)(long)d);
                return true;
            }
            string s = raw as string;
            if (s == null) s = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uint uhex;
                if (uint.TryParse(s.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out uhex))
                {
                    value = unchecked((int)uhex);
                    return true;
                }
                return false;
            }
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null) return false;
            if (raw is double)
            {
                value = (long)(double)raw;
                return true;
            }
            string s = raw as string;
            if (s == null) s = Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long hex;
                if (long.TryParse(s.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out hex))
                {
                    value = hex;
                    return true;
                }
                return false;
            }
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string ToStr(object raw)
        {
            if (raw == null) return "";
            string s = raw as string;
            if (s != null) return s;
            if (raw is bool) return ((bool)raw) ? "1" : "0";
            return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        }
    }
}
