﻿/* ============================================================
 * 文件说明：资源管理器右键菜单项枚举与启停（注册表键标记）
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

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

        /// <summary>
        /// 枚举右键菜单扩展项。
        /// 某些位置（HKLM）在非管理员下会读取失败：失败原因累计写入 error，
        /// 页面据此提示"部分位置未读到（需要管理员权限）"，而不是假装"没有条目"。
        /// </summary>
        public static List<ContextEntry> List(out string error)
        {
            error = "";
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
                catch (Exception ex)
                {
                    // 不再静默：只记第一条失败原因即可（通常都是"非管理员读不了 HKLM"）
                    if (error.Length == 0) error = loc.Label + "：" + ex.Message;
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
            if (entry == null || string.IsNullOrEmpty(entry.FullPath))
            {
                error = "菜单项信息无效（注册表路径为空）。";
                return false;
            }
            try
            {
                // List() 里的 FullPath 是磁盘上的真实键名，被禁用项本身就以 _disabled 结尾；
                // 必须先剥回不含后缀的基名，否则「启用」会去复制 xxx_disabled_disabled（源不存在）而必然失败。
                string basePath = entry.FullPath;
                if (basePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    basePath = basePath.Substring(0, basePath.Length - DisabledSuffix.Length);
                }

                string src = enable ? basePath + DisabledSuffix : basePath;
                string dst = enable ? basePath : basePath + DisabledSuffix;

                Shell.Result copy = Shell.Run("reg.exe",
                    "copy \"" + src + "\" \"" + dst + "\" /s /f", 20000, isChange: true);
                if (!copy.Ok)
                {
                    error = "复制注册表项失败：" + copy.All.Trim();
                    return false;
                }
                Shell.Result del = Shell.Run("reg.exe", "delete \"" + src + "\" /f", 20000, isChange: true);
                if (del.Ok) return true;

                // 复制成功但删除失败：回滚刚复制出的目标键，避免 xxx 与 xxx_disabled 两份同时存在
                //（正常状态下 dst 是本次新建的；若系统本已存在同名脏键，回滚会一并清掉，需人工重试）
                Shell.Result rollback = Shell.Run("reg.exe", "delete \"" + dst + "\" /f", 20000, isChange: true);
                error = "删除原注册表项失败：" + del.All.Trim()
                    + (rollback.Ok
                        ? "（已回滚本次改动，未留下副本）"
                        : "（回滚同样失败，可能残留一份副本，请在注册表中手动处理：" + rollback.All.Trim() + "）");
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
