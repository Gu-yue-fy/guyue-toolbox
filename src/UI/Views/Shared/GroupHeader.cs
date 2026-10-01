/* UI/Views/Optimize/GroupHeader.cs — 优化项分组标题：可点击折叠/展开，右侧显示该组优化项数量。 */

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
            A11y.MakeFocusable(this, AccessibleRole.PushButton);
            AccessibleName = (title == null ? "" : title) + "（可折叠）";
        }

        /// <summary>键盘可达：Enter / 空格折叠或展开该分组。</summary>
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

        protected override void OnClick(EventArgs e)
        {
            Collapsed = !Collapsed;
            StartArrowAnim();
            Invalidate();
            EventHandler h = CollapsedChanged;
            if (h != null) h(this, EventArgs.Empty);
            base.OnClick(e);
        }

        // ---------------- 箭头动画：展开（朝下）↔ 折叠（朝右）之间平滑变形 ----------------

        private float _arrowPos;      // 0 = 展开（朝下），1 = 折叠（朝右）
        private bool _arrowAnimating;

        private void StartArrowAnim()
        {
            if (!AppSettings.Animations)
            {
                _arrowPos = Collapsed ? 1f : 0f;
                return;
            }
            if (_arrowAnimating) return;
            _arrowAnimating = true;
            AnimationClock.Instance.Subscribe(ArrowTick);
        }

        private void ArrowTick()
        {
            float target = Collapsed ? 1f : 0f;
            _arrowPos += (target - _arrowPos) * 0.3f;
            if (Math.Abs(target - _arrowPos) < 0.02f)
            {
                _arrowPos = target;
                _arrowAnimating = false;
                AnimationClock.Instance.Unsubscribe(ArrowTick);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) AnimationClock.Instance.Unsubscribe(ArrowTick);
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            g.FillRectangle(GdiCache.Brush(Theme.WindowBg), ClientRectangle);

            int y = Height - 16;
            GraphicsState barState = g.Save();
            g.TranslateTransform(2, y - 6);
            g.FillPath(GdiCache.Brush(AccentColor), GdiCache.RoundRect(new Rectangle(0, 0, 3, 15), 2));
            g.Restore(barState);

            g.DrawString(Text, Theme.FontBodyBold, GdiCache.Brush(Theme.TextPrimary),
                new Rectangle(14, y - 12, 300, 24), GdiCache.NearMiddle);

            if (!string.IsNullOrEmpty(CountText))
            {
                g.DrawString(CountText, Theme.FontSmall, GdiCache.Brush(Theme.TextMuted),
                    new Rectangle(Math.Max(10, Width - 240), y - 12, 190, 24), GdiCache.FarMiddle);
            }

            // 右侧折叠箭头：展开朝下（V）、折叠朝右（>），切换时按 _arrowPos 平滑变形
            Rectangle a = new Rectangle(Width - 26, (Height - 12) / 2, 12, 12);
            {
                Pen p = GdiCache.Pen(Theme.TextMuted, 1.5f);
                float t = _arrowPos;
                // V 的两个端点 → > 的两个端点，三点线性插值
                float x1 = 1 + 3 * t, y1 = 4 - 3 * t;
                float x2 = 6 + 3 * t, y2 = 9 - 3 * t;
                float x3 = 11 - 7 * t, y3 = 4 + 7 * t;
                g.DrawLine(p, a.X + x1, a.Y + y1, a.X + x2, a.Y + y2);
                g.DrawLine(p, a.X + x2, a.Y + y2, a.X + x3, a.Y + y3);
            }

            // 仅键盘导航时提示焦点（鼠标点击后不留框）
            if (Focused && ShowFocusCues) A11y.DrawFocusRing(g, new Rectangle(0, 0, Width - 1, Height - 1), Theme.RadiusChip);
        }
    }
}
