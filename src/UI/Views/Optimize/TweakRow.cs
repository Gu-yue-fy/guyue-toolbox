﻿/* ============================================================
 * 文件说明：优化项行控件：分组色图标 + 名称/描述 + 推荐标签 + 开关，状态切换带渐变反馈。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>单条优化项。</summary>
    internal sealed class TweakRow : RoundPanel
    {
        public readonly ITweak Tweak;

        private readonly BadgeLabel _badge = new BadgeLabel();
        private readonly ToggleSwitch _toggle = new ToggleSwitch();
        private readonly OptimizeView _owner;
        private bool _applied;
        private bool _suppressToggle;
        private bool _hover;
        private bool _selected;
        private readonly Color _defaultBorder;

        private const int RowH1 = 64;  // 单行高度
        private const int RowH2 = 84;  // 双行高度（描述需要折行时）
        private bool _twoLine;

        /// <summary>本行的正常高度（与内部双行判定一致）。分组展开动画据此取目标高度。</summary>
        public int NormalHeight
        {
            get { return _twoLine ? RowH2 : RowH1; }
        }

        /// <summary>
        /// 分组折叠 / 展开动画期间置 true：抑制"Resize 自动回到正常高度"的自愈逻辑，
        /// 否则动画每帧改高度都会被它立刻改回去（动画完全失效）。
        /// </summary>
        public bool SuppressAutoHeight;

        public TweakRow(ITweak tweak, OptimizeView owner)
        {
            Tweak = tweak;
            _owner = owner;

            BackColor = Theme.CardBg;
            _defaultBorder = BorderColor;
            Radius = Theme.RadiusCard;
            Margin = new Padding(0, 0, 0, 7);
            Tag = "stretch";

            // 行内只显示简短说明（第一句）；过长才扩到 2 行
            _twoLine = MeasureTwoLine(Brief(tweak.Description));
            Height = _twoLine ? RowH2 : RowH1;

            // 关键修复：行继承自 BufferPanel，基类未开启 StandardClick，
            // 导致 WinForms 不对该控件派发 Click 事件——行上的
            // 「Click += 查看详情」永远不触发，表现就是"点了优化项右边没说明"。
            // 与 GroupHeader 一致，显式开启 StandardClick / StandardDoubleClick。
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);

            // 风险徽章常显（四档四色，与右栏详情共用同一判定）：
            // 启用与否由右侧开关表达，不再拿同一个徽标兼表状态。
            _badge.Size = new Size(58, 20);
            _badge.Filled = false;
            _badge.Text = TweakRiskBadge.Label(Tweak);
            _badge.BadgeColor = TweakRiskBadge.Color(Tweak);
            Controls.Add(_badge);

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

            Resize += delegate
            {
                _twoLine = MeasureTwoLine(Brief(Tweak.Description));
                if (!SuppressAutoHeight) Height = _twoLine ? RowH2 : RowH1;
                LayoutChildren();
            };
            LayoutChildren();
        }

        /// <summary>
        /// 行内显示的**简短**说明：只取描述的第一句（。；！？—— 之前的部分）并限长，
        /// 完整解释留给右侧详情栏（作用 / 原理 / 风险与恢复 / 将写入的项）。
        /// 条目描述原本常占两三行，扫读时又吵又乱（此处不写死条数，避免增删条目后文档过期）。
        /// </summary>
        internal static string Brief(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            string s = desc.Trim();

            string[] stops = new string[] { "。", "；", "！", "？", "——", "\r\n", "\n" };
            int cut = -1;
            for (int i = 0; i < stops.Length; i++)
            {
                int p = s.IndexOf(stops[i], StringComparison.Ordinal);
                if (p > 0 && (cut < 0 || p < cut)) cut = p;
            }
            if (cut > 0) s = s.Substring(0, cut);

            const int max = 42;
            if (s.Length > max)
            {
                // 退到最近的逗号/顿号处截断，避免把词切一半
                int p = s.LastIndexOfAny(new char[] { '，', '、', ',', ' ' }, max - 1);
                s = (p >= 12 ? s.Substring(0, p) : s.Substring(0, max)) + "…";
            }
            return s;
        }

        /// <summary>
        /// 行首图标：先按「项名 + 分组」里的关键词取（磁盘/显卡/网络/服务/隐私… 一眼能分辨主题），
        /// 取不到再退回分组图标。分组色调仍然保留，所以同组仍是一族颜色。
        /// </summary>
        internal static string IconOf(ITweak t)
        {
            string g = t == null ? "" : (t.Group ?? "");
            string s = (t == null ? "" : (t.Name ?? "")) + " " + g;
            if (s.Length == 0) return IconOfGroup(g);

            if (Has(s, "NVMe", "SSD", "固态", "硬盘", "磁盘", "存储", "预取", "Superfetch", "索引", "缓存")) return "disk";
            if (Has(s, "显卡", "GPU", "NVIDIA", "显存", "渲染", "DirectX", "着色")) return "gpu";
            if (Has(s, "鼠标", "键盘", "输入", "手柄", "触控")) return "device";
            if (Has(s, "内存", "RAM", "页面文件", "工作集", "压缩")) return "memory";
            if (Has(s, "CPU", "处理器", "核心", "线程", "调度", "频率")) return "cpu";
            if (Has(s, "网络", "TCP", "DNS", "网卡", "带宽", "延迟", "Nagle", "QoS", "IP")) return "network";
            if (Has(s, "服务", "svchost")) return "services";
            if (Has(s, "计划任务", "任务计划")) return "task";
            if (Has(s, "启动", "开机", "引导", "Boot", "登录")) return "startup";
            if (Has(s, "电源", "节能", "休眠", "待机", "功耗", "Power")) return "power";
            if (Has(s, "隐私", "遥测", "广告", "跟踪", "收集", "Copilot", "Bing", "推荐")) return "shield";
            if (Has(s, "右键", "菜单", "资源管理器", "任务栏", "开始菜单", "桌面", "小组件")) return "menu";
            if (Has(s, "音频", "声音", "媒体", "MMCSS", "播放")) return "play";
            if (Has(s, "更新", "升级", "Windows Update")) return "refresh";
            if (Has(s, "驱动", "设备", "中断", "MSI")) return "maint";
            if (Has(s, "时间", "计时", "时钟", "精度", "节拍")) return "clock";
            if (Has(s, "进程", "优先级", "前台", "后台")) return "process";
            if (Has(s, "安全", "Defender", "防护", "UAC", "漏洞")) return "admin";
            if (Has(s, "应用", "UWP", "商店", "预装", "组件")) return "apps";
            if (Has(s, "临时", "垃圾", "日志", "诊断")) return "temp";
            return IconOfGroup(g);
        }

        private static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (s.IndexOf(keys[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>描述文本在当前列宽下是否需要折成 2 行。</summary>
        private bool MeasureTwoLine(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return false;
            int avail = Math.Max(200, Width > 0 ? Width - 180 : 400);
            int textW = TextRenderer.MeasureText(desc, Theme.FontSmall).Width;
            return textW > avail;
        }

        private void LayoutChildren()
        {
            int mid = (Height - 22) / 2;
            _toggle.Location = new Point(Math.Max(10, Width - 62), mid);
            int riskX = Math.Max(10, Width - 128);
            _badge.Location = new Point(riskX, mid + 1);
        }

        /// <summary>分组 → 图标名。行首图标块让 234 行一眼分得清领域，而不是清一色的文字墙。</summary>
        private static string IconOfGroup(string group)
        {
            switch (group)
            {
                case "游戏优化": return "play";
                case "性能优化":
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
                case "性能优化":
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
                    // 行在动画途中被销毁（列表重建 / 页面关闭）时直接出列，
                    // 否则会对已释放控件调 Invalidate
                    if (r.IsDisposed)
                    {
                        _animating.RemoveAt(i);
                        continue;
                    }
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

        /// <summary>宿主页销毁时停掉共享时钟并断开对行的引用（静态字段否则活到进程结束）。</summary>
        public static void ShutdownAnimClock()
        {
            if (_animClock != null)
            {
                _animClock.Stop();
                _animClock.Dispose();
                _animClock = null;
            }
            _animating.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animating.Remove(this); // 行自己先从全局待刷新表出列
            base.Dispose(disposing);
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
            return Theme.Ease.CubicOut(_animStep / 10f);
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
            // 折叠/展开动画期间行高会被压到极小值：此时不绘制，
            // 避免圆角与文本在 1~5px 高度下的绘制异常（视觉上是"正在收拢"的过渡）
            if (Height < 6) return;

            BackColor = AnimBackColor();
            float t = AnimProgress();
            // 描边动画只保留"变绿"方向（开启开关的成就反馈）；
            // 关闭方向恒为普通描边——旧公式 blend(绿,灰,t) 首帧是纯绿，
            // 每个未启用行初次绘制都会"闪一下绿"（用户可见的渲染缺陷）。
            Color baseBorder = _applied
                ? Gfx.Blend(Theme.GlassBorder, Gfx.Alpha(Theme.Success, 110), t)
                : Theme.GlassBorder;
            // 悬停描边发亮：指示"整行可点"（详情入口），而不是只有开关能点
            BorderColor = _hover && !_applied ? Gfx.Alpha(Theme.Accent, 170) : baseBorder;
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 悬停光晕：内侧再描一圈更亮的强调色（外圈是 RoundPanel 的边框），
            // 形成"双层描边"的光感，让整行可点的提示更醒目
            if (_hover && !_applied)
            {
                Rectangle inner = new Rectangle(2, 2, Math.Max(4, Width - 5), Math.Max(4, Height - 5));
                Pen glow = GdiCache.Pen(Gfx.Alpha(Theme.Accent, 70), 1.5f);
                g.DrawPath(glow, GdiCache.RoundRect(inner, Math.Max(2, Theme.RadiusCard - 1)));
            }

            // 左侧状态条：路径与画刷都走缓存——列表里每行每次重绘都会走到这里，
            // 234 行规模下每次通盘重绘曾在这里产生数百个 GraphicsPath/SolidBrush
            GraphicsState barState = g.Save();
            g.TranslateTransform(0, 14);
            g.FillPath(GdiCache.Brush(_applied ? Theme.Success : Theme.Border),
                GdiCache.RoundRect(new Rectangle(0, 0, 3, Math.Max(1, Height - 28)), 2));
            g.Restore(barState);

            // 行首领域图标块：按分组着色。234 行若都是"文字 + 胶囊 + 开关"，
            // 扫视时找不到落点；有了颜色与图形，领域可以一眼分辨。
            string group = Tweak.Group == null ? "" : Tweak.Group;
            Color tone = ToneOfGroup(group);
            Rectangle iconBox = new Rectangle(14, (Height - 30) / 2, 30, 30);
            Gfx.FillRound(g, iconBox, Theme.RadiusChip, Gfx.Alpha(tone, _applied ? 64 : 32));
            IconPainter.Draw(g, IconOf(Tweak),
                new Rectangle(iconBox.X + 9, iconBox.Y + 9, 12, 12),
                _applied ? tone : Gfx.Blend(Theme.TextMuted, tone, 0.55));

            // 文本区右边界与右侧「风险徽章 + 开关」联动避让
            int leftmost = _badge.Left;
            int textRight = Math.Max(60, leftmost - 16);
            int textX = iconBox.Right + 10;

            Gfx.DrawTextEllipsis(g, Tweak.Name, Theme.FontBodyBold, Theme.TextPrimary,
                new Rectangle(textX, 9, textRight - textX, 22));

            // 行内只写"作用"这一句；点开右侧详情栏看完整解释。
            string brief = Brief(Tweak.Description);

            // 悬停时把描述行换成操作提示：行点击 = 打开四段式详情。
            // 不提示的话用户不会知道行本身可点（页面上显式的可交互控件只有开关）。
            if (_hover)
            {
                Gfx.DrawTextEllipsis(g, "点击查看作用说明",
                    Theme.FontSmall, Theme.Accent, new Rectangle(textX, 31, textRight - textX, 18));
            }
            else if (_twoLine)
            {
                // 罕见的长摘要：用自动换行代替省略号
                using (StringFormat sf = new StringFormat())
                {
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.Alignment = StringAlignment.Near;
                    sf.LineAlignment = StringAlignment.Near;
                    g.DrawString(brief, Theme.FontSmall, GdiCache.Brush(Theme.TextMuted),
                        new Rectangle(textX, 31, textRight - textX, 36), sf);
                }
            }
            else
            {
                Gfx.DrawTextEllipsis(g, brief, Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(textX, 31, textRight - textX, 18));
            }
        }
    }

    /// <summary>风险档（行内 chip 与详情栏 chip 共用同一判定，四档四色）。</summary>
    internal enum TweakRiskLevel
    {
        Safe = 0,       // 安全：绿 Theme.Success
        Recommended,    // 推荐：蓝 Theme.Accent
        Caution,        // 谨慎：琥珀 Theme.Warning
        Danger          // 危险：红 Theme.Danger
    }

    /// <summary>风险判定：安全 / 推荐 / 谨慎 / 危险。行内与右栏共用，避免两处各自打分不一致。</summary>
    internal static class TweakRiskBadge
    {
        public static TweakRiskLevel Of(ITweak t)
        {
            if (t == null) return TweakRiskLevel.Safe;
            if (t.Risky)
            {
                // 「极限性能」组（关 Defender / 防火墙 / 内核缓解等）不可滥用红色，单独标记「危险」。
                if (string.Equals(t.Group, TweakLibrary.GExtreme, StringComparison.Ordinal))
                    return TweakRiskLevel.Danger;
                return TweakRiskLevel.Caution;
            }
            if (t.Recommended) return TweakRiskLevel.Recommended;
            return TweakRiskLevel.Safe;
        }

        public static string Label(ITweak t)
        {
            switch (Of(t))
            {
                case TweakRiskLevel.Recommended: return "推荐";
                case TweakRiskLevel.Caution: return "谨慎";
                case TweakRiskLevel.Danger: return "危险";
                default: return "安全";
            }
        }

        public static Color Color(ITweak t)
        {
            switch (Of(t))
            {
                case TweakRiskLevel.Recommended: return Theme.Accent;
                case TweakRiskLevel.Caution: return Theme.Warning;
                case TweakRiskLevel.Danger: return Theme.Danger;
                default: return Theme.Success;
            }
        }
    }
}
