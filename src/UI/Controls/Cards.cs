﻿/* ============================================================
 * 文件说明：统计与提示：NoticeBar（页内提示条）/ StatCard（统计卡）/ StatStrip（横向统计条）。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{

    /// <summary>
    /// 主题化的信息提示条（34 个页面共用）。
    ///
    /// 外观设计（一轮重做，此前"发脏"的原因写在各自注释里）：
    /// · 淡强调色底面 + 中性发丝边：一层语义只用一种表达，不再叠「左色块 + 强调色描边」；
    /// · 22px 圆底图标：给每页提示条一个稳定的视觉起点与重心；
    /// · 正文用主文本色：提示的价值在于被读到，灰色看着像禁用；
    /// · 关闭按钮 24×24 命中区 + 悬停底色：不再是一个 14px 的裸 ×。
    ///
    /// UX 行为不变：默认单行（38px），点关后按所在页面记忆
    /// （AppSettings.IsNoticeDismissed），此后不再出现，避免长期挡视野。
    /// 关闭记忆的 key 自动取宿主页面的类名（向上遍历 Parent 链找 ViewBase），页面无需任何配置。
    /// </summary>
    public class NoticeBar : RoundPanel
    {
        private string _text = "";
        private Color _accent = Theme.Warning;
        private string _icon = "info";
        private bool _clickable;
        private bool _hoverClose;          // 鼠标是否悬停在关闭按钮上
        private bool _memoryChecked;       // 是否已做过"关闭过"检查（防重复）

        public NoticeBar()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusItem;
            Height = 38;
            CornerColor = Theme.WindowBg;
            // 中性发丝边：原先写「强调色 70 透明」的描边，叠在同色系底上会混成脏色
            BorderColor = Theme.GlassBorder;
            Highlight = false;
        }

        public string NoticeText
        {
            get { return _text; }
            set { _text = value == null ? "" : value; Recompute(); }
        }

        public Color NoticeAccent
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public string NoticeIcon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool Clickable
        {
            get { return _clickable; }
            set { _clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; }
        }

        /// <summary>
        /// 关闭按钮命中区（右上角 24×24）。
        /// 此前是 14×14 且直接把半透明的 × 画在上面：又小又没有悬停反馈，看着像误触按钮。
        /// </summary>
        private Rectangle CloseRect
        {
            get { return new Rectangle(Width - 36, (Height - 24) / 2, 24, 24); }
        }

        /// <summary>高度自适应后触发（ViewBase 借此强制整栏重排，避免后续行停在过期坐标）。</summary>
        public event EventHandler PreferredHeightChanged;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recompute();
        }

        /// <summary>按当前宽度重测提示文字高度：一行 34px，超宽自动换行并增高；高度变化触发父容器重排。</summary>
        private void Recompute()
        {
            if (Width <= 0 || Parent == null) return; // 未挂载时宽度是 200px 默认值，测出来必错
            // 可用宽度：左侧 = 13 内边距 + 22 图标 + 10 间距；右侧给关闭按钮留 40
            int avail = Math.Max(10, Width - 86);
            Size sz = TextRenderer.MeasureText(_text, Theme.FontSmall,
                new Size(avail, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            int single = Theme.FontSmall.Height + 10;
            int target = sz.Height <= single ? 38 : sz.Height + 14;
            if (Height != target)
            {
                Height = target;
                if (Parent != null) Parent.PerformLayout();
                if (PreferredHeightChanged != null) PreferredHeightChanged(this, EventArgs.Empty);
            }
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CheckDismissedMemory();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            CheckDismissedMemory(); // 行挂载晚于句柄创建时的兜底
        }

        /// <summary>若用户在此页面关闭过提示：隐藏自身（提示条直挂 Body 时绝不能动 Parent——那会藏掉整个内容区）。</summary>
        private void CheckDismissedMemory()
        {
            if (_memoryChecked) return;
            _memoryChecked = true;
            string pageId = FindPageId();
            if (pageId != null && AppSettings.IsNoticeDismissed(pageId))
            {
                Visible = false; // FlowLayoutPanel 会跳过隐藏控件的占位，不留空隙
            }
        }

        /// <summary>向上遍历父链找宿主页面（ViewBase），用其类名作关闭记忆 key。</summary>
        private string FindPageId()
        {
            Control c = Parent;
            while (c != null)
            {
                if (c is ViewBase) return c.GetType().Name;
                c = c.Parent;
            }
            return null;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverClose) { _hoverClose = false; Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = CloseRect.Contains(e.Location);
            if (over != _hoverClose)
            {
                _hoverClose = over;
                Cursor = over || _clickable ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location))
            {
                // 只藏自己：提示条可能直接挂在 Body（整个内容容器）上，
                // 动 Parent 会把整页内容藏掉（"系统概览没了"事故的根因）
                Visible = false;
                string pageId = FindPageId();
                if (pageId != null) AppSettings.MarkNoticeDismissed(pageId); // 永久记忆
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 描边固定为中性发丝边：原先每帧按强调色改 BorderColor，
            // 强调色半透描边叠在同色系底面上会混出一圈脏边
            BorderColor = Theme.GlassBorder;
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 1) 淡强调色底面：整条铺一层极低透明度的强调色（内缩 1px，让发丝边留着）。
            //    取代原来的「左侧 3px 色块 + 强调色描边」——同一层语义只留一种表达：
            //    色块与图标重复表意，描边又与卡片底混色，是这条提示看着"脏"的主因。
            Rectangle inner = new Rectangle(1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
            Gfx.FillRound(g, inner, Radius - 1, Gfx.Alpha(_accent, Theme.IsLight ? 16 : 22));

            // 2) 图标底座：22px 圆底（强调色淡填充）+ 内嵌 14px 图标。
            //    比"裸图标贴在文字左侧"更有重心，各页提示条的起点也因此一致。
            int chip = 22;
            Rectangle chipRect = new Rectangle(13, (Height - chip) / 2, chip, chip);
            using (SolidBrush chipBg = new SolidBrush(Gfx.Alpha(_accent, Theme.IsLight ? 38 : 52)))
            {
                g.FillEllipse(chipBg, chipRect);
            }
            IconPainter.Draw(g, _icon,
                new Rectangle(chipRect.X + 4, chipRect.Y + 4, chip - 8, chip - 8), _accent);

            // 3) 正文用主文本色：此前是次要灰，压在淡色底上像"禁用文字"，
            //    而提示恰恰是需要被读到的内容。超宽自动换行，整句可见。
            Rectangle textRect = new Rectangle(chipRect.Right + 10, 0,
                Math.Max(10, Width - chipRect.Right - 10 - 40), Height);
            TextRenderer.DrawText(g, _text, Theme.FontSmall, textRect, Theme.TextPrimary,
                TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter
                | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.ExternalLeading);

            // 4) 关闭：常驻 24×24 命中区，悬停时整块给底色反馈 + × 提亮
            //    （原先只有 14px 的 × 自己变色，命中区小、反馈弱）
            if (_hoverClose) Gfx.FillRound(g, CloseRect, 7, Gfx.Alpha(_accent, Theme.IsLight ? 30 : 40));
            Pen closePen = GdiCache.Pen(_hoverClose ? Theme.TextPrimary : Gfx.Alpha(Theme.TextMuted, 200), 1.5f);
            {
                Rectangle r = CloseRect;
                int m = 8;
                g.DrawLine(closePen, r.Left + m, r.Top + m, r.Right - m, r.Bottom - m);
                g.DrawLine(closePen, r.Right - m, r.Top + m, r.Left + m, r.Bottom - m);
            }
        }
    }
    /// <summary>指标卡：图标 + 标题 + 大号数值 + 占用条 + 脚注。</summary>
    public class StatCard : RoundPanel
    {
        public string MetricText = "--";
        public string CaptionText = "";
        public string IconKind = "cpu";
        public double Percent = -1;
        public Color AccentColor = Theme.Accent;
        public string FooterText = "";

        private double _shownPercent = -1;   // 进度条当前显示值
        private double _percentFrom;         // 进度条动画起点
        private string _shownMetric;         // 指标文本当前显示值（数字滚动）
        private double _numFrom, _numTo;     // 数字滚动起止
        private string _numSuffix = "";      // 数字后缀（%、ms、GB 等）
        private bool _barAnim, _numAnim;
        private float _animPos;

        public StatCard()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;   // 与全站卡片同圆角，不再单独写 12
            Height = 128;
        }

        public void SetData(string caption, string metric, double percent, string footer, Color accent)
        {
            CaptionText = caption;
            string oldShown = _shownMetric ?? MetricText;
            MetricText = metric;
            Percent = percent;
            FooterText = footer;
            AccentColor = accent;
            StartBarAnim();
            StartNumAnim(oldShown, metric);
            Invalidate();
        }

        /// <summary>拆出前导数字与后缀（如 "45 %" → 45 / " %"）；无前导数字返回 false。</summary>
        private static bool TrySplit(string text, out double num, out string suffix)
        {
            num = 0; suffix = "";
            if (string.IsNullOrEmpty(text)) return false;
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(text, @"^\s*(-?\d+(?:\.\d+)?)\s*(.*)$");
            if (!m.Success) return false;
            bool ok = double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out num);
            suffix = m.Groups[2].Value;
            return ok;
        }

        private void StartBarAnim()
        {
            if (!AppSettings.Animations || Percent < 0 || _shownPercent < 0 ||
                Math.Abs(_shownPercent - Percent) < 0.5)
            {
                _shownPercent = Percent;
                return;
            }
            _percentFrom = _shownPercent;
            _barAnim = true;
            StartCardAnim();
        }

        /// <summary>指标数字滚动：旧值 → 新值插值，后缀保持。</summary>
        private void StartNumAnim(string oldShown, string target)
        {
            double a, b; string aSuf, bSuf;
            bool oldNum = TrySplit(oldShown ?? "", out a, out aSuf);
            bool newNum = TrySplit(target ?? "", out b, out bSuf);
            if (!AppSettings.Animations || !oldNum || !newNum || Math.Abs(a - b) < 0.01)
            {
                _shownMetric = target;
                _numAnim = false;
                return;
            }
            _numFrom = a; _numTo = b; _numSuffix = bSuf;
            _shownMetric = a.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + bSuf;
            _numAnim = true;
            StartCardAnim();
        }

        /// <summary>统一动画钟：进度条与数字滚动共用一条 EaseOutCubic 时间线；由全局 AnimationClock 驱动。</summary>
        private void StartCardAnim()
        {
            // 每次（重新）启动动画都把时间线归零：否则完成后面 _animPos 停在 1.0，
            // 连续 SetData（仪表盘实时刷新）会让后续动画首帧即 done、只瞬跳到终值不播放过渡。
            _animPos = 0f;
            AnimationClock.Instance.Subscribe(CardTick);
        }

        private void CardTick()
        {
            _animPos += 0.14f;
            bool done = _animPos >= 1f;
            if (done) _animPos = 1f;
            float t = Theme.Ease.CubicOut(_animPos);

            if (_barAnim)
            {
                _shownPercent = _percentFrom + (Percent - _percentFrom) * t;
                if (done) _barAnim = false;
            }
            if (_numAnim)
            {
                double cur = _numFrom + (_numTo - _numFrom) * t;
                _shownMetric = cur.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + _numSuffix;
                if (done) _numAnim = false;
            }

            Invalidate();
            if (done && !_barAnim && !_numAnim) AnimationClock.Instance.Unsubscribe(CardTick);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) AnimationClock.Instance.Unsubscribe(CardTick);
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 图标底
            Rectangle iconBox = new Rectangle(16, 14, 28, 28);
            Gfx.FillRound(g, iconBox, Theme.RadiusItem, Gfx.Alpha(AccentColor, 36));
            IconPainter.Draw(g, IconKind, new Rectangle(23, 21, 14, 14), AccentColor);

            // 数值滚动动画期间这里每 16ms 重绘一次，画刷改用缓存对象
            Gfx.DrawTextEllipsis(g, CaptionText, Theme.FontSmall, Theme.TextSecondary,
                new Rectangle(52, 18, Math.Max(10, Width - 68), 20));
            g.DrawString(MetricText, Theme.FontMetric, GdiCache.Brush(Theme.TextPrimary), 12, 44);

            int barY = Height - 32;
            Rectangle track = new Rectangle(16, barY, Math.Max(10, Width - 32), 6);
            Gfx.FillRound(g, track, 3, Theme.CardBgAlt);
            if (Percent >= 0)
            {
                double shown = _shownPercent < 0 ? Percent : _shownPercent;
                int w = (int)(track.Width * Math.Min(100, Math.Max(0, shown)) / 100.0);
                if (w < 6) w = 6;
                Gfx.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), 3, AccentColor);
            }

            if (!string.IsNullOrEmpty(FooterText))
            {
                Gfx.DrawTextEllipsis(g, FooterText, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(16, Height - 22, Math.Max(10, Width - 32), 16));
            }
        }
    }
    /// <summary>横向统计条：一行放多组「标签 / 数值」。</summary>
    public class StatStrip : RoundPanel
    {
        public sealed class Item
        {
            public string Label = "";
            public string Value = "";

            /// <summary>
            /// 数值色；Color.Empty 表示「跟随主题默认前景色」（绘制时实时解析）。
            /// 不可用 Theme.TextPrimary 作字段初始化器——那会把加项当刻的方案色快照进来，
            /// 切换到浅色方案后仍是深色方案的近白色，落在白色卡片上就是白底白字。
            /// </summary>
            public Color ValueColor = Color.Empty;
        }

        private readonly List<Item> _items = new List<Item>();

        public string Caption = "";
        public string IconKind = "";
        public Color CaptionColor = Theme.Accent;

        public StatStrip()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;   // 同上：圆角走 token，避免同页两种圆角
        }

        public int PreferredHeight
        {
            get { return string.IsNullOrEmpty(Caption) ? 76 : 108; }
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>所属页面（挂到可视树时解析一次，用来判断"正在加载"还是"加载完仍没数据"）。</summary>
        private GuyueBox.UI.Views.ViewBase _owner;

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            _owner = null;
            Control p = Parent;
            while (p != null)
            {
                GuyueBox.UI.Views.ViewBase v = p as GuyueBox.UI.Views.ViewBase;
                if (v != null) { _owner = v; break; }
                p = p.Parent;
            }
        }

        private bool OwnerBusy()
        {
            return _owner != null && _owner.IsBusy;
        }

        /// <summary>加一项，数值使用主题默认前景色（绘制时解析，自动跟随主题切换）。</summary>
        public void Add(string label, string value)
        {
            Add(label, value, Color.Empty);
        }

        public void Add(string label, string value, Color valueColor)
        {
            Item it = new Item();
            it.Label = label == null ? "" : label;
            it.Value = value == null ? "" : value;
            it.ValueColor = valueColor;
            _items.Add(it);
        }

        /// <summary>
        /// 方案切换后重解析本控件的主题相关颜色。
        /// Item.ValueColor / CaptionColor 是「加项时快照」进字段的值，不经控件的
        /// BackColor/ForeColor 重映射（见 ThemeSkin.Reload），必须显式再做一次 旧色→新色 映射，
        /// 否则会残留旧方案的颜色（浅色下表现为白底白字）。
        /// </summary>
        internal void RemapThemeColors(Color[] from, Color[] to)
        {
            if (from == null || to == null) return;

            CaptionColor = RemapOne(CaptionColor, from, to);
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].ValueColor = RemapOne(_items[i].ValueColor, from, to);
            }
            Invalidate();
        }

        private static Color RemapOne(Color c, Color[] from, Color[] to)
        {
            if (c.IsEmpty) return c; // 跟随默认前景色，绘制时解析
            int cur = c.ToArgb();
            int n = Math.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
            {
                if (cur == from[i].ToArgb()) return to[i];
            }
            return c;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int top = 0;
            if (!string.IsNullOrEmpty(Caption))
            {
                top = Card.HeaderSize;
                int x = 16;
                if (!string.IsNullOrEmpty(IconKind))
                {
                    Rectangle box = new Rectangle(16, 13, 22, 22);
                    Gfx.FillRound(g, box, Theme.RadiusChip, Gfx.Alpha(CaptionColor, 32));
                    IconPainter.Draw(g, IconKind, new Rectangle(20, 17, 14, 14), CaptionColor);
                    x = 46;
                }
                SolidBrush b = GdiCache.Brush(Theme.TextPrimary);
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Caption, Theme.FontBodyBold, b,
                        new Rectangle(x, 12, Math.Max(10, Width - x - 18), 24), sf);
                }
                Pen p = GdiCache.Pen(Theme.BorderSoft, 1f);
                {
                    g.DrawLine(p, 16, Card.HeaderSize - 3, Width - 17, Card.HeaderSize - 3);
                }
            }

            if (_items.Count == 0)
            {
                // 数据项为空时不留下大片空白：不少页面要等数据加载完成才 Add 数据项，
                // 这段空窗期若只画标题，就是一张「只有标题的空白卡片」，看起来像界面坏了。
                //
                // 但文案要跟着真实状态走：页面正在忙 → 「数据加载中…」；
                // 加载已结束仍无数据（失败 / 本机不支持） → 「暂无数据」。
                // 此前一律写死「数据加载中…」，失败时卡片会永远停在这一句，像卡死了。
                bool busy = OwnerBusy();
                Gfx.DrawTextEllipsis(g, busy ? "数据加载中…" : "暂无数据", Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(16, top + Math.Max(0, (Height - top) / 2 - 10), Math.Max(40, Width - 32), 20));
                return;
            }

            int area = Height - top;
            int colWidth = Math.Max(60, (Width - 32) / _items.Count);

            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                int x = 16 + i * colWidth;

                Gfx.DrawTextEllipsis(g, it.Label, Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(x, top + area / 2 - 22, colWidth - 12, 18));

                Color valueColor = it.ValueColor.IsEmpty ? Theme.TextPrimary : it.ValueColor;
                Gfx.DrawTextEllipsis(g, it.Value, Theme.FontSubTitle, valueColor,
                    new Rectangle(x, top + area / 2 - 2, colWidth - 12, 24));
            }
        }
    }
}
