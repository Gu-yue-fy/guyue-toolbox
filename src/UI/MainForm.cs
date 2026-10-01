﻿/* ============================================================
 * 文件说明：主窗体：整体框架布局（顶栏/侧栏导航/内容区/状态栏）、页面切换与路由、窗口持久化与全局快捷操作。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{
    /// <summary>
    /// 主窗体：左侧分组导航（大类下可选页签）+ 右侧内容区，顶部命令栏与 Ctrl+K 命令面板可直达任意功能。
    /// 无系统边框与标题栏（含最小化/最大化/关闭全部自绘），随深浅方案切换。
    /// </summary>
    public sealed partial class MainForm : Form
    {
        public const string AppName = "古月工具箱";

        /// <summary>界面显示与更新比较用的版本号。唯一来源是 <see cref="AppInfo.Version"/>（src\AppInfo.cs），
        /// 此处只做转发——改版本请改那一处，别在这里写死。</summary>
        public const string AppVersion = AppInfo.Version;

        // 尺寸统一取自 Theme 布局令牌：改 Theme 一处即可全局生效
        private const int TopBarHeight = Theme.TopBarHeight;
        private const int StatusHeight = Theme.StatusBarHeight;
        private const int SidebarFooterHeight = Theme.SidebarFooterHeight;
        private const int SidebarLogoHeight = 64;

        private sealed class NavEntry
        {
            public string Key;
            public string Text;
            public string Icon;
            public string Group;
            public Func<ViewBase> Factory;
            public ViewBase View;
            public NavSideItem Button;
        }

        private const int SidebarWidth = Theme.SidebarWidth;

        private readonly List<NavEntry> _entries = new List<NavEntry>();
        private readonly SurfacedPanel _topBar = new SurfacedPanel();
        private readonly SurfacedPanel _sidebar = new SurfacedPanel();
        private readonly Panel _content = new Panel();
        private readonly Panel _statusBar = new Panel();

        /// <summary>大功能页签栏（内容区顶部）：列出当前大功能下的小功能。</summary>
        private readonly TabStrip _tabStrip = new TabStrip();
        private readonly ScrollHost _sidebarScroll = new ScrollHost();
    private readonly Panel _sidebarFooter = new Panel();
        private readonly Panel _sidebarLogo = new Panel();
        private readonly FlowLayoutPanel _navFlow = new FlowLayoutPanel();
        private readonly Spinner _spinner = new Spinner();
        private readonly StatusDot _statusDot = new StatusDot();
        private readonly ToastHost _toast = new ToastHost();
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer();
        private readonly WheelRouter _wheelRouter = new WheelRouter();

        private string _currentKey = "";
        private string _statusText = "就绪";
        private string _pageTitle = "";
        private AccentButton _themeButton;
        private readonly Commands.Palette _palette;

        /// <summary>自绘标题栏右侧按钮（按加入顺序靠右排列）。</summary>
        private readonly List<Control> _titleButtons = new List<Control>();
        private CaptionButton _maxButton;

        public static MainForm Current;

        public MainForm()
        {
            Current = this;

            // 必须先套用配色方案再取用任何 Theme 颜色——紧接着的窗体 BackColor 就会读它
            Theme.ApplyScheme(AppSettings.LightTheme ? 1 : 0, AppSettings.AccentIndex);

            Text = AppName;
            // 设计外壳：无系统边框，标题栏与窗框全部自绘（CaptionHeight 40 + 8px 缩放边）
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.ShellBg;
            ClientSize = new Size(1380, 880);   // 设计默认窗口尺寸
            MinimumSize = new Size(1180, 720);
            Padding = new Padding(1);            // 让出 1px 供渐变窗框绘制
            KeyPreview = true;
            Font = Theme.FontBody;
            DoubleBuffered = true;

            // 恢复上次关闭时的窗口状态（边界须与某块屏幕相交才应用，防拔掉显示器后窗口丢失）
            if (AppSettings.HasWindowBounds)
            {
                Rectangle b = AppSettings.WindowBounds;
                bool onScreen = false;
                foreach (Screen s in Screen.AllScreens)
                {
                    if (s.WorkingArea.IntersectsWith(b)) { onScreen = true; break; }
                }
                if (onScreen && b.Width >= MinimumSize.Width && b.Height >= MinimumSize.Height)
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = b;
                }
                if (AppSettings.WindowMaximized) WindowState = FormWindowState.Maximized;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
            try
            {
                // 与 exe 内嵌图标一致（任务栏 / Alt+Tab 显示）
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            Perf.Mark("startup.chrome.begin");
            BuildTopBar();
            BuildSidebar();
            BuildStatusBar();

            _content.BackColor = Theme.WindowBg;

            Controls.Add(_content);
            Controls.Add(_sidebar);
            Controls.Add(_topBar);
            Controls.Add(_statusBar);

            // Toast 浮层：覆盖在内容区底部居中。条目增多会改变自身高度，
            // 故用 Resized 回调重新贴底，避免它长出来后压出可视区。
            _toast.Resized += delegate { LayoutToast(); };
            Controls.Add(_toast);

            // 大功能页签栏（内容区顶部）：由 NavigateTo 按当前分组填充
            _tabStrip.Selected += delegate (string key) { NavigateTo(key); };
            Controls.Add(_tabStrip);

            RegisterViews();
            LayoutChrome();
            Perf.Mark("startup.chrome.done");

            // 命令面板浮层：盖满整个客户区，默认隐藏，Ctrl+K 唤出
            _palette = new Commands.Palette();
            Controls.Add(_palette);

            // 启动页：--page 指定优先（自动化验证），其次开启「回到上次页面」且该页仍存在时跳转，否则回首页
            // （页 Key 可能在版本升级后被移除，必须校验存在性，否则会白屏）
            string startKey = StartPageKey;
            if (string.IsNullOrEmpty(startKey) || FindEntry(startKey) == null)
            {
                startKey = AppSettings.RestoreLastPage ? AppSettings.LastPage : "";
            }
            if (string.IsNullOrEmpty(startKey) || FindEntry(startKey) == null) startKey = "dashboard";
            NavigateTo(startKey);
            Perf.Mark("startup.firstPage");

            Application.AddMessageFilter(_wheelRouter);

            _statusTimer.Interval = 400;
            _statusTimer.Tick += delegate { UpdateBusyIndicator(); };
            _statusTimer.Start();

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5)
                {
                    RefreshCurrent();
                }
                else if (e.Control && e.KeyCode == Keys.K)
                {
                    // 全局命令面板开关：Ctrl+K 打开 / 再次按下关闭
                    TogglePalette();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape && _palette != null && _palette.Visible)
                {
                    _palette.Close();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.Control && e.KeyCode == Keys.Tab)
                {
                    NextPage(e.Shift ? -1 : 1);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        /// <summary>--page 指定的启动页（自动化验证用；为空时按「回到上次页面」设置决定）。</summary>
        public static string StartPageKey;

        /// <summary>
        /// 测试模式（--shots / --page）：使用独立互斥体，且不写回窗口状态与「上次页面」，
        /// 避免自动化跑一遍就改掉用户自己的启动页与窗口位置。
        /// </summary>
        public static bool TestMode
        {
            get
            {
                return !string.IsNullOrEmpty(AutoShotDir)
                    || !string.IsNullOrEmpty(StartPageKey)
                    || !string.IsNullOrEmpty(PerfTourFile);
            }
        }

        /// <summary>退出时保存窗口状态（非最大化记边界；最大化只记标志）。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (TestMode) return;   // 测试模式不落盘，避免污染用户配置
            try
            {
                if (WindowState == FormWindowState.Maximized)
                    AppSettings.SaveWindow(RestoreBounds, true);
                else
                    AppSettings.SaveWindow(Bounds, false);
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                FinishSwitchAnim(); // 切页过渡中途关窗：先停表并归位，别让 Tick 触碰已释放的页面
                Application.RemoveMessageFilter(_wheelRouter);
                _statusTimer.Dispose();
            }
            base.Dispose(disposing);
        }


        public void SetStatus(string text)
        {
            _statusText = text == null ? "" : text;
            _statusBar.Invalidate();
        }

        // ==============================================================
        // 深色标题栏
        // ==============================================================

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Perf.Mark("startup.shown");

            ApplyWindowChrome();

            LayoutChrome();
            if (!Native.IsElevatedCached)
            {
                SetStatus("提示：部分系统级操作需要管理员权限，建议以管理员身份重新运行。");
            }

            StartUpdateCheck();

            // --shots 自动截图模式：窗口就绪后开始逐页截图，完成即自动退出
            if (!string.IsNullOrEmpty(AutoShotDir))
            {
                BeginInvoke((MethodInvoker)delegate { RunAutoShots(AutoShotDir); });
            }
            // --perf-tour 性能巡检模式：逐页打点后落盘退出
            else if (!string.IsNullOrEmpty(PerfTourFile))
            {
                BeginInvoke((MethodInvoker)delegate { RunPerfTour(PerfTourFile); });
            }
        }


        /// <summary>标题栏明暗跟随配色方案（浅色方案下关闭沉浸式深色标题栏）。</summary>
        private void ApplyWindowChrome()
        {
            try
            {
                int v = Theme.IsLight ? 0 : 1;
                if (DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4) != 0)
                {
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref v, 4);
                }
            }
            catch
            {
            }
        }

        /// <summary>主题色等全局设置变化后重绘所有已加载页面。</summary>
        public void RefreshAll()
        {
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].View != null) _entries[i].View.Invalidate(true);
                }
                _topBar.Invalidate();
                Invalidate(true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 切换配色方案并立即生效：先把控件树里由主题赋值的底色从旧色重映射到新色
        /// （否则构造时写入的 BackColor 仍是旧方案），再重刷标题栏明暗，最后全量重绘。
        /// </summary>
        public void ApplyThemeAndRefresh(bool light)
        {
            try
            {
                Color[] backFrom = Theme.SchemeSnapshot();
                Color[] foreFrom = Theme.ForeSnapshot();
                Theme.ApplyScheme(light ? 1 : 0, AppSettings.AccentIndex);
                ThemeSkin.Reload(this, backFrom, Theme.SchemeSnapshot(),
                    foreFrom, Theme.ForeSnapshot());
                ApplyWindowChrome();
                RefreshAll();
                // 让当前页重走一次激活流程，重刷按行写入的语义色（如表格单元格前景色）
                RefreshCurrent();
            }
            catch
            {
            }
        }

        /// <summary>打开 / 关闭命令面板（Ctrl+K）。</summary>
        private void TogglePalette()
        {
            if (_palette == null) return;
            if (_palette.Visible) _palette.Close();
            else _palette.Open();
        }

        /// <summary>切换深浅配色并即时生效（顶栏主题切换按钮）。</summary>
        private void ToggleTheme()
        {
            AppSettings.LightTheme = !AppSettings.LightTheme;
            ApplyThemeAndRefresh(AppSettings.LightTheme);
            if (_themeButton != null)
            {
                _themeButton.Text = AppSettings.LightTheme ? "深色" : "浅色";
                // 文案长度变化后要重算宽度：否则按钮宽度停在旧文字上（字被挤窄或留白偏大）
                _themeButton.FitToText(86);
            }
        }

        private void StartUpdateCheck()
        {
            if (!AppSettings.AutoUpdateCheck) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    UpdateInfo info = UpdateChecker.Check();
                    if (info == null || !info.Ok || !info.IsNewerThan(AppVersion)) return;

                    BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (IsDisposed) return;
                            if (Dialog.Confirm(this, "发现新版本 v" + info.Version,
                                "最新版本：v" + info.Version + "（当前 v" + AppVersion + "）\r\n\r\n" +
                                "更新说明：" + (string.IsNullOrEmpty(info.Notes) ? "（无）" : info.Notes) +
                                "\r\n\r\n是否前往「关于与更新」页安装？"))
                            {
                                NavigateTo("about"); // 与 PageCatalog 的模块键一致（改名遗留会导致跳转失效）
                            }
                        }
                        catch
                        {
                        }
                    });
                }
                catch
                {
                }
            });
        }


        // ==============================================================
        // 状态栏
        // ==============================================================

        private void BuildStatusBar()
        {
            _statusBar.BackColor = Theme.WindowBg;
            _statusBar.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);

                SolidBrush b = GdiCache.Brush(Theme.WindowBg);
                {
                    g.FillRectangle(b, _statusBar.ClientRectangle);
                }
                Pen p = GdiCache.Pen(Theme.BorderSoft, 1f);
                {
                    g.DrawLine(p, 0, 0, _statusBar.Width, 0);
                }
                // 状态文字与顶栏标题同一套过渡：切页时淡入 + 轻微右移
                float cue = CueProgress();
                int cueDx = CueOffset(cue, 8);
                System.Drawing.Text.TextRenderingHint oldHint = g.TextRenderingHint;
                if (cue < 1f) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                Gfx.DrawTextEllipsis(g, _statusText, Theme.FontSmall, CueColor(Theme.TextSecondary, cue),
                    new Rectangle(14 + cueDx, 0, Math.Max(60, _statusBar.Width - 280 - cueDx), _statusBar.Height));
                g.TextRenderingHint = oldHint;
            };

            _spinner.Size = new Size(16, 16);
            _statusBar.Controls.Add(_spinner);

            _statusDot.Size = new Size(140, 20);
            _statusDot.DotColor = Native.IsElevatedCached ? Theme.Success : Theme.Warning;
            _statusDot.Text = Native.IsElevatedCached ? "管理员权限" : "标准用户权限";
            _statusBar.Controls.Add(_statusDot);
        }


        // ==============================================================
        // 布局
        // ==============================================================

        private void LayoutChrome()
        {
            // DisplayRectangle 已扣除窗框 Padding(1px)：据此布局，自绘窗框才不会被内容盖住
            Rectangle box = DisplayRectangle;
            int x0 = box.X;
            int y0 = box.Y;
            int w = box.Width;
            int h = box.Height;
            if (w <= 0 || h <= 0) return;

            int statusTop = y0 + h - StatusHeight;
            int sideH = Math.Max(0, statusTop - y0 - TopBarHeight);

            _topBar.SetBounds(x0, y0, w, TopBarHeight);
            _sidebar.SetBounds(x0, y0 + TopBarHeight, SidebarWidth, sideH);
            _sidebarLogo.SetBounds(0, 0, SidebarWidth, SidebarLogoHeight);
            _sidebarFooter.SetBounds(0, Math.Max(0, sideH - SidebarFooterHeight), SidebarWidth, SidebarFooterHeight);
            _sidebarScroll.SetBounds(0, SidebarLogoHeight, SidebarWidth,
                Math.Max(0, sideH - SidebarLogoHeight - SidebarFooterHeight));
            _navFlow.Width = SidebarWidth - 14;

            // 页签栏占用内容区顶部一条：其余高度给页面（大功能下只有一页时隐藏，不占位）
            int stripH = _tabStrip.Visible ? TabStrip.StripHeight : 0;
            _tabStrip.SetBounds(x0 + SidebarWidth, y0 + TopBarHeight, Math.Max(0, w - SidebarWidth), stripH);
            _content.SetBounds(x0 + SidebarWidth, y0 + TopBarHeight + stripH,
                Math.Max(0, w - SidebarWidth), Math.Max(0, sideH - stripH));
            _statusBar.SetBounds(x0, statusTop, w, StatusHeight);

            _statusDot.Location = new Point(_statusBar.Width - _statusDot.Width - 12, 5);
            LayoutToast();
            _spinner.Location = new Point(_statusBar.Width - _statusDot.Width - 32, 7);

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].View != null)
                {
                    _entries[i].View.SetBounds(0, 0, _content.Width, _content.Height);
                }
            }

            // 命令面板盖满整个客户区，紧随窗口缩放
            if (_palette != null)
            {
                _palette.SetBounds(0, 0, ClientSize.Width, ClientSize.Height);
            }
        }

        // ==============================================================
        // Toast 反馈
        // ==============================================================

        /// <summary>
        /// 显示一条 Toast（页面经 <see cref="ViewBase.Toast"/> 调用）。
        /// 反馈走浮层而不是页头副标题：副标题在页头、字号小、切页即丢，容易错过。
        /// </summary>
        public void ShowToast(string title, string sub, ToastKind kind)
        {
            // 自动截图模式：不弹浮层，否则截图里会压着一条与实际界面无关的提示
            if (!string.IsNullOrEmpty(AutoShotDir)) return;
            _toast.Show(title, sub, kind);
            _toast.BringToFront();
        }

        private void LayoutToast()
        {
            Rectangle box = _content.Bounds;
            int w = Math.Min(400, Math.Max(220, box.Width - 48));
            _toast.Width = w;
            _toast.Left = box.Left + (box.Width - w) / 2;
            _toast.Top = Math.Max(box.Top + 8, box.Bottom - _toast.Height - 24);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
        }
    }
}

