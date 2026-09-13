/* ============================================================
 * 文件说明：优化项库「Appearance」组的全部优化项声明。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Appearance()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak ext = new RegTweak();
            ext.IdValue = "show_file_ext";
            ext.GroupValue = GAppearance;
            ext.NameValue = "显示文件扩展名";
            ext.DescriptionValue = "在资源管理器中显示 .exe、.txt 等扩展名，便于识别文件真实类型。";
            ext.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "HideFileExt", 0));
            list.Add(ext);

            RegTweak hidden = new RegTweak();
            hidden.IdValue = "show_hidden_files";
            hidden.GroupValue = GAppearance;
            hidden.NameValue = "显示隐藏文件";
            hidden.DescriptionValue = "在资源管理器中显示隐藏文件和文件夹。";
            hidden.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Hidden", 1));
            list.Add(hidden);

            RegTweak shake = new RegTweak();
            shake.IdValue = "disable_shake";
            shake.GroupValue = GAppearance;
            shake.NameValue = "禁用 Aero Shake 窗口最小化";
            shake.DescriptionValue = "防止误拖动鼠标导致其它窗口全部最小化。";
            shake.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "DisallowShaking", 1));
            list.Add(shake);

            RegTweak recent = new RegTweak();
            recent.IdValue = "no_recent_docs";
            recent.GroupValue = GAppearance;
            recent.NameValue = "关闭'最近使用的文件'记录";
            recent.DescriptionValue = "不再记录最近打开的文件与常用文件夹，同时保护隐私。";
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackDocs", 0));
            recent.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "Start_TrackProgs", 0));
            list.Add(recent);

            RegTweak classicMenu = new RegTweak();
            classicMenu.IdValue = "win11_classic_menu";
            classicMenu.GroupValue = GAppearance;
            classicMenu.NameValue = "恢复 Windows 10 经典右键菜单 (Win11)";
            classicMenu.DescriptionValue = "在 Windows 11 上跳过'显示更多选项'，直接展开完整右键菜单。仅对 Win11 有效。";
            classicMenu.RiskyValue = true;
            classicMenu.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", ""));
            list.Add(classicMenu);

            RegTweak taskbarAl = new RegTweak();
            taskbarAl.IdValue = "taskbar_left";
            taskbarAl.GroupValue = GAppearance;
            taskbarAl.NameValue = "开始菜单与任务栏左对齐";
            taskbarAl.DescriptionValue = "把 Win11 默认居中的任务栏图标与开始菜单恢复为 Windows 10 风格的左对齐，纯外观偏好。";
            taskbarAl.AdminOnlyValue = false;
            taskbarAl.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1));
            list.Add(taskbarAl);

            return list;
        }

        // ---------------- 隐私 ----------------

    }
}
