using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

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

            // 行的可见性 / 自适应高度在挂载后才变化（数据回填显示体检卡、提示条按宽换行），
            // FlowLayoutPanel 不会因此重排 → 后续行停在过期坐标（概览页提示条曾被下方卡片压住）。
            // 统一挂钩：任何子行可见性变化或 NoticeBar 高度变化都强制整栏重排。
            Body.ControlAdded += delegate(object s, ControlEventArgs e)
            {
                Control added = e.Control;
                // 非 force：宽度未变时走短路分支（补宽度 + PerformLayout + StackRows），
                // 足以把显示/变高的行落位，且比全量重排便宜得多（筛选 249 行时不抖）
                added.VisibleChanged += delegate { LayoutRows(); };
                NoticeBar nb = added as NoticeBar;
                if (nb != null) nb.PreferredHeightChanged += delegate { LayoutRows(); };
            };

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
            // 仅在页面可见时启用：否则每个页面各起一个常驻轮询空转（页面越多越明显）
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

            // 句柄就绪后补投「构造函数阶段被延迟的后台回调」（见 Post）
            HandleCreated += delegate { FlushDeferredPost(); };
        }

        public string TitleText
        {
            get { return Text; }
        }

        private bool _embedHeader;

        /// <summary>
        /// 嵌入模式：被"页签宿主"作为子页内嵌时，隐藏自身标题头、从容器顶部开始布局。
        /// 说明：当前的页签栏（<see cref="TabStrip"/>）做在壳层，页面之间不嵌套，
        /// 因此本开关暂时没有调用方；保留它是因为"页内嵌子页"的场景仍可能需要，
        /// 且横向内边距的对齐账（见下）只有这里记着。
        /// 外层页的 Body 已经提供 PagePadX 的左右内边距，子页若再留一份，
        /// 页签（父页 +30）与子页内容（+30+30=60）、子页按钮（+30+26=56）会各差一截、互不对齐。
        /// </summary>
        public bool EmbedHeader
        {
            get { return _embedHeader; }
            set
            {
                if (_embedHeader == value) return;
                _embedHeader = value;
                // 嵌入时左右内边距归零（由外层提供）；非嵌入时恢复页面标准内边距
                int padX = value ? 0 : Theme.PagePadX;
                Body.Padding = new Padding(padX, Theme.PagePadTop, padX, Theme.PagePadBottom);
                ApplyHeaderHeight();
                LayoutActions();
                RefreshLayout();
            }
        }

        /// <summary>嵌入模式下保留的操作按钮条高度（只隐藏标题，不隐藏操作按钮）。</summary>
        public const int ActionBarHeight = Theme.ActionBarHeight;

        /// <summary>
        /// 页头总高：标题区（HeaderHeight）+ 有操作按钮时的按钮行。
        /// 按钮行独立成行（自左缘排起），因此高度随"有没有按钮"变化；
        /// 内容区视口（ViewportHeight）由 ScrollHost 自动跟随，各页面无需改高度账。
        /// </summary>
        protected int EffectiveHeader
        {
            get
            {
                if (EmbedHeader) return ActionBarHeight;
                return HeaderHeight + (_actions.Count > 0 ? Theme.ActionButtonHeight + 8 : 0);
            }
        }

        protected int ViewportHeight
        {
            get { return _scroll.ViewportHeight; }
        }

        /// <summary>
        /// 页面滚动容器。滚轮路由在指针落在页头工具条等"滚动容器之外"的位置时回退到它，
        /// 否则那些位置上滚轮毫无反应（用户会以为滚轮失灵）。
        /// </summary>
        public ScrollHost Scroller
        {
            get { return _scroll; }
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

        private readonly List<Control> _cascControls = new List<Control>();
        private readonly List<int> _cascFrom = new List<int>();
        private readonly List<int> _cascDelta = new List<int>();
        private float _cascPos;

        /// <summary>
        /// 页面元素级联入场：首屏前 6 个元素错峰上移到位（约 220ms，EaseOutCubic）。
        /// 数据在后台加载的同时界面渐进呈现——「加载感」被动效吸收。
        /// </summary>
        /// <summary>
        /// 本页的入场动画由「切页」负责挂载时置为 1：让紧随其后的可见性回调不再重复挂一次，
        /// 否则同一次切页会挂两份级联（后者先还原再重新捕获坐标），快速连点切页时会抖一下。
        /// </summary>
        private int _enterOwnedBySwitch;

        /// <summary>供 MainForm 切页时调用：让新页内容做一次级联入场（与整页上浮叠加成"淡入上浮"）。</summary>
        public void PlayEnterAnimation()
        {
            _enterOwnedBySwitch = 1;
            AnimateContentIn();
        }

        protected void AnimateContentIn()
        {
            if (!AppSettings.Animations || Body == null || Body.Controls.Count < 2) return;

            if (_cascControls.Count > 0)
            {
                AnimationClock.Instance.Unsubscribe(CascTick);
                RestoreCasc();
            }

            _cascControls.Clear();
            _cascFrom.Clear();
            _cascDelta.Clear();
            int n = Math.Min(6, Body.Controls.Count);
            int vis = 0;
            for (int i = 0; i < n; i++)
            {
                Control c = Body.Controls[i];
                if (!c.Visible) continue; // 布局会跳过的行（如被关闭记忆隐藏、之后又被 LoadData
                                          // 强制显示的 NoticeBar）绝不能入场：其 Top 是过期值，
                                          // 动画每帧写回捕获坐标，会把 StackRows 的落位钉回 (·,0)
                                          // 压住别的行（概览页提示条实锤）。
                _cascControls.Add(c);
                _cascFrom.Add(c.Top);
                _cascDelta.Add(18 + vis * 5);
                c.Top = c.Top + _cascDelta[vis];
                vis++;
            }
            if (_cascControls.Count == 0) return;
            _cascPos = 0;

            Body.SuspendLayout(); // 动画期间挂起流式重排，防止手动位移被布局覆盖
            AnimationClock.Instance.Subscribe(CascTick);
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
            // 恢复的是动画开始时捕获的坐标；动画期间布局可能已变（行显示/隐藏、行高变化），
            // 这里按最新布局重排一次——布局对最终落位拥有决定权（非 force 即可，落位由 StackRows 保证）。
            LayoutRows();
        }

        private void CascTick()
        {
            _cascPos += Theme.Motion.CascStep;
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
                AnimationClock.Instance.Unsubscribe(CascTick);
            }
        }

        /// <summary>
        /// 对指定控件集合做级联入场（自上而下错峰上浮到位，约 200ms）。
        /// 用于「内容结构发生突变」的过渡：展开分组、切换分类、批量显示新行——
        /// 只动传进来的控件（通常是刚出现的行），不牵连页面其它区域；动画期间挂起流式重排，
        /// 防止手动位移被布局覆盖（与首屏入场同一套机制，故两处不会互相打架）。
        /// </summary>
        protected void CascadeIn(IList<Control> controls)
        {
            if (!AppSettings.Animations || Body == null || controls == null || controls.Count == 0) return;

            if (_cascControls.Count > 0)
            {
                AnimationClock.Instance.Unsubscribe(CascTick);
                RestoreCasc();
            }

            _cascControls.Clear();
            _cascFrom.Clear();
            _cascDelta.Clear();

            int vis = 0;
            for (int i = 0; i < controls.Count; i++)
            {
                Control c = controls[i];
                if (c == null || c.IsDisposed || !c.Visible) continue;
                _cascControls.Add(c);
                _cascFrom.Add(c.Top);
                _cascDelta.Add(10 + vis * 3);   // 错峰幅度随序号递增，形成"依次落位"的层次
                c.Top = c.Top + _cascDelta[vis];
                vis++;
                if (vis >= 12) break;           // 只动画前 12 个：再多会拖长首帧、收益递减
            }
            if (_cascControls.Count == 0) return;
            _cascPos = 0;

            Body.SuspendLayout();
            AnimationClock.Instance.Subscribe(CascTick);
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
                        // 本次切页已由 MainForm 调过 PlayEnterAnimation，这里不再重复挂载
                        if (_enterOwnedBySwitch > 0) _enterOwnedBySwitch--;
                        else AnimateContentIn();
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
                Gfx.EnableComposited(cp);
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
        /// 原先 20+ 个视图各自复制一份同名的私有 Post，现统一收在基类。
        ///
        /// 句柄尚未创建时**延迟投递**而不是丢弃：有些页面在构造函数里就发起后台加载
        /// （那时控件还没挂进窗体，句柄未创建），早期实现把回调直接丢弃，导致
        /// 「忙碌标记永远为真、页面一直转圈、再也加载不出来」（N卡设置 / 显卡伪装两页即此情况）。
        /// 现在先存进待投队列，句柄创建后自动补投一次。
        /// 句柄已销毁（窗口关闭）时仍然丢弃，不抛异常。
        /// </summary>
        // 不引入 System.Threading：它会与 System.Windows.Forms.Timer 争夺 Timer 这个名字
        protected bool Post(System.Threading.ThreadStart action)
        {
            if (action == null) return false;
            try
            {
                if (IsDisposed) return false;
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)delegate { action(); });
                    return true;
                }

                lock (_deferredPost) { _deferredPost.Add(action); }
                return true;
            }
            catch
            {
            }
            return false;
        }

        private readonly System.Collections.Generic.List<System.Threading.ThreadStart> _deferredPost =
            new System.Collections.Generic.List<System.Threading.ThreadStart>();

        /// <summary>句柄创建后补投此前被延迟的回调（见 <see cref="Post"/>）。</summary>
        private void FlushDeferredPost()
        {
            System.Collections.Generic.List<System.Threading.ThreadStart> pending;
            lock (_deferredPost)
            {
                if (_deferredPost.Count == 0) return;
                pending = new System.Collections.Generic.List<System.Threading.ThreadStart>(_deferredPost);
                _deferredPost.Clear();
            }
            for (int i = 0; i < pending.Count; i++) Post(pending[i]);
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
        /// 「统计条 + 内容」布局的公共前半段：摆好统计条高度，返回内容区可用高度。
        /// 统计条下方的形态各页不同（整张表格 / 两卡片均分 / 卡片列表），
        /// 故这里只收口高度账，不管内容怎么摆——各页按返回值自行分配。
        /// </summary>
        /// <param name="summary">顶部统计条</param>
        /// <param name="extraUsed">统计条之外还需占掉的额外高度（如搜索行 34+18）</param>
        /// <param name="minHeight">内容区最小高度</param>
        protected int LayoutSummary(StatStrip summary, int extraUsed, int minHeight)
        {
            int summaryH = summary.PreferredHeight;
            summary.Height = summaryH;
            Control row = summary.Parent;
            if (row != null) row.Height = summaryH;

            // 42 = 提示条高度，18 = 提示条与统计条各自的下方间距
            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18 + extraUsed;
            int avail = ViewportHeight - used;
            return avail < minHeight ? minHeight : avail;
        }

        /// <summary>
        /// 「统计条 + 表格」布局的标准高度重算（撑满剩余高度的表格页）。
        /// 原先多个页面逐字重复同一段逻辑（只在额外占位高度与最小高度上不同），现参数化收在此处。
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

            // 表格高度改为「实测表格上方的真实占位」：
            // 提示条高度各页不同（34/56/78/84…，多行提示的页面更高），原先按 42px 写死，
            // 多行提示条的页面表格会被撑出视口——最底一行被窗口边缘裁掉且滚不出来。
            // 实测宁可略短不可超长（表格偏短只会多出一点空白，超出则会永久挡住底行）。
            int top = 0;
            foreach (Control c in Body.Controls)
            {
                if (c == grid) break;
                int b = c.Top + c.Height;
                if (b > top) top = b;
            }
            if (grid.Top > top) top = grid.Top;   // 表格已定位时以实际位置为准（含行间距）
            if (top < Body.Padding.Top) top = Body.Padding.Top;

            int avail = ViewportHeight - top - Body.Padding.Bottom - extraUsed;
            ApplyGridHeight(grid, avail < minHeight ? minHeight : avail);
        }

        /// <summary>
        /// 「工具行 + 撑满剩余高度的表格」布局（页面没有统计条时的形态）：
        /// extraUsed 为页头/提示条/工具行等固定占位的总高。
        /// </summary>
        protected void LayoutFill(DarkGrid grid, int extraUsed, int minHeight)
        {
            int avail = ViewportHeight - (Body.Padding.Top + Body.Padding.Bottom + extraUsed);
            ApplyGridHeight(grid, avail < minHeight ? minHeight : avail);
        }

        /// <summary>表格高度落定 + 触发布局刷新（两种布局形态共用的收尾）。</summary>
        private void ApplyGridHeight(DarkGrid grid, int avail)
        {
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
                AnimationClock.Instance.Unsubscribe(CascTick);
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
            // 指定宽度只作为「期望宽度」，不得小于文字所需宽度：
            // 原来直接 b.Width = width，把 FitToText 算出的宽度覆盖掉了 ——
            // 于是「一键清理（推荐项）」这类文字较长的按钮指定 150 后放不下，
            // 被内部省略号截成「一键清理（推…」。这里取两者较大值。
            int need = b.NaturalWidth;
            b.Width = width > need ? width : need;
            b.NaturalWidth = b.Width; // 记录原始宽度：窄窗口收缩后据此还原，避免累计变窄
            if (onClick != null) b.Click += onClick;
            _actions.Add(b);
            _header.Controls.Add(b);
            ApplyHeaderHeight();   // 首个按钮会让页头长出一行（按钮在标题下方、自左缘排列）
            LayoutActions();
            return b;
        }

        /// <summary>
        /// 变更类操作前的管理员门禁：已提权返回 true（调用方继续执行）；
        /// 未提权时弹确认询问是否以管理员身份重启，同意则重启并退出当前进程，返回 false（调用方应中止）。
        /// 所有需要写 HKLM / 系统目录 / 服务 / 计划任务的页面动作都应先走这里，
        /// 以统一"权限不足"的提示与出口（而不是等操作失败再报一句原始错误）。
        /// </summary>
        protected bool EnsureElevated(string reason)
        {
            if (GuyueBox.Core.Native.IsElevated()) return true;

            bool go = Dialog.Confirm(this, "需要管理员权限",
                reason + "\r\n\r\n是否以管理员身份重新启动本程序？");
            if (!go) return false;

            if (GuyueBox.Core.Shell.RestartElevated("")) System.Windows.Forms.Application.Exit();
            else Dialog.Error(this, "提权失败", "未能以管理员身份启动，请右键程序选择「以管理员身份运行」后重试。");
            return false;
        }

        /// <summary>
        /// 操作按钮排列：在标题 / 副标题下方的独立一行，自左缘 26px 起向右依次排列。
        /// 原先是"贴右缘、从右往左"排，各页按钮位置随按钮数量漂移，且与本程序其它区域
        /// （工具行、工具栏按钮）一律左起的排版不统一；现统一为左起，间距固定 Theme.GapTight。
        /// 按钮总宽超出可用宽度时按比例收缩（保留下限），文字过长由按钮内部自动省略号处理。
        /// </summary>
        /// <summary>
        /// 页头操作按钮（按添加顺序）。页面做「忙碌时全禁用」这类批量控制时，
        /// 直接遍历它即可，不必为每个按钮各留一个字段。
        /// </summary>
        public System.Collections.Generic.IList<AccentButton> Actions { get { return _actions; } }

        protected void LayoutActions()
        {
            if (_actions.Count == 0) return;

            // 按钮条左缘必须与「Body 行内容的左缘」是同一条线：
            // ・非嵌入页：内容从 PagePadX 起排，原先固定 26 会比内容行左移 4px（上下按钮不对齐）；
            // ・嵌入子页：左右内边距已由外层页提供，这里从 0 起排，
            //   子页按钮才会与上方页签、下方内容行对齐。
            int leftPad = EmbedHeader ? 0 : Theme.PagePadX;
            int rightPad = leftPad;
            const int minButton = 58;     // 按钮收缩下限（仍能容纳图标）
            const int gap = Theme.GapTight;

            int y;
            if (EmbedHeader)
            {
                // 嵌入模式（页签宿主的内嵌子页）：只有按钮条，垂直居中
                y = Math.Max(2, (ActionBarHeight - Theme.ActionButtonHeight) / 2);
            }
            else
            {
                y = HeaderHeight + 4;
            }

            int[] want = new int[_actions.Count];
            int total = 0;
            for (int i = 0; i < _actions.Count; i++)
            {
                AccentButton b = _actions[i];
                want[i] = b.NaturalWidth > 0 ? b.NaturalWidth : b.Width;
                total += want[i];
            }

            int usable = _header.Width - leftPad - rightPad - gap * (_actions.Count - 1);
            if (usable < 0) usable = 0;
            bool shrink = total > usable && total > 0;
            double ratio = shrink ? (double)usable / total : 1.0;

            int x = leftPad;
            for (int i = 0; i < _actions.Count; i++)
            {
                AccentButton b = _actions[i];
                int w = want[i];
                if (shrink)
                {
                    w = (int)Math.Floor(w * ratio);
                    if (w < minButton) w = minButton;
                }
                if (b.Width != w) b.Width = w;
                b.Location = new Point(x, y);
                x += w + gap;
            }
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            SolidBrush b = GdiCache.Brush(Theme.WindowBg);
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

            SolidBrush glow = GdiCache.Brush(Gfx.Alpha(Theme.Accent, 14));
            {
                g.FillEllipse(glow, 4, -34, 300, 92);
            }

            // 嵌入模式（页签宿主的内嵌子页）：只画按钮条背景，不画标题/副标题（外层已有）
            if (EmbedHeader)
            {
                Pen sepPen = GdiCache.Pen(Theme.BorderSoft, 1f);
                {
                    g.DrawLine(sepPen, 0, ActionBarHeight - 1, _header.Width, ActionBarHeight - 1);
                }
                return;
            }

            // 按钮已移到标题下方的独立行（自左缘排起），标题 / 副标题可用整宽，无需再为按钮让位。
            // 左缘用 PagePadX：与按钮条、Body 行内容共用同一条线（原先写死 26，比内容行左移 4px）
            int padX = Theme.PagePadX;
            int textW = Math.Max(60, _header.Width - padX * 2);
            Gfx.DrawTextEllipsis(g, Text, Theme.FontTitle, Theme.TextPrimary,
                new Rectangle(padX, 18, textW, 30));

            if (!string.IsNullOrEmpty(_subtitle))
            {
                Gfx.DrawTextEllipsis(g, _subtitle, Theme.FontSmall, _subtitleColor,
                    new Rectangle(padX, 52, textW, 20));
            }

            Pen p = GdiCache.Pen(Theme.BorderSoft, 1f);
            {
                g.DrawLine(p, 0, EffectiveHeader - 1, _header.Width, EffectiveHeader - 1);
            }
        }

        // --------------------------------------------------------------
        // 布局
        // --------------------------------------------------------------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyHeaderHeight();
        }

        /// <summary>
        /// 按当前页头高度（标题区 + 按钮行）重排页头 / 内容区 / 加载遮罩。
        /// 页头高度会随"有没有操作按钮"变化，因此 OnResize 与 AddAction 都要调用它。
        /// </summary>
        protected void ApplyHeaderHeight()
        {
            if (_header == null) return;
            int hh = EffectiveHeader;
            _header.SetBounds(0, 0, ClientSize.Width, hh);
            _header.Visible = true; // 嵌入模式也显示按钮条（高度为 ActionBarHeight）
            if (_scroll != null)
            {
                _scroll.SetBounds(0, hh, ClientSize.Width, Math.Max(0, ClientSize.Height - hh));
            }
            if (_busyOverlay != null)
            {
                // 加载遮罩只覆盖内容滚动区，不再遮挡页头按钮
                _busyOverlay.SetBounds(0, hh, ClientSize.Width, Math.Max(0, ClientSize.Height - hh));
            }
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
                // 宽度还没定型（首次挂载常为 0）：此时排会把行内控件压到最小宽度并固化，
                // 之后即使变宽也回不去（实测聚合页里的多行输入框被压成小方块）。等拿到真实宽度再排。
                if (avail <= 0) return;
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
                    StackRows();
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
                    // 宽度变化会触发行内自适应控件（NoticeBar 按新宽度重测高度）改变行高；
                    // 与上方短路分支一致，这里也必须显式重排，否则其后行停在过期坐标（概览页提示条被卡片压住）。
                    Body.PerformLayout();
                    StackRows();
                }
            }
            finally
            {
                _laying = false;
            }

            _scroll.Relayout();
        }

        /// <summary>
        /// 显式自上而下堆叠 Body 的直接子行，不依赖 FlowLayoutPanel 的自动排版。
        /// 实测它对「挂载时不可见、之后才被显示」的子项（如被关闭记忆隐藏、又被 LoadData
        /// 强制显示的 NoticeBar）不重排，行会永远留在 (0,0) 压住首行——概览页实锤。
        /// 这里按 Padding + Margin 复刻堆叠规则，每行落位 deterministic，整类坐标 bug 一次终结。
        /// </summary>
        private void StackRows()
        {
            // SuspendLayout：逐行 SetBounds 每次都会触发 Body 的整体流式重排（O(N²)）；
            // 挂起后循环内只做纯赋值，收尾一次性恢复。249 行的优化中心实测差异显著。
            Body.SuspendLayout();
            try
            {
                int padL = Body.Padding.Left;
                int y = Body.Padding.Top;
                for (int i = 0; i < Body.Controls.Count; i++)
                {
                    Control c = Body.Controls[i];
                    if (!c.Visible) continue;
                    int cx = padL + c.Margin.Left;
                    int cy = y + c.Margin.Top;
                    c.SetBounds(cx, cy, c.Width, c.Height);
                    y = cy + c.Height + c.Margin.Bottom;
                }
            }
            finally
            {
                Body.ResumeLayout(false);
            }
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
                int totalN = 0;
                for (int i = 0; i < n; i++)
                {
                    Control c0 = row.Controls[i];
                    int h = c0.Height + c0.Margin.Vertical;
                    if (h > tallestN) tallestN = h;
                    totalN += c0.Width + c0.Margin.Horizontal;
                }
                if (autoHeight && tallestN > 0 && row.Height != tallestN) row.Height = tallestN;

                // 溢出保护：自然宽度行的总宽可能超出可视区（例如「自动释放 + 下拉框 + 阈值 + 说明文字」），
                // 超出部分会被窗口右缘直接裁掉——看不见也点不到。这里从右往左把放不下的文本标签
                // 转成「定宽 + 省略号」，保证整行内容都在可视区内。
                int availN = row.ClientSize.Width;
                if (availN > 0 && totalN > availN)
                {
                    for (int i = n - 1; i >= 0 && totalN > availN; i--)
                    {
                        Label lb = row.Controls[i] as Label;
                        if (lb == null || string.IsNullOrEmpty(lb.Text)) continue;
                        int others = 0;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i) continue;
                            others += row.Controls[j].Width + row.Controls[j].Margin.Horizontal;
                        }
                        int room = availN - others;
                        if (room < 60) room = 60;
                        if (lb.PreferredWidth > room)
                        {
                            lb.AutoSize = false;
                            lb.AutoEllipsis = true;
                            lb.Width = room;
                            totalN = others + room + lb.Margin.Horizontal;
                        }
                    }
                }
                return;
            }

            int gap = Theme.GapInline;
            int avail = row.ClientSize.Width;
            int totalGap = gap * (n - 1);

            // ① 文字类控件（标签 / 按钮）的宽度下限 = 文字实际所需宽度。
            //    等分本意是给「容器类」分配行宽，但文字控件被一并压到等宽后，
            //    长说明会被省略号截断（内存优化页的「超阈值时在本页提醒（0 = 关闭）」实测）。
            //    这里只把「剩余空间」分给非文字控件，文字控件保持自身宽度。
            int[] need = new int[n];
            int needSum = 0;
            int flexN = 0;
            for (int i = 0; i < n; i++)
            {
                Control ci = row.Controls[i];
                Label lb = ci as Label;
                AccentButton ab = ci as AccentButton;
                if (lb != null) need[i] = Math.Max(ci.Width, lb.PreferredWidth);
                else if (ab != null) need[i] = Math.Max(ci.Width, ab.NaturalWidth);
                // 输入框 / 下拉框：宽度是「用户输入区」的设计宽度，必须保持，不参与弹性分配。
                // 否则会被当成容器吸走整行剩余宽度——实测端口输入框从 130px 被拉到 333px，
                // 「自定义精度」等也一起变宽，整行看着全是输入框（"输入框太大/找不到"的根因）。
                else if (ci is ThemeInput)
                    need[i] = Math.Max(ci.Width, ((ThemeInput)ci).DesignWidth) + ci.Margin.Horizontal;
                else if (ci is TextBox || ci is ComboBox)
                    need[i] = ci.Width + ci.Margin.Horizontal;
                else need[i] = 0;

                if (need[i] > 0) needSum += need[i];
                else flexN++;
            }

            // ② 空间不足（文字总宽已超出，或弹性控件连最小宽度都分不到）时退回整体等分，
            //    宁可文字省略也不能让整行溢出到窗口外。
            int w = (avail - totalGap) / n;
            if (w < 80) w = 80;
            bool overflow = needSum + totalGap > avail;
            bool fallback = overflow || (flexN > 0 && avail - totalGap - needSum < 80 * flexN);

            int flexW = 0;
            if (flexN > 0)
            {
                flexW = (avail - totalGap - needSum) / flexN;
                if (flexW < 80) flexW = 80;
            }

            int tallest = 0;
            for (int i = 0; i < n; i++)
            {
                Control c = row.Controls[i];
                c.Width = fallback ? w : (need[i] > 0 ? need[i] : flexW);
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
