/* ============================================================
 * 文件说明：主窗体：整体框架布局（顶栏/侧栏导航/内容区/状态栏）、页面切换与路由、窗口持久化与全局快捷操作。
 * 项目：古月工具包（GuyueBox）
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
    /// 主窗体：摒弃传统「侧栏导航 + 多页面」范式，采用命令面板式架构——
    /// 顶部命令栏（搜索即导航，Ctrl+K 直达任意功能）+ 单屏工作台（一键优化主流程）。
    /// 原生边框与标题栏，DWM 沉浸式深色模式。
    /// </summary>
    public sealed class MainForm : Form
    {
        public const string AppName = "古月工具包";

        /// <summary>界面显示与更新比较用的版本号。唯一来源是 <see cref="AppInfo.Version"/>（src\AppInfo.cs），
        /// 此处只做转发——改版本请改那一处，别在这里写死。</summary>
        public const string AppVersion = AppInfo.Version;

        // 尺寸统一取自 Theme 布局令牌：改 Theme 一处即可全局生效
        private const int TopBarHeight = Theme.TopBarHeight;
        private const int StatusHeight = Theme.StatusBarHeight;
        private const int SidebarFooterHeight = Theme.SidebarFooterHeight;

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
        private readonly Panel _topBar = new Panel();
        private readonly Panel _sidebar = new Panel();
        private readonly Panel _content = new Panel();
        private readonly Panel _statusBar = new Panel();
        private readonly ScrollHost _sidebarScroll = new ScrollHost();
    private readonly Panel _sidebarFooter = new Panel();
        private readonly FlowLayoutPanel _navFlow = new FlowLayoutPanel();
        private readonly Spinner _spinner = new Spinner();
        private readonly StatusDot _statusDot = new StatusDot();
        private readonly ToastHost _toast = new ToastHost();
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer();
        private readonly WheelRouter _wheelRouter = new WheelRouter();

        private string _currentKey = "";
        private string _statusText = "就绪";

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
            ClientSize = new Size(1360, 860);   // 设计默认窗口尺寸
            MinimumSize = new Size(1100, 700);
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

            RegisterViews();
            LayoutChrome();

            // 启动页：开启「回到上次页面」且该页仍存在时跳转，否则回首页
            // （页 Key 可能在版本升级后被移除，必须校验存在性，否则会白屏）
            string startKey = AppSettings.RestoreLastPage ? AppSettings.LastPage : "";
            if (string.IsNullOrEmpty(startKey) || FindEntry(startKey) == null) startKey = "dashboard";
            NavigateTo(startKey);

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
                else if (e.Control && e.KeyCode == Keys.Tab)
                {
                    NextPage(e.Shift ? -1 : 1);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        /// <summary>退出时保存窗口状态（非最大化记边界；最大化只记标志）。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
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
                // 切页动画进行中关窗：先停表并归位，防止 Tick 继续触碰已释放的页面
                FinishPageAnim();
                Application.RemoveMessageFilter(_wheelRouter);
                _statusTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ==============================================================
        // 无边框窗框：渐变形描边 + 内侧高光（设计 Shell.Frame）
        // ==============================================================

        /// <summary>
        /// 自绘窗框。窗体外圈 1px 由 Padding 让出，绘制竖向三停渐变描边
        /// （上 #52647D → 侧 #334155 → 下 #1E293B），并叠一道内侧高光。
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Rectangle rc = ClientRectangle;

            using (SolidBrush b = new SolidBrush(Theme.ShellBg))
            {
                g.FillRectangle(b, rc);
            }

            int h = Math.Max(1, rc.Height);
            using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, 1, h), Theme.FrameTop, Theme.FrameBottom, 90f))
            {
                System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(3);
                cb.Colors = new Color[] { Theme.FrameTop, Theme.FrameSide, Theme.FrameBottom };
                cb.Positions = new float[] { 0f, 0.42f, 1f };
                lb.InterpolationColors = cb;

                using (Pen p = new Pen(lb, 1f))
                {
                    g.DrawRectangle(p, 0, 0, rc.Width - 1, rc.Height - 1);
                }
            }

            // 内高光：上缘通亮，左右两侧渐隐（设计 Shell.Frame.Brush.InnerHighlight）
            g.DrawLine(GdiCache.Pen(Theme.FrameInnerHighlight, 1f), 1, 1, rc.Width - 2, 1);
            g.DrawLine(GdiCache.Pen(Theme.LightBeam, 1f), 1, 1, 1, rc.Height - 2);
            g.DrawLine(GdiCache.Pen(Theme.LightBeam, 1f), rc.Width - 2, 1, rc.Width - 2, rc.Height - 2);
        }

        // ==============================================================
        // 无边框窗口的缩放与最大化钳制
        // ==============================================================

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                          HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXPOINT { public int X, Y; }

        /// <summary>
        /// WM_GETMINMAXINFO 的参数结构。本程序只用 ptMinTrackSize，
        /// 但前三个字段必须保留：缺少它们会让 ptMinTrackSize 落到错误的偏移上。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public MINMAXPOINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        /// <summary>边缘缩放热区宽度（设计 WindowChrome ResizeBorderThickness = 8）。</summary>
        private const int ResizeBorder = 8;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);

                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point(lp & 0xFFFF, (lp >> 16) & 0xFFFF));
                bool left = p.X <= ResizeBorder;
                bool right = p.X >= ClientSize.Width - ResizeBorder;
                bool top = p.Y <= ResizeBorder;
                bool bottom = p.Y >= ClientSize.Height - ResizeBorder;

                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                try
                {
                    // 无边框窗口最大化时默认会盖住任务栏，这里按屏幕工作区钳制
                    Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    mmi.ptMaxPosition.X = wa.X;
                    mmi.ptMaxPosition.Y = wa.Y;
                    mmi.ptMaxSize.X = wa.Width;
                    mmi.ptMaxSize.Y = wa.Height;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch
                {
                }
                return;
            }

            base.WndProc(ref m);
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

            ApplyWindowChrome();

            LayoutChrome();
            if (!Native.IsElevated())
            {
                SetStatus("提示：部分系统级操作需要管理员权限，建议以管理员身份重新运行。");
            }

            StartUpdateCheck();
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
        // 顶部命令栏
        // ==============================================================

        private void BuildTopBar()
        {
            _topBar.BackColor = Theme.ShellBg;
            _topBar.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                Rectangle rc = _topBar.ClientRectangle;

                using (SolidBrush b = new SolidBrush(Theme.ShellBg))
                {
                    g.FillRectangle(b, rc);
                }

                // 环境光晕：设计 Shell.Frame.Brush.Ambient（以左侧为心的冷蓝径向光，只在上缘铺开）
                using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    gp.AddEllipse(-rc.Width, -rc.Height * 2, rc.Width * 2, rc.Height * 4);
                    using (System.Drawing.Drawing2D.PathGradientBrush pg =
                        new System.Drawing.Drawing2D.PathGradientBrush(gp))
                    {
                        pg.CenterColor = Gfx.Alpha(Theme.HeroGlow, Theme.IsLight ? 26 : 46);
                        pg.SurroundColors = new Color[] { Color.Transparent };
                        g.FillPath(pg, gp);
                    }
                }

                // 品牌：Logo 方块（强调色→青渐变）+ 「古月」强调色 +「工具包」主文字 + 版本胶囊
                Rectangle logo = new Rectangle(16, (rc.Height - 24) / 2, 24, 24);
                using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(logo, Theme.Accent, Theme.Cyan, 45f))
                using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(logo, Theme.RadiusChip))
                {
                    g.FillPath(lb, p);
                }
                IconPainter.Draw(g, "tune", new Rectangle(logo.X + 4, logo.Y + 4, 16, 16), Color.White);

                int x = logo.Right + 10;
                string brandA = "古月";
                string brandB = "工具包";
                Size sa = TextRenderer.MeasureText(brandA, Theme.FontBodyBold);
                Size sb = TextRenderer.MeasureText(brandB, Theme.FontBodyBold);
                int ty = (rc.Height - sa.Height) / 2;
                g.DrawString(brandA, Theme.FontBodyBold, GdiCache.Brush(Theme.Accent), x, ty);
                g.DrawString(brandB, Theme.FontBodyBold, GdiCache.Brush(Theme.TextPrimary), x + sa.Width - 2, ty);
                x += sa.Width + sb.Width - 2 + 10;

                // 版本胶囊（设计：CardBackgroundHover 底 + 弱化文字）
                string ver = "v" + AppVersion;
                Size vs = TextRenderer.MeasureText(ver, Theme.FontMicro);
                Rectangle chip = new Rectangle(x, (rc.Height - 18) / 2, vs.Width + 14, 18);
                Gfx.FillRound(g, chip, Theme.RadiusChip, Theme.GlassCardHover);
                Gfx.DrawTextCenter(g, ver, Theme.FontMicro, Theme.TextMuted, chip);

                // 底部发丝分隔
                g.DrawLine(GdiCache.Pen(Theme.BorderSoft, 1f), 0, rc.Height - 1, rc.Width, rc.Height - 1);
            };

            // 拖动：非按钮区域按下即拖窗。走 Native.DragWindow（ReleaseCapture + WM_NCLBUTTONDOWN/HTCAPTION），
            // 因此 Aero Snap 与「双击标题栏最大化」都由系统原生提供。
            _topBar.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
            };

            _topBar.Resize += delegate { LayoutTitleButtons(); };
            BuildTitleButtons();
        }

        /// <summary>标题栏右侧按钮：功能入口 + 最小化/最大化/关闭（设计 WinControlBtn / WinCloseBtn）。</summary>
        private void BuildTitleButtons()
        {
            AddTitleText("反馈", delegate { Shell.OpenPath(UpdateChecker.ProjectUrl + "/issues"); });
            AddTitleText("关于", delegate { NavigateTo("about"); });

            CaptionButton min = new CaptionButton("min");
            min.HoverColor = Theme.CardHover;
            min.Click += delegate { WindowState = FormWindowState.Minimized; };
            AddTitleButton(min, 44);

            _maxButton = new CaptionButton("max");
            _maxButton.HoverColor = Theme.CardHover;
            _maxButton.Click += delegate { ToggleMaximize(); };
            AddTitleButton(_maxButton, 44);

            CaptionButton close = new CaptionButton("close");
            close.HoverColor = Theme.Danger; // 设计：关闭按钮悬停转红
            close.Click += delegate { Close(); };
            AddTitleButton(close, 44);
        }

        private void AddTitleText(string text, EventHandler onClick)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.Variant = ButtonVariant.Ghost;
            b.Font = Theme.FontSmall;
            b.Height = TopBarHeight - 10;
            b.Width = 58;
            b.Click += onClick;
            _titleButtons.Add(b);
            _topBar.Controls.Add(b);
            LayoutTitleButtons();
        }

        private void AddTitleButton(Control c, int width)
        {
            c.Height = TopBarHeight - 4;
            c.Width = width;
            _titleButtons.Add(c);
            _topBar.Controls.Add(c);
            LayoutTitleButtons();
        }

        /// <summary>标题栏按钮靠右排列（最后加入的在最右）。</summary>
        private void LayoutTitleButtons()
        {
            int x = _topBar.Width;
            for (int i = _titleButtons.Count - 1; i >= 0; i--)
            {
                Control c = _titleButtons[i];
                x -= c.Width;
                c.Location = new Point(Math.Max(0, x), (TopBarHeight - c.Height) / 2);
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            if (_maxButton != null)
            {
                _maxButton.IconKind = WindowState == FormWindowState.Maximized ? "restore" : "max";
            }
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

                using (SolidBrush b = new SolidBrush(Theme.WindowBg))
                {
                    g.FillRectangle(b, _statusBar.ClientRectangle);
                }
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 0, 0, _statusBar.Width, 0);
                }
                Gfx.DrawTextEllipsis(g, _statusText, Theme.FontSmall, Theme.TextSecondary,
                    new Rectangle(14, 0, Math.Max(60, _statusBar.Width - 280), _statusBar.Height));
            };

            _spinner.Size = new Size(16, 16);
            _statusBar.Controls.Add(_spinner);

            _statusDot.Size = new Size(140, 20);
            _statusDot.DotColor = Native.IsElevated() ? Theme.Success : Theme.Warning;
            _statusDot.Text = Native.IsElevated() ? "管理员权限" : "标准用户权限";
            _statusBar.Controls.Add(_statusDot);
        }

        // ==============================================================
        // 页面（懒加载）
        // ==============================================================

        private void RegisterViews()
        {
            // 数据驱动：页面模块统一在 PageCatalog 注册，这里只负责装配
            for (int i = 0; i < PageCatalog.All.Count; i++)
            {
                PageModule m = PageCatalog.All[i];
                AddEntry(m.Group, m.Key, m.Name, m.Icon, m.Factory);
            }

            BuildSidebarItems();
        }

        /// <summary>全部已注册页面模块（供 UI 探针与命令面板动态枚举）。</summary>
        public IList<PageModule> Modules
        {
            get { return PageCatalog.All; }
        }

        private void AddEntry(string group, string key, string text, string icon, Func<ViewBase> factory)
        {
            NavEntry entry = new NavEntry();
            entry.Key = key;
            entry.Text = text;
            entry.Icon = icon;
            entry.Group = group;
            entry.Factory = factory;
            _entries.Add(entry);
        }

        /// <summary>
        /// 侧栏：设计式深空蓝竖向渐变 + 右缘 1px 光带 + 底部深玻璃状态卡片。
        /// 导航项自身内缩 8px 绘制胶囊，故容器只留纵向呼吸。
        /// </summary>
        private void BuildSidebar()
        {
            _sidebar.BackColor = Theme.SidebarBg;
            _sidebar.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Rectangle rc = _sidebar.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;

                // 设计 SidebarBackground：#050A18 → #070E21（竖向）
                using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(0, 0, 1, rc.Height), Theme.SidebarBgTop, Theme.SidebarBgBottom, 90f))
                {
                    g.FillRectangle(lb, rc);
                }

                // 右缘光带：垂直渐变 透明 → 15% 白 → 透明（设计 Sidebar Light Beam Divider）
                if (rc.Width > 2)
                {
                    using (System.Drawing.Drawing2D.LinearGradientBrush beam =
                        new System.Drawing.Drawing2D.LinearGradientBrush(
                            new Rectangle(0, 0, 1, rc.Height), Color.Transparent, Color.Transparent, 90f))
                    {
                        System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(3);
                        cb.Colors = new Color[] { Color.Transparent, Theme.LightBeam, Color.Transparent };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        beam.InterpolationColors = cb;
                        g.FillRectangle(beam, rc.Width - 1, 0, 1, rc.Height);
                    }
                }
            };

            _navFlow.FlowDirection = FlowDirection.TopDown;
            _navFlow.WrapContents = false;
            _navFlow.AutoScroll = false;
            _navFlow.BackColor = Theme.SidebarBg;
            _navFlow.Padding = new Padding(0, 10, 0, 12);
            _navFlow.SetBounds(0, 0, SidebarWidth, 600);

            // 滚动容器同色：不设的话默认内容区底色，导航项结束后会露出一段异色带
            _sidebarScroll.BackColor = Theme.SidebarBg;
            _sidebarScroll.SetContent(_navFlow);
            _sidebar.Controls.Add(_sidebarScroll);

            // 侧栏底部：设计「深玻璃状态卡片」——盾牌图标 + 状态点(带同色光晕) + 运行模式 + 版本 + 齿轮
            _sidebarFooter.BackColor = Theme.SidebarBg;
            _sidebarFooter.Height = SidebarFooterHeight;
            _sidebarFooter.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                Rectangle rc = _sidebarFooter.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;

                g.FillRectangle(GdiCache.Brush(Theme.SidebarBg), new Rectangle(0, 0, Math.Max(1, rc.Width - 1), rc.Height));
                g.DrawLine(GdiCache.Pen(Gfx.Alpha(Theme.LightBeam, Theme.LightBeam.A / 2), 1f),
                    rc.Width - 1, 0, rc.Width - 1, rc.Height);

                Rectangle card = new Rectangle(10, 8, Math.Max(40, rc.Width - 20), Math.Max(20, rc.Height - 16));
                Gfx.FillRound(g, card, Theme.RadiusCard, Theme.GlassCardBg);
                Gfx.StrokeRound(g, card, Theme.RadiusCard, Theme.GlassBorder, 1f);

                Rectangle shield = new Rectangle(card.X + 10, card.Y + (card.Height - 32) / 2, 32, 32);
                Gfx.FillRound(g, shield, Theme.RadiusChip, Theme.CardBgAlt);
                IconPainter.Draw(g, "admin", new Rectangle(shield.X + 8, shield.Y + 8, 16, 16), Theme.TextMuted);

                bool admin = Native.IsElevated();
                Color dot = admin ? Theme.Success : Theme.Warning;
                using (SolidBrush glow = new SolidBrush(Gfx.Alpha(dot, 60)))
                {
                    g.FillEllipse(glow, shield.Right - 7, shield.Bottom - 7, 12, 12);
                }
                g.FillEllipse(GdiCache.Brush(dot), shield.Right - 5, shield.Bottom - 5, 8, 8);

                int textX = shield.Right + 10;
                int textW = Math.Max(20, card.Right - textX - 30);
                Gfx.DrawTextEllipsis(g, admin ? "管理员模式运行中" : "普通模式运行中",
                    Theme.FontSmall, Theme.TextPrimary, new Rectangle(textX, card.Y + 14, textW, 18));
                Gfx.DrawTextEllipsis(g, "v" + AppVersion, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(textX, card.Y + 32, textW, 16));

                IconPainter.Draw(g, "feature",
                    new Rectangle(card.Right - 28, card.Y + (card.Height - 18) / 2, 18, 18), Theme.TextMuted);
            };
            _sidebarFooter.Cursor = Cursors.Hand;
            _sidebarFooter.Click += delegate { NavigateTo("settings"); };
            _sidebar.Controls.Add(_sidebarFooter);
        }

        /// <summary>按分组重建侧栏项（RegisterViews 末尾调用一次）。</summary>
        private void BuildSidebarItems()
        {
            string currentGroup = null;
            foreach (NavEntry e in _entries)
            {
                if (e.Group != currentGroup)
                {
                    currentGroup = e.Group;
                    _navFlow.Controls.Add(new NavSideGroup(currentGroup));
                }
                NavSideItem item = new NavSideItem(e.Text, e.Icon);
                item.Width = SidebarWidth - 24;
                string key = e.Key;
                item.Click += delegate { NavigateTo(key); };
                e.Button = item;
                _navFlow.Controls.Add(item);
            }
            _navFlow.Height = _navFlow.Controls.Count * 36 + 24;
        }

        private NavEntry FindEntry(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key) return _entries[i];
            }
            return null;
        }

        private ViewBase EnsureView(NavEntry entry)
        {
            if (entry.View != null) return entry.View;

            ViewBase view = entry.Factory();
            entry.View = view;
            view.Visible = false;
            view.SetBounds(0, 0, Math.Max(1, _content.Width), Math.Max(1, _content.Height));
            _content.Controls.Add(view);
            return view;
        }

        private System.Windows.Forms.Timer _pageAnimTimer;
        private Control _pageAnimNew;
        private Control _pageAnimOld;

        /// <summary>把全部页面视图归位到 (0,0)（视差动画只动两个页，其余页可能残留偏移）。</summary>
        private void ResetViewLocations()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Control v = _entries[i].View;
                if (v != null && v.Location != new Point(0, 0)) v.Location = new Point(0, 0);
            }
        }

        /// <summary>立即终结进行中的切页动画（快速连点时的竞态防护）。</summary>
        private void FinishPageAnim()
        {
            if (_pageAnimTimer == null) return;
            _pageAnimTimer.Stop();
            _pageAnimTimer.Dispose();
            _pageAnimTimer = null;
            if (_pageAnimNew != null) _pageAnimNew.Location = new Point(0, 0);
            if (_pageAnimOld != null)
            {
                _pageAnimOld.Location = new Point(0, 0);
                _pageAnimOld.Visible = false;
            }
            _pageAnimNew = null;
            _pageAnimOld = null;
            ResetViewLocations(); // 多轮连点后可能有更早的页残留偏移，全部归位
        }

        public void NavigateTo(string key)
        {
            if (key == _currentKey)
            {
                NavEntry curEntry = FindEntry(key);
                if (curEntry == null) return;
                EnsureView(curEntry).BringToFront();
                return;
            }

            FinishPageAnim(); // 上一次切页动画未结束时先归位，避免两个 Timer 争抢页面状态

            NavEntry target = FindEntry(key);
            if (target == null) return;

            Control oldView = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                NavEntry e = _entries[i];
                if (e.Button != null) e.Button.Selected = e == target;
                if (e.View != null && e.View.Visible)
                {
                    e.View.OnDeactivated();
                    if (e.View != target.View) oldView = e.View; // 旧页留到动画结束再隐藏（视差）
                }
            }

            ViewBase view = EnsureView(target);
            _currentKey = key;
            AppSettings.LastPage = key; // 记录所在页面，供「启动时回到上次页面」使用
            view.Visible = true;
            view.BringToFront();
            view.OnActivated();
            view.ForceRefresh();
            AnimateViewIn(view, oldView);
            SetStatus(target.Text);
        }

        /// <summary>
        /// 页面切换：直切（旧页立即隐藏、新页原地显示）。
        /// 不用视差滑动动画：它与快速切页存在竞态（两页重叠 / 残影 / 内容加载被中断），
        /// 且对低配机是纯负担。切换即时完成。
        /// </summary>
        private void AnimateViewIn(Control view, Control oldView)
        {
            if (oldView != null) oldView.Visible = false;
            view.Location = new Point(0, 0);
            view.Visible = true;
        }

        private void RefreshCurrent()
        {
            NavEntry e = FindEntry(_currentKey);
            if (e == null) return;
            ViewBase view = EnsureView(e);
            view.OnDeactivated();
            view.OnActivated();
        }

        private void NextPage(int dir)
        {
            int idx = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == _currentKey) { idx = i; break; }
            }
            if (idx < 0) return;

            int next = (idx + dir + _entries.Count) % _entries.Count;
            NavigateTo(_entries[next].Key);
        }

        private void UpdateBusyIndicator()
        {
            bool busy = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                NavEntry e = _entries[i];
                if (e.View != null && e.View.Visible && e.View.IsBusy) { busy = true; break; }
            }

            if (busy)
            {
                if (!_spinner.Visible)
                {
                    _spinner.Visible = true;
                    _spinner.StartSpin();
                }
            }
            else if (_spinner.Visible)
            {
                _spinner.StopSpin();
                _spinner.Visible = false;
            }
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
            _sidebarFooter.SetBounds(0, Math.Max(0, sideH - SidebarFooterHeight), SidebarWidth, SidebarFooterHeight);
            _sidebarScroll.SetBounds(0, 0, SidebarWidth, Math.Max(0, sideH - SidebarFooterHeight));
            _navFlow.Width = SidebarWidth - 14;
            _content.SetBounds(x0 + SidebarWidth, y0 + TopBarHeight, Math.Max(0, w - SidebarWidth), sideH);
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
