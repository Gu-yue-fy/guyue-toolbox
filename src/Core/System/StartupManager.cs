using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public enum StartupSource
    {
        RegistryRun,
        RegistryRunOnce,
        StartupFolder
    }

    public sealed class StartupItem
    {
        public string Name;
        public string Command;
        public string Publisher;
        public string Location;
        public StartupSource Source;

        /// <summary>是否处于启用状态。</summary>
        public bool Enabled;

        /// <summary>是否可以启用/禁用（部分条目无法安全切换）。</summary>
        public bool CanToggle = true;

        // --- 内部定位信息 ---
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyPath;
        public string ValueName;
        public string FilePath;

        /// <summary>用于 StartupApproved 的名称。</summary>
        public string ApprovedName
        {
            get { return Source == StartupSource.StartupFolder ? Path.GetFileName(FilePath) : ValueName; }
        }

        public string SourceText
        {
            get
            {
                switch (Source)
                {
                    case StartupSource.RegistryRun:
                        return Hive == RegistryHive.CurrentUser ? "注册表 (当前用户)" : "注册表 (本机)";
                    case StartupSource.RegistryRunOnce:
                        return Hive == RegistryHive.CurrentUser ? "注册表 RunOnce (当前用户)" : "注册表 RunOnce (本机)";
                    default:
                        return "启动文件夹";
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (!CanToggle) return "只读";
                return Enabled ? "已启用" : "已禁用";
            }
        }
    }

    /// <summary>
    /// 读取与切换 Windows 启动项。
    /// 启用/禁用使用系统自身的 StartupApproved 机制（与任务管理器"启动"选项卡一致）。
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        private const string Run32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOnce32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
        private const string ApprovedRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ApprovedRun32 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
        private const string ApprovedRunOnce = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\RunOnce";
        private const string ApprovedFolder = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        public static List<StartupItem> Load()
        {
            // 5 个注册表分支 + 2 个启动文件夹互不相关：并行读取。
            // 每个读取器写自己的列表（读取方法签名不变），再按原顺序合并，
            // 结果与串行一致（最终仍按「启用优先 + 名称」统一排序）
            Func<List<StartupItem>>[] readers = new Func<List<StartupItem>>[]
            {
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadRegistryRun(l, RegistryHive.LocalMachine, RegistryView.Registry64, RunKey, StartupSource.RegistryRun, ApprovedRun); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadRegistryRun(l, RegistryHive.LocalMachine, RegistryView.Registry32, Run32Key, StartupSource.RegistryRun, ApprovedRun32); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadRegistryRun(l, RegistryHive.CurrentUser, RegistryView.Registry64, RunKey, StartupSource.RegistryRun, ApprovedRun); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadRegistryRun(l, RegistryHive.CurrentUser, RegistryView.Registry64, RunOnceKey, StartupSource.RegistryRunOnce, ApprovedRunOnce); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadRegistryRun(l, RegistryHive.CurrentUser, RegistryView.Registry32, RunOnce32Key, StartupSource.RegistryRunOnce, ApprovedRunOnce); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadStartupFolder(l, Environment.SpecialFolder.Startup, "用户启动文件夹"); return l; },
                delegate { List<StartupItem> l = new List<StartupItem>(); ReadStartupFolder(l, Environment.SpecialFolder.CommonStartup, "公共启动文件夹"); return l; }
            };

            List<StartupItem>[] parts = new List<StartupItem>[readers.Length];
            Parallel.For(0, readers.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(int i) { parts[i] = readers[i](); });

            List<StartupItem> list = new List<StartupItem>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null) list.AddRange(parts[i]);
            }

            list.Sort(delegate (StartupItem a, StartupItem b)
            {
                if (a.Enabled != b.Enabled) return a.Enabled ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static void ReadRegistryRun(List<StartupItem> list, RegistryHive hive, RegistryView view,
            string keyPath, StartupSource source, string approvedPath)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(keyPath, false))
                {
                    if (key == null) return;

                    string[] names = key.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string name = names[i];
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.StartsWith("OptionalComponents", StringComparison.OrdinalIgnoreCase)) continue;

                        object val = key.GetValue(name);
                        if (val == null) continue;
                        string command = val.ToString();
                        if (string.IsNullOrWhiteSpace(command)) continue;

                        StartupItem item = new StartupItem();
                        item.Name = name;
                        item.Command = command;
                        item.Source = source;
                        item.Hive = hive;
                        item.View = view;
                        item.KeyPath = keyPath;
                        item.ValueName = name;
                        item.Location = (hive == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\") + keyPath;
                        item.Publisher = GuessPublisher(command);
                        item.Enabled = IsApproved(hive, view, approvedPath, name);
                        item.CanToggle = true;
                        list.Add(item);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ReadStartupFolder(List<StartupItem> list, Environment.SpecialFolder folder, string label)
        {
            string dir;
            try { dir = Environment.GetFolderPath(folder); }
            catch { return; }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                string f = files[i];
                string ext = Path.GetExtension(f);
                if (string.Equals(ext, ".ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                StartupItem item = new StartupItem();
                item.Name = Path.GetFileName(f);
                item.Command = f;
                item.Source = StartupSource.StartupFolder;
                item.FilePath = f;
                item.ValueName = Path.GetFileName(f);
                item.Location = label + "：" + dir;
                item.Publisher = Path.GetFileNameWithoutExtension(f);
                item.Enabled = IsApproved(RegistryHive.CurrentUser, RegistryView.Registry64,
                    ApprovedFolder, Path.GetFileName(f));
                item.CanToggle = true;
                list.Add(item);
            }
        }

        // ---------------------------------------------------------------
        // StartupApproved 读写
        // ---------------------------------------------------------------

        private static bool IsApproved(RegistryHive hive, RegistryView view, string approvedPath, string name)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(approvedPath, false))
                {
                    if (key == null) return true; // 没有记录 => 默认启用
                    object val = key.GetValue(name);
                    if (val == null) return true;
                    byte[] data = val as byte[];
                    if (data == null || data.Length == 0) return true;
                    // 0x02 / 0x06 表示启用，其余（0x03 / 0x01 等）表示禁用
                    return data[0] == 0x02 || data[0] == 0x06;
                }
            }
            catch
            {
                return true;
            }
        }

        public static bool SetEnabled(StartupItem item, bool enabled)
        {
            if (item == null || !item.CanToggle) return false;

            string approvedPath = GetApprovedPath(item);
            if (approvedPath == null) return false;

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (RegistryKey key = baseKey.CreateSubKey(approvedPath))
                {
                    if (key == null) return false;

                    byte[] data = new byte[12];
                    if (enabled)
                    {
                        data[0] = 0x02;
                    }
                    else
                    {
                        data[0] = 0x03;
                        long fileTime = DateTime.UtcNow.ToFileTimeUtc();
                        byte[] ft = BitConverter.GetBytes(fileTime);
                        Array.Copy(ft, 0, data, 4, 8);
                    }

                    key.SetValue(item.ApprovedName, data, RegistryValueKind.Binary);
                }
                item.Enabled = enabled;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetApprovedPath(StartupItem item)
        {
            if (item.Source == StartupSource.StartupFolder) return ApprovedFolder;

            bool is32 = (item.View == RegistryView.Registry32);
            if (item.Source == StartupSource.RegistryRunOnce) return ApprovedRunOnce;
            if (is32) return ApprovedRun32;
            return ApprovedRun;
        }

        /// <summary>
        /// 删除启动项。注册表项删除对应值，启动文件夹项删除文件。
        /// </summary>
        public static bool Delete(StartupItem item)
        {
            if (item == null) return false;
            try
            {
                if (item.Source == StartupSource.StartupFolder)
                {
                    if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                }
                else
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(item.Hive, item.View))
                    using (RegistryKey key = baseKey.OpenSubKey(item.KeyPath, true))
                    {
                        if (key == null) return false;
                        key.DeleteValue(item.ValueName, false);
                    }
                }

                // 同时清理 StartupApproved 记录
                string approvedPath = GetApprovedPath(item);
                if (approvedPath != null)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                        using (RegistryKey key = baseKey.OpenSubKey(approvedPath, true))
                        {
                            if (key != null) key.DeleteValue(item.ApprovedName, false);
                        }
                    }
                    catch
                    {
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>从命令行推测发布者 / 程序名。</summary>
        public static string GuessPublisher(string command)
        {
            if (string.IsNullOrEmpty(command)) return "";

            string path = ExtractExecutable(command);
            if (string.IsNullOrEmpty(path)) return "未知";

            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch
            {
            }
            return "未知";
        }

        public static string ExtractExecutable(string command)
        {
            if (string.IsNullOrEmpty(command)) return "";
            string c = command.Trim();

            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                if (end > 1) return c.Substring(1, end - 1);
            }

            int space = c.IndexOf(' ');
            if (space > 0)
            {
                string first = c.Substring(0, space);
                if (first.IndexOf('\\') >= 0 || first.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    return first;
            }

            // 形如 C:\a\b.exe -arg
            int exeIdx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0) return c.Substring(0, exeIdx + 4);

            return space > 0 ? c.Substring(0, space) : c;
        }

        /// <summary>尝试定位启动程序并检测文件是否仍然存在。</summary>
        public static bool CommandExists(string command)
        {
            string path = ExtractExecutable(command);
            if (string.IsNullOrEmpty(path)) return true;
            if (path.IndexOf('\\') < 0 && path.IndexOf('/') < 0) return true;
            string expanded = Environment.ExpandEnvironmentVariables(path);
            try { return File.Exists(expanded); }
            catch { return true; }
        }
    }
}
