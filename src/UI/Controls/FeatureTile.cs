using System;
using System.Drawing;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI
{
    /// <summary>
    /// 功能磁贴：工作台上的大号功能入口——彩色图标块 + 名称 + 一句话描述，
    /// 悬停提亮。让全部核心功能一屏可见、看着就想点。
    /// </summary>
    public class FeatureTile : Control
    {
        private readonly string _icon;
        private readonly Color _accent;
        private readonly string _desc;
        private bool _hover;

        private const int IconBlock = 40;

        public FeatureTile(string title, string desc, string icon, Color accent)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Text = title;
            _desc = desc;
            _icon = icon;
            _accent = accent;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 86;
            Font = Theme.FontBody;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            if (Parent != null)
            {
                using (SolidBrush b = new SolidBrush(Parent.BackColor))
                {
                    g.FillRectangle(b, ClientRectangle);
                }
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            // 卡片底：悬停时提亮并染上本磁贴的主题色边
            Gfx.FillRound(g, r, 11, _hover ? Theme.CardHover : Theme.CardBg);
            Gfx.StrokeRound(g, r, 11, _hover ? Gfx.Alpha(_accent, 200) : Theme.Border, 1f);

            // 彩色图标块
            Rectangle iconBg = new Rectangle(12, 12, IconBlock, IconBlock);
            using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                new System.Drawing.Drawing2D.LinearGradientBrush(iconBg, _accent, Gfx.Shade(_accent, 0.72), 60f))
            using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(iconBg, 10))
            {
                g.FillPath(lb, p);
            }
            IconPainter.Draw(g, _icon, new Rectangle(
                iconBg.X + (IconBlock - 22) / 2, iconBg.Y + (IconBlock - 22) / 2, 22, 22), Color.White);

            // 标题 + 描述
            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            {
                g.DrawString(Text, Theme.FontBodyBold, b,
                    new Rectangle(iconBg.Right + 10, 14, Math.Max(40, Width - iconBg.Right - 20), 20),
                    GdiCache.Ellipsis);
            }
            using (SolidBrush b = new SolidBrush(_hover ? Theme.TextSecondary : Theme.TextMuted))
            {
                g.DrawString(_desc, Theme.FontSmall, b,
                    new Rectangle(iconBg.Right + 10, 38, Math.Max(40, Width - iconBg.Right - 20), 34),
                    GdiCache.Ellipsis);
            }
        }
    }
}
