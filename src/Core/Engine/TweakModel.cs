/* Core/Engine/TweakModel.cs — 优化项数据模型：写入描述(RegWrite)、声明式优化项(RegTweak)、CPU 厂商检测。 */

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>一条可开关的优化项：启用/停用/状态检测三件套，全部改动经 Engine 备份链可还原。</summary>
    public interface ITweak
    {
        string Id { get; }
        string Group { get; }
        string Name { get; }
        string Description { get; }
        bool AdminOnly { get; }
        bool Risky { get; }
        bool Recommended { get; }
        bool IsApplied();
        bool Apply();
        bool Revert();
    }

    /// <summary>单条注册表写入描述。DeviceOnly 意为仅写已存在设备实例键（不凭空造键）。</summary>
    public sealed class RegWrite
    {
        public RegistryHive Hive = RegistryHive.CurrentUser;
        public string Path = "";
        public string Name = "";
        public RegistryValueKind Kind = RegistryValueKind.DWord;
        public object Value;

        /// <summary>为 true 时表示删除该值（回到系统默认）。</summary>
        public bool Delete;

        /// <summary>设备实例键专用：只写已存在的键，避免 CreateSubKey 凭空制造幽灵键。</summary>
        public bool OnlyIfExists;

        public static RegWrite Dword(RegistryHive hive, string path, string name, int value)
        {
            return new RegWrite { Hive = hive, Path = path, Name = name, Kind = RegistryValueKind.DWord, Value = value };
        }

        public static RegWrite Qword(RegistryHive hive, string path, string name, long value)
        {
            return new RegWrite { Hive = hive, Path = path, Name = name, Kind = RegistryValueKind.QWord, Value = value };
        }

        public static RegWrite Str(RegistryHive hive, string path, string name, string value)
        {
            return new RegWrite { Hive = hive, Path = path, Name = name, Kind = RegistryValueKind.String, Value = value };
        }

        public static RegWrite Binary(RegistryHive hive, string path, string name, byte[] value)
        {
            return new RegWrite { Hive = hive, Path = path, Name = name, Kind = RegistryValueKind.Binary, Value = value };
        }

        public static RegWrite Remove(RegistryHive hive, string path, string name)
        {
            return new RegWrite { Hive = hive, Path = path, Name = name, Delete = true };
        }

        public bool MatchesCurrent()
        {
            if (Delete) return RegHelper.GetValue(Hive, Path, Name) == null;

            object cur = RegHelper.GetValue(Hive, Path, Name);
            if (cur == null) return false;
            try
            {
                if (Value is int)
                {
                    return Convert.ToInt32(cur, CultureInfo.InvariantCulture) == (int)Value;
                }
                if (Value is long)
                {
                    return Convert.ToInt64(cur, CultureInfo.InvariantCulture) == (long)Value;
                }
                byte[] want = Value as byte[];
                if (want != null)
                {
                    // REG_BINARY 按字节序列比较：ToString() 恒为 "System.Byte[]"，字符串比较会让
                    // "值存在即相等"，导致全新系统上 BINARY 项初始状态误报"已启用"
                    byte[] have = cur as byte[];
                    if (have == null || have.Length != want.Length) return false;
                    for (int i = 0; i < have.Length; i++)
                    {
                        if (have[i] != want[i]) return false;
                    }
                    return true;
                }
                return string.Equals(cur.ToString(), Value == null ? "" : Value.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>执行写入。返回是否真正成功（权限/ACL 拦截时为 false，杜绝"假成功"）。</summary>
        public bool Write(string backupId)
        {
            if (Delete) return RegHelper.DeleteValue(Hive, Path, Name, backupId);

            object value = Value;
            RegistryValueKind kind = Kind;
            if (kind == RegistryValueKind.DWord && !(value is int))
            {
                int iv;
                if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out iv)) value = iv;
            }
            if (value is long && kind == RegistryValueKind.DWord)
            {
                long lv = (long)value;
                if (lv >= int.MinValue && lv <= int.MaxValue) value = (int)lv;
            }
            if (OnlyIfExists) return RegHelper.SetValueOnExisting(Hive, Path, Name, value, kind, backupId);
            return RegHelper.SetValue(Hive, Path, Name, value, kind, backupId);
        }
    }

    /// <summary>
    /// 声明式优化项：Enable 为一组目标写入；RevertWrites 为可选手工还原动作
    /// （不指定时直接使用备份还原）。适用性检测 ApplicableValue 未达标的项恒为"未启用"。
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
        public string BackupIdValue = ""; // 同键多档项共享的备份组（如 Win32PrioritySeparation 各档）
        public List<RegWrite> Enable = new List<RegWrite>();
        public List<RegWrite> RevertWrites = new List<RegWrite>();
        public bool UseBackupOnRevert = true;
        public Func<bool> ApplicableValue;
        private ICondition _condition;
        /// <summary>门控条件（若有），供 UI 提示"不适用"原因与自动隐藏判定。</summary>
        internal ICondition Condition { get { return _condition; } }
        /// <summary>本机是否适用：无门控恒为 true；有门控按 ApplicableValue 判定。</summary>
        internal bool IsApplicable { get { return Applicable(); } }

        public static RegTweak Create(string id, string group, string name, string description,
            bool adminOnly = false, bool risky = false, bool recommended = false,
            string backupId = null, bool useBackupOnRevert = true,
            Func<bool> applicable = null, params RegWrite[] enable)
        {
            RegTweak t = new RegTweak
            {
                IdValue = id,
                GroupValue = group,
                NameValue = name,
                DescriptionValue = description,
                AdminOnlyValue = adminOnly,
                RiskyValue = risky,
                RecommendedValue = recommended,
                BackupIdValue = backupId == null ? "" : backupId,
                UseBackupOnRevert = useBackupOnRevert,
                ApplicableValue = applicable,
            };
            if (enable != null) t.Enable.AddRange(enable);
            return t;
        }

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

        /// <summary>按条件门控：满足时才可应用/显示（不满足默认隐藏）。可链式调用。</summary>
        public RegTweak ApplicableWhen(ICondition c)
        {
            _condition = c;
            ApplicableValue = delegate { return c.Satisfied(); };
            return this;
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
                try { if (!Enable[i].Write(BackupId)) ok = false; }
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
                    try { if (!RevertWrites[i].Write(null)) ok = false; }
                    catch { ok = false; }
                }
                return ok;
            }
            if (UseBackupOnRevert) return RegHelper.Restore(BackupId);
            return false;
        }
    }

    /// <summary>CPU 厂商检测：按处理器名称串判断（Intel / AMD），供厂商专属项适用性护栏使用。</summary>
    public static class CpuVendor
    {
        private static string _name;

        private static string Name()
        {
            if (_name != null) return _name;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
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
            get
            {
                return Name().IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       Name().IndexOf("ryzen", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}