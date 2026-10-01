﻿/* ============================================================
 * 文件说明：按钮族：ButtonVariant 枚举 / AccentButton（主按钮，悬停与按下带插值动画）/ CaptionButton（标题栏按钮）。
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
    // 按钮
    // ===================================================================

    public enum ButtonVariant
    {
        Primary,
        Secondary,
        Danger,
        Ghost,
        /// <summary>琥珀：有破坏性但可撤销的操作（设计语义：红=删除/禁用，琥珀=可回退）。</summary>
        Warning,
        Success
    }

    /// <summary>完全自绘的扁平按钮。</summary>
    public class AccentButton : Control
    {
        private bool _hover;
        private bool _pressed;

        // 悬停 / 按下的过渡进度（0..1）：鼠标事件只改目标值，由 Timer 逐帧插值。
        // 对齐设计主按钮的「hover 150ms 颜色 + press 100ms」三段式，避免状态瞬间跳变
        private float _hoverP;
        private float _pressP;
        private int _animStart;
        private float _hoverFrom;
        private float _pressFrom;
        private int _hoverDur;
        private bool _paintBackColor;
        private bool _selected;
        private int _naturalWidth;
        private ButtonVariant _variant = ButtonVariant.Secondary;
        private int _radius = Theme.RadiusButton; // 设计 Token.Radius.Button = 4（精密而非圆润）
        private string _icon = "";

        public AccentButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.FontBody;
            Cursor = Cursors.Hand;
            Size = new Size(100, 32);
            A11y.MakeFocusable(this, AccessibleRole.PushButton);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            try { AccessibleName = Text; } catch { }
        }

        /// <summary>Enter 立即激活；空格在抬起时激活（与系统按钮一致）。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Enabled)
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
            if (e.KeyCode == Keys.Space && Enabled)
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

        public ButtonVariant Variant
        {
            get { return _variant; }
            set { _variant = value; Invalidate(); }
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        public string IconKind
        {
            get { return _icon; }
            set { _icon = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>
        /// 色板模式：直接以自身 BackColor 作为圆角填充。
        /// 变体着色路径只用 Parent.BackColor 铺底、再按变体取色，**从不使用自身 BackColor**——
        /// 因此"设置 BackColor 当颜色块"必须显式开启本模式，否则色块一片空白
        /// （设置页的主题色选择器曾因此完全看不到颜色）。
        /// </summary>
        public bool PaintBackColor
        {
            get { return _paintBackColor; }
            set { _paintBackColor = value; Invalidate(); }
        }

        /// <summary>选中态：色板模式下以加粗描边标记当前选中项。</summary>
        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        /// <summary>自然宽度：页头在窄窗口下收缩排布时用它还原，避免反复收缩累计变窄。</summary>
        public int NaturalWidth
        {
            get { return _naturalWidth; }
            set { _naturalWidth = value; }
        }

        /// <summary>按文字实际宽度调整按钮宽度，避免文字被截断。</summary>
        public void FitToText()
        {
            FitToText(84);
        }

        public void FitToText(int minimumWidth)
        {
            int textWidth = TextRenderer.MeasureText(Text == null ? "" : Text, Font).Width;
            int w = (_icon.Length > 0) ? textWidth + 46 : textWidth + 28;
            if (w < minimumWidth) w = minimumWidth;
            if (Width != w) Width = w;
            _naturalWidth = w;
        }

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

        /// <summary>
        /// 启动悬停 / 按下的过渡插值。用 Environment.TickCount 计算已过时间（不是 tick 累加，避免漂移），
        /// 关闭动效或未启用时直接取终值。
        /// </summary>
        private void StartAnim()
        {
            // 尊重「减少动效」设置：直接落位，不做过渡
            bool motion = Enabled && AppSettings.Animations;
            if (!motion)
            {
                AnimationClock.Instance.Unsubscribe(OnAnimTick);
                _hoverP = _hover ? 1f : 0f;
                _pressP = _pressed ? 1f : 0f;
                Invalidate();
                return;
            }

            _hoverFrom = _hoverP;
            _pressFrom = _pressP;
            // 退出比进入慢一点（120 / 160），收得更稳
            _hoverDur = _hover ? Theme.Motion.HoverIn : Theme.Motion.HoverOut;
            _animStart = Environment.TickCount;
            AnimationClock.Instance.Subscribe(OnAnimTick);
            Invalidate();
        }

        private void OnAnimTick()
        {
            try
            {
                if (IsDisposed || Disposing) { AnimationClock.Instance.Unsubscribe(OnAnimTick); return; }

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
                    AnimationClock.Instance.Unsubscribe(OnAnimTick);
                    Invalidate();
                }
            }
            catch
            {
                try { AnimationClock.Instance.Unsubscribe(OnAnimTick); } catch { }
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 过渡动画进行中销毁控件时必须停表并释放定时器：
                // 否则定时器继续触发、持有已释放的控件（与 ToggleSwitch / ScrollHost 的处理一致）
                AnimationClock.Instance.Unsubscribe(OnAnimTick);
            }
            base.Dispose(disposing);
        }

        private void ResolveColors(out Color fill, out Color text, out Color border)
        {
            // 三态色（静止 / 悬停 / 按下）→ 按 hoverP、pressP 两级插值，
            // 让状态切换是渐变而不是瞬间跳变。
            // 注意：Ghost 的静止态是透明，必须用 BlendArgb（Blend 会把 alpha 固定成 255）
            Color rest, hover, press;
            Color restText, hoverText;
            Color restBorder = Color.Empty, hoverBorder = Color.Empty;

            switch (_variant)
            {
                case ButtonVariant.Primary:
                    rest = Theme.Accent; hover = Theme.AccentHover; press = Theme.AccentPress;
                    // 设计：强调蓝偏亮，用深墨文字才达到可读对比（TextOnAccent #0F172A），而非白色
                    restText = Theme.TextOnAccent; hoverText = Theme.TextOnAccent;
                    break;
                case ButtonVariant.Danger:
                    rest = Theme.Danger; hover = Theme.DangerHover; press = Gfx.Shade(Theme.Danger, 0.85);
                    restText = Color.White; hoverText = Color.White;
                    break;
                case ButtonVariant.Warning:
                    // 琥珀比强调蓝亮，同样用深墨文字才够对比
                    rest = Theme.Warning; hover = Gfx.Shade(Theme.Warning, 1.12); press = Gfx.Shade(Theme.Warning, 0.85);
                    restText = Theme.TextOnAccent; hoverText = Theme.TextOnAccent;
                    break;
                case ButtonVariant.Success:
                    rest = Theme.Success; hover = Gfx.Shade(Theme.Success, 1.12); press = Gfx.Shade(Theme.Success, 0.85);
                    restText = Color.FromArgb(10, 28, 18); hoverText = Color.FromArgb(10, 28, 18);
                    break;
                case ButtonVariant.Ghost:
                    rest = Color.Transparent; hover = Gfx.Alpha(Theme.Border, 90); press = Theme.CardBgAlt;
                    restText = Theme.TextSecondary; hoverText = Theme.TextPrimary;
                    hoverBorder = Theme.BorderStrong;
                    break;
                default:
                    rest = Theme.CardBgAlt; hover = Theme.CardHover; press = Gfx.Shade(Theme.CardBgAlt, 0.92);
                    restText = Theme.TextSecondary; hoverText = Theme.TextPrimary;
                    restBorder = Theme.Border; hoverBorder = Theme.BorderStrong;
                    break;
            }

            fill = Gfx.BlendArgb(Gfx.BlendArgb(rest, hover, _hoverP), press, _pressP);
            text = Gfx.BlendArgb(restText, hoverText, _hoverP);
            border = Gfx.BlendArgb(restBorder, hoverBorder, _hoverP);

            if (!Enabled)
            {
                fill = _variant == ButtonVariant.Ghost ? Color.Transparent : Gfx.Blend(fill, Theme.CardBg, 0.7);
                text = Theme.TextMuted;
                border = Theme.BorderSoft;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 底面：请父级把「它那块背景」重画一遍（父级可能是带光晕的卡片，
            // 直接铺 Parent.BackColor 会盖掉光晕，圆角外露出一圈硬边方框）；
            // 父级不支持时退回原来的平铺。
            GuyueBox.UI.Backdrop.Paint(g, this);

            Color fill, text, border;
            if (_paintBackColor)
            {
                // 色板模式：直接用自身 BackColor 作填充（变体着色路径不会用到 BackColor）
                fill = Gfx.Blend(BackColor, Gfx.Shade(BackColor, 0.82), _pressP);
                text = Theme.TextPrimary;
                border = (_selected || _hover) ? Theme.TextPrimary : Theme.Border;
            }
            else
            {
                ResolveColors(out fill, out text, out border);
            }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (fill.A > 0)
            {
                Gfx.FillRound(g, r, _radius, fill);
            }
            if (border != Color.Empty && border.A > 0)
            {
                Gfx.StrokeRound(g, r, _radius, border, (_paintBackColor && _selected) ? 2f : 1f);
            }

            int textLeft = 10;
            int textRight = Width - 10;
            if (!string.IsNullOrEmpty(_icon))
            {
                int iconSize = 14;
                IconPainter.Draw(g, _icon, new Rectangle(11, (Height - iconSize) / 2, iconSize, iconSize), text);
                textLeft = 11 + iconSize + 6;
            }

            Gfx.DrawTextCenter(g, Text, Font, text,
                new Rectangle(textLeft, 0, Math.Max(0, textRight - textLeft), Height));

            // 焦点可视：键盘用户必须看得出当前焦点在哪个按钮上。
            // 仅在 **键盘导航** 时画（ShowFocusCues：上一次输入来自键盘才为真）：
            // 鼠标点完按钮会继续持有焦点，早先只判 Focused 会让按钮一直挂着一圈框
            // （内存优化「一键释放」点完就留框，就是这个原因）。
            if (Focused && ShowFocusCues) A11y.DrawFocusRing(g, r, _radius);
        }
    }
    /// <summary>无边框窗口的标题栏按钮。</summary>
    public class CaptionButton : Control
    {
        private bool _hover;
        private string _icon = "min";
        private Color _hoverColor = Theme.CardBgAlt;

        public CaptionButton(string icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _icon = icon;
            Size = new Size(44, 32);
            Cursor = Cursors.Hand;
            BackColor = Theme.ChromeBg;
            A11y.MakeFocusable(this, AccessibleRole.PushButton);
            // 标题栏按钮只有图形，读屏需要文字名
            AccessibleName = icon == "min" ? "最小化"
                : icon == "max" ? "最大化"
                : icon == "restore" ? "还原窗口"
                : icon == "close" ? "关闭" : "";
        }

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

        public Color HoverColor
        {
            get { return _hoverColor; }
            set { _hoverColor = value; }
        }

        public string IconKind
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 同 AccentButton：铺底请父级画它自己的背景（标题栏是纯色，行为不变）
            GuyueBox.UI.Backdrop.Paint(g, this);
            if (_hover)
            {
                bool danger = _hoverColor == Theme.Danger;
                Gfx.FillRound(g, new Rectangle(4, 4, Width - 8, Height - 8), 6,
                    danger ? Theme.Danger : Theme.CardBgAlt);
            }

            Color iconColor = _hover && _hoverColor == Theme.Danger ? Color.White : Theme.TextSecondary;
            IconPainter.Draw(g, _icon, new Rectangle((Width - 11) / 2, (Height - 11) / 2, 11, 11), iconColor);

            // 同 AccentButton：鼠标点击后不留框，只有键盘导航时才提示焦点
            if (Focused && ShowFocusCues) A11y.DrawFocusRing(g, new Rectangle(0, 0, Width - 1, Height - 1), 6);
        }
    }
}
