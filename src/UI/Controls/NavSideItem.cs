﻿/* ============================================================
 * 文件说明：侧栏导航项与分组标题
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{
    /// <summary>
    /// 侧栏导航项。对齐设计 GlassPillNavItem：
    /// 34px 高（Theme.NavItemHeight）、左右内缩 8px、8px 圆角胶囊；悬停为 5% 白叠加，选中为竖向蓝色渐变玻璃 +
    /// 渐变描边 + 26px 图标底 + 左侧 2px 强调状态条；文字色随状态在 Secondary/Primary 间过渡。
    /// 有意不做外发光——设计的设计约束是「层级靠色调而非发光」。
    /// </summary>
    public class NavSideItem : Control
    {
        private readonly string _icon;
        private bool _hover;
        private bool _pressed;
        private bool _selected;
        // 选中 / 悬停过渡进度 0..1：与 AccentButton / FeatureTile 一致，改用「基于时间 + Theme.Ease 缓动」，
        // 过渡更简洁流利（见 TickAnim），替代原先的剩余距离硬编码步进。
        private float _selP;
        private float _hoverP;
        private int _animStart;
        private float _selFrom;
        private float _hoverFrom;
        private int _selDur;
        private int _hoverDur;

        public NavSideItem(string text, string icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text;
            _icon = icon;
            BackColor = Theme.SidebarBg;
            Height = Theme.NavItemHeight; // 侧栏专用行高（见 Theme.NavItemHeight）
            Margin = new Padding(0);      // 同上：清零 FlowLayoutPanel 默认边距
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

        /// <summary>启动选中 / 悬停过渡：与按钮一致——基于 Environment.TickCount 计算已过时间
        /// （而非每帧累加，避免卡顿漂移），用 Theme.Ease.CubicOut 缓动，时长取 Motion 档；
        /// 关闭动效时直接落位。</summary>
        private void StartFade()
        {
            if (!AppSettings.Animations)
            {
                _selP = _selected ? 1f : 0f;
                _hoverP = _hover ? 1f : 0f;
                Invalidate();
                return;
            }
            _selFrom = _selP;
            _hoverFrom = _hoverP;
            _selDur = Theme.Motion.State;                                       // 选中态统一 150ms
            _hoverDur = _hover ? Theme.Motion.HoverIn : Theme.Motion.HoverOut;  // 进 120 / 出 160
            _animStart = Environment.TickCount;
            AnimationClock.Instance.Subscribe(TickAnim);
            Invalidate();
        }

        /// <summary>每帧按时间与缓动推进选中 / 悬停进度；两端都到位后退出全局时钟驱动。</summary>
        private void TickAnim()
        {
            try
            {
                if (IsDisposed || Disposing) { AnimationClock.Instance.Unsubscribe(TickAnim); return; }

                int elapsed = Environment.TickCount - _animStart;
                float ts = Theme.Ease.CubicOut((float)elapsed / _selDur);
                float th = Theme.Ease.CubicOut((float)elapsed / _hoverDur);

                _selP = _selFrom + ((_selected ? 1f : 0f) - _selFrom) * ts;
                _hoverP = _hoverFrom + ((_hover ? 1f : 0f) - _hoverFrom) * th;

                Invalidate();

                if (elapsed >= _selDur && elapsed >= _hoverDur)
                {
                    _selP = _selected ? 1f : 0f;
                    _hoverP = _hover ? 1f : 0f;
                    AnimationClock.Instance.Unsubscribe(TickAnim);
                    Invalidate();
                }
            }
            catch
            {
                try { AnimationClock.Instance.Unsubscribe(TickAnim); } catch { }
            }
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
            if (_hoverP > 0.01f && _selP < 0.99f)
            {
                Gfx.FillRound(g, pill, Theme.RadiusNav, Scaled(Theme.NavHover, (int)(_hoverP * 255)));
            }

            // 选中：竖向蓝色玻璃渐变 + 渐变描边 + 左侧 2px 强调状态条
            if (_selP > 0.01f)
            {
                Rectangle gr = new Rectangle(pill.X, pill.Y, Math.Max(1, pill.Width), Math.Max(1, pill.Height));
                using (LinearGradientBrush lb = new LinearGradientBrush(gr,
                    Scaled(Theme.NavSelTop, (int)(_selP * 255)), Scaled(Theme.NavSelBottom, (int)(_selP * 255)), 90f))
                using (GraphicsPath path = Gfx.RoundRect(pill, Theme.RadiusNav))
                {
                    g.FillPath(lb, path);
                }
                Gfx.StrokeRound(g, pill, Theme.RadiusNav, Scaled(Theme.NavSelBorderTop, (int)(_selP * 255)), 1f);

                int barH = 20;
                Rectangle bar = new Rectangle(pill.X, pill.Y + (pill.Height - barH) / 2, 2, barH);
                g.FillRectangle(GdiCache.Brush(Gfx.Alpha(Theme.Accent, (int)(_selP * 255))), bar);
            }

            // 按下：设计用实底 NavItemPressed 整体替换（含选中态）
            if (_pressed)
            {
                Gfx.FillRound(g, pill, Theme.RadiusNav, Theme.NavPressed);
            }

            Color text = (_selected || _hover) ? Theme.TextPrimary : Theme.TextSecondary;

            // 图标：26×26 图标底（选中时约 12% 强调色底），图标本体 16×16
            Rectangle box = new Rectangle(pill.X + 10, (Height - 26) / 2, 26, 26);
            if (_selP > 0.01f)
            {
                Gfx.FillRound(g, box, Theme.RadiusChip, Scaled(Theme.NavSelIconBg, (int)(_selP * 255)));
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

            // 仅键盘导航时提示焦点（鼠标点过的导航项不留框）
            if (Focused && ShowFocusCues) A11y.DrawFocusRing(g, pill, Theme.RadiusNav);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) AnimationClock.Instance.Unsubscribe(TickAnim);
            base.Dispose(disposing);
        }
    }
}
