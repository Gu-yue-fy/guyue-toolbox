/* ============================================================
 * 文件说明：页面基类：提供标题/副标题/操作按钮区、Body 行式布局器、滚动与缩放处理等公共设施；所有功能页继承此类。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Commands;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 所有功能页的基类：顶部标题栏（含操作按钮）+ 自绘滚动内容区。
    /// 内容区使用自绘滚动条（ScrollHost），配合 WS_EX_COMPOSITED 统一双缓冲，
    /// 避免大量自绘子控件在原生滚动时逐个擦除重绘造成的卡顿。
    /// </summary>
    public abstract class ViewBase : Panel
    {
        public const int HeaderHeight = 92;

        private readonly BufferPanel _header;
        private readonly ScrollHost _scroll;
        private readonly BusyOverlay _busyOverlay;
        private readonly Timer _busyPoll;
        private readonly List<AccentButton> _actions = new List<AccentButton>();
        private string _subtitle = "";
        private Color _subtitleColor = Theme.TextSecondary;
        private bool _laying;
        private bool _busyVisual;

        /// <summary>内容容器：自上而下排列。</summary>
        protected readonly FlowLayoutPanel Body;

        protected ViewBase(string title, string subtitle)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Text = title;
            _subtitle = subtitle;
            Padding = new Padding(0);

            Body = new FlowLayoutPanel();
            Body.FlowDirection = FlowDirection.TopDown;
            Body.WrapContents = false;
            Body.AutoScroll = false;
            Body.BackColor = Theme.WindowBg;
            Body.Padding = new Padding(30, 20, 30, 18);
            Body.SetBounds(0, 0, 400, 400);

            _scroll = new ScrollHost();
            _scroll.SetContent(Body);

            _header = new BufferPanel();
            _header.BackColor = Theme.WindowBg;
            _header.Paint += HeaderPaint;

            // 加载指示遮罩：IsBusy=true 时淡入中央旋转环
            _busyOverlay = new BusyOverlay();
            _busyOverlay.Visible = false;

            Controls.Add(_scroll);
            Controls.Add(_header);
            Controls.Add(_busyOverlay);

            // 低频轮询 IsBusy，变化时切换遮罩（各页面无需手动通知）；
            // 仅在页面可见时启用，避免 23 个页面全部常驻空转
            _busyPoll = new Timer();
            _busyPoll.Interval = 250;
            _busyPoll.Enabled = false;
            _busyPoll.Tick += delegate
            {
                bool busy;
                try { busy = Visible && IsBusy; }
                catch { busy = false; }
                if (busy != _busyVisual)
                {
                    _busyVisual = busy;
                    // 显示前强制同步位置与尺寸，避免遮罩残留在过期坐标上
                    int hh = EffectiveHeader;
                    _busyOverlay.SetBounds(0, hh, ClientSize.Width,
                        Math.Max(0, ClientSize.Height - hh));
                    _busyOverlay.Visible = busy;
                    if (busy) { _busyOverlay.BringToFront(); _busyOverlay.StartSpin(); }
                    else _busyOverlay.StopSpin();
                }
            };
            VisibleChanged += delegate
            {
                try { _busyPoll.Enabled = Visible; }
                catch { }
            };

            Body.Resize += delegate { LayoutRows(); };
            Body.ControlAdded += delegate { LayoutRows(); };
            Body.ControlRemoved += delegate { LayoutRows(); };
            _header.Resize += delegate { LayoutActions(); };
        }

        public string TitleText
        {
            get { return Text; }
        }

        /// <summary>嵌入模式：作为合并页的子页时隐藏自身标题头，从容器顶部开始布局。</summary>
        public bool EmbedHeader { get; set; }

        /// <summary>嵌入模式下保留的操作按钮条高度（只隐藏标题，不隐藏操作按钮）。</summary>
        public const int ActionBarHeight = 46;

        private int EffectiveHeader
        {
            get { return EmbedHeader ? ActionBarHeight : HeaderHeight; }
        }

        protected int ViewportHeight
        {
            get { return _scroll.ViewportHeight; }
        }

        /// <summary>内容尺寸变化后通知滚动容器重新测量。</summary>
        public void RefreshLayout()
        {
            _scroll.Relayout();
        }

        /// <summary>强制整体重排并重绘。页面切到前台后调用，避免出现空白页。</summary>
        public void ForceRefresh()
        {
            LayoutRows();
            _scroll.Relayout(true);
            _scroll.Invalidate(true);
            Invalidate(true);
        }

        // ---------------- 页面元素级联入场 ----------------

        private System.Windows.Forms.Timer _cascTimer;
        private readonly List<Control> _cascControls = new List<Control>();
        private readonly List<int> _cascFrom = new List<int>();
        private readonly List<int> _cascDelta = new List<int>();
        private float _cascPos;

        /// <summary>
        /// 页面元素级联入场：首屏前 6 个元素错峰上移到位（约 220ms，EaseOutCubic）。
        /// 数据在后台加载的同时界面渐进呈现——「加载感」被动效吸收。
        /// </summary>
        private void AnimateContentIn()
        {
            if (!AppSettings.Animations || Body == null || Body.Controls.Count < 2) return;

            if (_cascTimer != null)
            {
                _cascTimer.Stop();
                RestoreCasc();
            }

            _cascControls.Clear();
            _cascFrom.Clear();
            _cascDelta.Clear();
            int n = Math.Min(6, Body.Controls.Count);
            for (int i = 0; i < n; i++)
            {
                Control c = Body.Controls[i];
                _cascControls.Add(c);
                _cascFrom.Add(c.Top);
                _cascDelta.Add(18 + i * 5);
                c.Top = c.Top + _cascDelta[i];
            }
            _cascPos = 0;

            if (_cascTimer == null)
            {
                _cascTimer = new System.Windows.Forms.Timer { Interval = 16 };
                _cascTimer.Tick += delegate
                {
                    _cascPos += 0.13f;
                    bool done = _cascPos >= 1f;
                    if (done) _cascPos = 1f;
                    float t = 1f - (1f - _cascPos) * (1f - _cascPos) * (1f - _cascPos);
                    for (int i = 0; i < _cascControls.Count; i++)
                    {
                        Control c = _cascControls[i];
                        if (c.IsDisposed) continue;
                        c.Top = _cascFrom[i] + (int)(_cascDelta[i] * (1f - t));
                    }
                    if (done)
                    {
                        RestoreCasc();
                        _cascTimer.Stop();
                    }
                };
            }
            Body.SuspendLayout(); // 动画期间挂起流式重排，防止手动位移被布局覆盖
            _cascTimer.Start();
        }

        private void RestoreCasc()
        {
            for (int i = 0; i < _cascControls.Count; i++)
            {
                Control c = _cascControls[i];
                if (!c.IsDisposed) c.Top = _cascFrom[i];
            }
            _cascControls.Clear();
            _cascFrom.Clear();
            _cascDelta.Clear();
            Body.ResumeLayout(false);
        }

        // ---------------- 加载状态反馈 ----------------

        /// <summary>
        /// 加载反馈统一走现有 BusyOverlay（构造时挂载、_busyPoll 轮询 IsBusy 自动开关）。
        /// 页面需要加载提示时只需 override IsBusy 返回真实忙碌状态，无需自行管理控件。
        /// </summary>

        /// <summary>
        /// 页面每次变为可见时强制重排一次：首次显示时父容器与内容宽度的布局时序
        /// 可能尚未稳定（数据又是后台异步填充），兜底避免「切走再切回才显示正确」。
        /// </summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated)
            {
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        int hh = EffectiveHeader;
                        _busyOverlay.SetBounds(0, hh, ClientSize.Width,
                            Math.Max(0, ClientSize.Height - hh));
                        LayoutRows();
                        LayoutActions();
                        _scroll.Relayout(true);
                        Invalidate(true);
                        AnimateContentIn();
                    });
                }
                catch (InvalidOperationException)
                {
                    // 句柄正在创建/销毁的窗口期，下一次可见切换会再触发
                }
            }
        }

        /// <summary>WS_EX_COMPOSITED：整页统一双缓冲，消除切页与滚动时的闪烁。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        /// <summary>命令层等外部调用者的公开通知入口（转发到页头副标题）。</summary>
        public void Notify(string text, Color color)
        {
            SetSubtitle(text, color);
        }

        protected void SetSubtitle(string text, Color color)
        {
            _subtitle = text == null ? "" : text;
            _subtitleColor = color;
            if (_header != null) _header.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _busyPoll.Dispose();
                if (_cascTimer != null) _cascTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // --------------------------------------------------------------
        // 页头
        // --------------------------------------------------------------

        protected AccentButton AddAction(string text, string icon, ButtonVariant variant, EventHandler onClick)
        {
            return AddAction(text, icon, variant, onClick, 0);
        }

        protected AccentButton AddAction(string text, string icon, ButtonVariant variant, EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = icon;
            b.Variant = variant;
            b.Height = 34;
            b.FitToText(96);
            if (width > 0) b.Width = width;
            if (onClick != null) b.Click += onClick;
            _actions.Add(b);
            _header.Controls.Add(b);
            LayoutActions();
            return b;
        }

        /// <summary>
        /// 绑定一个命令按钮：点击经 CommandHub 统一执行（异常兜底在命令层）。
        /// 命令必须已在 CommandHub 注册，否则启动期即抛出暴露问题。
        /// </summary>
        protected AccentButton AddCommand(string commandId, string icon, ButtonVariant variant, int width)
        {
            AppCommand cmd = CommandHub.Find(commandId);
            if (cmd == null)
            {
                throw new InvalidOperationException("未注册的命令: " + commandId);
            }
            AccentButton b = AddAction(cmd.Title, icon, variant, delegate
            {
                CommandHub.Run(this, cmd);
            }, width);
            b.Tag = cmd.Id; // 命令号随按钮携带，便于排查与自动化
            return b;
        }

        /// <summary>操作按钮宽度变化后重新排列（从右向左）。</summary>
        protected void LayoutActions()
        {
            int x = _header.Width - 26;
            int y = Math.Max(2, (EffectiveHeader - 34) / 2); // 按钮在头部高度内垂直居中
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                AccentButton b = _actions[i];
                x -= b.Width;
                b.Location = new Point(x, y);
                x -= 10;
            }
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            using (SolidBrush b = new SolidBrush(Theme.WindowBg))
            {
                g.FillRectangle(b, _header.ClientRectangle);
            }

            // 嵌入模式（合并页子页）：只画按钮条背景，不画标题/副标题（外层已有）
            if (EmbedHeader)
            {
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 0, ActionBarHeight - 1, _header.Width, ActionBarHeight - 1);
                }
                return;
            }

            // 标题与副标题一致：避让右侧操作按钮，窄窗口下截断而不是被按钮盖住
            int titleW = _header.Width - 52;
            if (_actions.Count > 0)
            {
                int right = _header.Width;
                for (int i = 0; i < _actions.Count; i++)
                {
                    if (_actions[i].Left < right) right = _actions[i].Left;
                }
                titleW = Math.Max(60, right - 26 - 16);
            }
            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            {
                Gfx.DrawTextEllipsis(g, Text, Theme.FontTitle, Theme.TextPrimary,
                    new Rectangle(26, 18, titleW, 30));
            }

            if (!string.IsNullOrEmpty(_subtitle))
            {
                int w = _header.Width - 52;
                if (_actions.Count > 0)
                {
                    int right = _header.Width;
                    for (int i = 0; i < _actions.Count; i++)
                    {
                        if (_actions[i].Left < right) right = _actions[i].Left;
                    }
                    w = Math.Max(60, right - 26 - 16);
                }
                Gfx.DrawTextEllipsis(g, _subtitle, Theme.FontSmall, _subtitleColor,
                    new Rectangle(26, 52, Math.Max(60, w), 20));
            }

            using (Pen p = new Pen(Theme.BorderSoft))
            {
                g.DrawLine(p, 0, HeaderHeight - 1, _header.Width, HeaderHeight - 1);
            }
        }

        // --------------------------------------------------------------
        // 布局
        // --------------------------------------------------------------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int hh = EffectiveHeader;
            _header.SetBounds(0, 0, ClientSize.Width, hh);
            _header.Visible = true; // 嵌入模式也显示按钮条（高度为 ActionBarHeight）
            _scroll.SetBounds(0, hh, ClientSize.Width,
                Math.Max(0, ClientSize.Height - hh));
            // 加载遮罩只覆盖内容滚动区，不再遮挡页头按钮
            _busyOverlay.SetBounds(0, hh, ClientSize.Width,
                Math.Max(0, ClientSize.Height - hh));
            LayoutActions();
        }

        /// <summary>创建一个横向排列的行容器，行高随最高的子控件变化。</summary>
        protected static FlowLayoutPanel MakeRow(int height, int bottomGap)
        {
            return MakeRowCore(height, bottomGap, "row");
        }

        /// <summary>创建一个行高固定的横向行容器。</summary>
        protected static FlowLayoutPanel MakeRowFixed(int height, int bottomGap)
        {
            return MakeRowCore(height, bottomGap, "rowfixed");
        }

        private static FlowLayoutPanel MakeRowCore(int height, int bottomGap, string tag)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.AutoScroll = false;
            row.BackColor = Theme.WindowBg;
            row.Height = height;
            row.Margin = new Padding(0, 0, 0, bottomGap);
            row.Tag = tag;
            return row;
        }

        /// <summary>把一个需要撑满整行的控件加入内容区。</summary>
        protected void AddFull(Control c, int height, int bottomGap)
        {
            c.Tag = "stretch";
            c.Height = height;
            c.Margin = new Padding(0, 0, 0, bottomGap);
            Body.Controls.Add(c);
            LayoutRows();
        }

        protected void AddRow(FlowLayoutPanel row)
        {
            Body.Controls.Add(row);
            LayoutRows();
        }

        protected void AddSpacer(int height)
        {
            Panel p = new Panel();
            p.BackColor = Theme.WindowBg;
            p.Height = height;
            p.Margin = new Padding(0);
            p.Tag = "stretch";
            Body.Controls.Add(p);
            LayoutRows();
        }

        private bool _suspendRowLayout;

        /// <summary>
        /// 批量挂载行时抑制逐控件触发的全表重排（ControlAdded → LayoutRows）：
        /// 挂载前调用，挂载完成手动 LayoutRows 一次。避免 N 行 = N 次全表重排的 O(N²) 卡顿。
        /// </summary>
        protected void SuspendRowLayout()
        {
            _suspendRowLayout = true;
        }

        protected void ResumeRowLayout()
        {
            _suspendRowLayout = false;
            LayoutRows();
        }

        private int _lastAvailWidth = -1;

        protected void LayoutRows()
        {
            if (_laying || _suspendRowLayout) return;
            _laying = true;
            try
            {
                int avail = Body.ClientSize.Width - Body.Padding.Left - Body.Padding.Right;
                // 宽度没变（如仅拖动窗口高度）时跳过等分重排——拖拽窗口从此不卡。
                // 但必须补齐 stretch 行宽度：立即渲染的行挂载时 Body 宽度可能早已定型
                //（不再触发 Resize），若不补，新行会保持默认 200px 宽 → 行宽塌陷。
                // Width 同值赋值在 WinForms 内部会被短路，157 行遍历为纳秒级。
                if (avail > 0 && avail == _lastAvailWidth)
                {
                    for (int i = 0; i < Body.Controls.Count; i++)
                    {
                        Control c = Body.Controls[i];
                        if ((c.Tag as string) == "stretch" && c.Width != avail) c.Width = avail;
                    }
                    _scroll.Relayout();
                    return;
                }
                _lastAvailWidth = avail;
                if (avail > 0)
                {
                    for (int i = 0; i < Body.Controls.Count; i++)
                    {
                        Control c = Body.Controls[i];
                        string tag = c.Tag as string;
                        if (tag == "stretch")
                        {
                            if (c.Width != avail) c.Width = avail;
                        }
                        else if (tag == "row" || tag == "rowfixed")
                        {
                            if (c.Width != avail) c.Width = avail;
                            LayoutRowChildren((FlowLayoutPanel)c, tag == "row");
                        }
                    }
                }
            }
            finally
            {
                _laying = false;
            }

            _scroll.Relayout();
        }

        /// <summary>等分一行中各控件的宽度。autoHeight 为 true 时行高取最高的子控件。
        /// 行内含 AutoSize 控件（自然宽度的说明文字等）时跳过宽度等分，保留各自宽度，
        /// 避免按钮/文字被强行拉宽变形——但行高仍按最高子控件计算，
        /// 否则 MakeRow(0,…) 的自然宽度行会保持 0 高，整行内容不可见。</summary>
        private static void LayoutRowChildren(FlowLayoutPanel row, bool autoHeight)
        {
            int n = row.Controls.Count;
            if (n == 0) return;

            bool natural = false;
            for (int i = 0; i < n; i++)
            {
                if (row.Controls[i].AutoSize) { natural = true; break; }
            }
            if (natural)
            {
                int tallestN = 0;
                for (int i = 0; i < n; i++)
                {
                    int h = row.Controls[i].Height + row.Controls[i].Margin.Vertical;
                    if (h > tallestN) tallestN = h;
                }
                if (autoHeight && tallestN > 0 && row.Height != tallestN) row.Height = tallestN;
                return;
            }

            int gap = 16;
            int avail = row.ClientSize.Width;
            int w = (avail - gap * (n - 1)) / n;
            if (w < 80) w = 80;

            int tallest = 0;
            for (int i = 0; i < n; i++)
            {
                Control c = row.Controls[i];
                c.Width = w;
                c.Margin = new Padding(0, 0, i == n - 1 ? 0 : gap, 0);
                if (c.Height > tallest) tallest = c.Height;
            }

            if (autoHeight && tallest > 0 && row.Height != tallest) row.Height = tallest;
        }

        /// <summary>页面被切换到前台时调用，用于按需刷新数据。</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>页面被切走时调用，用于暂停后台刷新。</summary>
        public virtual void OnDeactivated()
        {
        }

        /// <summary>页面是否处于忙碌状态（主窗体用于提示）。</summary>
        public virtual bool IsBusy
        {
            get { return false; }
        }
    }
}
