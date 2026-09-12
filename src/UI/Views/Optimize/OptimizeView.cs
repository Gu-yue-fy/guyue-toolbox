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
        }

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
        }
    }

    /// <summary>单条优化项。</summary>
    internal sealed class TweakRow : RoundPanel
    {
        public readonly ITweak Tweak;

        private readonly BadgeLabel _badge = new BadgeLabel();
        private readonly ToggleSwitch _toggle = new ToggleSwitch();
        private readonly OptimizeView _owner;
        private bool _applied;
        private bool _suppressToggle;

        public TweakRow(ITweak tweak, OptimizeView owner)
        {
            Tweak = tweak;
            _owner = owner;

            BackColor = Theme.CardBg;
            Radius = 11;
            Height = 72;
            Margin = new Padding(0, 0, 0, 9);
            Tag = "stretch";

            _badge.Size = new Size(58, 20);
            _badge.Filled = false;
            Controls.Add(_badge);

            _toggle.Size = new Size(40, 22);
            _toggle.CheckedChanged += OnToggleChanged;
            Controls.Add(_toggle);

            // 悬停高亮：光标进入行时轻微提亮，指示可交互
            MouseEnter += delegate { BackColor = Theme.CardHover; };
            MouseLeave += delegate { BackColor = Theme.CardBg; };

            Resize += delegate { LayoutChildren(); };
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            _badge.Location = new Point(Math.Max(10, Width - 128), 26);
            _toggle.Location = new Point(Math.Max(10, Width - 62), 25);
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

            _badge.Visible = !applied;
            if (!applied)
            {
                _badge.Text = Tweak.Risky ? "谨慎" : "未启用";
                _badge.BadgeColor = Tweak.Risky ? Theme.Warning : Theme.TextMuted;
                _badge.Invalidate();
            }

            StartStateAnimation();
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
            Color off = Theme.CardBg;
            Color on = Gfx.Blend(Theme.CardBg, Theme.Success, 0.06);
            float t = AnimProgress();
            Color from = _applied ? off : on;
            Color to = _applied ? on : off;
            return Gfx.Blend(from, to, t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            BackColor = AnimBackColor();
            float t = AnimProgress();
            BorderColor = _applied
                ? Gfx.Blend(Theme.Border, Gfx.Alpha(Theme.Success, 110), t)
                : Gfx.Blend(Gfx.Alpha(Theme.Success, 110), Theme.Border, t);
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 左侧状态条
            using (GraphicsPath p = Gfx.RoundRect(new Rectangle(0, 16, 3, Height - 32), 2))
            using (SolidBrush b = new SolidBrush(_applied ? Theme.Success : Theme.Border))
            {
                g.FillPath(b, p);
            }

            // 文本区右边界与右侧开关/徽标联动避让，窄宽度下不再重叠
            int textRight = Math.Max(60, _toggle.Left - 26);
            Gfx.DrawTextEllipsis(g, Tweak.Name, Theme.FontBodyBold, Theme.TextPrimary,
                new Rectangle(20, 12, textRight - 20, 24));

            Gfx.DrawTextEllipsis(g, Tweak.Description, Theme.FontSmall, Theme.TextMuted,
                new Rectangle(20, 38, textRight - 20, 20));
        }
    }

    public class OptimizeView : ViewBase
    {
        private readonly List<ITweak> _tweaks = new List<ITweak>();
        private readonly List<TweakRow> _rows = new List<TweakRow>();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly Dictionary<string, bool> _states = new Dictionary<string, bool>();
        private readonly Panel _toolbar = new Panel();
        private readonly FlowLayoutPanel _chips = new FlowLayoutPanel();
        private readonly List<AccentButton> _chipButtons = new List<AccentButton>();
        /// <summary>当前分类包含的底层分组集合（null = 全部）。</summary>
        private string[] _extraGroups;

        private bool _busy;
        private bool _loaded;
        private string _stateFilter;
        private readonly FlowLayoutPanel _filterRow = new FlowLayoutPanel();
        private readonly List<AccentButton> _stateChips = new List<AccentButton>();
        private readonly FlowLayoutPanel _profileRow = new FlowLayoutPanel();
        private readonly FlowLayoutPanel _profileChips = new FlowLayoutPanel();
        private string _groupFilter;
        private readonly Dictionary<string, bool> _collapsedGroups = new Dictionary<string, bool>();

        /// <summary>外部（磁贴/快捷方式）希望进入页面时直接选中的分类，进入后消费一次。</summary>
        public static string PendingGroup;

        public OptimizeView()
            : this("优化中心", "全部优化项：游戏 / 网络 / 性能 / 电源 / 隐私 / 精简 / 外观 / 服务，逐项开关随时还原", null)
        {
        }

        /// <summary>groupFilter 非空时只显示该分组的优化项（如「游戏优化」页）。</summary>
        public OptimizeView(string title, string subtitle, string groupFilter)
            : base(title, subtitle)
        {
            _groupFilter = groupFilter;
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            // 硬件感知提示：告诉用户本机 CPU/GPU，专属项只对本机硬件生效
            _notice.NoticeText = "每一项优化都会自动备份修改前的注册表内容，关闭开关即可还原到系统默认状态。" +
                BuildHardwareHint();

            _summary.Caption = "优化概览";
            _summary.IconKind = "tune";
            _summary.CaptionColor = Theme.Success;

            AddAction("一键推荐优化", "bolt", ButtonVariant.Primary, OnRecommendedClick, 152);
            AddAction("全部还原", "refresh", ButtonVariant.Ghost, OnRestoreAllClick, 110);
            AddAction("导出方案", "doc", ButtonVariant.Ghost, OnExportProfile, 110);
            AddAction("导入方案", "add", ButtonVariant.Secondary, OnImportProfile, 110);
            AddAction("刷新状态", "refresh", ButtonVariant.Secondary, delegate { Load(true); }, 110);

            AddFull(_notice, 42, 18);

            FlowLayoutPanel row = MakeRow(0, 8);
            row.Controls.Add(_summary);
            AddRow(row);
            BuildToolbar();
            AddFull(_toolbar, 34, 10);
            AddFull(_filterRow, 34, 8);
            AddFull(_profileRow, 34, 10);
            AddExtraControls();
            Relayout();
        }

        /// <summary>扩展点：在列表上方追加本页专属控件。</summary>
        protected virtual void AddExtraControls()
        {
        }

        private void BuildToolbar()
        {
            _toolbar.BackColor = Theme.WindowBg;
            _toolbar.Height = 34;

            // 状态筛选 + 方案库行
            _filterRow.FlowDirection = FlowDirection.LeftToRight;
            _filterRow.WrapContents = false;
            _filterRow.BackColor = Theme.WindowBg;
            _filterRow.Height = 34;

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
            new string[] { TweakLibrary.GPerformance, TweakLibrary.GPower, TweakLibrary.GServices },
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
            int summaryHeight = _summary.PreferredHeight;
            _summary.Height = summaryHeight;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryHeight;
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
                    // 旧组名映射到主分类：按分组归属查找
                    string[][] maps = new string[][]
                    {
                        new string[] { TweakLibrary.GGame },
                        new string[] { TweakLibrary.GPerformance, TweakLibrary.GPower, TweakLibrary.GServices },
                        new string[] { TweakLibrary.GNetwork },
                        new string[] { TweakLibrary.GPrivacy, TweakLibrary.GSlim, TweakLibrary.GAppearance }
                    };
                    for (int i = 1; i < _chipButtons.Count && hit < 0; i++)
                    {
                        for (int k = 0; k < maps[i - 1].Length; k++)
                        {
                            if (maps[i - 1][k] == PendingGroup) { hit = i; break; }
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
        }

        /// <summary>
        /// 状态筛选应用：只切换行与组头的可见性，不重建任何控件（毫秒级）。
        /// 之前每次切换筛选/开关状态都全量重建 157 行（数百个控件销毁重建），
        /// 是列表交互卡顿的主要来源；FlowLayoutPanel 会自动跳过隐藏行占位。
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

        /// <summary>状态筛选判定（"只看已启用"等）。</summary>
        private bool PassFilter(ITweak t)
        {
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

            _summary.Clear();
            _summary.Add("优化项总数", _tweaks.Count + " 项");
            _summary.Add("已启用", applied + " 项", applied > 0 ? Theme.Success : Theme.TextPrimary);
            _summary.Add("待优化", Math.Max(0, _tweaks.Count - applied) + " 项");
            _summary.Add("需谨慎使用", risky + " 项", risky > 0 ? Theme.Warning : Theme.TextPrimary);
            _summary.Invalidate();
            Relayout();
            ApplyFilters(); // 状态变化后同步筛选可见性（幂等，隐藏行才重排）
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

            bool ok = applied ? t.Revert() : t.Apply();

            if (!ok)
            {
                row.SetState(applied);
                Dialog.Error(this, applied ? "还原失败" : "应用失败",
                    "未能" + (applied ? "还原" : "应用") + "「" + t.Name + "」。\r\n\r\n" +
                    "请确认程序以管理员身份运行，并且目标服务或注册表项存在。");
                return;
            }

            bool nowApplied = !applied;
            _states[t.Id] = nowApplied;
            row.SetState(nowApplied);
            UpdateSummary();

            string suffix = (t.Id == "hibernate_off" || t.Id == "ntfs_lastaccess" ||
                             t.Id == "win11_classic_menu") ? "（部分设置需重启后生效）" : "";
            SetSubtitle("已" + (nowApplied ? "启用" : "还原") + "：" + t.Name + suffix, Theme.Success);
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
                    string note = EnsureRecentRestorePoint("GuyueBox - 方案同步前");
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (gen != _loadGen || IsDisposed || Disposing) return;
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

        /// <summary>24 小时内已有还原点则跳过，否则创建一个。返回给用户看的备注（空 = 已有，无需创建）。</summary>
        private static string EnsureRecentRestorePoint(string title)
        {
            try
            {
                List<RestorePoint> points = RestorePoints.List();
                for (int i = 0; i < points.Count; i++)
                {
                    if ((DateTime.Now - points[i].Created).TotalHours < 24) return "";
                }
            }
            catch { }
            string err;
            bool ok = RestorePoints.Create(title, out err);
            if (ok) return "已创建系统还原点";
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

            List<string> ids = TweakLibrary.RecommendedIds();
            List<ITweak> targets = new List<ITweak>();
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool applied;
                if (!_states.TryGetValue(_tweaks[i].Id, out applied)) applied = false;
                if (applied) continue;
                if (ids.Contains(_tweaks[i].Id)) targets.Add(_tweaks[i]);
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
        }

        private void Post(ThreadStart action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke((MethodInvoker)delegate { action(); });
                }
            }
            catch
            {
            }
        }
    }
}
