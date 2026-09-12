using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public sealed class ContextEntry
    {
        public string Name = "";
        public string Location = "";
        public string FullPath = "";
        public bool Enabled;
    }

    public static class ContextMenu
    {
        private sealed class Loc
        {
            public string Root;        // HKLM / HKCU
            public string Path;
            public string Label;
            public RegistryHive Hive;
            public bool User;
        }

        private static readonly Loc[] Locations = new Loc[]
        {
            new Loc { Root = "HKLM", Hive = RegistryHive.LocalMachine, User = false,
                Path = @"Software\Classes\*\shell", Label = "文件(*)" },
            new Loc { Root = "HKLM", Hive = RegistryHive.LocalMachine, User = false,
                Path = @"Software\Classes\Directory\shell", Label = "文件夹" },
            new Loc { Root = "HKLM", Hive = RegistryHive.LocalMachine, User = false,
                Path = @"Software\Classes\Directory\Background\shell", Label = "文件夹背景" },
            new Loc { Root = "HKLM", Hive = RegistryHive.LocalMachine, User = false,
                Path = @"Software\Classes\Folder\shell", Label = "Folder" },
            new Loc { Root = "HKLM", Hive = RegistryHive.LocalMachine, User = false,
                Path = @"Software\Classes\Drive\shell", Label = "磁盘" },
            new Loc { Root = "HKCU", Hive = RegistryHive.CurrentUser, User = true,
                Path = @"Software\Classes\*\shell", Label = "文件(*)-用户" },
            new Loc { Root = "HKCU", Hive = RegistryHive.CurrentUser, User = true,
                Path = @"Software\Classes\Directory\Background\shell", Label = "文件夹背景-用户" },
        };

        private const string DisabledSuffix = "_disabled";

        public static List<ContextEntry> List()
        {
            List<ContextEntry> list = new List<ContextEntry>();
            foreach (Loc loc in Locations)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(loc.Hive, RegistryView.Default))
                    using (RegistryKey key = baseKey.OpenSubKey(loc.Path, false))
                    {
                        if (key == null) continue;
                        foreach (string sub in key.GetSubKeyNames())
                        {
                            ContextEntry e = new ContextEntry();
                            e.Location = loc.Label;
                            e.FullPath = loc.Root + @"\" + loc.Path + @"\" + sub;
                            if (sub.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
                            {
                                e.Name = sub.Substring(0, sub.Length - DisabledSuffix.Length);
                                e.Enabled = false;
                            }
                            else
                            {
                                e.Name = sub;
                                e.Enabled = true;
                            }
                            list.Add(e);
                        }
                    }
                }
                catch
                {
                }
            }

            list.Sort(delegate (ContextEntry a, ContextEntry b)
            {
                int c = string.Compare(a.Location, b.Location, StringComparison.CurrentCultureIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        public static bool Set(ContextEntry entry, bool enable, out string error)
        {
            error = "";
            try
            {
                string src = enable
                    ? entry.FullPath + DisabledSuffix
                    : entry.FullPath;
                string dst = enable
                    ? entry.FullPath
                    : entry.FullPath + DisabledSuffix;

                Shell.Result copy = Shell.Run("reg.exe",
                    "copy \"" + src + "\" \"" + dst + "\" /s /f", 20000);
                if (!copy.Ok)
                {
                    error = "复制注册表项失败：" + copy.All.Trim();
                    return false;
                }
                Shell.Result del = Shell.Run("reg.exe", "delete \"" + src + "\" /f", 20000);
                if (!del.Ok)
                {
                    error = "删除原注册表项失败：" + del.All.Trim();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
