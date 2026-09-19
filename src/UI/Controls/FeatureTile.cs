using System;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
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
        private bool _pressed;

        // 悬停 / 按下的过渡进度（0..1）：对齐设计磁贴的 hover 抬升 1px（进 120ms / 出 160ms）
        private float _hoverP;
        private float _pressP;
        private Timer _anim;
        private int _animStart;
        private float _hoverFrom;
        private float _pressFrom;
        private int _hoverDur;

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
            A11y.MakeFocusable(this, AccessibleRole.PushButton);
            AccessibleName = (title == null ? "" : title) +
                (string.IsNullOrEmpty(desc) ? "" : "：" + desc);
        }

        /// <summary>键盘可达：Enter / 空格触发。</summary>
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

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            StartAnim();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            StartAnim();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            StartAnim();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            StartAnim();
            base.OnMouseUp(e);
        }

        /// <summary>启动悬停 / 按下的过渡插值（同 AccentButton 的做法）。</summary>
        private void StartAnim()
        {
            if (_anim == null)
            {
                _anim = new Timer { Interval = 16 };
                _anim.Tick += OnAnimTick;
            }

            // 与 AccentButton 保持一致：禁用状态的磁贴不做悬停过渡（否则会给"不可点击"错误的反馈）
            if (!Enabled || !AppSettings.Animations)
            {
                _anim.Stop();
                _hoverP = _hover ? 1f : 0f;
                _pressP = _pressed ? 1f : 0f;
                Invalidate();
                return;
            }

            _hoverFrom = _hoverP;
            _pressFrom = _pressP;
            _hoverDur = _hover ? Theme.Motion.HoverIn : Theme.Motion.HoverOut;
            _animStart = Environment.TickCount;
            _anim.Start();
            Invalidate();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            try
            {
                if (IsDisposed || Disposing) { _anim.Stop(); return; }

                int elapsed = Environment.TickCount - _animStart;
                float th = Theme.Ease.CubicOut((float)elapsed / _hoverDur);
                float tp = Theme.Ease.CubicOut((float)elapsed / Theme.Motion.Press);

                _hoverP = _hoverFrom + ((_hover ? 1f : 0f) - _hoverFrom) * th;
                _pressP = _pressFrom + ((_pressed ? 1f : 0f) - _pressFrom) * tp;

                Invalidate();

                if (elapsed >= _hoverDur && elapsed >= Theme.Motion.Press)
                {
                    _hoverP = _hover ? 1f : 0f;
                    _pressP = _pressed ? 1f : 0f;
                    _anim.Stop();
                    Invalidate();
                }
            }
            catch
            {
                try { _anim.Stop(); } catch { }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 悬停动画进行中销毁磁贴时停表并释放定时器，避免定时器继续触发已释放控件
                if (_anim != null)
                {
                    _anim.Stop();
                    _anim.Dispose();
                    _anim = null;
                }
            }
            base.Dispose(disposing);
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

            // 悬停抬升：整块（卡片 + 图标 + 文字）一起上移 1px —— 设计磁贴同一手法，
            // 幅度极小但明显；只抬卡片框而内容不动会显得错位，故所有矩形统一偏移
            int lift = _hoverP >= 0.5f ? 1 : 0;
            Rectangle r = new Rectangle(0, -lift, Width - 1, Height - 1);

            // 卡片底：悬停提亮并染上主题色边，按下再压暗一档。
            // 描边半径必须与填充半径一致，否则边框外扩、四角错位。
            // 用 BlendArgb：GlassBorder 是半透明，普通 Blend 会把 alpha 拉成 255
            Color fill = Gfx.BlendArgb(Theme.CardBg, Theme.CardHover, _hoverP);
            fill = Gfx.BlendArgb(fill, Gfx.Shade(Theme.CardHover, 0.92), _pressP);
            Color border = Gfx.BlendArgb(Theme.GlassBorder, Gfx.Alpha(_accent, 200), _hoverP);
            Gfx.FillRound(g, r, Theme.RadiusCard, fill);
            Gfx.StrokeRound(g, r, Theme.RadiusCard, border, 1f);

            // 彩色图标块
            Rectangle iconBg = new Rectangle(12, 12 - lift, IconBlock, IconBlock);
            using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                new System.Drawing.Drawing2D.LinearGradientBrush(iconBg, _accent, Gfx.Shade(_accent, 0.72), 60f))
            using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(iconBg, Theme.RadiusItem))
            {
                g.FillPath(lb, p);
            }
            IconPainter.Draw(g, _icon, new Rectangle(
                iconBg.X + (IconBlock - 22) / 2, iconBg.Y + (IconBlock - 22) / 2, 22, 22), Color.White);

            // 标题 + 描述
            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            {
                g.DrawString(Text, Theme.FontBodyBold, b,
                    new Rectangle(iconBg.Right + 10, 14 - lift, Math.Max(40, Width - iconBg.Right - 20), 20),
                    GdiCache.Ellipsis);
            }
            using (SolidBrush b = new SolidBrush(
                Gfx.Blend(Theme.TextMuted, Theme.TextSecondary, _hoverP)))
            {
                g.DrawString(_desc, Theme.FontSmall, b,
                    new Rectangle(iconBg.Right + 10, 38 - lift, Math.Max(40, Width - iconBg.Right - 20), 34),
                    GdiCache.Ellipsis);
            }

            if (Focused) A11y.DrawFocusRing(g, r, Theme.RadiusCard);
        }
    }
}
