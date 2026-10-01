using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 显卡伪装：把 Windows 在「设备管理器 / 系统信息 / 多数检测软件」里显示的显卡名称，
    /// 改成用户指定的型号。常用于让只识别特定厂商或型号的游戏 / 软件放行，或隐藏真实型号。
    /// 注意：只改显示名，不改变实际驱动能力；需重启或重新扫描设备后生效；可一键还原。
    /// </summary>
    public sealed class GpuSpoof
    {
        private const string GpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        private const string BackupRoot = @"SOFTWARE\GuyueBox\GpuSpoof";

        public sealed class GpuEntry
        {
            public string SubKey;
            public string DriverDesc;
            public string AdapterString;
            public string DeviceString;
            public string CurrentName
            {
                get { return string.IsNullOrEmpty(AdapterString) ? DriverDesc : AdapterString; }
            }
        }

        public static List<GpuEntry> Detect()
        {
            List<GpuEntry> list = new List<GpuEntry>();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass))
                {
                    if (cls == null) return list;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (!IsInstance(sub)) continue;
                        string path = GpuClass + "\\" + sub;
                        GpuEntry e = new GpuEntry();
                        e.SubKey = sub;
                        e.DriverDesc = ToStr(RegHelper.GetValue(RegistryHive.LocalMachine, path, "DriverDesc"));
                        e.AdapterString = ToStr(RegHelper.GetValue(RegistryHive.LocalMachine, path, "HardwareInformation.AdapterString"));
                        e.DeviceString = ToStr(RegHelper.GetValue(RegistryHive.LocalMachine, path, "HardwareInformation.DeviceString"));
                        if (string.IsNullOrEmpty(e.DriverDesc) && string.IsNullOrEmpty(e.AdapterString) && string.IsNullOrEmpty(e.DeviceString)) continue;
                        list.Add(e);
                    }
                }
            }
            catch { }
            return list;
        }

        public static bool IsSpoofed()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(BackupRoot))
                    return k != null && k.SubKeyCount > 0;
            }
            catch { return false; }
        }

        public static bool Spoof(string displayName, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(displayName)) { error = "型号名不能为空"; return false; }
            List<GpuEntry> entries = Detect();
            if (entries.Count == 0) { error = "未检测到显示适配器"; return false; }

            RegHelper.BeginBackup("gpu_spoof");
            try
            {
                using (RegistryKey root = Registry.LocalMachine.CreateSubKey(BackupRoot))
                {
                    foreach (GpuEntry e in entries)
                    {
                        string path = GpuClass + "\\" + e.SubKey;
                        using (RegistryKey bk = root.CreateSubKey(e.SubKey))
                        {
                            bk.SetValue("DriverDesc", e.DriverDesc ?? "", RegistryValueKind.String);
                            bk.SetValue("AdapterString", e.AdapterString ?? "", RegistryValueKind.String);
                            bk.SetValue("DeviceString", e.DeviceString ?? "", RegistryValueKind.String);
                        }
                        // 三个值必须都写进去才算伪装成功：此前丢弃返回值，
                        // HKLM 写失败也返回 true，界面弹"已应用"但设备管理器毫无变化。
                        bool a = RegHelper.SetValue(RegistryHive.LocalMachine, path, "HardwareInformation.AdapterString", displayName, RegistryValueKind.String, "gpu_spoof");
                        bool b = RegHelper.SetValue(RegistryHive.LocalMachine, path, "HardwareInformation.DeviceString", displayName, RegistryValueKind.String, "gpu_spoof");
                        bool c = RegHelper.SetValue(RegistryHive.LocalMachine, path, "DriverDesc", displayName, RegistryValueKind.String, "gpu_spoof");
                        if (!a || !b || !c)
                        {
                            error = "伪装写入失败（可能缺少管理员权限）："
                                + ((a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0)) + "/3 项成功。"
                                + "可点「还原真实型号」回到原值，或右键以管理员身份运行后重试。";
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Restore(out string error)
        {
            error = "";
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(BackupRoot))
                {
                    if (root == null) { error = "没有可还原的备份"; return false; }
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        using (RegistryKey bk = root.OpenSubKey(sub))
                        {
                            if (bk == null) continue;
                            string path = GpuClass + "\\" + sub;
                            WriteBack(path, "DriverDesc", bk);
                            WriteBack(path, "HardwareInformation.AdapterString", bk);
                            WriteBack(path, "HardwareInformation.DeviceString", bk);
                        }
                    }
                }
                try { Registry.LocalMachine.DeleteSubKeyTree(BackupRoot); }
                catch { }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static void WriteBack(string path, string name, RegistryKey bk)
        {
            object v = bk.GetValue(name);
            if (v == null) return;
            RegHelper.SetValue(RegistryHive.LocalMachine, path, name, v, RegistryValueKind.String, "gpu_spoof");
        }

        private static string ToStr(object o) { return o == null ? "" : Convert.ToString(o); }
        private static bool IsInstance(string name)
        {
            if (name.Length != 4) return false;
            foreach (char c in name) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
