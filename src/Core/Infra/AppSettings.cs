using System;
using System.Drawing;
using Microsoft.Win32;

namespace SysToolbox.Core
{
    /// <summary>
    /// 软件设置（HKCU\Software\SysToolbox\Settings）：
    /// 主题色、动画开关、启动检查更新。读写均即时生效并落盘。
    /// </summary>
    public static class AppSettings
    {
        private const string KeyPath = @"Software\SysToolbox\Settings";

        private static int _accentIndex = 0;
        private static bool _animations = true;
        private static bool _autoUpdateCheck = true;
        private static bool _loaded;

        /// <summary>主题色索引 0-5（对应 Theme.Palette）。</summary>
        public static int AccentIndex
        {
            get { return _accentIndex; }
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
            get { return _animations; }
            set
            {
                _animations = value;
                Save("Animations", value ? 1 : 0);
            }
        }

        /// <summary>启动时自动检查更新。</summary>
        public static bool AutoUpdateCheck
        {
            get { return _autoUpdateCheck; }
            set
            {
                _autoUpdateCheck = value;
                Save("AutoUpdateCheck", value ? 1 : 0);
            }
        }

        // ---------------- 窗口尺寸 / 位置持久化 ----------------

        private static bool _winLoaded;
        private static bool _winHas;
        private static int _winX, _winY, _winW, _winH;
        private static bool _winMax;

        /// <summary>上次关闭时的窗口边界（无记录时 HasWindow=false）。</summary>
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
