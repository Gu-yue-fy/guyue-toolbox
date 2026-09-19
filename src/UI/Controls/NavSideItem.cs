using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{
    /// <summary>
    /// 侧栏分组标题。对齐设计 GroupHeader：11px 中等字重、TextSecondary 色、
    /// 左侧缩进 18px、上方留白宽松下方紧凑（靠下对齐）。
    /// </summary>
    public class NavSideGroup : Control
    {
        public NavSideGroup(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            BackColor = Theme.SidebarBg;
            Height = 34;
            Dock = DockStyle.Top; // 在侧栏 FlowLayoutPanel 中铺满行宽，否则分组标题宽度为 0 不可见
            Font = Theme.FontSmall;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.FillRectangle(GdiCache.Brush(Theme.SidebarBg), ClientRectangle);

            using (StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Far })
            {
                g.DrawString(Text, Font, GdiCache.Brush(Theme.NavGroupText),
                    new Rectangle(18, 0, Math.Max(10, Width - 26), Math.Max(1, Height - 8)), sf);
            }
        }
    }

    /// <summary>
    /// 侧栏导航项。对齐设计 GlassPillNavItem：
    /// 36px 高、左右内缩 8px、8px 圆角胶囊；悬停为 5% 白叠加，选中为竖向蓝色渐变玻璃 +
    /// 渐变描边 + 26px 图标底 + 左侧 2px 强调状态条；文字色随状态在 Secondary/Primary 间过渡。
    /// 有意不做外发光——设计的设计约束是「层级靠色调而非发光」。
    /// </summary>
    public class NavSideItem : Control
    {
        private readonly string _icon;
        private bool _hover;
        private bool _pressed;
        private bool _selected;
        private int _selAlpha;   // 选中态进度 0..255
        private int _hoverAlpha; // 悬停态进度 0..255
        private System.Windows.Forms.Timer _anim;

        public NavSideItem(string text, string icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            _icon = icon;
            BackColor = Theme.SidebarBg;
            Height = Theme.ItemHeight; // 36，与设计 Token.Size.ButtonHeight 一致
            Cursor = Cursors.Hand;
            Font = Theme.FontNav;
            A11y.MakeFocusable(this, AccessibleRole.ListItem);
            AccessibleName = text == null ? "" : text;
        }

        /// <summary>键盘可达：Enter 立即跳转，空格抬起时跳转。</summary>
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

        private void StartFade()
        {
            if (!AppSettings.Animations)
            {
                _selAlpha = _selected ? 255 : 0;
                _hoverAlpha = _hover ? 255 : 0;
                Invalidate();
                return;
            }
            if (_anim == null)
            {
                _anim = new System.Windows.Forms.Timer { Interval = 16 };
                _anim.Tick += delegate
                {
                    bool done = true;
                    int sd = (_selected ? 255 : 0) - _selAlpha;
                    if (sd != 0)
                    {
                        _selAlpha += Math.Sign(sd) * Math.Min(Math.Abs(sd), Math.Max(20, Math.Abs(sd) / 3));
                        done = false;
                    }
                    int hd = (_hover ? 255 : 0) - _hoverAlpha;
                    if (hd != 0)
                    {
                        _hoverAlpha += Math.Sign(hd) * Math.Min(Math.Abs(hd), 48);
                        done = false;
                    }
                    Invalidate();
                    if (done) _anim.Stop();
                };
            }
            _anim.Start();
        }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                StartFade();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            StartFade();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            StartFade();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        /// <summary>按进度缩放令牌自带的不透明度（令牌本身就是半透明叠加色）。</summary>
        private static Color Scaled(Color c, int progress)
        {
            int a = c.A * progress / 255;
            if (a <= 0) return Color.Transparent;
            return Color.FromArgb(a, c);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);
            g.FillRectangle(GdiCache.Brush(Theme.SidebarBg), ClientRectangle);

            // 设计导航胶囊：左右各内缩 8px，高度占满 36px 行高
            Rectangle pill = new Rectangle(8, 0, Math.Max(20, Width - 16), Height);

            // 悬停：5% 白叠加（选中态已足够醒目，故选中时不再叠悬停）
            if (_hoverAlpha > 0 && _selAlpha < 255)
            {
                Gfx.FillRound(g, pill, Theme.RadiusNav, Scaled(Theme.NavHover, _hoverAlpha));
            }

            // 选中：竖向蓝色玻璃渐变 + 渐变描边 + 左侧 2px 强调状态条
            if (_selAlpha > 0)
            {
                Rectangle gr = new Rectangle(pill.X, pill.Y, Math.Max(1, pill.Width), Math.Max(1, pill.Height));
                using (LinearGradientBrush lb = new LinearGradientBrush(gr,
                    Scaled(Theme.NavSelTop, _selAlpha), Scaled(Theme.NavSelBottom, _selAlpha), 90f))
                using (GraphicsPath path = Gfx.RoundRect(pill, Theme.RadiusNav))
                {
                    g.FillPath(lb, path);
                }
                Gfx.StrokeRound(g, pill, Theme.RadiusNav, Scaled(Theme.NavSelBorderTop, _selAlpha), 1f);

                int barH = 20;
                Rectangle bar = new Rectangle(pill.X, pill.Y + (pill.Height - barH) / 2, 2, barH);
                g.FillRectangle(GdiCache.Brush(Gfx.Alpha(Theme.Accent, _selAlpha)), bar);
            }

            // 按下：设计用实底 NavItemPressed 整体替换（含选中态）
            if (_pressed)
            {
                Gfx.FillRound(g, pill, Theme.RadiusNav, Theme.NavPressed);
            }

            Color text = (_selected || _hover) ? Theme.TextPrimary : Theme.TextSecondary;

            // 图标：26×26 图标底（选中时约 12% 强调色底），图标本体 16×16
            Rectangle box = new Rectangle(pill.X + 10, (Height - 26) / 2, 26, 26);
            if (_selAlpha > 0)
            {
                Gfx.FillRound(g, box, Theme.RadiusChip, Scaled(Theme.NavSelIconBg, _selAlpha));
            }
            IconPainter.Draw(g, _icon,
                new Rectangle(box.X + 5, box.Y + 5, 16, 16),
                _selected ? Theme.Accent : text);

            using (StringFormat sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                g.DrawString(Text, Font, GdiCache.Brush(text),
                    new Rectangle(box.Right + 10, 0, Math.Max(20, pill.Right - box.Right - 18), Height), sf);
            }

            if (Focused) A11y.DrawFocusRing(g, pill, Theme.RadiusNav);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) _anim.Dispose();
            base.Dispose(disposing);
        }
    }
}
