/* ============================================================
 * 文件说明：优化中心：全部注册表优化的浏览/搜索/筛选/开关执行页；行列表按分类懒加载，过滤走可见性切换（零重建）。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>分组标题：点击可折叠/展开该组。</summary>
    internal sealed class GroupHeader : Control
    {
        public string CountText = "";
        public Color AccentColor = Theme.Accent;
        public bool Collapsed;
        public event EventHandler CollapsedChanged;

        public GroupHeader(string title, Color accent)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            Text = title;
            AccentColor = accent;
            Height = 42;
            Margin = new Padding(2, 8, 0, 4);
            Tag = "stretch";
            Cursor = Cursors.Hand;
            A11y.MakeFocusable(this, AccessibleRole.PushButton);
            AccessibleName = (title == null ? "" : title) + "（可折叠）";
        }

        /// <summary>键盘可达：Enter / 空格折叠或展开该分组。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnClick(EventArgs.Empty);
                return;
            }
            if (e.KeyCode == Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnClick(EventArgs.Empty);
                return;
            }
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnClick(EventArgs e)
        {
            Collapsed = !Collapsed;
            Invalidate();
            EventHandler h = CollapsedChanged;
            if (h != null) h(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            using (SolidBrush b = new SolidBrush(Theme.WindowBg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            int y = Height - 16;
            using (GraphicsPath p = Gfx.RoundRect(new Rectangle(2, y - 6, 3, 15), 2))
            using (SolidBrush b = new SolidBrush(AccentColor))
            {
                g.FillPath(b, p);
            }

            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Theme.FontBodyBold, b, new Rectangle(14, y - 12, 300, 24), sf);
            }

            if (!string.IsNullOrEmpty(CountText))
            {
                using (SolidBrush b = new SolidBrush(Theme.TextMuted))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Alignment = StringAlignment.Far;
                    g.DrawString(CountText, Theme.FontSmall, b,
                        new Rectangle(Math.Max(10, Width - 240), y - 12, 190, 24), sf);
                }
            }

            // 右侧折叠箭头：展开朝下，折叠朝右
            Rectangle a = new Rectangle(Width - 26, (Height - 12) / 2, 12, 12);
            using (Pen p = new Pen(Theme.TextMuted, 1.5f))
            {
                if (Collapsed)
                {
                    g.DrawLine(p, a.X + 3, a.Y + 2, a.X + 7, a.Y + 6);
                    g.DrawLine(p, a.X + 7, a.Y + 6, a.X + 3, a.Y + 10);
                }
                else
                {
                    g.DrawLine(p, a.X + 1, a.Y + 4, a.X + 6, a.Y + 9);
                    g.DrawLine(p, a.X + 6, a.Y + 9, a.X + 11, a.Y + 4);
                }
            }

            if (Focused) A11y.DrawFocusRing(g, new Rectangle(0, 0, Width - 1, Height - 1), Theme.RadiusChip);
        }
    }

    /// <summary>
    /// 优化详情右栏（主从布局的「从」侧）：
    /// 点击左侧任意优化项后持续显示它的四段式说明与启用/停用操作，
    /// 取代「每点一次弹一次模态框」的查看方式——对比多行时不必反复开关弹窗。
    /// </summary>
    internal sealed class TweakDetailPanel : RoundPanel
    {
        private ITweak _tweak;
        private DetailInfo _info;
        private readonly AccentButton _action = new AccentButton();
        private readonly AccentButton _close = new AccentButton();
        private readonly OptimizeView _owner;
        private readonly Panel _scroll = new Panel();
        private readonly Label _title = new Label();
        private readonly Label _meta = new Label();
        private readonly Panel _divider = new Panel();
        private readonly Label _empty = new Label();
        private readonly Label[] _heads = new Label[5];
        private readonly Label[] _bodies = new Label[5];

        /// <summary>由页面注入：读取优化项当前是否已启用，保证右栏与左侧开关一致。</summary>
        public Func<ITweak, bool> StateReader;

        public TweakDetailPanel(OptimizeView owner)
        {
            _owner = owner;
            Radius = Theme.RadiusCard;
            CornerColor = Theme.WindowBg;   // 圆角外侧露出页面底色，四角自然融合

            // 滚动容器：承载全部说明，过长可滚动，绝不整片空白
            _scroll.Dock = DockStyle.Fill;
            _scroll.AutoScroll = true;
            _scroll.BackColor = Color.Transparent;
            _scroll.Padding = new Padding(16, 46, 14, 54);
            _scroll.Resize += delegate { LayoutContent(); };
            Controls.Add(_scroll);

            _title.AutoSize = true;
            _title.Font = Theme.FontBodyBold;
            _title.ForeColor = Theme.TextPrimary;
            _title.BackColor = Theme.CardBg;
            _scroll.Controls.Add(_title);

            _meta.AutoSize = true;
            _meta.Font = Theme.FontSmall;
            _meta.ForeColor = Theme.TextMuted;
            _meta.BackColor = Theme.CardBg;
            _scroll.Controls.Add(_meta);

            _divider.Height = 1;
            _divider.BackColor = Theme.BorderSoft;
            _scroll.Controls.Add(_divider);

            string[] labels = new string[] { "这是什么", "为什么会这样", "风险与影响", "是否可撤销", "怎么处理" };
            for (int i = 0; i < 5; i++)
            {
                Label h = new Label();
                h.AutoSize = true;
                h.Font = Theme.FontSmall;
                h.ForeColor = Theme.TextMuted;
                h.BackColor = Theme.CardBg;
                h.Text = labels[i];
                _scroll.Controls.Add(h);
                _heads[i] = h;

                Label b = new Label();
                b.AutoSize = true;
                b.Font = Theme.FontSmall;
                b.ForeColor = Theme.TextSecondary;
                b.BackColor = Theme.CardBg;
                b.UseMnemonic = false;
                _scroll.Controls.Add(b);
                _bodies[i] = b;
            }

            _empty.Text = "点击左侧任意优化项\n它的完整说明会显示在这里";
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Dock = DockStyle.Fill;
            _empty.ForeColor = Theme.TextMuted;
            _empty.Font = Theme.FontBody;
            _empty.BackColor = Color.Transparent;
            Controls.Add(_empty);

            _action.Height = 32;
            _action.Click += delegate
            {
                if (_tweak != null && _owner != null) _owner.ToggleFromRail(_tweak);
            };
            Controls.Add(_action);

            // 收起按钮（右上角 ×）：让详情栏可主动关闭，列表收回满宽，不再一直占着右侧
            _close.Text = "×";
            _close.Variant = ButtonVariant.Ghost;
            _close.Size = new Size(28, 28);
            _close.Click += delegate { if (_owner != null) _owner.CloseRail(); };
            Controls.Add(_close);

            // Z 序实证（本机实测）：先加入者在上层。_scroll 为 Dock=Fill 且最先加入、
            // 盖住整栏，后加入的 _action/_close 会被压在其下——启用/停用与 × 按钮
            // 不可见也不可点（"详情栏按键没反应"的直接原因），必须显式置顶。
            _close.BringToFront();
            _action.BringToFront();

            Resize += delegate { LayoutRailCtrls(); UpdateRegion(); };
            LayoutRailCtrls();
            UpdateRegion();
        }

        /// <summary>重排栏内浮层控件（关闭按钮固定右上角）。</summary>
        private void LayoutRailCtrls()
        {
            _close.Location = new Point(Width - 34, 10);
            if (_action != null) _action.Location = new Point(16, Math.Max(8, Height - 44));
        }

        /// <summary>用圆角 Region 裁剪整栏，确保内容被裁成圆角卡片（控件本身不透明，渲染可靠）。</summary>
        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            Region old = this.Region;
            this.Region = new Region(Gfx.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusCard));
            if (old != null) old.Dispose();
        }

        /// <summary>当前展示的优化项（null = 空态）。</summary>
        public ITweak Current { get { return _tweak; } }

        public bool IsEmpty { get { return _tweak == null; } }

        public void Show(ITweak t, DetailInfo info)
        {
            _tweak = t;
            _info = info;
            if (t == null || info == null)
            {
                _empty.Visible = true;
                _scroll.Visible = false;
                _action.Visible = false;
                return;
            }
            _empty.Visible = false;
            _scroll.Visible = true;

            DetailInfo d = info;
            _title.Text = string.IsNullOrEmpty(d.Title) ? t.Name : d.Title;

            bool applied = StateReader != null && StateReader(t);
            string meta = (applied ? "已启用" : "未启用")
                + (t.Risky ? " · 谨慎项" : " · 安全项")
                + (t.Recommended ? " · 推荐" : "");
            _meta.Text = meta;
            _meta.ForeColor = applied ? Theme.Success : Theme.TextMuted;

            string[] texts = new string[] { d.What, d.Why, d.Risk, d.Reversible, d.How };
            for (int i = 0; i < 5; i++)
            {
                bool emptySection = string.IsNullOrEmpty(texts[i]);
                _bodies[i].Text = emptySection ? "" : texts[i];
                _heads[i].Visible = !emptySection;
                _bodies[i].Visible = !emptySection;
            }
            _heads[2].ForeColor = d.RiskTone ? Theme.Warning : Theme.TextMuted;
            _heads[3].ForeColor = d.Irreversible ? Theme.Warning : Theme.Success;

            SyncAction();
            LayoutContent();
        }

        /// <summary>状态可能被行内开关、一键推荐等路径改变，同步一次按钮语义与文案。</summary>
        public void RefreshState()
        {
            if (_tweak != null && _info != null) Show(_tweak, _info);
        }

        private void SyncAction()
        {
            bool has = _tweak != null;
            _action.Visible = has && _scroll.Visible;
            if (!has) return;
            bool applied = StateReader != null && StateReader(_tweak);
            _action.Text = applied ? "停用此项" : "启用此项";
            _action.Variant = applied ? ButtonVariant.Ghost : ButtonVariant.Primary;
            _action.FitToText(96);
            _action.Location = new Point(16, Math.Max(8, Height - 44));
        }

        /// <summary>在滚动容器内自上而下排布标题 / 元信息 / 分隔线 / 五个分段。</summary>
        private void LayoutContent()
        {
            int w = _scroll.ClientSize.Width - _scroll.Padding.Left - _scroll.Padding.Right
                - SystemInformation.VerticalScrollBarWidth;
            if (w < 60) w = 60;
            int x = 0;
            int y = 0;

            _title.MaximumSize = new Size(w, 0);
            _title.Location = new Point(x, y);
            y += _title.Height + 6;

            _meta.MaximumSize = new Size(w, 0);
            _meta.Location = new Point(x, y);
            y += _meta.Height + 12;

            _divider.SetBounds(x, y, w, 1);
            y += 13;

            for (int i = 0; i < 5; i++)
            {
                if (!_heads[i].Visible) continue;
                _heads[i].Location = new Point(x, y);
                y += _heads[i].Height + 4;
                _bodies[i].MaximumSize = new Size(w, 0);
                _bodies[i].Location = new Point(x, y);
                y += _bodies[i].Height + 14;
            }
        }


    }

    /// <summary>单条优化项。</summary>
    internal sealed class TweakRow : RoundPanel
    {
        public readonly ITweak Tweak;

        private readonly BadgeLabel _badge = new BadgeLabel();
        private readonly BadgeLabel _rec = new BadgeLabel();
        private readonly ToggleSwitch _toggle = new ToggleSwitch();
        private readonly OptimizeView _owner;
        private bool _applied;
        private bool _suppressToggle;
        private bool _hover;
        private bool _selected;
        private readonly Color _defaultBorder;

        public TweakRow(ITweak tweak, OptimizeView owner)
        {
            Tweak = tweak;
            _owner = owner;

            BackColor = Theme.CardBg;
            _defaultBorder = BorderColor;
            Radius = Theme.RadiusCard;
            // 64 而非 72：单行更紧凑，同屏多出一行；信息密度靠"领域图标 + 名称 + 描述 + 标签"承担
            Height = 64;
            Margin = new Padding(0, 0, 0, 7);
            Tag = "stretch";

            // 关键修复：行继承自 BufferPanel，基类未开启 StandardClick，
            // 导致 WinForms 不对该控件派发 Click 事件——行上的
            // 「Click += 查看详情」永远不触发，表现就是"点了优化项右边没说明"。
            // 与 GroupHeader 一致，显式开启 StandardClick / StandardDoubleClick。
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);

            // 风险标签常显：风险档是这一行的固有属性（对齐设计 mod-tag），
            // 启用与否由右侧开关表达，不再拿同一个徽标兼表状态。
            _badge.Size = new Size(58, 20);
            _badge.Filled = false;
            _badge.Text = Tweak.Risky ? "谨慎" : "安全";
            _badge.BadgeColor = Tweak.Risky ? Theme.Warning : Theme.Success;
            Controls.Add(_badge);

            // 推荐标签：仅推荐项出现
            if (Tweak.Recommended)
            {
                _rec.Size = new Size(48, 20);
                _rec.Filled = false;
                _rec.Text = "推荐";
                _rec.BadgeColor = Theme.Accent;
                Controls.Add(_rec);
            }

            _toggle.Size = new Size(40, 22);
            _toggle.CheckedChanged += OnToggleChanged;
            Controls.Add(_toggle);

            // 悬停高亮：光标进入行时轻微提亮，指示可交互
            MouseEnter += delegate { _hover = true; BackColor = Theme.CardHover; Invalidate(); };
            MouseLeave += delegate { _hover = false; BackColor = Theme.CardBg; Invalidate(); };

            // 行点击 = 查看详情（开关自身会吃掉自己的点击，不会冲突）：
            // 优化项只有一句描述不足以让人决定是否启用，详情给出四段式解释
            Click += delegate
            {
                if (_owner != null) _owner.ShowTweakDetail(Tweak);
            };

            Resize += delegate { LayoutChildren(); };
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int mid = (Height - 22) / 2;
            _toggle.Location = new Point(Math.Max(10, Width - 62), mid);
            int riskX = Math.Max(10, Width - 128);
            _badge.Location = new Point(riskX, mid + 1);
            if (_rec.Parent != null) _rec.Location = new Point(Math.Max(10, riskX - 54), mid + 1);
        }

        /// <summary>分组 → 图标名。行首图标块让 234 行一眼分得清领域，而不是清一色的文字墙。</summary>
        private static string IconOfGroup(string group)
        {
            switch (group)
            {
                case "游戏优化": return "play";
                case "性能加速":
                case "极限性能": return "bolt";
                case "网络优化": return "globe";
                case "系统服务": return "services";
                case "电源与启动": return "power";
                case "隐私与安全": return "shield";
                case "系统精简": return "trash";
                case "外观与体验": return "feature";
                case "音频优化": return "list";
                case "扩展优化包": return "share";
                default: return "tune";
            }
        }

        private static Color ToneOfGroup(string group)
        {
            switch (group)
            {
                case "游戏优化": return Theme.Purple;
                case "性能加速":
                case "极限性能": return Theme.Cyan;
                case "网络优化": return Theme.Success;
                case "系统服务": return Theme.Warning;
                case "隐私与安全": return Theme.Accent;
                case "系统精简": return Theme.Danger;
                case "外观与体验": return Theme.Prism;
                case "音频优化": return Theme.Purple;
                default: return Theme.Accent;
            }
        }

        private void OnToggleChanged(object sender, EventArgs e)
        {
            if (_suppressToggle) return;
            _owner.ToggleTweak(this);
        }

        public bool IsApplied
        {
            get { return _applied; }
        }

        public void SetState(bool applied)
        {
            _applied = applied;

            _suppressToggle = true;
            _toggle.SetCheckedSilent(applied);
            _suppressToggle = false;

            // 风险标签常显（见构造函数注释）；启用状态完全由开关表达
            _badge.Visible = true;

            StartStateAnimation();
            Invalidate();
            if (_owner != null) _owner.OnRowStateChanged(this);
        }

        /// <summary>选中态：右栏正在显示本行时用强调色描边标记（与悬停提亮互不冲突）。</summary>
        public void SetSelected(bool on)
        {
            if (_selected == on) return;
            _selected = on;
            BorderColor = on ? Theme.Accent : _defaultBorder;
            Invalidate();
        }

        // 状态切换的背景色渐变：全部行共享一个全局动画时钟（单 Timer 驱动所有
        // 动画中的行），避免 157 个行各自持 Timer——快速连点时动画互不踩踏。
        private static System.Windows.Forms.Timer _animClock;
        private static readonly List<TweakRow> _animating = new List<TweakRow>();
        private int _animStep = 10;
        private bool _animFrom;

        private static void EnsureAnimClock()
        {
            if (_animClock != null) return;
            _animClock = new System.Windows.Forms.Timer();
            _animClock.Interval = 15;
            _animClock.Tick += delegate
            {
                for (int i = _animating.Count - 1; i >= 0; i--)
                {
                    TweakRow r = _animating[i];
                    r._animStep += 1;
                    if (r._animStep >= 10)
                    {
                        r._animStep = 10;
                        _animating.Remove(r);
                    }
                    r.Invalidate();
                }
                if (_animating.Count == 0) _animClock.Stop();
            };
        }

        private void StartStateAnimation()
        {
            // 批量场景（方案同步/一键推荐会同时更新大量行）跳过逐行动画，
            // 避免每帧上百次行重绘引发闪烁；动画只保留给单行操作的反馈。
            if (!AppSettings.Animations || _animating.Count > 24)
            {
                _animStep = 10;
                Invalidate();
                return;
            }
            _animFrom = !_applied;
            _animStep = 0;
            EnsureAnimClock();
            if (!_animating.Contains(this)) _animating.Add(this);
            _animClock.Start();
        }

        private float AnimProgress()
        {
            if (_animStep >= 10) return 1f;
            float t = _animStep / 10f;
            // EaseOutCubic
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private Color AnimBackColor()
        {
            // 同描边：只保留"变绿"方向的背景渐变；关闭方向直接回到常态底色（不闪绿）
            Color off = Theme.CardBg;
            Color on = Gfx.Blend(Theme.CardBg, Theme.Success, 0.06);
            float t = AnimProgress();
            return _applied ? Gfx.Blend(off, on, t) : off;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            BackColor = AnimBackColor();
            float t = AnimProgress();
            // 描边动画只保留"变绿"方向（开启开关的成就反馈）；
            // 关闭方向恒为普通描边——旧公式 blend(绿,灰,t) 首帧是纯绿，
            // 每个未启用行初次绘制都会"闪一下绿"（用户可见的渲染缺陷）。
            Color baseBorder = _applied
                ? Gfx.Blend(Theme.GlassBorder, Gfx.Alpha(Theme.Success, 110), t)
                : Theme.GlassBorder;
            // 悬停描边发亮：指示"整行可点"（详情入口），而不是只有开关能点
            BorderColor = _hover && !_applied ? Gfx.Alpha(Theme.Accent, 120) : baseBorder;
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 左侧状态条
            using (GraphicsPath p = Gfx.RoundRect(new Rectangle(0, 14, 3, Height - 28), 2))
            using (SolidBrush b = new SolidBrush(_applied ? Theme.Success : Theme.Border))
            {
                g.FillPath(b, p);
            }

            // 行首领域图标块：按分组着色。234 行若都是"文字 + 胶囊 + 开关"，
            // 扫视时找不到落点；有了颜色与图形，领域可以一眼分辨。
            string group = Tweak.Group == null ? "" : Tweak.Group;
            Color tone = ToneOfGroup(group);
            Rectangle iconBox = new Rectangle(14, (Height - 30) / 2, 30, 30);
            Gfx.FillRound(g, iconBox, Theme.RadiusChip, Gfx.Alpha(tone, _applied ? 64 : 32));
            IconPainter.Draw(g, IconOfGroup(group),
                new Rectangle(iconBox.X + 9, iconBox.Y + 9, 12, 12),
                _applied ? tone : Gfx.Blend(Theme.TextMuted, tone, 0.55));

            // 文本区右边界与右侧「标签组 + 开关」联动避让：推荐标签存在时要再让出一格
            int leftmost = _badge.Left;
            if (_rec.Parent != null && _rec.Left < leftmost) leftmost = _rec.Left;
            int textRight = Math.Max(60, leftmost - 16);
            int textX = iconBox.Right + 10;

            Gfx.DrawTextEllipsis(g, Tweak.Name, Theme.FontBodyBold, Theme.TextPrimary,
                new Rectangle(textX, 9, textRight - textX, 22));

            // 悬停时把描述行换成操作提示：行点击 = 打开四段式详情。
            // 不提示的话用户不会知道行本身可点（页面上显式的可交互控件只有开关）。
            if (_hover)
            {
                Gfx.DrawTextEllipsis(g, "点击查看详情：这是什么 / 为什么 / 怎么处理 / 风险与可撤销",
                    Theme.FontSmall, Theme.Accent, new Rectangle(textX, 31, textRight - textX, 18));
            }
            else
            {
                Gfx.DrawTextEllipsis(g, Tweak.Description, Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(textX, 31, textRight - textX, 18));
            }
        }
    }

    public class OptimizeView : ViewBase
    {
        private readonly List<ITweak> _tweaks = new List<ITweak>();
        private readonly List<TweakRow> _rows = new List<TweakRow>();
        /// <summary>设计模块页的圆环健康卡：同时表达已启用比例与风险分布，比扁平统计条信息量更大。</summary>
        private readonly ModuleHealthCard _health = new ModuleHealthCard();
        private readonly NoticeBar _notice = new NoticeBar();
        /// <summary>右侧详情栏（主从布局的「从」侧），构造函数中创建。</summary>
        private readonly TweakDetailPanel _rail;
        /// <summary>右栏宽度；列表展开时通过加宽 Body 右内边距为它让位。</summary>
        private const int RailWidth = 300;

        /// <summary>右栏动画进度：0=完全收起 1=完全展开；点击优化项后由 0 滑到 1（宇奇式滑动浮现）。</summary>
        private double _railAnim;
        private bool _railOpening;
        private readonly System.Windows.Forms.Timer _railAnimTimer = new System.Windows.Forms.Timer();

        private readonly Dictionary<string, bool> _states = new Dictionary<string, bool>();
        private readonly Panel _toolbar = new Panel();
        private readonly FlowLayoutPanel _chips = new FlowLayoutPanel();
        private readonly List<AccentButton> _chipButtons = new List<AccentButton>();
        /// <summary>当前分类包含的底层分组集合（null = 全部）。</summary>
        private string[] _extraGroups;

        private bool _busy;
        private bool _loaded;
        private string _stateFilter;
        private readonly TextBox _search = new TextBox();
        private string _keyword = "";
        private readonly FlowLayoutPanel _filterRow = new FlowLayoutPanel();
        private readonly List<AccentButton> _stateChips = new List<AccentButton>();
        private readonly FlowLayoutPanel _profileRow = new FlowLayoutPanel();
        private readonly FlowLayoutPanel _profileChips = new FlowLayoutPanel();
        private string _groupFilter;
        private readonly Dictionary<string, bool> _collapsedGroups = new Dictionary<string, bool>();

        /// <summary>外部（磁贴/快捷方式）希望进入页面时直接选中的分类，进入后消费一次。</summary>
        public static string PendingGroup;

        /// <summary>选中某个优化项：右栏滑动浮现显示详情、对应行加选中态（取代模态弹窗）。</summary>
        internal void ShowTweakDetail(ITweak t)
        {
            if (t == null) return;
            DetailInfo info;
            try
            {
                info = TweakDetails.Build(t);
            }
            catch (Exception ex)
            {
                try
                {
                    using (System.IO.StreamWriter sw = System.IO.File.AppendText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "rail_diag.log")))
                    {
                        sw.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  [ShowTweakDetail] Build 抛异常，tweak=" + (t == null ? "null" : t.Id));
                        sw.WriteLine(ex.ToString());
                    }
                }
                catch { }
                // 兜底：推导失败也必须给出非空内容，否则右栏会变成空白黑块（用户感知为"黑块没反应"）
                info = new DetailInfo();
                string grp = string.IsNullOrEmpty(t.Group) ? TweakPackProvider.GPack : t.Group;
                info.Title = t.Name + "（" + grp + "）";
                info.What = string.IsNullOrEmpty(t.Description) ? t.Name : t.Description;
                info.Why = "（详细推导暂时不可用，已回退为基础说明）";
                info.Risk = t.Risky ? "此项标记为「谨慎」，请先单项启用确认无异常。" : "此项属于普遍适用的安全调整。";
                info.Reversible = "可撤销：关闭开关即可还原到系统默认状态。";
            }
            _rail.Show(t, info);
            OpenRail();   // 点击即让右栏滑出（已展开时也安全：仅确保展开态）
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(_rows[i].Tweak == t);
        }

        /// <summary>右栏按钮触发的开关：定位到对应行后复用行级 ToggleTweak（含危险确认与备份链）。</summary>
        internal void ToggleFromRail(ITweak t)
        {
            if (t == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Tweak == t)
                {
                    ToggleTweak(_rows[i]);
                    _rail.RefreshState();
                    return;
                }
            }
        }

        /// <summary>行状态变化后同步右栏按钮语义（右栏可能正显示该项）。</summary>
        internal void OnRowStateChanged(TweakRow row)
        {
            if (_rail != null && !_rail.IsEmpty && _rail.Current == row.Tweak) _rail.RefreshState();
        }

        public OptimizeView()
            : this("优化中心", "全部优化项：游戏 / 网络 / 性能 / 电源 / 隐私 / 精简 / 外观 / 服务，逐项开关随时还原", null)
        {
        }

        /// <summary>groupFilter 非空时只显示该分组的优化项（如「游戏优化」页）。</summary>
        public OptimizeView(string title, string subtitle, string groupFilter)
            : base(title, subtitle)
        {
            _groupFilter = groupFilter;
            // 固定分组模式：必须同时设 _extraGroups 才会真正过滤——FilterByGroups 只在
            // _extraGroups 非空时生效，只设 _groupFilter 的话该参数形同虚设。
            bool fixedGroup = !string.IsNullOrEmpty(groupFilter);
            _extraGroups = fixedGroup ? new string[] { groupFilter } : null;

            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            // 硬件感知提示：告诉用户本机 CPU/GPU，专属项只对本机硬件生效
            _notice.NoticeText = "每一项优化都会自动备份修改前的注册表内容，关闭开关即可还原到系统默认状态。" +
                BuildHardwareHint();

            AddAction("一键推荐优化", "bolt", ButtonVariant.Primary, OnRecommendedClick, 152);
            AddAction("全部还原", "refresh", ButtonVariant.Ghost, OnRestoreAllClick, 110);
            if (!fixedGroup)
            {
                // 方案库与方案文件都是跨分类功能：固定分组页里放它们会误导（保存出来的是残缺方案）
                AddAction("导出方案", "doc", ButtonVariant.Ghost, OnExportProfile, 110);
                AddAction("导入方案", "plus", ButtonVariant.Secondary, OnImportProfile, 110);
            }
            AddAction("刷新状态", "refresh", ButtonVariant.Secondary, delegate { Load(true); }, 110);

            AddFull(_notice, 30, 8);

            // 页头压缩：健康卡与状态筛选并成一行（各占一半）。
            // 优化中心的可用性直接由"首屏能看到几行"决定，每省一行竖高就多露一整行优化项。
            BuildToolbar();
            FlowLayoutPanel row = MakeRow(0, 8);
            _filterRow.Margin = new Padding(0);
            row.Controls.Add(_health);
            row.Controls.Add(_filterRow);
            AddRow(row);

            // 固定分组页 = 设计式「模块页」：只保留状态筛选，不再出现主分类切换与方案库
            if (!fixedGroup) AddFull(_toolbar, 30, 6);
            if (!fixedGroup) AddFull(_profileRow, 30, 8);
            AddExtraControls();
            Relayout();

            // 主从布局：详情栏默认收起（列表满宽、不挡 UI），点击优化项后从右缘滑动浮现。
            // 展开时列表临时加宽右内边距让位，栏滑入填满预留区；收起后列表收回满宽。
            _rail = new TweakDetailPanel(this);
            _rail.StateReader = delegate (ITweak t)
            {
                bool applied = false;
                _states.TryGetValue(t.Id, out applied);
                return applied;
            };
            _rail.Visible = false;
            Controls.Add(_rail);
            // Z 序实证（本机实测）：先加入者在其中在上层，BringToFront 即提到最前。
            // _scroll 在基类构造中最先加入、位于 _rail 之上，若不显式置顶，
            // 详情栏会被整宽内容面板盖住——不可见也不可点，用户感知为"按了没反应"。
            _rail.BringToFront();
            Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop, Theme.PagePadX, Theme.PagePadBottom);
            _railAnimTimer.Interval = 16;
            _railAnimTimer.Tick += delegate { RailAnimTick(); };
            Resize += delegate { PositionRail(); };
            PositionRail();
        }

        /// <summary>把详情栏钉到视口右侧（页头之下、随窗口缩放跟随）。
        /// Z 序实证（本机实测）：先加入者在上层，BringToFront 即提到最前；
        /// _rail 已在构造中加入后显式置顶，这里无需再动 z 序。</summary>
        private void PositionRail()
        {
            int hh = EmbedHeader ? ActionBarHeight : HeaderHeight;
            int y = hh + Theme.PagePadTop;
            int h = Math.Max(0, ClientSize.Height - y - Theme.PagePadBottom);
            int finalX = Math.Max(0, ClientSize.Width - Theme.PagePadX - RailWidth);
            // 收起进度越高越靠右推：_railAnim=0 时整栏在视口外，滑到 1 时归位
            int x = finalX + (int)((1 - _railAnim) * (RailWidth + 16));
            _rail.SetBounds(x, y, RailWidth, h);
        }

        /// <summary>点击优化项：列表让出右侧空间，详情栏从右缘滑动浮现（宇奇式主从交互）。</summary>
        private void OpenRail()
        {
            _rail.Visible = true;
            _railOpening = true;
            Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop,
                Theme.PagePadX + RailWidth + 10, Theme.PagePadBottom);
            _railAnimTimer.Start();
        }

        /// <summary>收起详情栏：滑出右缘后隐藏，列表收回满宽。</summary>
        internal void CloseRail()
        {
            if (_railAnim <= 0) { _rail.Visible = false; return; }
            _railOpening = false;
            _railAnimTimer.Start();
        }

        /// <summary>每帧推进右栏滑入/滑出动画，匀速逼近目标，手感顺滑。</summary>
        private void RailAnimTick()
        {
            double step = 0.16;
            _railAnim += _railOpening ? step : -step;
            if (_railAnim >= 1) { _railAnim = 1; _railAnimTimer.Stop(); }
            if (_railAnim <= 0)
            {
                _railAnim = 0;
                _railAnimTimer.Stop();
                _rail.Visible = false;
                Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop, Theme.PagePadX, Theme.PagePadBottom);
                PositionRail();
                return;
            }
            PositionRail();
        }

        /// <summary>扩展点：在列表上方追加本页专属控件。</summary>
        protected virtual void AddExtraControls()
        {
        }

        private void BuildToolbar()
        {
            _toolbar.BackColor = Theme.WindowBg;
            _toolbar.Height = 34;

            // 状态筛选 + 方案库行：筛选行允许换行（WrapContents），
            // 否则 5 个状态 chip + 搜索框在半宽行里会被裁掉最后一个「谨慎」chip
            _filterRow.FlowDirection = FlowDirection.LeftToRight;
            _filterRow.WrapContents = true;
            _filterRow.AutoSize = true;
            _filterRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _filterRow.BackColor = Theme.WindowBg;

            string[] stateChips = new string[] { "全部状态", "已启用", "未启用", "推荐", "谨慎" };
            string[] stateKeys = new string[] { null, "on", "off", "rec", "risky" };
            for (int i = 0; i < stateChips.Length; i++)
            {
                string key = stateKeys[i];
                AccentButton chip = new AccentButton();
                chip.Text = stateChips[i];
                chip.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                chip.Click += delegate
                {
                    _stateFilter = key;
                    for (int k = 0; k < _stateChips.Count; k++)
                    {
                        _stateChips[k].Variant = _stateChips[k] == chip
                            ? ButtonVariant.Primary : ButtonVariant.Ghost;
                    }
                    ApplyFilters(); // 只切可见性，不重建
                };
                _stateChips.Add(chip);
                _filterRow.Controls.Add(chip);
            }

            // 关键词搜索：242 个优化项光靠分类 / 状态筛选不够，
            // 按名称 / 说明 / 标识实时过滤（只切可见性，不重建行）
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Theme.FontBody;
            _search.Size = new Size(200, 28);
            Native.SetCue(_search, "搜索优化项…");
            _search.Margin = new Padding(8, 0, 0, 0);
            _search.TextChanged += delegate
            {
                _keyword = _search.Text.Trim();
                ApplyFilters();
            };
            _filterRow.Controls.Add(_search);

            // 方案行：独立一行，避免与状态筛选挤在一行被裁剪
            _profileRow.FlowDirection = FlowDirection.LeftToRight;
            _profileRow.WrapContents = false;
            _profileRow.BackColor = Theme.WindowBg;
            _profileRow.Height = 34;
            _profileRow.Tag = "stretch";

            AccentButton saveProfile = new AccentButton();
            saveProfile.Text = "＋保存方案";
            saveProfile.Variant = ButtonVariant.Secondary;
            saveProfile.Height = 28;
            saveProfile.FitToText(72);
            saveProfile.Margin = new Padding(0, 0, 8, 0);
            saveProfile.Click += OnSaveProfileClick;
            _profileRow.Controls.Add(saveProfile);

            BuildProfileChips();
            _profileRow.Controls.Add(_profileChips);

            // 搜索框已移除：分类直接可见可点，无需搜索
            _toolbar.Controls.Add(_chips);

            // 分类筛选条：收敛为 5 个主要分类（对应底层分组聚合）
            _chips.FlowDirection = FlowDirection.LeftToRight;
            _chips.WrapContents = false;
            _chips.BackColor = Theme.WindowBg;
            _chips.Height = 34;

            for (int i = 0; i < CategoryNames.Length; i++)
            {
                AccentButton chip = new AccentButton();
                chip.Text = CategoryNames[i];
                chip.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                int idx = i; // 闭包按索引固定
                chip.Click += delegate { ApplyCategory(idx); };
                _chipButtons.Add(chip);
                _chips.Controls.Add(chip);
            }

            _toolbar.Resize += delegate
            {
                _chips.SetBounds(0, 0, Math.Max(60, _toolbar.Width), 34);
            };
            _chips.SetBounds(0, 0, Math.Max(60, _toolbar.Width), 34);
        }

        /// <summary>主分类名与对应的底层分组集合（索引 0 = 全部）。</summary>
        private static readonly string[] CategoryNames = new string[]
        {
            "全部", "游戏", "系统性能", "网络", "隐私与精简"
        };

        private static readonly string[][] CategoryGroups = new string[][]
        {
            null,
            new string[] { TweakLibrary.GGame },
            new string[] { TweakLibrary.GPerformance, TweakLibrary.GPower, TweakLibrary.GServices, TweakLibrary.GAudio },
            new string[] { TweakLibrary.GNetwork },
            new string[] { TweakLibrary.GPrivacy, TweakLibrary.GSlim, TweakLibrary.GAppearance }
        };

        /// <summary>切换到指定主分类：可打断在途探测（旧结果按会话令牌作废），立即切换。</summary>
        private void ApplyCategory(int index)
        {
            if (index < 0 || index >= CategoryNames.Length) return;
            _groupFilter = CategoryGroups[index] == null ? null : CategoryGroups[index][0];
            _extraGroups = CategoryGroups[index];
            for (int k = 0; k < _chipButtons.Count; k++)
            {
                _chipButtons[k].Variant = k == index
                    ? ButtonVariant.Primary : ButtonVariant.Ghost;
                _chipButtons[k].Invalidate();
            }
            _loadGen++;      // 在途探测结果全部作废
            _busy = false;   // 允许新会话立即开始（点多快都能立即响应）
            _loaded = false;
            _tweaks.Clear();
            _states.Clear();
            ClearRows();
            Load(false);
        }

        // 注意：本页刻意不 override IsBusy——状态探测期间行列表已即时渲染，
        // 若显示全屏遮罩反而挡住内容；进度通过页头副标题反馈。

        /// <summary>按当前分类的分组集合过滤（_extraGroups 为 null 表示全部）。</summary>
        private List<ITweak> FilterByGroups(List<ITweak> source)
        {
            if (_extraGroups == null) return new List<ITweak>(source);
            List<ITweak> result = new List<ITweak>();
            for (int i = 0; i < source.Count; i++)
            {
                for (int k = 0; k < _extraGroups.Length; k++)
                {
                    if (source[i].Group == _extraGroups[k])
                    {
                        result.Add(source[i]);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>本机 CPU/GPU 摘要，让用户清楚哪些厂商专属项适用于自己。</summary>
        private static string BuildHardwareHint()
        {
            string cpu = CpuVendor.IsIntel ? "Intel CPU" : (CpuVendor.IsAmd ? "AMD CPU" : "CPU");
            string gpu = GpuLatencyTweak.DetectVendor() == "N" ? "NVIDIA 显卡"
                : GpuLatencyTweak.DetectVendor() == "A" ? "AMD 显卡"
                : GpuLatencyTweak.DetectVendor() == "I" ? "Intel 核显" : "未知显卡";
            return "（本机：" + cpu + " + " + gpu + "，不适用本机硬件的专属项会自动不可用）";
        }

        private void Relayout()
        {
            // 健康卡高度固定 ⇒ 行高固定，不再随数据变化（也就不会在挂载后触发重排）
            Control row = _health.Parent;
            if (row != null) row.Height = _health.Height;
            RefreshLayout();
        }

        public override void OnActivated()
        {
            // 磁贴带入的分组（如「游戏优化」）：映射到对应主分类并选中，只消费一次
            if (!string.IsNullOrEmpty(PendingGroup) && _chipButtons.Count > 0)
            {
                int hit = -1;
                for (int i = 0; i < _chipButtons.Count; i++)
                {
                    if (_chipButtons[i].Text == PendingGroup) { hit = i; break; }
                }
                if (hit < 0)
                {
                    // 按主分类归属查找：直接复用 CategoryGroups（单一数据源），
                    // 避免手写 maps 与分类数量脱节导致索引越界崩溃
                    for (int i = 0; i < CategoryGroups.Length && hit < 0; i++)
                    {
                        string[] groups = CategoryGroups[i];
                        if (groups == null) continue;
                        for (int k = 0; k < groups.Length; k++)
                        {
                            if (groups[k] == PendingGroup) { hit = i; break; }
                        }
                    }
                }
                if (hit >= 0)
                {
                    ApplyCategory(hit);
                }
                PendingGroup = null;
                // 分组变了必须重载数据：清空现有列表触发立即重建 + 后台探测
                if (_loaded)
                {
                    _loaded = false;
                    _tweaks.Clear();
                    _states.Clear();
                    ClearRows();
                }
            }
            if (!_loaded) Load(false);
        }

        public override void OnDeactivated()
        {
            if (_rows.Count > 0) CheckOverflow();
        }

        /// <summary>把超出内容区右侧的分组标题排好（分组标题宽度依赖内容区宽度）。</summary>
        private void CheckOverflow()
        {
            LayoutRows();
        }

        // --------------------------------------------------------------

        private int _loadGen;

        private void Load(bool force)
        {
            // 强制重载（刷新按钮）同样可打断在途会话；普通 Load 在忙时跳过
            if (_busy && !force) return;
            _busy = false;
            _busy = true;
            _loadGen++;                // 会话令牌：期间发生的任何重载使旧探测结果作废
            int gen = _loadGen;
            SetSubtitle("正在读取当前优化状态…", Theme.Warning);

            if (force) ClearRows();

            // 关键体验修复：立即渲染完整列表（状态先用「未启用」占位），
            // 后台探测完成后逐行回填真实状态——点进去马上能看到全部优化项，
            // 不再是几秒空白、要切走切回才出现。
            if (_tweaks.Count == 0)
            {
                List<ITweak> all = FilterByGroups(TweakLibrary.All());
                _tweaks.Clear();
                _tweaks.AddRange(all);
                _states.Clear();
                for (int i = 0; i < _tweaks.Count; i++) _states[_tweaks[i].Id] = false;
                BuildRows();
                UpdateSummary();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ITweak> all = null;
                Dictionary<string, bool> states = new Dictionary<string, bool>();
                string error = null;
                try
                {
                    all = FilterByGroups(TweakLibrary.All());
                    // 并行探测：171 项的注册表读在多核上同时进行，等待时间缩短数倍
                    object sync = new object();
                    System.Threading.Tasks.Parallel.For(0, all.Count, delegate(int i)
                    {
                        bool applied = false;
                        try { applied = all[i].IsApplied(); }
                        catch { }
                        lock (sync) { states[all[i].Id] = applied; }
                    });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    if (gen != _loadGen) return; // 已切换分类/重载：丢弃过期探测结果
                    _busy = false;
                    if (error != null)
                    {
                        SetSubtitle("读取失败：" + error, Theme.Danger);
                        return;
                    }

                    _states.Clear();
                    foreach (KeyValuePair<string, bool> kv in states) _states[kv.Key] = kv.Value;

                    // 逐行回填真实状态（不重建行，保持滚动位置）
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        bool applied;
                        if (_states.TryGetValue(_rows[i].Tweak.Id, out applied)) _rows[i].SetState(applied);
                    }
                    UpdateSummary();
                    _loaded = true;
                    SetSubtitle("共 " + _tweaks.Count + " 项优化，已启用 " + CountApplied() + " 项。",
                        Theme.TextSecondary);
                });
            });
        }

        private void ClearRows()
        {
            Body.SuspendLayout();
            List<Control> remove = new List<Control>();
            for (int i = 0; i < Body.Controls.Count; i++)
            {
                if (Body.Controls[i] is TweakRow || Body.Controls[i] is GroupHeader)
                    remove.Add(Body.Controls[i]);
            }
            for (int i = 0; i < remove.Count; i++)
            {
                Body.Controls.Remove(remove[i]);
                remove[i].Dispose();
            }
            _rows.Clear();
            Body.ResumeLayout(false);
        }

        private void BuildRows()
        {
            Body.SuspendLayout();
            ClearRows();
            // 批量挂载：抑制每行触发的全表重排（否则 N 行 = O(N²) 卡顿）
            SuspendRowLayout();
            try
            {
                BuildRowsInner();
            }
            finally
            {
                Body.ResumeLayout(false);
                ResumeRowLayout();
            }
        }

        private void BuildRowsInner()
        {
            // (由 BuildRows 包裹：Suspend → 挂载 → Resume 单次重排)

            string currentGroup = null;
            GroupHeader header = null;
            bool collapsed = false;
            int groupCount = 0;

            for (int i = 0; i < _tweaks.Count; i++)
            {
                ITweak t = _tweaks[i];
                if (t.Group != currentGroup)
                {
                    if (header != null) header.CountText = groupCount + " 项";
                    currentGroup = t.Group;
                    groupCount = 0;
                    header = new GroupHeader(currentGroup, GroupColor(currentGroup));
                    bool wasCollapsed;
                    _collapsedGroups.TryGetValue(currentGroup, out wasCollapsed);
                    header.Collapsed = wasCollapsed;
                    GroupHeader thisHeader = header; // 闭包按组固定，避免共享变量
                    header.CollapsedChanged += delegate
                    {
                        _collapsedGroups[thisHeader.Text] = thisHeader.Collapsed;
                        BuildRows();
                    };
                    Body.Controls.Add(header);
                    collapsed = wasCollapsed;
                }

                groupCount++;
                if (collapsed) continue; // 折叠：只计数量，不建行

                TweakRow row = new TweakRow(t, this);
                bool applied = false;
                _states.TryGetValue(t.Id, out applied);
                row.SetState(applied);
                _rows.Add(row);
                Body.Controls.Add(row);
            }

            if (header != null) header.CountText = groupCount + " 项";

            ApplyFilters();
            RefreshLayout();

            // 右栏默认收起（列表满宽）；点击任意优化项即滑动浮现详情，符合「宇奇」式交互。
        }

        /// <summary>
        /// 状态筛选应用：只切换行与组头的可见性，不重建任何控件（毫秒级）。
        /// 不可用「全量重建行」实现筛选：157 行的销毁重建是列表交互卡顿的主要来源；
        /// FlowLayoutPanel 会自动跳过隐藏行的占位。
        /// </summary>
        private void ApplyFilters()
        {
            if (_rows.Count == 0 && Body.Controls.Count == 0) return;

            Dictionary<string, int> visibleCount = new Dictionary<string, int>();
            Body.SuspendLayout();
            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    TweakRow r = _rows[i];
                    bool vis = PassFilter(r.Tweak);
                    if (r.Visible != vis) r.Visible = vis;
                    if (vis)
                    {
                        int n;
                        visibleCount.TryGetValue(r.Tweak.Group, out n);
                        visibleCount[r.Tweak.Group] = n + 1;
                    }
                }

                for (int i = 0; i < Body.Controls.Count; i++)
                {
                    GroupHeader h = Body.Controls[i] as GroupHeader;
                    if (h == null) continue;
                    int n;
                    bool any = visibleCount.TryGetValue(h.Text, out n) && n > 0;
                    // 折叠的组永远显示（它是展开入口，行本身未创建）
                    h.Visible = any || h.Collapsed;
                    if (any) h.CountText = n + " 项";
                }
            }
            finally
            {
                Body.ResumeLayout(false);
            }
            RefreshLayout();
        }

        /// <summary>状态筛选判定（"只看已启用"等）+ 关键词搜索。</summary>
        private bool PassFilter(ITweak t)
        {
            // 关键词：名称 / 说明 / 标识 / 分组 任一命中即保留（不区分大小写）
            if (_keyword.Length > 0)
            {
                bool hit =
                    (t.Name != null && t.Name.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Description != null && t.Description.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Id != null && t.Id.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Group != null && t.Group.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit) return false;
            }

            if (_stateFilter == "on")
            {
                bool a;
                if (!_states.TryGetValue(t.Id, out a) || !a) return false;
            }
            else if (_stateFilter == "off")
            {
                bool a;
                if (_states.TryGetValue(t.Id, out a) && a) return false;
            }
            else if (_stateFilter == "rec" && !t.Recommended) return false;
            else if (_stateFilter == "risky" && !t.Risky) return false;
            return true;
        }

        private static Color GroupColor(string group)
        {
            if (group == TweakLibrary.GPerformance) return Theme.Accent;
            if (group == TweakLibrary.GAppearance) return Theme.Cyan;
            if (group == TweakLibrary.GPrivacy) return Theme.Purple;
            if (group == TweakLibrary.GServices) return Theme.Warning;
            if (group == TweakLibrary.GPower) return Theme.Success;
            if (group == TweakLibrary.GGame) return Theme.Cyan;
            if (group == TweakLibrary.GSlim) return Theme.Danger;
            if (group == TweakLibrary.GAudio) return Theme.Cyan;
            return Theme.Accent;
        }

        private int CountApplied()
        {
            int n = 0;
            foreach (KeyValuePair<string, bool> kv in _states)
            {
                if (kv.Value) n++;
            }
            return n;
        }

        private void UpdateSummary()
        {
            int applied = 0;
            int risky = 0;
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) applied++;
                if (_tweaks[i].Risky) risky++;
            }

            // 圆环健康卡：圆环进度 = 启用率，图例给出「安全 / 谨慎」两档项数
            _health.SetData(_tweaks.Count, applied, _tweaks.Count - risky, risky);
            RefreshChipCounts();
            Relayout();
            ApplyFilters(); // 状态变化后同步筛选可见性（幂等，隐藏行才重排）
        }

        /// <summary>
        /// 刷新筛选 chips 上的项数（设计 mod-filter-chip 的写法：标签右侧带数量）。
        /// 数量让用户在点之前就知道会看到多少项，避免"点进去是空的"。
        /// </summary>
        private void RefreshChipCounts()
        {
            if (_stateChips.Count != 5) return;

            int on = 0, rec = 0, rk = 0;
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) on++;
                if (_tweaks[i].Recommended) rec++;
                if (_tweaks[i].Risky) rk++;
            }

            string[] labels = new string[] { "全部状态", "已启用", "未启用", "推荐", "谨慎" };
            int[] counts = new int[] { _tweaks.Count, on, _tweaks.Count - on, rec, rk };

            for (int i = 0; i < _stateChips.Count; i++)
            {
                _stateChips[i].Text = labels[i] + " " + counts[i];
                _stateChips[i].FitToText(72);
            }
        }

        // --------------------------------------------------------------

        internal void ToggleTweak(TweakRow row)
        {
            if (row == null) return;
            if (_busy)
            {
                // 忙碌时拒绝操作，但开关已被用户点开——恢复到真实状态，避免视觉与实际脱节
                row.SetState(row.Tweak.IsApplied());
                SetSubtitle("正在执行其他操作，请稍候再试。", Theme.Warning);
                return;
            }
            ITweak t = row.Tweak;
            bool applied = row.IsApplied;

            if (!applied && t.AdminOnly && !Native.IsElevated())
            {
                bool go = Dialog.Confirm(this, "需要管理员权限",
                    "「" + t.Name + "」需要管理员权限才能修改。\r\n\r\n是否以管理员身份重新启动本程序？");
                row.SetState(false);
                if (go)
                {
                    if (Shell.RestartElevated(""))
                    {
                        Application.Exit();
                    }
                    else
                    {
                        Dialog.Error(this, "提权失败", "未能以管理员身份启动，请右键程序选择「以管理员身份运行」。");
                    }
                }
                return;
            }

            if (!applied && t.Risky)
            {
                bool go = Dialog.Confirm(this, "确认操作",
                    "「" + t.Name + "」属于高级选项。\r\n\r\n" + t.Description +
                    "\r\n\r\n应用前将自动创建系统还原点（若 24 小时内已创建过则跳过），是否继续？");
                if (!go)
                {
                    row.SetState(false);
                    return;
                }

                // 危险项闸门：先创建系统还原点（后台执行，24 小时内已有则跳过），再应用
                _busy = true;
                row.SetState(t.IsApplied());
                SetSubtitle("正在创建系统还原点…", Theme.Warning);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool hasRecent = false;
                    string rpNote = "";
                    try
                    {
                        List<RestorePoint> points = RestorePoints.List();
                        for (int i = 0; i < points.Count; i++)
                        {
                            if ((DateTime.Now - points[i].Created).TotalHours < 24) { hasRecent = true; break; }
                        }
                    }
                    catch { }
                    if (!hasRecent)
                    {
                        string err;
                        hasRecent = RestorePoints.Create("GuyueBox - 启用 " + t.Name + " 前", out err);
                        if (!hasRecent) rpNote = string.IsNullOrEmpty(err) ? "还原点创建失败" : err;
                        else rpNote = "已创建系统还原点";
                    }
                    else rpNote = "24 小时内已存在还原点，跳过创建";

                    // #22：谨慎项应用前必须确保有还原点兜底，创建失败则取消应用
                    if (!hasRecent)
                    {
                        Post(delegate
                        {
                            _busy = false;
                            row.SetState(applied); // 保持原状态（未应用）
                            Dialog.Warn(this, "还原点创建失败",
                                "谨慎项「" + t.Name + "」应用前必须确保有系统还原点兜底，但创建失败了：\r\n" + rpNote +
                                "\r\n\r\n本次应用已取消。请检查系统保护是否开启（系统属性 → 系统保护）后重试。");
                            SetSubtitle("还原点创建失败，已取消：" + t.Name, Theme.Danger);
                        });
                        return;
                    }

                    bool applyOk = false;
                    try { applyOk = t.Apply(); } catch { }
                    Post(delegate
                    {
                        _busy = false;
                        _states[t.Id] = applyOk;
                        row.SetState(applyOk);
                        UpdateSummary();
                        if (!applyOk)
                        {
                            Dialog.Error(this, "应用失败", "未能应用「" + t.Name + "」。");
                            SetSubtitle("应用失败：" + t.Name, Theme.Danger);
                            return;
                        }
                        SetSubtitle("已启用：" + t.Name +
                            (rpNote.Length > 0 ? "（" + rpNote + "）" : ""), Theme.Success);
                    });
                });
                return;
            }

            if (applied)
            {
                // 还原本身是安全操作（自动回到系统默认），高频操作不打断——直接执行。
                // 「谨慎项」例外：关闭也确认一次。
                if (t.Risky)
                {
                    bool go = Dialog.Confirm(this, "还原谨慎项",
                        "「" + t.Name + "」属于高级选项，确定要还原为系统默认状态吗？");
                    if (!go)
                    {
                        row.SetState(true);
                        return;
                    }
                }
            }

            // 应用/还原可能执行外部命令（powercfg/powershell/sc），放到后台线程避免界面卡顿，
            // 结果回主线程更新状态。UI 线程只负责确认弹窗，不再被命令阻塞。
            bool wasApplied = applied;
            ITweak tw = t;
            TweakRow r = row;
            SetSubtitle(wasApplied ? "正在还原…" : "正在应用…", Theme.Warning);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = wasApplied ? tw.Revert() : tw.Apply(); }
                catch { ok = false; }
                Post(delegate
                {
                    if (!ok)
                    {
                        r.SetState(wasApplied);
                        Dialog.Error(this, wasApplied ? "还原失败" : "应用失败",
                            "未能" + (wasApplied ? "还原" : "应用") + "「" + tw.Name + "」。\r\n\r\n" +
                            "请确认程序以管理员身份运行，并且目标服务或注册表项存在。");
                        return;
                    }
                    bool nowApplied = !wasApplied;
                    _states[tw.Id] = nowApplied;
                    r.SetState(nowApplied);
                    UpdateSummary();
                    string suffix = (tw.Id == "hibernate_off" || tw.Id == "ntfs_lastaccess" ||
                                     tw.Id == "win11_classic_menu") ? "（部分设置需重启后生效）" : "";
                    SetSubtitle("已" + (nowApplied ? "启用" : "还原") + "：" + tw.Name + suffix, Theme.Success);
                });
            });
        }

        // ---------- 内置方案库 ----------

        private const string ProfilesRoot = @"Software\GuyueBox\Profiles";

        private static string[] ListProfiles()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(ProfilesRoot))
                {
                    if (k == null) return new string[0];
                    return k.GetSubKeyNames();
                }
            }
            catch { return new string[0]; }
        }

        private static string ReadProfile(string name)
        {
            try
            {
                return Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "ids", "") as string ?? "";
            }
            catch { return ""; }
        }

        private void OnSaveProfileClick(object sender, EventArgs e)
        {
            if (_groupFilter != null)
            {
                SetSubtitle("当前只显示「" + _groupFilter + "」分类——请先切回「全部」再保存方案，否则方案不完整。", Theme.Warning);
                return;
            }
            List<string> ids = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsApplied) ids.Add(_rows[i].Tweak.Id);
            }
            if (ids.Count == 0)
            {
                Dialog.Info(this, "空方案", "当前没有任何已启用的优化项，没有可保存的内容。");
                return;
            }
            string name = Dialog.Input(this, "保存方案", "方案名称（如：游戏模式 / 日常）：", "");
            if (name == null || name.Trim().Length == 0) return;
            name = name.Trim();
            if (name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0) name = name.Replace('\\', '_').Replace('/', '_');

            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "ids",
                string.Join(",", ids.ToArray()));
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\" + ProfilesRoot + "\\" + name, "saved",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            BuildProfileChips();
            SetSubtitle("方案「" + name + "」已保存（" + ids.Count + " 项）。点击方案名即可一键切换。", Theme.Success);
        }

        private void ApplyProfile(string name)
        {
            if (_busy) return;
            if (_groupFilter != null)
            {
                SetSubtitle("当前只显示「" + _groupFilter + "」分类——请先切回「全部」再应用方案，否则其余分类不会被同步。", Theme.Warning);
                return;
            }
            string raw = ReadProfile(name);
            if (raw.Length == 0)
            {
                Dialog.Info(this, "空方案", "方案「" + name + "」没有记录任何优化项。");
                return;
            }
            Dictionary<string, bool> want = new Dictionary<string, bool>();
            foreach (string id in raw.Split(','))
            {
                if (id.Trim().Length > 0) want[id.Trim()] = true;
            }

            List<TweakRow> toApply = new List<TweakRow>();
            List<TweakRow> toRevert = new List<TweakRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                TweakRow row = _rows[i];
                bool inProfile;
                if (!want.TryGetValue(row.Tweak.Id, out inProfile))
                {
                    if (row.IsApplied) toRevert.Add(row);
                    continue;
                }
                if (!row.IsApplied) toApply.Add(row);
            }

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                SetSubtitle("当前状态与方案「" + name + "」一致。", Theme.TextSecondary);
                return;
            }
            if (!Dialog.Confirm(this, "应用方案",
                "将同步到方案「" + name + "」：\r\n\r\n  · 启用 " + toApply.Count + " 项\r\n  · 还原 " + toRevert.Count + " 项\r\n\r\n继续吗？"))
            {
                return;
            }

            List<TweakRow> all = new List<TweakRow>();
            all.AddRange(toApply);
            all.AddRange(toRevert);
            ImportRun(all, toApply.Count, toRevert.Count);
        }

        private void DeleteProfile(string name)
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(ProfilesRoot + "\\" + name, false);
            }
            catch { }
            BuildProfileChips();
            SetSubtitle("方案「" + name + "」已删除。", Theme.TextSecondary);
        }

        /// <summary>重建方案 chips（有方案才显示整行区域）。左键应用，右键删除。</summary>
        private void BuildProfileChips()
        {
            _profileChips.FlowDirection = FlowDirection.LeftToRight;
            _profileChips.WrapContents = false;
            _profileChips.BackColor = Theme.WindowBg;
            _profileChips.Height = 28;
            _profileChips.AutoSize = true; // 宽度随内容自适应，避免提示文字被默认 200px 宽度裁剪
            _profileChips.Controls.Clear();

            string[] names = ListProfiles();
            if (names.Length == 0)
            {
                Label empty = new Label();
                empty.Text = "暂无保存的方案——先按需要开好优化，再点「＋保存方案」";
                empty.ForeColor = Theme.TextMuted;
                empty.Font = Theme.FontSmall;
                empty.AutoSize = true;
                empty.Margin = new Padding(0, 6, 0, 0);
                _profileChips.Controls.Add(empty);
                return;
            }

            Label cap = new Label();
            cap.Text = "我的方案:";
            cap.ForeColor = Theme.TextMuted;
            cap.Font = Theme.FontSmall;
            cap.AutoSize = true;
            cap.Margin = new Padding(16, 6, 8, 0);
            _profileChips.Controls.Add(cap);

            foreach (string name in names)
            {
                string captured = name;
                AccentButton chip = new AccentButton();
                chip.Text = captured;
                chip.Variant = ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                chip.Click += delegate { ApplyProfile(captured); };
                chip.MouseUp += delegate (object s, MouseEventArgs me)
                {
                    if (me.Button == MouseButtons.Right &&
                        Dialog.Confirm(this, "删除方案", "删除方案「" + captured + "」吗？"))
                    {
                        DeleteProfile(captured);
                    }
                };
                _profileChips.Controls.Add(chip);
            }
        }

        // ---------- 优化方案导出 / 导入 ----------

        /// <summary>把当前所有「已启用」的优化项 Id 快照保存为方案文件。</summary>
        private void OnExportProfile(object sender, EventArgs e)
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsApplied) ids.Add(_rows[i].Tweak.Id);
            }
            if (ids.Count == 0)
            {
                Dialog.Info(this, "空方案", "当前没有任何已启用的优化项，没有可保存的内容。");
                return;
            }

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "优化方案 (*.stprofile)|*.stprofile";
                d.FileName = "我的优化方案.stprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                StringBuilder sb = new StringBuilder();
                sb.Append("{\"app\":\"GuyueBox\",\"version\":1,\"saved\":\"");
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                sb.Append("\",\"ids\":[");
                for (int i = 0; i < ids.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('"').Append(ids[i]).Append('"');
                }
                sb.Append("]}");
                try
                {
                    System.IO.File.WriteAllText(d.FileName, sb.ToString(),
                        new System.Text.UTF8Encoding(false));
                    SetSubtitle("方案已保存（" + ids.Count + " 项）：" + d.FileName, Theme.Success);
                }
                catch (Exception ex)
                {
                    Dialog.Error(this, "保存失败", ex.Message);
                }
            }
        }

        /// <summary>读取方案文件，把优化中心同步到方案记录的状态（应用缺失的、还原多余的）。</summary>
        private void OnImportProfile(object sender, EventArgs e)
        {
            if (_busy) return;

            string file;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "优化方案 (*.stprofile)|*.stprofile";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }

            string[] ids;
            try
            {
                string json = System.IO.File.ReadAllText(file);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                    json, "\"ids\"\\s*:\\s*\\[(.*?)\\]", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (!m.Success)
                {
                    Dialog.Error(this, "文件无效", "不是本工具导出的优化方案文件。");
                    return;
                }
                List<string> list = new List<string>();
                foreach (System.Text.RegularExpressions.Match s in
                    System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, "\"([a-z0-9_]+)\""))
                {
                    list.Add(s.Groups[1].Value);
                }
                ids = list.ToArray();
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "读取失败", ex.Message);
                return;
            }

            // 与当前状态求差
            Dictionary<string, bool> want = new Dictionary<string, bool>();
            foreach (string id in ids) want[id] = true;

            List<TweakRow> toApply = new List<TweakRow>();
            List<TweakRow> toRevert = new List<TweakRow>();
            int unknown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                TweakRow row = _rows[i];
                bool inProfile;
                if (!want.TryGetValue(row.Tweak.Id, out inProfile))
                {
                    if (!row.IsApplied) continue; // 未启用且不在方案里：无需动
                    // 方案里没有但当前已启用 → 还原
                    toRevert.Add(row);
                    continue;
                }
                if (row.IsApplied) continue; // 已与方案一致
                toApply.Add(row);
            }
            // 统计方案中本库不存在的项
            Dictionary<string, bool> known = new Dictionary<string, bool>();
            for (int i = 0; i < _rows.Count; i++) known[_rows[i].Tweak.Id] = true;
            foreach (string id in ids)
            {
                if (!known.ContainsKey(id)) unknown++;
            }

            if (toApply.Count == 0 && toRevert.Count == 0)
            {
                SetSubtitle("当前状态与方案一致，无需变更" + (unknown > 0 ? "（方案中 " + unknown + " 项在本库不存在，已跳过）" : "") + "。",
                    Theme.TextSecondary);
                return;
            }

            string msg = "将把优化中心同步到该方案：\r\n\r\n  · 启用 " + toApply.Count + " 项\r\n  · 还原 " + toRevert.Count + " 项" +
                (unknown > 0 ? "\r\n\r\n注意：方案中 " + unknown + " 项在本库不存在，将跳过。" : "") +
                "\r\n\r\n继续执行吗？";
            if (!Dialog.Confirm(this, "导入方案", msg)) return;

            List<TweakRow> all = new List<TweakRow>();
            all.AddRange(toApply);
            all.AddRange(toRevert);
            ImportRun(all, toApply.Count, toRevert.Count);
        }

        /// <summary>后台线程逐项执行方案同步。</summary>
        private void ImportRun(List<TweakRow> rows, int applyCount, int revertCount)
        {
            _busy = true;
            _loadGen++;              // 会话令牌：批量期间用户切分类/强制刷新，本批次的 UI 回调整体作废
            int gen = _loadGen;

            // 高危闸门（与单项开关一致）：批次含「谨慎项启用」时先确认/创建还原点
            bool hasRiskyApply = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Tweak.Risky && wantApply(rows, rows[i], applyCount))
                {
                    hasRiskyApply = true;
                    break;
                }
            }
            if (hasRiskyApply)
            {
                SetSubtitle("批次含谨慎项，正在确认系统还原点…", Theme.Warning);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    bool rpOk;
                    string note = EnsureRecentRestorePoint("GuyueBox - 方案同步前", out rpOk);
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (gen != _loadGen || IsDisposed || Disposing) return;
                            if (!rpOk)
                            {
                                // #22：Risky 项的还原点必须确认创建成功，失败即取消整批应用
                                SetSubtitle("还原点创建失败，已取消本次方案同步：" + note, Theme.Danger);
                                Dialog.Warn(this, "还原点创建失败",
                                    "谨慎项应用前必须确保有系统还原点兜底，但创建失败了：\r\n" + note +
                                    "\r\n\r\n本次同步已取消。请检查系统保护是否开启（系统属性 → 系统保护）后重试。");
                                return;
                            }
                            SetSubtitle(note.Length > 0 ? note + "，开始同步方案…" : "开始同步方案…", Theme.Warning);
                            ImportRunCore(rows, applyCount, revertCount, gen);
                        });
                    }
                    catch { }
                });
                return;
            }
            ImportRunCore(rows, applyCount, revertCount, gen);
        }

        /// <summary>24 小时内已有还原点则跳过，否则创建一个。
        /// 返回给用户看的备注；ok=false 表示还原点创建失败（Risky 项应用必须就此取消——#22 校验要求）。</summary>
        private static string EnsureRecentRestorePoint(string title, out bool ok)
        {
            try
            {
                List<RestorePoint> points = RestorePoints.List();
                for (int i = 0; i < points.Count; i++)
                {
                    if ((DateTime.Now - points[i].Created).TotalHours < 24) { ok = true; return ""; }
                }
            }
            catch { }
            string err;
            bool created = RestorePoints.Create(title, out err);
            if (created) { ok = true; return "已创建系统还原点"; }
            ok = false;
            return string.IsNullOrEmpty(err) ? "还原点创建失败" : err;
        }

        private void ImportRunCore(List<TweakRow> rows, int applyCount, int revertCount, int gen)
        {
            SetSubtitle("正在同步方案（0/" + rows.Count + "）…", Theme.Warning);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int done = 0, failed = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    TweakRow row = rows[i];
                    bool target = wantApply(rows, row, applyCount);
                    try
                    {
                        bool ok = target ? row.Tweak.Apply() : row.Tweak.Revert();
                        if (!ok) failed++;
                    }
                    catch
                    {
                        failed++;
                    }
                    done++;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (gen != _loadGen) return; // 界面已重建，不再触碰旧行
                            if (row.IsDisposed) return;
                            row.SetState(row.Tweak.IsApplied());
                            _states[row.Tweak.Id] = row.Tweak.IsApplied();
                            SetSubtitle("正在同步方案（" + done + "/" + rows.Count + "）…", Theme.Warning);
                        });
                    }
                    catch
                    {
                    }
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (gen != _loadGen) return; // 界面已重建：状态以新会话的探测为准
                        _busy = false;
                        UpdateSummary();
                        SetSubtitle("方案同步完成：启用 " + applyCount + "、还原 " + revertCount +
                            (failed > 0 ? "，" + failed + " 项失败（详见状态列）" : "，全部成功") + "。",
                            failed > 0 ? Theme.Warning : Theme.Success);
                    });
                }
                catch
                {
                }
            });
        }

        private static bool wantApply(List<TweakRow> rows, TweakRow row, int applyCount)
        {
            // ImportRun 的 rows 前 applyCount 个是待应用，其余是待还原
            return rows.IndexOf(row) < applyCount;
        }

        private void OnRecommendedClick(object sender, EventArgs e)
        {
            if (_busy) return;

            // 单一数据源：与「推荐」筛选一致，直接以 ITweak.Recommended 为准
            List<ITweak> targets = new List<ITweak>();
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool applied;
                if (!_states.TryGetValue(_tweaks[i].Id, out applied)) applied = false;
                if (applied) continue;
                if (_tweaks[i].Recommended) targets.Add(_tweaks[i]);
            }

            if (targets.Count == 0)
            {
                Dialog.Info(this, "无需优化", "推荐项目都已经处于启用状态。");
                return;
            }

            string list = "";
            for (int i = 0; i < targets.Count; i++) list += "· " + targets[i].Name + "\r\n";

            if (!Dialog.Confirm(this, "一键推荐优化",
                "将启用以下 " + targets.Count + " 项推荐优化：\r\n\r\n" + list +
                "\r\n所有改动都会被备份，可随时单独还原。是否继续？"))
                return;

            int okCount = 0;
            int failCount = 0;
            int adminNeeded = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                ITweak t = targets[i];
                if (t.AdminOnly && !Native.IsElevated())
                {
                    adminNeeded++;
                    failCount++;
                    continue;
                }
                if (t.Apply())
                {
                    okCount++;
                    _states[t.Id] = true;
                }
                else
                {
                    failCount++;
                }
            }

            SyncRows();
            UpdateSummary();

            string text = "成功启用 " + okCount + " 项优化。";
            if (failCount > 0) text += "\r\n有 " + failCount + " 项未能应用。";
            if (adminNeeded > 0)
            {
                text += "\r\n\r\n其中 " + adminNeeded + " 项需要管理员权限，请以管理员身份重新运行后再试。";
            }
            text += "\r\n\r\n部分设置需要重启或重新登录后才会生效。";

            SetSubtitle("一键优化完成：" + okCount + " 项成功。", failCount > 0 ? Theme.Warning : Theme.Success);
            Dialog.Success(this, "一键优化", text);
        }

        private void OnRestoreAllClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<ITweak> appliedList = new List<ITweak>();
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) appliedList.Add(_tweaks[i]);
            }

            if (appliedList.Count == 0)
            {
                Dialog.Info(this, "无需还原", "当前没有已启用的优化项。");
                return;
            }

            if (!Dialog.Confirm(this, "全部还原",
                "将把 " + appliedList.Count + " 项优化全部还原为原状态。\r\n\r\n" +
                "还原依据的是启用时自动保存的备份，因此可以准确恢复。是否继续？"))
                return;

            int okCount = 0;
            int failCount = 0;
            for (int i = 0; i < appliedList.Count; i++)
            {
                if (appliedList[i].Revert())
                {
                    okCount++;
                    _states[appliedList[i].Id] = false;
                }
                else
                {
                    failCount++;
                }
            }

            SyncRows();
            UpdateSummary();

            SetSubtitle("已还原 " + okCount + " 项。", failCount > 0 ? Theme.Warning : Theme.Success);
            Dialog.Success(this, "还原完成",
                "成功还原 " + okCount + " 项。" +
                (failCount > 0 ? "\r\n" + failCount + " 项没有备份记录，已保持当前状态。" : "") +
                "\r\n\r\n部分设置需要重启后生效。");
        }

        private void SyncRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_rows[i].Tweak.Id, out a);
                _rows[i].SetState(a);
            }
        }    }
}
