/* ============================================================
 * 文件说明：优化项库「补充组」：资源管理器与任务栏的常用体验项。
 *           全部为 HKCU（当前用户）下的设置——无需管理员权限、不改安全策略、
 *           每项都可读取当前状态并可还原到系统原始值。
 *
 *           新增前已逐一核验：下列注册表值名（LaunchTo / NavPaneExpandToCurrentFolder /
 *           AutoCheckSelect / SeparateProcess / TaskbarGlomLevel / SearchboxTaskbarMode /
 *           EnableAutoTray / ScoobeSystemSettingEnabled）均不与既有 158 个优化项冲突，
 *           避免同一键值被两项反复覆盖导致还原值错乱。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        /// <summary>
        /// 体验增强补充项：只做「让系统更好用」的界面与交互设置，
        /// 不涉及 Defender / UAC / 更新 / 驱动等高风险面。
        /// </summary>
        private static IEnumerable<ITweak> Extras()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------- 资源管理器 ----------------

            RegTweak launchTo = new RegTweak();
            launchTo.IdValue = "explorer_launch_this_pc";
            launchTo.GroupValue = GAppearance;
            launchTo.NameValue = "资源管理器打开「此电脑」";
            launchTo.DescriptionValue = "默认进入「此电脑」而不是「快速访问」，打开新窗口直接看到磁盘列表，找文件少一步。仅影响资源管理器初始视图，不改变任何文件。";
            launchTo.RecommendedValue = true;
            launchTo.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "LaunchTo", 1));
            list.Add(launchTo);

            RegTweak navPane = new RegTweak();
            navPane.IdValue = "explorer_navpane_expand";
            navPane.GroupValue = GAppearance;
            navPane.NameValue = "导航窗格展开到当前文件夹";
            navPane.DescriptionValue = "左侧目录树自动展开并定位到当前所在文件夹，跳转层级一目了然，不用手动一路点开。";
            navPane.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "NavPaneExpandToCurrentFolder", 1));
            list.Add(navPane);

            RegTweak checkSelect = new RegTweak();
            checkSelect.IdValue = "explorer_checkbox_select";
            checkSelect.GroupValue = GAppearance;
            checkSelect.NameValue = "文件显示复选框（方便多选）";
            checkSelect.DescriptionValue = "鼠标移到文件上即显示复选框，批量选取文件不必按住 Ctrl。习惯框选/快捷键的人可不开。";
            checkSelect.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "AutoCheckSelect", 1));
            list.Add(checkSelect);

            RegTweak separateProcess = new RegTweak();
            separateProcess.IdValue = "explorer_separate_process";
            separateProcess.GroupValue = GAppearance;
            separateProcess.NameValue = "资源管理器独立进程";
            separateProcess.DescriptionValue = "每个资源管理器窗口运行在独立进程中：某个窗口卡死或崩溃时，不会再带走桌面任务栏和其他窗口。资源占用略增。";
            separateProcess.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "SeparateProcess", 1));
            list.Add(separateProcess);

            // ---------------- 任务栏与托盘 ----------------

            RegTweak glom = new RegTweak();
            glom.IdValue = "taskbar_combine_never";
            glom.GroupValue = GAppearance;
            glom.NameValue = "任务栏按钮从不合并";
            glom.DescriptionValue = "同类窗口在任务栏上各自独立显示（并显示窗口标题），不再挤成一个图标。开很多同类型窗口时更好辨认，任务栏会变长。";
            glom.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "TaskbarGlomLevel", 2));
            list.Add(glom);

            RegTweak searchBox = new RegTweak();
            searchBox.IdValue = "taskbar_search_hide";
            searchBox.GroupValue = GAppearance;
            searchBox.NameValue = "隐藏任务栏搜索框";
            searchBox.DescriptionValue = "去掉任务栏的搜索框/搜索图标，任务栏更清爽。仍可用 Win+S 或开始菜单直接输入搜索，功能不受影响。";
            searchBox.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "SearchboxTaskbarMode", 0));
            list.Add(searchBox);

            RegTweak trayIcons = new RegTweak();
            trayIcons.IdValue = "tray_show_all_icons";
            trayIcons.GroupValue = GAppearance;
            trayIcons.NameValue = "托盘区始终显示所有图标";
            trayIcons.DescriptionValue = "关闭系统对不常用托盘图标的自动折叠，所有后台程序图标常驻可见，不用再点展开箭头。托盘图标多时会占较多任务栏空间。";
            trayIcons.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerMain, "EnableAutoTray", 0));
            list.Add(trayIcons);

            // ---------------- 系统提示 ----------------

            RegTweak scoobe = new RegTweak();
            scoobe.IdValue = "scoobe_off";
            scoobe.GroupValue = GAppearance;
            scoobe.NameValue = "关闭「欢迎体验」提示";
            scoobe.DescriptionValue = "关闭登录后弹出的「欢迎体验 / 让我们完成设置」类引导卡片，不再被反复提醒。只影响提示本身，不影响任何功能。";
            scoobe.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0));
            list.Add(scoobe);

            return list;
        }
    }
}
