using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================
    public sealed class RegWrite
    {
        public RegistryHive Hive = RegistryHive.CurrentUser;
        public string Path = "";
        public string Name = "";
        public RegistryValueKind Kind = RegistryValueKind.DWord;
        public object Value;

        /// <summary>为 true 时表示删除该值（回到系统默认）。</summary>
        public bool Delete;

        public static RegWrite Dword(RegistryHive hive, string path, string name, int value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.DWord;
            w.Value = value;
            return w;
        }

        public static RegWrite Str(RegistryHive hive, string path, string name, string value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.String;
            w.Value = value;
            return w;
        }

        public static RegWrite Binary(RegistryHive hive, string path, string name, byte[] value)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Kind = RegistryValueKind.Binary;
            w.Value = value;
            return w;
        }

        public static RegWrite Remove(RegistryHive hive, string path, string name)
        {
            RegWrite w = new RegWrite();
            w.Hive = hive; w.Path = path; w.Name = name;
            w.Delete = true;
            return w;
        }

        public bool MatchesCurrent()
        {
            if (Delete)
            {
                return RegHelper.GetValue(Hive, Path, Name) == null;
            }
            object cur = RegHelper.GetValue(Hive, Path, Name);
            if (cur == null) return false;
            try
            {
                if (Value is int)
                {
                    int a = Convert.ToInt32(cur, CultureInfo.InvariantCulture);
                    return a == (int)Value;
                }
                return string.Equals(cur.ToString(), Value == null ? "" : Value.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void Write(string backupId)
        {
            if (Delete)
            {
                RegHelper.DeleteValue(Hive, Path, Name, backupId);
                return;
            }

            object value = Value;
            RegistryValueKind kind = Kind;
            if (kind == RegistryValueKind.DWord && !(value is int))
            {
                int iv;
                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out iv)) value = iv;
            }
            RegHelper.SetValue(Hive, Path, Name, value, kind, backupId);
        }
    }

    /// <summary>
    /// 声明式优化项：Enable 为一组目标写入，Revert 为可选的手工还原动作
    /// （不指定时直接使用备份还原）。
    /// </summary>
    public sealed class RegTweak : ITweak
    {
        public string IdValue = "";
        public string GroupValue = "";
        public string NameValue = "";
        public string DescriptionValue = "";
        public bool AdminOnlyValue;
        public bool RiskyValue;
        public bool RecommendedValue;

        /// <summary>
        /// 同键多档项共享的备份 ID（如 Win32PrioritySeparation 三个档位）：
        /// 首次应用记录系统原值，各档还原都回到最初默认，切档互不污染。
        /// 为空时使用 IdValue。
        /// </summary>
        public string BackupIdValue = "";

        public List<RegWrite> Enable = new List<RegWrite>();
        public List<RegWrite> RevertWrites = new List<RegWrite>();

        /// <summary>还原时是否强制使用备份（默认 true）。</summary>
        public bool UseBackupOnRevert = true;

        /// <summary>
        /// 适用性检测（可选）：本机不满足生效条件时返回 false。
        /// 未达标的项「已启用」状态恒为否，应用会直接拒绝，避免写无效键造成"开了却没生效"。
        /// </summary>
        public Func<bool> ApplicableValue;

        private string BackupId
        {
            get { return string.IsNullOrEmpty(BackupIdValue) ? IdValue : BackupIdValue; }
        }

        public string Id { get { return IdValue; } }
        public string Group { get { return GroupValue; } }
        public string Name { get { return NameValue; } }
        public string Description { get { return DescriptionValue; } }
        public bool AdminOnly { get { return AdminOnlyValue; } }
        public bool Risky { get { return RiskyValue; } }
        public bool Recommended { get { return RecommendedValue; } }

        private bool Applicable()
        {
            try { return ApplicableValue == null || ApplicableValue(); }
            catch { return true; }
        }

        public bool IsApplied()
        {
            if (Enable.Count == 0) return false;
            if (!Applicable()) return false;
            // 全部写入项都与目标值匹配才算"已启用"（部分写入失败时如实显示未启用）
            for (int i = 0; i < Enable.Count; i++)
            {
                if (!Enable[i].MatchesCurrent()) return false;
            }
            return true;
        }

        public bool Apply()
        {
            if (!Applicable()) return false; // 本机不满足生效条件，不写无效键
            RegHelper.BeginBackup(BackupId);
            bool ok = true;
            for (int i = 0; i < Enable.Count; i++)
            {
                try { Enable[i].Write(BackupId); }
                catch { ok = false; }
            }
            return ok;
        }

        public bool Revert()
        {
            if (RevertWrites.Count > 0)
            {
                bool ok = true;
                for (int i = 0; i < RevertWrites.Count; i++)
                {
                    try { RevertWrites[i].Write(null); }
                    catch { ok = false; }
                }
                return ok;
            }

            if (UseBackupOnRevert)
            {
                return RegHelper.Restore(BackupId);
            }
            return false;
        }
    }

    /// <summary>
    /// 写入显卡设备类 {4d36e968} 全部实例（0000、0001…）的键值，
    /// 用于 dunu 系显卡类调优（固件调度/全速渲染等），全实例覆盖避免依赖具体索引。
    /// </summary>
    /// <summary>
    /// CPU 厂商检测：按处理器名称串判断（Intel / AMD）。
    /// 用于让 CPU 厂商专属优化项（如 Intel TSX/调度键）在无关机器上自动判定「不适用」。
    /// </summary>
    public static class CpuVendor
    {
        private static string _name;

        private static string Name()
        {
            if (_name != null) return _name;
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", false))
                {
                    object n = k == null ? null : k.GetValue("ProcessorNameString");
                    _name = n == null ? "" : n.ToString();
                }
            }
            catch
            {
                _name = "";
            }
            return _name;
        }

        public static bool IsIntel
        {
            get { return Name().IndexOf("intel", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        public static bool IsAmd
        {
            get { return Name().IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       Name().IndexOf("ryzen", StringComparison.OrdinalIgnoreCase) >= 0; }
        }
    }
}
