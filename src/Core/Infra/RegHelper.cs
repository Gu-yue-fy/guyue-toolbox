/* ============================================================
 * 文件说明：注册表读写核心：修改前自动备份原值，还原时精确恢复（含『原本不存在则删除』的空键清理）。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 一条注册表备份记录。用于让每一项优化都可以被还原。
    /// 新格式：值名 = "Hive|Path|Name"（定位 O(1)），值内容 = "Existed|Kind|Data"。
    /// 旧格式（值名为序号、内容含全部字段）仅保留读取兼容。
    /// </summary>
    public sealed class RegEntry
    {
        public RegistryHive Hive;
        public string Path = "";
        public string Name = "";
        public bool Existed;
        public RegistryValueKind Kind = RegistryValueKind.String;
        public string Data = "";

        /// <summary>备份条目的定位键（即新格式的值名）。</summary>
        public string Key
        {
            get { return Hive.ToString() + "|" + Path + "|" + Name; }
        }

        /// <summary>旧格式整串（仅用于识别，不再写入）。</summary>
        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Hive.ToString()).Append('|');
            sb.Append(Path).Append('|');
            sb.Append(Name).Append('|');
            sb.Append(Existed ? '1' : '0').Append('|');
            sb.Append(((int)Kind).ToString(CultureInfo.InvariantCulture)).Append('|');
            sb.Append(Data);
            return sb.ToString();
        }

        /// <summary>新格式内容段（定位信息在值名中）。</summary>
        public string SerializePayload()
        {
            return (Existed ? "1" : "0") + "|" +
                ((int)Kind).ToString(CultureInfo.InvariantCulture) + "|" + Data;
        }

        public static RegEntry Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split(new char[] { '|' }, 6);
            if (parts.Length < 6) return null;

            RegEntry e = new RegEntry();
            try
            {
                e.Hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), parts[0]);
            }
            catch
            {
                e.Hive = RegistryHive.CurrentUser;
            }
            e.Path = parts[1];
            e.Name = parts[2];
            e.Existed = parts[3] == "1";
            int kind;
            if (int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out kind))
                e.Kind = (RegistryValueKind)kind;
            e.Data = parts[5];
            return e;
        }

        /// <summary>解析新格式（值名定位 + 内容段）。</summary>
        public static bool TryParseSlot(string slotName, string payload, out RegEntry e)
        {
            e = null;
            if (string.IsNullOrEmpty(slotName) || payload == null) return false;
            int p1 = slotName.IndexOf('|');
            int p2 = p1 < 0 ? -1 : slotName.IndexOf('|', p1 + 1);
            if (p1 <= 0 || p2 < 0) return false;

            string[] parts = payload.Split(new char[] { '|' }, 3);
            if (parts.Length < 3) return false;

            RegEntry r = new RegEntry();
            try
            {
                r.Hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), slotName.Substring(0, p1));
            }
            catch
            {
                return false;
            }
            r.Path = slotName.Substring(p1 + 1, p2 - p1 - 1);
            r.Name = slotName.Substring(p2 + 1);
            r.Existed = parts[0] == "1";
            int kind;
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out kind))
                r.Kind = (RegistryValueKind)kind;
            r.Data = parts[2];
            e = r;
            return true;
        }

        /// <summary>把记录写回已打开（可写）的目标键。返回是否真正成功。</summary>
        public bool RestoreInto(RegistryKey target)
        {
            if (target == null) return false;
            try
            {
                if (!Existed)
                {
                    target.DeleteValue(Name, false);
                    return true;
                }
                switch (Kind)
                {
                    case RegistryValueKind.DWord:
                        target.SetValue(Name, int.Parse(Data, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
                        break;
                    case RegistryValueKind.QWord:
                        target.SetValue(Name, long.Parse(Data, CultureInfo.InvariantCulture), RegistryValueKind.QWord);
                        break;
                    case RegistryValueKind.Binary:
                        target.SetValue(Name, Convert.FromBase64String(Data), RegistryValueKind.Binary);
                        break;
                    case RegistryValueKind.MultiString:
                        target.SetValue(Name, Data.Length == 0 ? new string[0] : Data.Split('\n'),
                            RegistryValueKind.MultiString);
                        break;
                    case RegistryValueKind.ExpandString:
                        target.SetValue(Name, Data, RegistryValueKind.ExpandString);
                        break;
                    default:
                        target.SetValue(Name, Data, RegistryValueKind.String);
                        break;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Restore()
        {
            try
            {
                RegistryKey baseKey = RegHelper.BaseForRestore(Hive);
                using (RegistryKey k = Existed ? baseKey.CreateSubKey(Path) : baseKey.OpenSubKey(Path, true))
                {
                    RestoreInto(k);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 注册表读写 + 自动备份 / 还原。
    /// </summary>
    public static class RegHelper
    {
        private const string BackupRoot = @"Software\GuyueBox\Backup";

        // 基键句柄缓存：HKLM/HKCU 等基键进程级常驻，省掉每次读写的 RegOpenKey/RegCloseKey
        // 系统调用（优化中心状态探测一轮要数千次）。共享句柄绝不 Dispose，由进程退出统一回收。
        private static readonly Dictionary<RegistryHive, RegistryKey> _baseCache =
            new Dictionary<RegistryHive, RegistryKey>();

        /// <summary>取（或建立）hive 的常驻基键。返回的键由缓存持有，调用方不得 Dispose。</summary>
        private static RegistryKey Base(RegistryHive hive)
        {
            lock (_baseCache)
            {
                RegistryKey k;
                if (_baseCache.TryGetValue(hive, out k)) return k;
                k = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                _baseCache[hive] = k;
                return k;
            }
        }

        public static RegistryKey OpenBase(RegistryHive hive)
        {
            return Base(hive);
        }

        /// <summary>关联类（RegEntry）内部使用的缓存基键。返回的键由缓存持有，不得 Dispose。</summary>
        internal static RegistryKey BaseForRestore(RegistryHive hive)
        {
            return Base(hive);
        }

        // ---------------- 读取 ----------------

        public static object GetValue(RegistryHive hive, string path, string name)
        {
            try
            {
                RegistryKey b = Base(hive);
                using (RegistryKey k = b.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    return k.GetValue(name, null);
                }
            }
            catch
            {
                return null;
            }
        }

        public static int GetInt(RegistryHive hive, string path, string name, int def)
        {
            object v = GetValue(hive, path, name);
            if (v == null) return def;
            try
            {
                if (v is int) return (int)v;
                int r;
                if (int.TryParse(v.ToString(), out r)) return r;
            }
            catch
            {
            }
            return def;
        }

        // ---------------- 写入（带备份） ----------------

        /// <summary>
        /// 设备实例键专用写入：只写已存在的键，键不存在时静默跳过（返回 true）。
        /// 杜绝 CreateSubKey 在单显卡/单声卡机器上凭空制造 0001/0002… 幽灵设备键。
        /// </summary>
        public static bool SetValueOnExisting(RegistryHive hive, string path, string name,
            object value, RegistryValueKind kind, string backupId)
        {
            try
            {
                RegistryKey b = Base(hive);
                using (RegistryKey probe = b.OpenSubKey(path, false))
                {
                    if (probe == null) return true; // 实例不存在：跳过，不算失败
                }
                return SetValue(hive, path, name, value, kind, backupId);
            }
            catch
            {
                return false;
            }
        }

        public static bool SetValue(RegistryHive hive, string path, string name, object value,
            RegistryValueKind kind, string backupId)
        {
            try
            {
                if (!string.IsNullOrEmpty(backupId))
                    RecordOriginal(backupId, hive, path, name);

                RegistryKey b = Base(hive);
                using (RegistryKey k = b.CreateSubKey(path))
                {
                    if (k == null)
                    {
                        RegLog.Add(backupId, "写入失败", hive + "\\" + path + " → " + name + "（无法创建键）");
                        return false;
                    }
                    k.SetValue(name, value, kind);
                    RegLog.Add(backupId, "写入", hive + "\\" + path + " → " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                RegLog.Add(backupId, "写入失败", hive + "\\" + path + " → " + name + "（" + ex.Message + "）");
                return false;
            }
        }

        public static bool DeleteValue(RegistryHive hive, string path, string name, string backupId)
        {
            try
            {
                if (!string.IsNullOrEmpty(backupId))
                    RecordOriginal(backupId, hive, path, name);

                RegistryKey b = Base(hive);
                using (RegistryKey k = b.OpenSubKey(path, true))
                {
                    if (k == null) return true;
                    k.DeleteValue(name, false);
                    RegLog.Add(backupId, "删除", hive + "\\" + path + " → " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                RegLog.Add(backupId, "删除失败", hive + "\\" + path + " → " + name + "（" + ex.Message + "）");
                return false;
            }
        }

        // ---------------- 备份 ----------------

        private static string BackupKeyPath(string backupId)
        {
            return BackupRoot + "\\" + backupId;
        }

        /// <summary>开始一次新的备份会话，会清除该 id 的旧备份。</summary>
        /// <summary>
        /// 备份组建立（保留式）：已有备份不重置。
        /// RecordOriginal 按键去重，保证重复 Apply、以及多个互斥变体共享同一
        /// 备份组（BackupIdValue）时，首次记录的系统原值不会被后续覆盖——
        /// 否则 apply→apply→revert 会把"优化值"当原值还原回去。
        /// </summary>
        public static void BeginBackup(string backupId)
        {
        }

        private static void RecordOriginal(string backupId, RegistryHive hive, string path, string name)
        {
            try
            {
                RegistryKey b = Base(RegistryHive.CurrentUser);
                using (RegistryKey key = b.CreateSubKey(BackupKeyPath(backupId)))
                {
                    if (key == null) return;

                    // O(1) 去重：值名即 "Hive|Path|Name"，已存在即记录过（首次记录的原值永不覆盖）
                    string slot = hive.ToString() + "|" + path + "|" + name;
                    if (key.GetValue(slot, null) != null) return;

                    RegEntry entry = new RegEntry();
                    entry.Hive = hive;
                    entry.Path = path;
                    entry.Name = name;

                    using (RegistryKey src = Base(hive).OpenSubKey(path, false))
                    {
                        if (src != null)
                        {
                            object val = src.GetValue(name, null);
                            if (val != null)
                            {
                                entry.Existed = true;
                                try { entry.Kind = src.GetValueKind(name); }
                                catch { entry.Kind = RegistryValueKind.String; }
                                entry.Data = SerializeValue(val, entry.Kind);
                            }
                        }
                    }

                    key.SetValue(slot, entry.SerializePayload(), RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        private static string SerializeValue(object val, RegistryValueKind kind)
        {
            try
            {
                switch (kind)
                {
                    case RegistryValueKind.DWord:
                        return Convert.ToInt32(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    case RegistryValueKind.QWord:
                        return Convert.ToInt64(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    case RegistryValueKind.Binary:
                        return Convert.ToBase64String((byte[])val);
                    case RegistryValueKind.MultiString:
                        return string.Join("\n", (string[])val);
                    default:
                        return val.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>把某项优化的所有改动还原到修改前的状态。同键多条目合并开键，兼容旧格式备份。
        /// 返回 true 表示备份存在且全部条目还原成功；存在备份但写入失败时返回 false（不再误报"已还原"）。</summary>
        public static bool Restore(string backupId)
        {
            bool any = false;
            int fail = 0;
            try
            {
                List<RegEntry> entries = new List<RegEntry>();
                RegistryKey b = Base(RegistryHive.CurrentUser);
                using (RegistryKey key = b.OpenSubKey(BackupKeyPath(backupId), false))
                {
                    if (key == null) return false;

                    string[] names = key.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string raw = key.GetValue(names[i]) as string;
                        if (raw == null) continue;
                        RegEntry e;
                        if (names[i].IndexOf('|') > 0)
                        {
                            if (!RegEntry.TryParseSlot(names[i], raw, out e)) continue;
                        }
                        else
                        {
                            e = RegEntry.Parse(raw); // 旧格式：序号值名 + 全字段内容
                            if (e == null) continue;
                        }
                        entries.Add(e);
                        any = true;
                    }
                }

                // 按 Hive+Path 分组，每组只开一次键
                Dictionary<string, List<RegEntry>> groups = new Dictionary<string, List<RegEntry>>();
                for (int i = 0; i < entries.Count; i++)
                {
                    string gk = entries[i].Hive + "|" + entries[i].Path;
                    List<RegEntry> g;
                    if (!groups.TryGetValue(gk, out g))
                    {
                        g = new List<RegEntry>();
                        groups[gk] = g;
                    }
                    g.Add(entries[i]);
                }

                foreach (KeyValuePair<string, List<RegEntry>> kv in groups)
                {
                    try
                    {
                        RegistryKey baseKey = Base(kv.Value[0].Hive);
                        {
                            bool needWrite = false;
                            foreach (RegEntry e in kv.Value)
                            {
                                if (e.Existed) { needWrite = true; break; }
                            }

                            if (needWrite)
                            {
                                using (RegistryKey target = baseKey.CreateSubKey(kv.Value[0].Path))
                                {
                                    if (target != null)
                                    {
                                        for (int i = 0; i < kv.Value.Count; i++)
                                            if (!kv.Value[i].RestoreInto(target)) fail++;
                                    }
                                }
                                RegLog.Add(backupId, "还原", kv.Value[0].Hive + "\\" + kv.Value[0].Path + "（" + kv.Value.Count + " 项）");
                            }
                            else
                            {
                                // 该组全部条目"原本不存在"= 本项新建的键：还原后清理空键
                                // （必须在 using 之外删除——句柄未关闭时 DeleteSubKeyTree 会静默失败）
                                string purgePath = null;
                                using (RegistryKey target = baseKey.OpenSubKey(kv.Value[0].Path, true))
                                {
                                    if (target != null)
                                    {
                                        for (int i = 0; i < kv.Value.Count; i++)
                                        {
                                            if (!kv.Value[i].RestoreInto(target)) fail++;
                                        }
                                        if (target.ValueCount == 0 && target.SubKeyCount == 0)
                                        {
                                            purgePath = kv.Value[0].Path;
                                        }
                                    }
                                }
                                if (purgePath != null)
                                {
                                    try { baseKey.DeleteSubKeyTree(purgePath); } catch { fail++; }
                                    RegLog.Add(backupId, "还原", kv.Value[0].Hive + "\\" + kv.Value[0].Path + "（清理新建键）");
                                }
                            }
                        }
                    }
                    catch
                    {
                        fail++;
                        RegLog.Add(backupId, "还原失败", kv.Value[0].Hive + "\\" + kv.Value[0].Path);
                    }
                }
            }
            catch
            {
                fail++;
            }
            return any && fail == 0;
        }

        public static bool HasBackup(string backupId)
        {
            try
            {
                RegistryKey b = Base(RegistryHive.CurrentUser);
                using (RegistryKey key = b.OpenSubKey(BackupKeyPath(backupId), false))
                {
                    return key != null && key.GetValueNames().Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 服务启动类型 ----------------

        public const int SvcBoot = 0;
        public const int SvcSystem = 1;
        public const int SvcAuto = 2;
        public const int SvcManual = 3;
        public const int SvcDisabled = 4;

        public static int GetServiceStart(string serviceName)
        {
            return GetInt(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\" + serviceName, "Start", -1);
        }

        public static bool SetServiceStart(string serviceName, int startType)
        {
            return SetServiceStart(serviceName, startType, null);
        }

        /// <summary>带备份的服务启动类型修改：backupId 非空时把系统原 Start 值记入备份组，
        /// 供 GetServiceStartOriginal 还原（否则重启后只能猜"手动"这个兜底值）。</summary>
        public static bool SetServiceStart(string serviceName, int startType, string backupId)
        {
            return SetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\" + serviceName, "Start",
                startType, RegistryValueKind.DWord, backupId);
        }

        /// <summary>读取备份组里记录的服务原始 Start 值。无记录返回 -1。</summary>
        public static int GetServiceStartOriginal(string backupId, string serviceName)
        {
            try
            {
                RegistryKey b = Base(RegistryHive.CurrentUser);
                using (RegistryKey key = b.OpenSubKey(BackupKeyPath(backupId), false))
                {
                    if (key == null) return -1;
                    string slot = RegistryHive.LocalMachine + "|SYSTEM\\CurrentControlSet\\Services\\" +
                        serviceName + "|Start";
                    string raw = key.GetValue(slot) as string;
                    if (raw == null) return -1;
                    RegEntry e;
                    if (!RegEntry.TryParseSlot(slot, raw, out e)) return -1;
                    if (!e.Existed) return -1;
                    int v;
                    if (int.TryParse(e.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
                    return -1;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static string ServiceStartText(int start)
        {
            switch (start)
            {
                case SvcBoot: return "引导";
                case SvcSystem: return "系统";
                case SvcAuto: return "自动";
                case SvcManual: return "手动";
                case SvcDisabled: return "已禁用";
                default: return "未知";
            }
        }
    }
}
