﻿/* ============================================================
 * 文件说明：小部件：BadgeLabel（状态标签）/ StatusDot（状态点）/ Spinner（转圈指示）。
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

    // ===================================================================
    // 展示类控件
    // ===================================================================

    public class BadgeLabel : Control
    {
        private Color _badgeColor = Theme.Accent;
        private bool _filled;

        public BadgeLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.FontMicro;
            Size = new Size(58, 20);
        }

        public Color BadgeColor
        {
            get { return _badgeColor; }
            set { _badgeColor = value; Invalidate(); }
        }

        public bool Filled
        {
            get { return _filled; }
            set { _filled = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 铺底走父级真实背景（父级带光晕时不再盖成平色方块）
            GuyueBox.UI.Backdrop.Paint(g, this);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            // 设计 Token.Radius.Badge = 4：徽标是精密小方块而非胶囊
            if (_filled)
            {
                Gfx.FillRound(g, r, Theme.RadiusChip, _badgeColor);
            }
            else
            {
                Gfx.FillRound(g, r, Theme.RadiusChip, Gfx.Alpha(_badgeColor, 30));
                Gfx.StrokeRound(g, r, Theme.RadiusChip, Gfx.Alpha(_badgeColor, 110), 1f);
            }

            Gfx.DrawTextCenter(g, Text, Font, _filled ? Color.White : _badgeColor, ClientRectangle);
        }
    }
    /// <summary>状态圆点 + 文本。</summary>
    public class StatusDot : Control
    {
        private Color _dotColor = Theme.Success;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.FontSmall;
            Size = new Size(130, 20);
        }

        public Color DotColor
        {
            get { return _dotColor; }
            set { _dotColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 同上：铺底走父级真实背景
            GuyueBox.UI.Backdrop.Paint(g, this);

            int cy = Height / 2;
            SolidBrush halo = GdiCache.Brush(Gfx.Alpha(_dotColor, 46));
            {
                g.FillEllipse(halo, 1, cy - 5, 12, 12);
            }
            SolidBrush b = GdiCache.Brush(_dotColor);
            {
                g.FillEllipse(b, 4, cy - 3, 7, 7);
            }

            SolidBrush textBrush = GdiCache.Brush(Theme.TextSecondary);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Font, textBrush, new Rectangle(18, 0, Math.Max(10, Width - 20), Height), sf);
            }
        }
    }
    /// <summary>旋转指示器。</summary>
    public class Spinner : Control
    {
        private float _angle;
        private Color _color = Theme.Accent;

        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(16, 16);
            // 旋转由全局 AnimationClock 驱动（16ms 粒度，0.5°/ms 等价于原 60ms × 30°）
        }

        public Color SpinnerColor
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        public void StartSpin()
        {
            AnimationClock.Instance.Subscribe(SpinnerTick);
        }

        public void StopSpin()
        {
            AnimationClock.Instance.Unsubscribe(SpinnerTick);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 铺底走父级真实背景：本控件只画一个圆环，圆环外若不上报父级底色，
            // 落在带光晕/渐变的卡片上就会露出一块硬边方框，旋转时还会残留上一帧像素
            GuyueBox.UI.Backdrop.Paint(g, this);

            // 画笔走全局缓存：转圈动画每 60ms 一帧，忙碌期间可能连续跑数秒
            Rectangle r = new Rectangle(2, 2, Width - 4, Height - 4);
            g.DrawEllipse(GdiCache.Pen(Gfx.Alpha(_color, 60), 2f), r);
            g.DrawArc(GdiCache.RoundPen(_color, 2f), r, _angle, 110);
        }

        private void SpinnerTick()
        {
            _angle = (_angle + Theme.Motion.SpinDegPerFrame) % 360f;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) AnimationClock.Instance.Unsubscribe(SpinnerTick);
            base.Dispose(disposing);
        }
    }
}
