﻿/* ============================================================
 * 文件说明：基础面板：BufferPanel（双缓冲基类）/ RoundPanel（圆角玻璃卡片）/ Card（带标题栏的卡片）。
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
    /// <summary>开启双缓冲的 Panel，减少自绘闪烁。</summary>
    public class BufferPanel : Panel
    {
        public BufferPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
        }
    }
    /// <summary>
    /// 背景提供者：自绘卡片（带光晕 / 渐变底面的面板）把「自己那块背景」画给子控件用。
    ///
    /// 由来：子控件（按钮 / 圆环 / 开关）原先一律用 Parent.BackColor 平铺铺底，
    /// 而卡片底面往往是画在 OnPaint 里的光晕——平铺会把光晕盖掉，
    /// 露出边界生硬的一块平色方块（修复中心「一键诊断」圆钮外那圈透明方框就是这个）。
    /// </summary>
    public interface IBackdropSource
    {
        /// <summary>在子控件的绘制上下文里，重画该子控件所占区域的父级背景。</summary>
        void PaintBackdrop(Graphics g, Control child);
    }

    /// <summary>子控件铺底：优先请父级画它的真实背景，父级不支持时退回 BackColor 平铺。</summary>
    public static class Backdrop
    {
        public static void Paint(Graphics g, Control self)
        {
            if (g == null || self == null) return;
            IBackdropSource src = self.Parent as IBackdropSource;
            if (src != null) { src.PaintBackdrop(g, self); return; }
            if (self.Parent != null) g.FillRectangle(GdiCache.Brush(self.Parent.BackColor), self.ClientRectangle);
        }
    }

    /// <summary>圆角面板 / 卡片容器。</summary>
    public class RoundPanel : BufferPanel, IBackdropSource
    {
        private int _radius = Theme.RadiusCard;
        // 设计卡片材质：玻璃层用「半透明白描边」（Card.Border #15FFFFFF）而非不透明灰边，
        // 配合 8px 圆角与顶部 1px 内高光，形成克制的玻璃承载层。
        // 这两个颜色此前是"构造期快照"：切深/浅配色后卡片四角仍露旧主题底色、描边仍是旧色
        // （ThemeSkin.Reload 只重映射 BackColor/ForeColor，不会回来改这两个私有字段）。
        // 改成 Empty = 跟随主题、取用时实时解析；显式指定过的颜色照旧生效。
        private Color _borderColor = Color.Empty;
        private Color _cornerColor = Color.Empty;
        private bool _showBorder = true;
        private bool _highlight = true;

        public RoundPanel()
        {
            BackColor = Theme.CardBg;
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        public Color BorderColor
        {
            get { return _borderColor.IsEmpty ? Theme.GlassBorder : _borderColor; }
            set
            {
                // 判等"实际生效色"后置脏：OnPaint 里回写此属性若每次都 Invalidate 会造成无限重绘
                if (BorderColor == value) return;
                _borderColor = value;
                Invalidate();
            }
        }

        /// <summary>圆角外侧露出的颜色，应设为父容器的背景色；未显式设置时跟随主题。</summary>
        public Color CornerColor
        {
            get { return _cornerColor.IsEmpty ? Theme.WindowBg : _cornerColor; }
            set
            {
                if (CornerColor == value) return;
                _cornerColor = value;
                Invalidate();
            }
        }

        public bool ShowBorder
        {
            get { return _showBorder; }
            set { _showBorder = value; Invalidate(); }
        }

        /// <summary>是否绘制 1px 内侧高光，制造轻微立体感。</summary>
        public bool Highlight
        {
            get { return _highlight; }
            set { _highlight = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            PaintSurface(g);

            base.OnPaint(e);
        }

        /// <summary>
        /// 卡片底面：圆角外露色 + 圆角底色 + 描边 + 内高光。
        /// 抽成方法是为了让子控件的铺底能复用同一段绘制（见 <see cref="PaintBackdrop"/>）。
        /// </summary>
        protected virtual void PaintSurface(Graphics g)
        {
            // 走属性而非字段：未显式指定时按当前主题实时取色（切主题不会残留旧配色）
            g.FillRectangle(GdiCache.Brush(CornerColor), ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_showBorder)
            {
                Gfx.DrawCard(g, r, _radius, BackColor, BorderColor, _highlight);
            }
            else
            {
                Gfx.FillRound(g, r, _radius, BackColor);
            }
        }

        /// <summary>子控件铺底：把自己在子控件区域内的底面重画一遍（含圆角与描边）。</summary>
        public virtual void PaintBackdrop(Graphics g, Control child)
        {
            if (g == null || child == null) return;
            GraphicsState st = g.Save();
            try
            {
                g.SetClip(new Rectangle(0, 0, child.Width, child.Height));
                g.TranslateTransform(-child.Left, -child.Top);
                PaintSurface(g);
            }
            finally { g.Restore(st); }
        }
    }
    /// <summary>带标题栏的卡片。</summary>
    public class Card : RoundPanel
    {
        public const int HeaderSize = 46;

        private string _title = "";
        private string _icon = "";
        private Color _iconColor = Theme.Accent;
        private bool _showSeparator = true;

        public Card()
        {
            Padding = new Padding(16, 0, 16, 0);
        }

        public string HeaderText
        {
            get { return _title; }
            set { _title = value == null ? "" : value; Invalidate(); }
        }

        public string HeaderIcon
        {
            get { return _icon; }
            set { _icon = value == null ? "" : value; Invalidate(); }
        }

        public Color HeaderIconColor
        {
            get { return _iconColor; }
            set { _iconColor = value; Invalidate(); }
        }

        public bool ShowSeparator
        {
            get { return _showSeparator; }
            set { _showSeparator = value; Invalidate(); }
        }

        public int HeaderHeight
        {
            get { return string.IsNullOrEmpty(_title) ? 14 : HeaderSize; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(_title)) return;

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int x = 16;
            if (!string.IsNullOrEmpty(_icon))
            {
                Rectangle box = new Rectangle(16, 13, 22, 22);
                Gfx.FillRound(g, box, Theme.RadiusChip, Gfx.Alpha(_iconColor, 32));
                IconPainter.Draw(g, _icon, new Rectangle(20, 17, 14, 14), _iconColor);
                x = 46;
            }

            SolidBrush b = GdiCache.Brush(Theme.TextPrimary);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(_title, Theme.FontBodyBold, b,
                    new Rectangle(x, 12, Math.Max(10, Width - x - 18), 24), sf);
            }

            if (_showSeparator)
            {
                Pen p = GdiCache.Pen(Theme.BorderSoft, 1f);
                {
                    g.DrawLine(p, 16, HeaderSize - 3, Width - 17, HeaderSize - 3);
                }
            }
        }
    }
}
