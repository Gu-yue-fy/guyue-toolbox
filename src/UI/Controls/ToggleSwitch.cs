using System;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 开关控件。用于表达"已优化 / 未优化"这类二元状态，比一对按钮更直观也更安静。
    /// </summary>
    public class ToggleSwitch : Control
    {
        private bool _checked;
        private bool _hover;
        private bool _press;
        private bool _readOnly;
        private float _animPos;   // 滑块位置 0（关）→1（开），插值动画
        private Timer _anim;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(40, 22);
            Cursor = Cursors.Hand;
            A11y.MakeFocusable(this, AccessibleRole.CheckButton);
        }

        /// <summary>Enter 立即切换；空格在抬起时切换（与系统按钮一致）。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_readOnly)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Checked = !Checked;
                return;
            }
            if (e.KeyCode == Keys.Space) { e.Handled = true; e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !_readOnly)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Checked = !Checked;
                return;
            }
            base.OnKeyUp(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                StartAnim();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        /// <summary>程序化设置状态：直接跳位，不触发滑动动画（批量初始化用）。</summary>
        public void SetCheckedSilent(bool value)
        {
            if (_anim != null) _anim.Stop();
            _animPos = value ? 1f : 0f;
            _checked = value;
            Invalidate();
        }

        private float _target;    // 滑块动画目标（0/1）。必须是字段：匿名回调若按闭包捕获，只会读到首次的值
        private float _animFrom;  // 本次动画起点位置
        private int _animBegin;   // 本次动画起始 TickCount

        /// <summary>滑块滑动动画：240ms 弹簧曲线（先过冲后回落），比固定步长的 ease-out 更有质感。</summary>
        private void StartAnim()
        {
            _target = _checked ? 1f : 0f;
            _animFrom = _animPos;
            _animBegin = Environment.TickCount;
            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 16;
                _anim.Tick += delegate
                {
                    float t = (Environment.TickCount - _animBegin) / (float)Theme.MotionActiveMs;
                    if (t >= 1f)
                    {
                        _animPos = _target;
                        _anim.Stop();
                    }
                    else
                    {
                        _animPos = _animFrom + (_target - _animFrom) * Theme.Spring(t);
                    }
                    Invalidate();
                };
            }
            _anim.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) _anim.Dispose();
            base.Dispose(disposing);
        }

        public bool ReadOnly
        {
            get { return _readOnly; }
            set
            {
                _readOnly = value;
                Cursor = value ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
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
            _press = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_readOnly) _press = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_press && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                Checked = !Checked;
            }
            _press = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            if (Parent != null)
            {
                using (SolidBrush back = new SolidBrush(Parent.BackColor))
                {
                    g.FillRectangle(back, ClientRectangle);
                }
            }

            int h = Math.Min(Height, 22);
            int w = Math.Min(Width, 40);
            Rectangle track = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);

            Color trackColor;
            if (_readOnly)
            {
                trackColor = _checked ? Gfx.Alpha(Theme.Success, 90) : Theme.CardBgAlt;
            }
            else if (_checked)
            {
                trackColor = _press ? Theme.AccentPress : (_hover ? Theme.AccentHover : Theme.Accent);
            }
            else
            {
                trackColor = _press ? Theme.BorderStrong : (_hover ? Theme.Border : Theme.CardBgAlt);
            }

            Gfx.FillRound(g, track, h / 2, trackColor);
            if (!_checked)
            {
                Gfx.StrokeRound(g, track, h / 2, _hover ? Theme.BorderStrong : Theme.Border, 1f);
            }

            int knob = h - 6;
            // 滑块位置按动画插值：开=右端，关=左端
            float pos = _anim != null && _anim.Enabled ? _animPos : (_checked ? 1f : 0f);
            // 弹簧过冲上限：允许轻微冲出轨道以保留弹性手感，但不越界过多
            if (pos < 0f) pos = 0f;
            if (pos > 1.05f) pos = 1.05f;
            int knobX = track.X + 3 + (int)Math.Round((track.Width - knob - 6) * pos);
            int knobY = track.Y + 3;
            Gfx.FillRound(g, new Rectangle(knobX, knobY, knob, knob), knob / 2,
                _checked ? Color.White : Theme.KnobOff);

            if (Focused) A11y.DrawFocusRing(g, track, h / 2);
        }
    }
}
