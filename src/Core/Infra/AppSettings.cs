/* ============================================================
 * 文件说明：用户设置（注册表持久化）：主题色、动画、更新检查、窗口尺寸、提示条关闭记忆等。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Drawing;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 软件设置（HKCU\Software\GuyueBox\Settings）：
    /// 主题色、动画开关、启动检查更新。读写均即时生效并落盘。
    /// </summary>
    public static class AppSettings
    {
        private const string KeyPath = @"Software\GuyueBox\Settings";

        private static int _accentIndex = 0;
        private static bool _animations = true;
        private static bool _autoUpdateCheck = true;
        private static bool _lightTheme;
        private static bool _restoreLastPage;
        private static string _lastPage;
        private static bool _loaded;

        // 注意：读取器必须先 EnsureLoaded()，否则持久化的设置永远不会被读回
        // （曾因只在写侧 Save、读侧不 Load，导致主题色/动画等偏好每次重启都丢失）。

        /// <summary>主题色索引 0-5（对应 Theme.Palette）。</summary>
        public static int AccentIndex
        {
            get { EnsureLoaded(); return _accentIndex; }
            set
            {
                if (value < 0 || value > 5) value = 0;
                _accentIndex = value;
                Save("AccentIndex", _accentIndex);
            }
        }

        /// <summary>界面动画开关。</summary>
        public static bool Animations
        {
            get { EnsureLoaded(); return _animations; }
            set
            {
                _animations = value;
                Save("Animations", value ? 1 : 0);
            }
        }

        /// <summary>启动时自动检查更新。</summary>
        public static bool AutoUpdateCheck
        {
            get { EnsureLoaded(); return _autoUpdateCheck; }
            set
            {
                _autoUpdateCheck = value;
                Save("AutoUpdateCheck", value ? 1 : 0);
            }
        }

        /// <summary>浅色配色方案（false = 深色，默认）。</summary>
        public static bool LightTheme
        {
            get { EnsureLoaded(); return _lightTheme; }
            set
            {
                _lightTheme = value;
                Save("LightTheme", value ? 1 : 0);
            }
        }

        /// <summary>
        /// 启动时回到上次所在页面。默认 false——保持"每次从首页开始"的既有行为，
        /// 避免升级后启动位置突变。
        /// </summary>
        public static bool RestoreLastPage
        {
            get { EnsureLoaded(); return _restoreLastPage; }
            set
            {
                _restoreLastPage = value;
                Save("RestoreLastPage", value ? 1 : 0);
            }
        }

        /// <summary>上次离开时所在页面的 Key（正由 MainForm 在每次导航后写入）。</summary>
        public static string LastPage
        {
            get { EnsureLoaded(); return _lastPage == null ? "" : _lastPage; }
            set
            {
                _lastPage = value == null ? "" : value;
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                    {
                        if (k != null) k.SetValue("LastPage", _lastPage, RegistryValueKind.String);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 全部界面偏好恢复默认值（主题色 / 配色方案 / 动画 / 自动检查更新 / 提示条记忆 / 启动页）。
        /// 刻意不动窗口位置与「首次运行」标记——那两项属于使用状态，不属于偏好。
        /// </summary>
        public static void ResetAll()
        {
            AccentIndex = 0;
            Animations = true;
            AutoUpdateCheck = true;
            LightTheme = false;
            RestoreLastPage = false;
            LastPage = "";
            ClearNotices();
        }

        // ---------------- 提示条关闭记忆 ----------------

        private static string _hiddenNotices;

        /// <summary>读取某页提示条是否被用户关闭过（key = 页面类名）。关闭过的提示不再出现，避免反复挡视野。</summary>
        public static bool IsNoticeDismissed(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return false;
            EnsureNoticesLoaded();
            return (_hiddenNotices + ";").IndexOf(";" + pageId + ";", StringComparison.Ordinal) >= 0;
        }

        /// <summary>记录用户关闭了某页的提示条（持久化，永久生效）。</summary>
        public static void MarkNoticeDismissed(string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || IsNoticeDismissed(pageId)) return;
            _hiddenNotices = string.IsNullOrEmpty(_hiddenNotices) ? pageId : _hiddenNotices + ";" + pageId;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k != null) k.SetValue("HiddenNotices", _hiddenNotices, RegistryValueKind.String);
                }
            }
            catch { }
        }

        /// <summary>已关闭的页面提示条数量（设置页展示用）。</summary>
        public static int DismissedNoticeCount()
        {
            EnsureNoticesLoaded();
            if (string.IsNullOrEmpty(_hiddenNotices)) return 0;
            string[] parts = _hiddenNotices.Split(';');
            int n = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0) n++;
            }
            return n;
        }

        /// <summary>清除「已关闭的提示条」记忆，让所有页面的提示重新出现。</summary>
        public static void ClearNotices()
        {
            _hiddenNotices = ""; // 非 null 即视为已加载，后续不再回读注册表
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k != null) k.DeleteValue("HiddenNotices", false);
                }
            }
            catch { }
        }

        private static void EnsureNoticesLoaded()
        {
            if (_hiddenNotices != null) return;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    _hiddenNotices = (k == null ? null : (k.GetValue("HiddenNotices") as string)) ?? "";
                }
            }
            catch { _hiddenNotices = ""; }
        }

        // ---------------- 窗口尺寸 / 位置持久化 ----------------

        private static bool _winLoaded;
        private static bool _winHas;
        private static int _winX, _winY, _winW, _winH;
        private static bool _winMax;

        /// <summary>上次关闭时的窗口边界（无记录时 HasWindow=false）。</summary>
        /// <summary>是否首次运行（读取后即置为 false；用于首页新手指引的一次性展示）。</summary>
        public static bool FirstRun
        {
            get
            {
                if (_firstRunRead) return _firstRun;
                _firstRunRead = true;
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath, false))
                    {
                        object v = k == null ? null : k.GetValue("FirstRunDone");
                        _firstRun = v == null || Convert.ToInt32(v) == 0;
                    }
                }
                catch { _firstRun = true; }
                return _firstRun;
            }
        }

        /// <summary>标记首次运行引导已完成。</summary>
        public static void MarkFirstRunDone()
        {
            Save("FirstRunDone", 1);
            _firstRun = false;
        }

        private static bool _firstRun = true;
        private static bool _firstRunRead;

        public static bool HasWindowBounds
        {
            get { EnsureWindow(); return _winHas; }
        }

        public static Rectangle WindowBounds
        {
            get { EnsureWindow(); return new Rectangle(_winX, _winY, _winW, _winH); }
        }

        /// <summary>上次关闭时是否处于最大化。</summary>
        public static bool WindowMaximized
        {
            get { EnsureWindow(); return _winMax; }
        }

        /// <summary>保存窗口状态（退出时调用；最大化只记标志，位置记还原前尺寸）。</summary>
        public static void SaveWindow(Rectangle bounds, bool maximized)
        {
            EnsureWindow();
            _winHas = true;
            _winX = bounds.X; _winY = bounds.Y;
            _winW = bounds.Width; _winH = bounds.Height;
            _winMax = maximized;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath + @"\Window", true))
                {
                    k.SetValue("X", _winX, RegistryValueKind.DWord);
                    k.SetValue("Y", _winY, RegistryValueKind.DWord);
                    k.SetValue("W", _winW, RegistryValueKind.DWord);
                    k.SetValue("H", _winH, RegistryValueKind.DWord);
                    k.SetValue("Max", _winMax ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }

        private static void EnsureWindow()
        {
            if (_winLoaded) return;
            _winLoaded = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath + @"\Window"))
                {
                    if (k == null) return;
                    object x = k.GetValue("X"), y = k.GetValue("Y"),
                           w = k.GetValue("W"), h = k.GetValue("H"), m = k.GetValue("Max");
                    if (x == null || y == null || w == null || h == null) return;
                    _winHas = true;
                    _winX = (int)x; _winY = (int)y;
                    _winW = (int)w; _winH = (int)h;
                    _winMax = m != null && (int)m == 1;
                }
            }
            catch
            {
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return;
                    _accentIndex = Clamp(ReadInt(k, "AccentIndex", 0), 0, 5);
                    _animations = ReadInt(k, "Animations", 1) != 0;
                    _autoUpdateCheck = ReadInt(k, "AutoUpdateCheck", 1) != 0;
                    _lightTheme = ReadInt(k, "LightTheme", 0) != 0;
                    _restoreLastPage = ReadInt(k, "RestoreLastPage", 0) != 0;
                    _lastPage = (k.GetValue("LastPage") as string) ?? "";
                }
            }
            catch
            {
            }
        }

        private static int ReadInt(RegistryKey k, string name, int def)
        {
            object v = k.GetValue(name);
            try { return v == null ? def : Convert.ToInt32(v); }
            catch { return def; }
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static void Save(string name, int value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k != null) k.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch
            {
            }
        }
    }
}
