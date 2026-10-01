/* 文件说明：外部优化包 Provider：装载程序目录与用户目录下 packs\*.json 清单，物化为 RegTweak。 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 优化包清单格式（JSON）—— 外部扩展能力：把清单丢进 packs 目录即可扩展工具，
    /// 不编译、不执行任何代码（只能声明注册表写入），扩展面与风险都受控。
    ///
    /// 包级：{ "pack": "包名", "version": "2", "author": "...", "description": "...", "enabled": true,
    ///          "items": [ ... ] }
    ///
    /// 条目级（v2 新增后标 ★）：
    /// {
    ///   "id": "pack_x", "group": "系统精简", "name": "标题", "desc": "说明",
    ///   "admin": false, "risky": false, "recommended": false, "enabled": true,
    /// ★ "when":  适用条件，不满足即"本机不适用"（不显示、不可应用）
    ///            { "vendor": "N,A", "cpu": "intel", "minBuild": 22000, "elevated": true }
    /// ★ "backup": 共享备份组标识（同键多档条目共用一个还原点，与内置「CPU 调度各档」同机制）
    /// ★ "revert": 显式还原写入，优先级高于备份（"只想改回某个固定值"时用它）
    ///   "writes": [ { "hive": "HKLM", "path": "SOFTWARE\\X", "name": "Y",
    ///                 "kind": "dword|qword|string|expand|multistring|binary|delete",
    ///                 "value": 1,
    /// ★               "revertValue": 0 } ]        // 等价于为该条写入单独声明"改回 0"
    /// }
    ///
    /// 还原优先级：条目级 revert > 写入级 revertValue > RegHelper 备份（默认）。
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

        /// <summary>最近一次装载的包清单：{ 文件名, 包名, 装载项数 }（供「优化包管理」按包展示结果）。</summary>
        public static readonly List<string[]> LoadedPacks = new List<string[]>();

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
            LoadedPacks.Clear();

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

            int loaded = 0;
            for (int i = 0; i < limit; i++)
            {
                ITweak t = BuildTweak(Json.AsObject(items[i]), packName, i);
                if (t == null) continue;
                _items.Add(t);
                loaded++;
            }
            LoadedPacks.Add(new string[] { fileName, packName, loaded.ToString() });
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
                RegWrite w = BuildWrite(Json.AsObject(arr[i]), packName, id, i, false);
                if (w != null) writes.Add(w);
            }
            if (writes.Count == 0)
            {
                Problems.Add(packName + "：" + id + " 的 writes 全部非法，已跳过。");
                return null;
            }

            // ★ 还原写入：清单可显式声明「怎么改回去」，优先级高于 RegHelper 备份。
            //   ① 条目级 "revert" 数组（最明确，推荐）；
            //   ② 每条写入的 "revertValue"（够用时最省事），但必须**全部**声明——
            //      因为 RegTweak.Revert 一旦看到 RevertWrites 就完全不再走备份，
            //      混用会让没声明的那几条永远留在系统里（下面会拦下并提示）。
            List<RegWrite> reverts = new List<RegWrite>();
            List<object> revArr = Json.AsArray(Json.Get(o, "revert"));
            if (revArr != null && revArr.Count > 0)
            {
                for (int i = 0; i < revArr.Count; i++)
                {
                    RegWrite rw = BuildWrite(Json.AsObject(revArr[i]), packName, id, i, true);
                    if (rw != null) reverts.Add(rw);
                    else Problems.Add(packName + "：" + id + " 的 revert 第 " + (i + 1) + " 条非法，已忽略。");
                }
            }
            else
            {
                int declared = 0;
                for (int i = 0; i < arr.Count; i++)
                {
                    Dictionary<string, object> wo = Json.AsObject(arr[i]);
                    if (wo != null && Json.Get(wo, "revertValue") != null) declared++;
                }
                if (declared > 0 && declared == arr.Count)
                {
                    for (int i = 0; i < arr.Count; i++)
                    {
                        RegWrite rw = BuildWrite(Json.AsObject(arr[i]), packName, id, i, true);
                        if (rw != null) reverts.Add(rw);
                    }
                }
                else if (declared > 0)
                {
                    Problems.Add(packName + "：" + id + " 只有 " + declared + "/" + arr.Count +
                        " 条写入声明了 revertValue：混用会漏还原，已整项退回备份还原；请全部声明或全部去掉。");
                }
            }

            RegTweak t = RegTweak.Create(id, group, name,
                Json.Str(o, "desc", "（来自优化包 " + packName + "）"),
                adminOnly: Json.Bool(o, "admin", false),
                risky: Json.Bool(o, "risky", false),
                recommended: Json.Bool(o, "recommended", false),
                backupId: Json.Str(o, "backup", null));
            for (int i = 0; i < writes.Count; i++) t.Enable.Add(writes[i]);
            for (int i = 0; i < reverts.Count; i++) t.RevertWrites.Add(reverts[i]);

            // ★ 适用条件：不满足时优化中心不显示、Apply 直接返回 false（不写无效键）
            ICondition cond = BuildCondition(Json.AsObject(Json.Get(o, "when")), packName, id);
            if (cond != null) t.ApplicableWhen(cond);
            return t;
        }

        /// <summary>
        /// 解析一条写入。forRevert=true 表示这是**还原方向**的写入：
        /// 取值改读 revertValue（条目级 revert 数组里也可直接写 value），
        /// 类型可用 revertKind 覆盖（启用方向是 delete、还原方向要写回原值时用得上）。
        /// </summary>
        private RegWrite BuildWrite(Dictionary<string, object> o, string packName, string id, int index,
            bool forRevert)
        {
            if (o == null)
            {
                Problems.Add(packName + "：" + id + " 第 " + (index + 1) +
                    (forRevert ? " 条还原写入不是对象，已跳过。" : " 条写入不是对象，已跳过。"));
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

            string kind = Json.Str(o, forRevert ? "revertKind" : "kind", null);
            if (string.IsNullOrEmpty(kind))
            {
                // 还原方向未写 revertKind 时沿用启用方向的类型；
                // 但启用方向是 delete 时不能拿 delete 当还原（那等于再删一次），退化为 dword 写回原值
                kind = Json.Str(o, "kind", "dword");
                if (forRevert && kind.Equals("delete", StringComparison.OrdinalIgnoreCase)) kind = "dword";
            }
            kind = kind.ToLowerInvariant();
            if (kind == "delete") return RegWrite.Remove(hive, path, valueName);

            object raw = Json.Get(o, forRevert ? "revertValue" : "value");
            // 条目级 revert 数组里通常直接写 "value"：还原方向没给 revertValue 时回落到 value
            if (raw == null && forRevert) raw = Json.Get(o, "value");

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

        /// <summary>
        /// 解析条目的 "when" 适用条件：不满足即"本机不适用"（优化中心不显示、Apply 直接返回 false）。
        /// 支持 vendor（显卡厂商，可逗号组合）/ cpu（处理器厂商）/ minBuild（Windows 内部版本下限）/
        /// elevated（需管理员）。多个字段是与关系；没有任何可识别字段时返回 null（= 恒适用）。
        /// 未知字段进 Problems，便于清单作者立刻发现拼写错误（否则会静默变成"恒适用"）。
        /// </summary>
        private ICondition BuildCondition(Dictionary<string, object> o, string packName, string id)
        {
            if (o == null) return null;

            List<ICondition> parts = new List<ICondition>();
            foreach (KeyValuePair<string, object> kv in o)
            {
                string key = kv.Key == null ? "" : kv.Key.ToLowerInvariant();
                switch (key)
                {
                    case "vendor":
                        {
                            string v = ToStr(kv.Value).Trim();
                            if (v.Length > 0) parts.Add(new GpuVendorCondition(v));
                            break;
                        }
                    case "cpu":
                        {
                            string v = ToStr(kv.Value).Trim();
                            if (v.Length > 0) parts.Add(new CpuVendorCondition(v));
                            break;
                        }
                    case "minbuild":
                        {
                            int build;
                            if (TryToInt(kv.Value, out build) && build > 0) parts.Add(new WindowsBuildCondition(build));
                            else Problems.Add(packName + "：" + id + " 的 when.minBuild 不是正整数，已忽略。");
                            break;
                        }
                    case "elevated":
                        {
                            if (Json.Bool(o, kv.Key, false)) parts.Add(new ElevatedCondition());
                            break;
                        }
                    default:
                        Problems.Add(packName + "：" + id + " 的 when 含未知字段 \"" + kv.Key +
                            "\"，已忽略（可用：vendor / cpu / minBuild / elevated）。");
                        break;
                }
            }

            if (parts.Count == 0) return null;
            return parts.Count == 1 ? parts[0] : new AllCondition(parts);
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
