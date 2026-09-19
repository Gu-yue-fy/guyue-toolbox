/* ============================================================
 * 文件说明：Toast 反馈浮层（对齐设计规则 2.1 / 2.4 / 3.9）：
 *           - 底部居中、不抢焦点、不打断当前操作（区别于 MessageBox）；
 *           - 主句说明"做了什么"，副句说明"细节/下一步"，两层信息不挤在一行；
 *           - 自动消失（成功 2.4s / 需注意 3.4s），点击立即关闭；
 *           - 栈式最多 3 条，超出丢弃最旧的。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>Toast 语义：决定图标与配色，也决定自动消失时长。</summary>
    public enum ToastKind
    {
        Success,
        Info,
        Warning,
        Danger
    }

    /// <summary>
    /// Toast 宿主：挂在主窗体上、覆盖内容区底部居中。
    /// 仅在存在可见 Toast 时运行定时器，空闲不占 CPU。
    /// </summary>
    internal sealed class ToastHost : Control
    {
        private sealed class Item
        {
            public string Title = "";
            public string Sub = "";
            public string Icon = "info";
            public Color Tone = Theme.Accent;
            public int LifeMs;
            public int ElapsedMs;
            public int FadeInMs = 160;
            public int FadeOutMs = 200;
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly Timer _timer;

        private const int MaxItems = 3;
        private const int PadY = 12;
        private const int Gap = 10;
        /// <summary>默认宽度。不可命名为 Width——那会隐藏 Control.Width。</summary>
        private const int ToastWidth = 400;

        public ToastHost()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            // 不进入 Tab 序列、不接受焦点：反馈浮层不该打断用户
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            Width = ToastWidth;

            _timer = new Timer();
            _timer.Interval = 40;
            _timer.Tick += delegate { Tick(); };
        }

        /// <summary>显示一条 Toast。副句可留空。</summary>
        public void Show(string title, string sub, ToastKind kind)
        {
            if (string.IsNullOrEmpty(title)) return;

            Item it = new Item();
            it.Title = title;
            it.Sub = sub == null ? "" : sub;
            it.Icon = IconOf(kind);
            it.Tone = ToneOf(kind);
            // 成功/信息读得快，需注意类多留一会儿（设计：2.2s ~ 3.2s）
            it.LifeMs = (kind == ToastKind.Success || kind == ToastKind.Info) ? 2400 : 3400;

            _items.Add(it);
            while (_items.Count > MaxItems) _items.RemoveAt(0);

            Relayout();
            if (!_timer.Enabled) _timer.Start();
            Visible = true;
            BringToFront();
            Invalidate();
        }

        private static Color ToneOf(ToastKind kind)
        {
            switch (kind)
            {
                case ToastKind.Success: return Theme.Success;
                case ToastKind.Warning: return Theme.Warning;
                case ToastKind.Danger: return Theme.Danger;
                default: return Theme.Accent;
            }
        }

        private static string IconOf(ToastKind kind)
        {
            switch (kind)
            {
                case ToastKind.Success: return "check";
                case ToastKind.Warning: return "warn";
                case ToastKind.Danger: return "close";
                default: return "info";
            }
        }

        private void Tick()
        {
            bool changed = false;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Item it = _items[i];
                it.ElapsedMs += _timer.Interval;
                if (it.ElapsedMs >= it.LifeMs + it.FadeOutMs)
                {
                    _items.RemoveAt(i);
                    changed = true;
                }
            }

            if (_items.Count == 0)
            {
                _timer.Stop();
                Visible = false;
                return;
            }

            if (changed) Relayout();
            Invalidate();
        }

        /// <summary>按条数调整自身高度（自底向上堆叠，故高度 = 全部条目 + 间距）。</summary>
        private void Relayout()
        {
            int h = 0;
            for (int i = 0; i < _items.Count; i++) h += ItemHeight(_items[i]) + Gap;
            if (h > 0) h -= Gap;
            if (Height != h && Parent != null)
            {
                Height = h;
                // 贴住内容区底部：高度变化后重新定位（由 MainForm 提供对齐回调）
                EventHandler resize = Resized;
                if (resize != null) resize(this, EventArgs.Empty);
            }
            else
            {
                Height = h;
            }
        }

        /// <summary>高度变化时通知宿主重新定位（避免 Toast 长出来后压出可视区）。</summary>
        public event EventHandler Resized;

        private static int ItemHeight(Item it)
        {
            return PadY * 2 + (it.Sub.Length > 0 ? 40 : 22);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            // 点击任意一条即关闭该条：不提供"关闭按钮"，减少视觉噪音
            int y = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = ItemHeight(_items[i]);
                if (e.Y >= y && e.Y < y + h)
                {
                    _items.RemoveAt(i);
                    if (_items.Count == 0) { _timer.Stop(); Visible = false; }
                    else { Relayout(); Invalidate(); }
                    return;
                }
                y += h + Gap;
            }
            base.OnMouseUp(e);
        }

        /// <summary>
        /// 把颜色换成"已合成的实色"：保留 RGB、把 alpha 落到指定透明度。
        /// 用于自绘时手动做淡入淡出，避免半透明叠加到错误的父级底色上。
        /// </summary>
        private static Color Opaque(Color c, int alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_items.Count == 0) return;

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int y = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                int h = ItemHeight(it);

                // 淡入 / 淡出：只在两端改变整体透明度，中间保持稳定可读
                double alpha = 1.0;
                if (it.ElapsedMs < it.FadeInMs)
                {
                    alpha = it.ElapsedMs / (double)it.FadeInMs;
                }
                else if (it.ElapsedMs > it.LifeMs)
                {
                    double t = (it.ElapsedMs - it.LifeMs) / (double)it.FadeOutMs;
                    alpha = 1.0 - (t > 1 ? 1 : t);
                }
                if (alpha <= 0.02) { y += h + Gap; continue; }
                int a = (int)(alpha * 255);

                // 颜色全部自己合成，不能依赖"多层半透明叠加"：
                // 本控件的父级是主窗体（背景为外壳色），而非页面内容，
                // 半透明会与外壳色混合出错误的底色。且玻璃色本身是"白 + 低 alpha"，
                // 直接把 alpha 改写成不透明会得到纯白底——暗色主题下就成了白底白字。
                Color card = Opaque(Theme.CardBg, a);
                Color tone = Opaque(it.Tone, a);
                Color toneSoft = Opaque(Gfx.Blend(Theme.CardBg, it.Tone, 0.24), a);
                Color border = Opaque(Gfx.Blend(Theme.CardBg, it.Tone, 0.55), a);

                Rectangle box = new Rectangle(0, y, Math.Max(80, Width - 1), h - 1);
                Gfx.FillRound(g, box, Theme.RadiusCard, card);
                Gfx.StrokeRound(g, box, Theme.RadiusCard, border, 1f);

                // 左侧语义色条：让人一眼分辨结果类型
                Gfx.FillRound(g, new Rectangle(box.X, box.Y + 10, 3, box.Height - 20), 2, tone);

                Rectangle iconBox = new Rectangle(box.X + 16, box.Y + PadY + 1, 20, 20);
                Gfx.FillRound(g, iconBox, 10, toneSoft);
                IconPainter.Draw(g, it.Icon, new Rectangle(iconBox.X + 5, iconBox.Y + 5, 10, 10),
                    Opaque(Gfx.Blend(Theme.CardBg, it.Tone, 0.9), a));

                int textX = iconBox.Right + 12;
                int textW = Math.Max(60, box.Right - textX - 16);
                Gfx.DrawTextEllipsis(g, it.Title, Theme.FontBodyBold, Opaque(Theme.TextPrimary, a),
                    new Rectangle(textX, box.Y + PadY - 1, textW, 20));
                if (it.Sub.Length > 0)
                {
                    Gfx.DrawTextEllipsis(g, it.Sub, Theme.FontSmall, Opaque(Theme.TextSecondary, a),
                        new Rectangle(textX, box.Y + PadY + 19, textW, 18));
                }

                y += h + Gap;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) _timer.Stop();
            base.Dispose(disposing);
        }
    }
}
