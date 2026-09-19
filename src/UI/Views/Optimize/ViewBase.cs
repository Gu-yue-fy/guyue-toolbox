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
        public const int HeaderHeight = Theme.HeaderHeight;

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
            Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop,
                Theme.PagePadX, Theme.PagePadBottom);
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
        public const int ActionBarHeight = Theme.ActionBarHeight;

        private int EffectiveHeader
        {
            get { return EmbedHeader ? ActionBarHeight : HeaderHeight; }
        }

        protected int ViewportHeight
        {
            get { return _scroll.ViewportHeight; }
        }

        /// <summary>
        /// 内容尺寸变化后重新测量：
        /// 强制走完整的「重测行高 + Body 重新堆叠」路径，而不是仅通知滚动容器。
        /// 否则行高在挂载后才定型时（概览卡回填数据、筛选切换可见性），
        /// 其后各行会停留在过期坐标，表现为行与行相互压叠。
        /// </summary>
        public void RefreshLayout()
        {
            // 注意：此处不能强制 LayoutRows(true)。
            // 实测强制完整重测会让 Body 与 ScrollHost 的宽度互相触发，反而放大错位（8px → 19px）。
            // 行错位的正解是「行高在挂载前定稿」+ 短路分支补 PerformLayout（见 LayoutRows / AddRow）。
            LayoutRows();
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
                    float t = Theme.Ease.CubicOut(_cascPos);
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

        /// <summary>
        /// 把后台线程的结果切回 UI 线程执行（返回是否成功投递）。
        /// 原先 20+ 个视图各自复制一份同名的私有 Post，现统一收在基类：
        /// 句柄未创建 / 已销毁时静默丢弃，不抛异常——后台任务扫完时窗口可能已关闭。
        /// </summary>
        // 不引入 System.Threading：它会与 System.Windows.Forms.Timer 争夺 Timer 这个名字
        protected bool Post(System.Threading.ThreadStart action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke((MethodInvoker)delegate { action(); });
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// 取表格当前选中行绑定的数据对象；未选中时返回 null。
        /// 原先 5 个页面各自复制了一份同名 Selected 属性，现统一收在此处。
        /// </summary>
        protected T SelectedFrom<T>(DarkGrid grid) where T : class
        {
            if (grid == null || grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as T;
        }

        /// <summary>
        /// 「统计条 + 表格」布局的标准高度重算。
        /// 原先 6 个页面逐字重复同一段逻辑（只在额外占位高度与最小高度上不同），现参数化收在此处。
        /// </summary>
        /// <param name="grid">撑满剩余高度的表格</param>
        /// <param name="summary">顶部统计条</param>
        /// <param name="extraUsed">统计条之外还需占掉的额外高度（如搜索行 34+18）</param>
        /// <param name="minHeight">表格最小高度</param>
        protected void LayoutGrid(DarkGrid grid, StatStrip summary, int extraUsed, int minHeight)
        {
            int summaryH = summary.PreferredHeight;
            summary.Height = summaryH;
            Control row = summary.Parent;
            if (row != null) row.Height = summaryH;

            // 42 = 提示条高度，18 = 提示条与统计条各自的下方间距
            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18 + extraUsed;
            int avail = ViewportHeight - used;
            if (avail < minHeight) avail = minHeight;

            if (grid.Height != avail) grid.Height = avail;
            grid.Invalidate();
            RefreshLayout();
        }

        protected void SetSubtitle(string text, Color color)
        {
            _subtitle = text == null ? "" : text;
            _subtitleColor = color;
            if (_header != null) _header.Invalidate();
        }

        /// <summary>
        /// 瞬时操作反馈（成功 / 需注意 / 失败）。
        /// 与 <see cref="SetSubtitle"/> 分工：副标题是常驻页头的"当前状态"，
        /// Toast 是"刚发生了什么"的结果——在主窗体可用时走浮层，不抢焦点、自动消失。
        /// </summary>
        protected void Toast(string title, string sub, ToastKind kind)
        {
            MainForm form = MainForm.Current;
            if (form != null)
            {
                form.ShowToast(title, sub, kind);
                return;
            }
            // 无主窗体（如单独构造页面的探测/测试）：退化为页头副标题，信息不丢失
            SetSubtitle(title + (string.IsNullOrEmpty(sub) ? "" : " " + sub),
                kind == ToastKind.Success ? Theme.Success
                : kind == ToastKind.Danger ? Theme.Danger
                : kind == ToastKind.Warning ? Theme.Warning : Theme.Accent);
        }

        protected void Toast(string title, string sub)
        {
            Toast(title, sub, ToastKind.Info);
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
            b.Height = Theme.ActionButtonHeight;
            b.FitToText(96);
            if (width > 0) b.Width = width;
            b.NaturalWidth = b.Width; // 记录原始宽度：窄窗口收缩后据此还原，避免累计变窄
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

        /// <summary>
        /// 操作按钮排列（从右向左）。
        /// 按钮总宽超出可用宽度时按比例收缩（保留下限），文字过长由按钮内部自动省略号处理。
        /// 不可无限左移：窄窗口下按钮会被排到负坐标，跑出可视区并压住页面标题。
        /// </summary>
        protected void LayoutActions()
        {
            if (_actions.Count == 0) return;

            const int rightPad = 26;      // 距右边缘
            const int titleReserve = 150; // 给页面标题预留的最小宽度，避免标题被完全挤没
            const int minButton = 58;     // 按钮收缩下限（仍能容纳图标）

            int gap = Theme.GapTight;
            int y = Math.Max(2, (EffectiveHeader - Theme.ActionButtonHeight) / 2);

            int[] want = new int[_actions.Count];
            int total = 0;
            for (int i = 0; i < _actions.Count; i++)
            {
                AccentButton b = _actions[i];
                want[i] = b.NaturalWidth > 0 ? b.NaturalWidth : b.Width;
                total += want[i];
            }

            int usable = _header.Width - rightPad - titleReserve - gap * (_actions.Count - 1);
            if (usable < 0) usable = 0;
            bool shrink = total > usable && total > 0;
            double ratio = shrink ? (double)usable / total : 1.0;

            int x = _header.Width - rightPad;
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                AccentButton b = _actions[i];
                int w = want[i];
                if (shrink)
                {
                    w = (int)Math.Floor(w * ratio);
                    if (w < minButton) w = minButton;
                }
                if (b.Width != w) b.Width = w;
                x -= w;
                b.Location = new Point(x, y);
                x -= gap;
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

            // 页头的"高科技"细节（纯叠加绘制，不改任何布局，因此所有页面一次性受益）：
            // ① 顶部一条从中间向两侧淡出的强调色光带；② 标题区一团低 alpha 柔光。
            // 分段插值而不是 LinearGradientBrush：与工程既有做法一致，且不引入新的绘制依赖。
            int hw = Math.Max(4, _header.Width);
            const int segs = 48;
            for (int i = 0; i < segs; i++)
            {
                int x0 = hw * i / segs;
                int x1 = hw * (i + 1) / segs;
                // 中间最亮、两端为 0：形成"光从中间打过来"的观感
                double t = 1.0 - Math.Abs((i + 0.5) / segs - 0.5) * 2.0;
                int alpha = (int)(t * 130);
                if (alpha <= 2) continue;
                g.FillRectangle(GdiCache.Brush(Gfx.Alpha(Theme.Accent, alpha)), x0, 0, x1 - x0, 2);
            }

            using (SolidBrush glow = new SolidBrush(Gfx.Alpha(Theme.Accent, 14)))
            {
                g.FillEllipse(glow, 4, -34, 300, 92);
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

        /// <summary>同上，使用标准区块间距（新页面推荐用这个重载，保持全站节奏一致）。</summary>
        protected static FlowLayoutPanel MakeRow(int height)
        {
            return MakeRowCore(height, Theme.GapSection, "row");
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

        /// <summary>同上，使用标准区块间距（新页面推荐用这个重载）。</summary>
        protected void AddFull(Control c, int height)
        {
            AddFull(c, height, Theme.GapSection);
        }

        protected void AddRow(FlowLayoutPanel row)
        {
            Body.Controls.Add(row);
            LayoutRows();
        }

        /// <summary>
        /// 把「右对齐的计数 / 状态标签」放进容器可视区内。
        ///
        /// 不可写成 <c>label.SetBounds(Math.Max(420, W - w - 4), ...)</c>：容器比 420 窄时
        /// x 被强行抬到 420，标签会被推到右边界之外、完全看不见。
        /// 这里用「默认右对齐 → 不压住左侧控件 → 不越出右边界」三级夹取，
        /// 空间实在不足时贴右边界，任何宽度下都可见。
        /// </summary>
        /// <param name="label">要摆放的标签</param>
        /// <param name="containerWidth">所在容器宽度</param>
        /// <param name="avoidLeft">左侧需避让的控件右边界（如搜索框 240 + 间距 8）</param>
        /// <param name="y">纵向位置</param>
        /// <param name="height">高度</param>
        protected static void LayoutRightLabel(Control label, int containerWidth, int avoidLeft, int y, int height)
        {
            if (label == null || containerWidth <= 0) return;

            int w = label.Width;
            int lx = containerWidth - w - 4;   // 默认：右对齐、右边留 4px
            if (lx < avoidLeft) lx = avoidLeft; // 不压住左侧控件
            int hi = containerWidth - w;        // 上限：不越出右边界
            if (lx > hi) lx = hi;
            if (lx < 0) lx = 0;
            label.SetBounds(lx, y, w, height);
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
            // 批量挂载收尾再对齐一次：挂载期间重排被抑制，先挂载的控件的最终高度
            // 与后挂载控件的坐标之间会留下错位（全部优化项页曾因此出现组头与首行 5px 重叠）。
            Body.PerformLayout();
        }

        private int _lastAvailWidth = -1;

        protected void LayoutRows()
        {
            LayoutRows(false);
        }

        /// <summary>
        /// force = true 时跳过「宽度未变」短路，强制重测每个行容器的高度并让 Body 重新堆叠。
        /// 内容更新（概览卡数据回填、筛选可见性变化等）会在行挂载之后才改变行高，
        /// 而短路分支不重测行高 → 其后已定位的行会停在过期坐标（实测被压上 5~19px）。
        /// </summary>
        private void LayoutRows(bool force)
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
                if (!force && avail > 0 && avail == _lastAvailWidth)
                {
                    for (int i = 0; i < Body.Controls.Count; i++)
                    {
                        Control c = Body.Controls[i];
                        if ((c.Tag as string) == "stretch" && c.Width != avail) c.Width = avail;
                    }
                    // 行的最终高度可能是上一次 LayoutRowChildren 才定下来的（如 row 高度取最高子控件），
                    // 而「高度变化」不会自动让 Body 重新堆叠 → 其后所有行会停留在过期坐标，
                    // 表现为行与行重叠（设置页曾出现 9px 重叠）。
                    // 这里显式重排一次：代价 O(子项数)，且只在挂载/改宽时发生。
                    Body.PerformLayout();
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

            int gap = Theme.GapInline;
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
