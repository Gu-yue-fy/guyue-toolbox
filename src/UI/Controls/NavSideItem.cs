using System;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{
    /// <summary>侧栏分组标题。</summary>
    public class NavSideGroup : Control
    {
        public NavSideGroup(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            BackColor = Theme.ChromeBg;
            Height = 24;
            Dock = DockStyle.Top; // 在侧栏 FlowLayoutPanel 中铺满行宽，否则分组标题宽度为 0 不可见
            Font = Theme.FontSmall;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.FillRectangle(GdiCache.Brush(Theme.ChromeBg), ClientRectangle);
            g.DrawString(Text, Font, GdiCache.Brush(Theme.TextMuted),
                new Rectangle(14, 0, Math.Max(10, Width - 20), Height),
                new StringFormat { LineAlignment = StringAlignment.Center });
        }
    }

    /// <summary>侧栏导航项：悬停高亮、选中主题色标识，均为缓动淡入而非硬切。</summary>
    public class NavSideItem : Control
    {
        private readonly string _icon;
        private bool _hover;
        private bool _selected;
        private int _selAlpha;    // 选中高亮当前 alpha（0..52）
        private int _hoverAlpha;  // 悬停高亮强度（0..255 的百分比基准）
        private System.Windows.Forms.Timer _anim;

        public NavSideItem(string text, string icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            _icon = icon;
            BackColor = Theme.ChromeBg;
            Height = 32;
            Cursor = Cursors.Hand;
            Font = Theme.FontBody;
        }

        private void StartFade()
        {
            if (!AppSettings.Animations)
            {
                _selAlpha = _selected ? 52 : 0;
                _hoverAlpha = _hover ? 100 : 0;
                Invalidate();
                return;
            }
            if (_anim == null)
            {
                _anim = new System.Windows.Forms.Timer { Interval = 16 };
                _anim.Tick += delegate
                {
                    bool done = true;
                    int selTarget = _selected ? 52 : 0;
                    int sd = selTarget - _selAlpha;
                    if (sd != 0)
                    {
                        int s = Math.Sign(sd) * Math.Min(Math.Abs(sd), Math.Max(2, Math.Abs(sd) / 3));
                        _selAlpha += s;
                        done = false;
                    }
                    int hovTarget = _hover ? 100 : 0;
                    int hd = hovTarget - _hoverAlpha;
                    if (hd != 0)
                    {
                        int s = Math.Sign(hd) * Math.Min(Math.Abs(hd), 12);
                        _hoverAlpha += s;
                        done = false;
                    }
                    Invalidate();
                    if (done) { _anim.Stop(); }
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
            StartFade();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);
            g.FillRectangle(GdiCache.Brush(Theme.ChromeBg), ClientRectangle);

            Rectangle r = new Rectangle(6, 1, Width - 12, Height - 2);
            if (_selAlpha > 0)
            {
                using (System.Drawing.Drawing2D.GraphicsPath path = Gfx.RoundRect(r, 7))
                {
                    using (SolidBrush b = new SolidBrush(Gfx.Alpha(Theme.Accent, _selAlpha)))
                    {
                        g.FillPath(b, path);
                    }
                    using (Pen pen = new Pen(Gfx.Alpha(Theme.Accent, Math.Min(150, _selAlpha * 3)), 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
            if (_hoverAlpha > 0)
            {
                using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(r, 7))
                using (SolidBrush b = new SolidBrush(Gfx.Alpha(Theme.CardHover, Math.Min(255, _hoverAlpha * 255 / 100))))
                {
                    g.FillPath(b, p);
                }
            }

            Color text = _selected ? Theme.Accent : (_hover ? Theme.TextPrimary : Theme.TextSecondary);
            Rectangle iconRect = new Rectangle(r.X + 8, (Height - 16) / 2, 16, 16);
            if (_selected)
            {
                // 选中态：图标 Accent→Purple 分段渐变，视觉焦点更精致
                IconPainter.DrawGradient(g, _icon, iconRect, Theme.Accent, Theme.Purple);
            }
            else
            {
                IconPainter.Draw(g, _icon, iconRect, text);
            }
            g.DrawString(Text, _selected ? Theme.FontBodyBold : Theme.FontBody,
                GdiCache.Brush(text),
                new Rectangle(r.X + 32, 0, Math.Max(20, r.Width - 36), Height),
                new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) _anim.Dispose();
            base.Dispose(disposing);
        }
    }
}
