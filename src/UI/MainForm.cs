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
        public const string AppVersion = "1.2.0";

        private const int TopBarHeight = 54;
        private const int StatusHeight = 30;
        private const int SidebarFooterHeight = 38;

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

        private const int SidebarWidth = 192;

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
        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer();
        private readonly WheelRouter _wheelRouter = new WheelRouter();

        private string _currentKey = "";
        private string _statusText = "就绪";

        public static MainForm Current;

        public MainForm()
        {
            Current = this;

            Text = AppName;
            FormBorderStyle = FormBorderStyle.Sizable;
            BackColor = Theme.WindowBg;
            ClientSize = new Size(1280, 820);
            MinimumSize = new Size(960, 640);
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

            Theme.ApplyAccent(AppSettings.AccentIndex);

            BuildTopBar();
            BuildSidebar();
            BuildStatusBar();

            _content.BackColor = Theme.WindowBg;

            Controls.Add(_content);
            Controls.Add(_sidebar);
            Controls.Add(_topBar);
            Controls.Add(_statusBar);

            RegisterViews();
            LayoutChrome();
            NavigateTo("dashboard");

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

            try
            {
                int v = 1;
                if (DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4) != 0)
                {
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref v, 4);
                }
            }
            catch
            {
            }

            LayoutChrome();
            if (!Native.IsElevated())
            {
                SetStatus("提示：部分系统级操作需要管理员权限，建议以管理员身份重新运行。");
            }

            StartUpdateCheck();
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
            _topBar.BackColor = Theme.ChromeBg;
            _topBar.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);

                using (SolidBrush b = new SolidBrush(Theme.ChromeBg))
                {
                    g.FillRectangle(b, _topBar.ClientRectangle);
                }
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
                }

                // 品牌区
                Rectangle logo = new Rectangle(14, 12, 30, 30);
                using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(logo, Theme.Accent, Theme.Purple, 45f))
                using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(logo, 8))
                {
                    g.FillPath(lb, p);
                }
                IconPainter.Draw(g, "tune", new Rectangle(19, 17, 20, 20), Color.White);

                using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
                {
                    g.DrawString(AppName, Theme.FontBodyBold, b, 52, 17);
                }
            };
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

        /// <summary>侧栏：分组标题 + 导航项，ScrollHost 提供滚轮与滚动条。</summary>
        private void BuildSidebar()
        {
            _sidebar.BackColor = Theme.ChromeBg;
            _sidebar.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    e.Graphics.DrawLine(p, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
                }
            };

            _navFlow.FlowDirection = FlowDirection.TopDown;
            _navFlow.WrapContents = false;
            _navFlow.AutoScroll = false;
            _navFlow.BackColor = Theme.ChromeBg;
            _navFlow.Padding = new Padding(6, 8, 6, 12);
            _navFlow.SetBounds(0, 0, SidebarWidth, 600);

            _sidebarScroll.SetContent(_navFlow);
            _sidebar.Controls.Add(_sidebarScroll);

            // 侧栏底部信息块：填满导航项下方空白，显示版本与权限状态
            _sidebarFooter.BackColor = Theme.ChromeBg;
            _sidebarFooter.Height = SidebarFooterHeight;
            _sidebarFooter.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 10, 0, _sidebarFooter.Width - 10, 0);
                }
                string perm = Native.IsElevated() ? "管理员权限" : "普通权限";
                Color permColor = Native.IsElevated() ? Theme.Success : Theme.Warning;
                using (SolidBrush b = new SolidBrush(Theme.TextMuted))
                {
                    g.DrawString("v" + AppVersion + " · " + perm, Theme.FontSmall, b,
                        new Rectangle(16, 8, _sidebarFooter.Width - 24, _sidebarFooter.Height - 10));
                }
                // 权限状态色点
                using (SolidBrush b = new SolidBrush(permColor))
                {
                    g.FillEllipse(b, 14, 13, 7, 7);
                }
            };
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
        }

        public void NavigateTo(string key)
        {
            if (key == _currentKey)
            {
                ViewBase cur = EnsureView(FindEntry(key));
                cur.BringToFront();
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
            view.Visible = true;
            view.BringToFront();
            view.OnActivated();
            view.ForceRefresh();
            AnimateViewIn(view, oldView);
            SetStatus(target.Text);
        }

        /// <summary>
        /// 页面切换动画：新页 EaseOutBack 轻回弹下滑（约 160ms），
        /// 旧页同步左移视差（约 1/3 幅度）后隐藏——方向感与层次感同时到位。
        /// </summary>
        private void AnimateViewIn(Control view, Control oldView)
        {
            if (!AppSettings.Animations)
            {
                if (oldView != null) oldView.Visible = false;
                return;
            }
            try
            {
                const int frames = 10;
                const int travel = 26;
                int step = 0;
                _pageAnimNew = view;
                _pageAnimOld = oldView;
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                _pageAnimTimer = timer;
                timer.Interval = 16;
                timer.Tick += delegate
                {
                    if (_pageAnimTimer != timer) { timer.Stop(); timer.Dispose(); return; } // 已被新动画接管
                    step++;
                    if (step >= frames)
                    {
                        view.Location = new Point(0, 0);
                        if (oldView != null && !oldView.IsDisposed) oldView.Visible = false;
                        _pageAnimTimer = null;
                        _pageAnimNew = null;
                        _pageAnimOld = null;
                        timer.Stop();
                        timer.Dispose();
                        return;
                    }
                    float t = step / (float)frames;
                    // EaseOutBack：终点前轻微过冲再回弹
                    float c1 = 1.7f;
                    float c3 = c1 + 1f;
                    float ease = 1f + c3 * (float)Math.Pow(t - 1, 3) + c1 * (float)Math.Pow(t - 1, 2);
                    view.Location = new Point(0, (int)Math.Round(travel * (1f - ease)));
                    if (oldView != null && !oldView.IsDisposed)
                    {
                        // 旧页视差：与进度同向左移并小幅上移，营造推入层次
                        oldView.Location = new Point((int)Math.Round(-60f * ease), (int)Math.Round(-18f * ease));
                    }
                };
                timer.Start();
            }
            catch
            {
                view.Location = new Point(0, 0);
                if (oldView != null) oldView.Visible = false;
            }
        }

        private void RefreshCurrent()
        {
            NavEntry e = FindEntry(_currentKey);
            if (e == null) return;
            ViewBase view = EnsureView(e);
            view.OnDeactivated();
            view.OnActivated();
        }

        // ==============================================================
        // 测试辅助
        // ==============================================================

        /// <summary>已注册功能总数。</summary>
        public int CountFeatures()
        {
            return _entries.Count;
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
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int statusTop = h - StatusHeight;
            _topBar.SetBounds(0, 0, w, TopBarHeight);
            _sidebar.SetBounds(0, TopBarHeight, SidebarWidth, statusTop - TopBarHeight);
            _sidebarFooter.SetBounds(0, (statusTop - TopBarHeight) - SidebarFooterHeight, SidebarWidth, SidebarFooterHeight);
            _sidebarScroll.SetBounds(0, 0, SidebarWidth, statusTop - TopBarHeight - SidebarFooterHeight);
            _navFlow.Width = SidebarWidth - 14;
            _content.SetBounds(SidebarWidth, TopBarHeight, w - SidebarWidth, statusTop - TopBarHeight);
            _statusBar.SetBounds(0, statusTop, w, StatusHeight);

            _statusDot.Location = new Point(_statusBar.Width - _statusDot.Width - 12, 5);
            _spinner.Location = new Point(_statusBar.Width - _statusDot.Width - 32, 7);

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].View != null)
                {
                    _entries[i].View.SetBounds(0, 0, _content.Width, _content.Height);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChrome();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush b = new SolidBrush(Theme.WindowBg))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }
    }
}
