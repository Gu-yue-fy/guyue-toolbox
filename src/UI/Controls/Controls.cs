/* ============================================================
 * 文件说明：自绘控件库：按钮/状态卡/提示条/输入框等基础控件（全部 GDI 自绘，无第三方 UI 依赖）。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{
    // ===================================================================
    // 基础面板
    // ===================================================================

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

    /// <summary>圆角面板 / 卡片容器。</summary>
    public class RoundPanel : BufferPanel
    {
        private int _radius = Theme.RadiusCard;
        // 设计卡片材质：玻璃层用「半透明白描边」（Card.Border #15FFFFFF）而非不透明灰边，
        // 配合 8px 圆角与顶部 1px 内高光，形成克制的玻璃承载层。
        private Color _borderColor = Theme.GlassBorder;
        private Color _cornerColor = Theme.WindowBg;
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
            get { return _borderColor; }
            set
            {
                // 判等后置脏：OnPaint 里回写此属性若每次都 Invalidate 会造成无限重绘
                if (_borderColor == value) return;
                _borderColor = value;
                Invalidate();
            }
        }

        /// <summary>圆角外侧露出的颜色，应设为父容器的背景色。</summary>
        public Color CornerColor
        {
            get { return _cornerColor; }
            set { _cornerColor = value; Invalidate(); }
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

            g.FillRectangle(GdiCache.Brush(_cornerColor), ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_showBorder)
            {
                Gfx.DrawCard(g, r, _radius, BackColor, _borderColor, _highlight);
            }
            else
            {
                Gfx.FillRound(g, r, _radius, BackColor);
            }

            base.OnPaint(e);
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

            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
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
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 16, HeaderSize - 3, Width - 17, HeaderSize - 3);
                }
            }
        }
    }

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
        private Timer _anim;
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
            if (_anim == null)
            {
                _anim = new Timer { Interval = 16 };
                _anim.Tick += OnAnimTick;
            }

            // 尊重「减少动效」设置：直接落位，不做过渡
            bool motion = Enabled && AppSettings.Animations;
            if (!motion)
            {
                _anim.Stop();
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
                if (_anim != null)
                {
                    _anim.Stop();
                    _anim.Dispose();
                    _anim = null;
                }
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

            if (Parent != null)
            {
                g.FillRectangle(GdiCache.Brush(Parent.BackColor), ClientRectangle);
            }

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

            // 焦点可视：键盘用户必须看得出当前焦点在哪个按钮上
            if (Focused) A11y.DrawFocusRing(g, r, _radius);
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

            using (SolidBrush b = new SolidBrush(Parent == null ? Theme.ChromeBg : Parent.BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            if (_hover)
            {
                bool danger = _hoverColor == Theme.Danger;
                Gfx.FillRound(g, new Rectangle(4, 4, Width - 8, Height - 8), 6,
                    danger ? Theme.Danger : Theme.CardBgAlt);
            }

            Color iconColor = _hover && _hoverColor == Theme.Danger ? Color.White : Theme.TextSecondary;
            IconPainter.Draw(g, _icon, new Rectangle((Width - 11) / 2, (Height - 11) / 2, 11, 11), iconColor);

            if (Focused) A11y.DrawFocusRing(g, new Rectangle(0, 0, Width - 1, Height - 1), 6);
        }
    }

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

            using (SolidBrush b = new SolidBrush(Parent == null ? Theme.CardBg : Parent.BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }

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

            using (SolidBrush b = new SolidBrush(Parent == null ? Theme.WindowBg : Parent.BackColor))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            int cy = Height / 2;
            using (SolidBrush halo = new SolidBrush(Gfx.Alpha(_dotColor, 46)))
            {
                g.FillEllipse(halo, 1, cy - 5, 12, 12);
            }
            using (SolidBrush b = new SolidBrush(_dotColor))
            {
                g.FillEllipse(b, 4, cy - 3, 7, 7);
            }

            using (SolidBrush b = new SolidBrush(Theme.TextSecondary))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(Text, Font, b, new Rectangle(18, 0, Math.Max(10, Width - 20), Height), sf);
            }
        }
    }

    /// <summary>旋转指示器。</summary>
    public class Spinner : Control
    {
        private readonly Timer _timer;
        private float _angle;
        private Color _color = Theme.Accent;

        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(16, 16);
            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += delegate { _angle = (_angle + 30f) % 360f; Invalidate(); };
        }

        public Color SpinnerColor
        {
            get { return _color; }
            set { _color = value; Invalidate(); }
        }

        public void StartSpin()
        {
            if (!_timer.Enabled) _timer.Start();
        }

        public void StopSpin()
        {
            _timer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            Rectangle r = new Rectangle(2, 2, Width - 4, Height - 4);
            using (Pen pen = new Pen(Gfx.Alpha(_color, 60), 2f))
            {
                g.DrawEllipse(pen, r);
            }
            using (Pen pen = new Pen(_color, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, r, _angle, 110);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>细长的进度条。</summary>
    public class BarMeter : Control
    {
        private double _value;
        private Color _barColor = Theme.Accent;

        public BarMeter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 6;
            BackColor = Theme.CardBgAlt;
        }

        public double Value
        {
            get { return _value; }
            set { _value = value; Invalidate(); }
        }

        public Color BarColor
        {
            get { return _barColor; }
            set { _barColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            if (Parent != null)
            {
                using (SolidBrush back = new SolidBrush(Theme.CardBg))
                {
                    g.FillRectangle(back, ClientRectangle);
                }
            }

            int h = Height;
            Gfx.FillRound(g, new Rectangle(0, 0, Width, h), h / 2, Theme.CardBgAlt);

            int w = (int)(Width * Math.Min(100, Math.Max(0, _value)) / 100.0);
            if (w > 0)
            {
                if (w < h) w = h;
                Gfx.FillRound(g, new Rectangle(0, 0, w, h), h / 2, _barColor);
            }
        }
    }

    /// <summary>
    /// 主题化的信息提示条（紧凑型，20+ 个页面共用）。
    /// UX 设计：提示默认只占一行（34px），右上角带关闭按钮——用户点关后
    /// 按所在页面记忆（AppSettings.IsNoticeDismissed），此后不再出现，避免长期挡视野。
    /// 关闭记忆的 key 自动取宿主页面的类名（向上遍历 Parent 链找 ViewBase），页面无需任何配置。
    /// </summary>
    public class NoticeBar : RoundPanel
    {
        private string _text = "";
        private Color _accent = Theme.Warning;
        private string _icon = "info";
        private bool _clickable;
        private bool _hoverClose;          // 鼠标是否悬停在关闭按钮上
        private bool _memoryChecked;       // 是否已做过"关闭过"检查（防重复）

        public NoticeBar()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusItem;
            Height = 34;
            CornerColor = Theme.WindowBg;
            BorderColor = Theme.GlassBorder;
            Highlight = false;
        }

        public string NoticeText
        {
            get { return _text; }
            set { _text = value == null ? "" : value; Invalidate(); }
        }

        public Color NoticeAccent
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public string NoticeIcon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool Clickable
        {
            get { return _clickable; }
            set { _clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; }
        }

        /// <summary>关闭按钮命中区（右上角 14px 方块）。</summary>
        private Rectangle CloseRect
        {
            get { return new Rectangle(Width - 26, (Height - 14) / 2, 14, 14); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CheckDismissedMemory();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            CheckDismissedMemory(); // 行挂载晚于句柄创建时的兜底
        }

        /// <summary>若用户在此页面关闭过提示：隐藏自身（提示条直挂 Body 时绝不能动 Parent——那会藏掉整个内容区）。</summary>
        private void CheckDismissedMemory()
        {
            if (_memoryChecked) return;
            _memoryChecked = true;
            string pageId = FindPageId();
            if (pageId != null && AppSettings.IsNoticeDismissed(pageId))
            {
                Visible = false; // FlowLayoutPanel 会跳过隐藏控件的占位，不留空隙
            }
        }

        /// <summary>向上遍历父链找宿主页面（ViewBase），用其类名作关闭记忆 key。</summary>
        private string FindPageId()
        {
            Control c = Parent;
            while (c != null)
            {
                if (c is ViewBase) return c.GetType().Name;
                c = c.Parent;
            }
            return null;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverClose) { _hoverClose = false; Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = CloseRect.Contains(e.Location);
            if (over != _hoverClose)
            {
                _hoverClose = over;
                Cursor = over || _clickable ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CloseRect.Contains(e.Location))
            {
                // 只藏自己：提示条可能直接挂在 Body（整个内容容器）上，
                // 动 Parent 会把整页内容藏掉（"系统概览没了"事故的根因）
                Visible = false;
                string pageId = FindPageId();
                if (pageId != null) AppSettings.MarkNoticeDismissed(pageId); // 永久记忆
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            BorderColor = Gfx.Alpha(_accent, 70);
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 左侧色条
            using (GraphicsPath p = Gfx.RoundRect(new Rectangle(0, 10, 3, Height - 20), 2))
            using (SolidBrush b = new SolidBrush(_accent))
            {
                g.FillPath(b, p);
            }

            IconPainter.Draw(g, _icon, new Rectangle(12, (Height - 14) / 2, 14, 14), _accent);

            // 文本：右侧给关闭按钮留位；小字号 + 省略号截断
            Rectangle textRect = new Rectangle(34, 0, Math.Max(10, Width - 64), Height);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(Theme.TextSecondary))
                {
                    g.DrawString(_text, Theme.FontSmall, b, textRect, sf);
                }
            }

            // 右上角关闭按钮（×）：平时淡色，悬停高亮
            using (Pen p = new Pen(_hoverClose ? Theme.TextPrimary : Gfx.Alpha(Theme.TextMuted, 160), 1.6f))
            {
                Rectangle r = CloseRect;
                int m = 4;
                g.DrawLine(p, r.Left + m, r.Top + m, r.Right - m, r.Bottom - m);
                g.DrawLine(p, r.Right - m, r.Top + m, r.Left + m, r.Bottom - m);
            }
        }
    }

    /// <summary>指标卡：图标 + 标题 + 大号数值 + 占用条 + 脚注。</summary>
    public class StatCard : RoundPanel
    {
        public string MetricText = "--";
        public string CaptionText = "";
        public string IconKind = "cpu";
        public double Percent = -1;
        public Color AccentColor = Theme.Accent;
        public string FooterText = "";

        private double _shownPercent = -1;   // 进度条当前显示值
        private double _percentFrom;         // 进度条动画起点
        private string _shownMetric;         // 指标文本当前显示值（数字滚动）
        private double _numFrom, _numTo;     // 数字滚动起止
        private string _numSuffix = "";      // 数字后缀（%、ms、GB 等）
        private bool _barAnim, _numAnim;
        private float _animPos;
        private System.Windows.Forms.Timer _anim;

        public StatCard()
        {
            BackColor = Theme.CardBg;
            Radius = 12;
            Height = 128;
        }

        public void SetData(string caption, string metric, double percent, string footer, Color accent)
        {
            CaptionText = caption;
            string oldShown = _shownMetric ?? MetricText;
            MetricText = metric;
            Percent = percent;
            FooterText = footer;
            AccentColor = accent;
            StartBarAnim();
            StartNumAnim(oldShown, metric);
            Invalidate();
        }

        /// <summary>拆出前导数字与后缀（如 "45 %" → 45 / " %"）；无前导数字返回 false。</summary>
        private static bool TrySplit(string text, out double num, out string suffix)
        {
            num = 0; suffix = "";
            if (string.IsNullOrEmpty(text)) return false;
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(text, @"^\s*(-?\d+(?:\.\d+)?)\s*(.*)$");
            if (!m.Success) return false;
            bool ok = double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out num);
            suffix = m.Groups[2].Value;
            return ok;
        }

        private void StartBarAnim()
        {
            if (!AppSettings.Animations || Percent < 0 || _shownPercent < 0 ||
                Math.Abs(_shownPercent - Percent) < 0.5)
            {
                _shownPercent = Percent;
                return;
            }
            _percentFrom = _shownPercent;
            _barAnim = true;
            StartCardAnim();
        }

        /// <summary>指标数字滚动：旧值 → 新值插值，后缀保持。</summary>
        private void StartNumAnim(string oldShown, string target)
        {
            double a, b; string aSuf, bSuf;
            bool oldNum = TrySplit(oldShown ?? "", out a, out aSuf);
            bool newNum = TrySplit(target ?? "", out b, out bSuf);
            if (!AppSettings.Animations || !oldNum || !newNum || Math.Abs(a - b) < 0.01)
            {
                _shownMetric = target;
                _numAnim = false;
                return;
            }
            _numFrom = a; _numTo = b; _numSuffix = bSuf;
            _shownMetric = a.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + bSuf;
            _numAnim = true;
            StartCardAnim();
        }

        /// <summary>统一动画钟：进度条与数字滚动共用一条 EaseOutCubic 时间线。</summary>
        private void StartCardAnim()
        {
            // 每次（重新）启动动画都把时间线归零：否则完成后面 _animPos 停在 1.0，
            // 连续 SetData（仪表盘实时刷新）会让后续动画首帧即 done、只瞬跳到终值不播放过渡。
            _animPos = 0f;
            if (_anim == null)
            {
                _anim = new System.Windows.Forms.Timer { Interval = 16 };
                _anim.Tick += delegate
                {
                    _animPos += 0.14f;
                    bool done = _animPos >= 1f;
                    if (done) _animPos = 1f;
                    float t = 1f - (1f - _animPos) * (1f - _animPos) * (1f - _animPos); // EaseOutCubic

                    if (_barAnim)
                    {
                        _shownPercent = _percentFrom + (Percent - _percentFrom) * t;
                        if (done) _barAnim = false;
                    }
                    if (_numAnim)
                    {
                        double cur = _numFrom + (_numTo - _numFrom) * t;
                        _shownMetric = cur.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + _numSuffix;
                        if (done) _numAnim = false;
                    }

                    Invalidate();
                    if (done && !_barAnim && !_numAnim) _anim.Stop();
                };
            }
            _anim.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null) _anim.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 图标底
            Rectangle iconBox = new Rectangle(16, 14, 28, 28);
            Gfx.FillRound(g, iconBox, Theme.RadiusItem, Gfx.Alpha(AccentColor, 36));
            IconPainter.Draw(g, IconKind, new Rectangle(23, 21, 14, 14), AccentColor);

            using (SolidBrush b = new SolidBrush(Theme.TextSecondary))
            {
                Gfx.DrawTextEllipsis(g, CaptionText, Theme.FontSmall, Theme.TextSecondary,
                    new Rectangle(52, 18, Math.Max(10, Width - 68), 20));
            }

            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            {
                g.DrawString(MetricText, Theme.FontMetric, b, 12, 44);
            }

            int barY = Height - 32;
            Rectangle track = new Rectangle(16, barY, Math.Max(10, Width - 32), 6);
            Gfx.FillRound(g, track, 3, Theme.CardBgAlt);
            if (Percent >= 0)
            {
                double shown = _shownPercent < 0 ? Percent : _shownPercent;
                int w = (int)(track.Width * Math.Min(100, Math.Max(0, shown)) / 100.0);
                if (w < 6) w = 6;
                Gfx.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), 3, AccentColor);
            }

            if (!string.IsNullOrEmpty(FooterText))
            {
                Gfx.DrawTextEllipsis(g, FooterText, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(16, Height - 22, Math.Max(10, Width - 32), 16));
            }
        }
    }

    /// <summary>横向统计条：一行放多组「标签 / 数值」。</summary>
    public class StatStrip : RoundPanel
    {
        public sealed class Item
        {
            public string Label = "";
            public string Value = "";

            /// <summary>
            /// 数值色；Color.Empty 表示「跟随主题默认前景色」（绘制时实时解析）。
            /// 不可用 Theme.TextPrimary 作字段初始化器——那会把加项当刻的方案色快照进来，
            /// 切换到浅色方案后仍是深色方案的近白色，落在白色卡片上就是白底白字。
            /// </summary>
            public Color ValueColor = Color.Empty;
        }

        private readonly List<Item> _items = new List<Item>();

        public string Caption = "";
        public string IconKind = "";
        public Color CaptionColor = Theme.Accent;

        public StatStrip()
        {
            BackColor = Theme.CardBg;
            Radius = 12;
        }

        public int PreferredHeight
        {
            get { return string.IsNullOrEmpty(Caption) ? 76 : 108; }
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>加一项，数值使用主题默认前景色（绘制时解析，自动跟随主题切换）。</summary>
        public void Add(string label, string value)
        {
            Add(label, value, Color.Empty);
        }

        public void Add(string label, string value, Color valueColor)
        {
            Item it = new Item();
            it.Label = label == null ? "" : label;
            it.Value = value == null ? "" : value;
            it.ValueColor = valueColor;
            _items.Add(it);
        }

        /// <summary>
        /// 方案切换后重解析本控件的主题相关颜色。
        /// Item.ValueColor / CaptionColor 是「加项时快照」进字段的值，不经控件的
        /// BackColor/ForeColor 重映射（见 ThemeSkin.Reload），必须显式再做一次 旧色→新色 映射，
        /// 否则会残留旧方案的颜色（浅色下表现为白底白字）。
        /// </summary>
        internal void RemapThemeColors(Color[] from, Color[] to)
        {
            if (from == null || to == null) return;

            CaptionColor = RemapOne(CaptionColor, from, to);
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].ValueColor = RemapOne(_items[i].ValueColor, from, to);
            }
            Invalidate();
        }

        private static Color RemapOne(Color c, Color[] from, Color[] to)
        {
            if (c.IsEmpty) return c; // 跟随默认前景色，绘制时解析
            int cur = c.ToArgb();
            int n = Math.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
            {
                if (cur == from[i].ToArgb()) return to[i];
            }
            return c;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int top = 0;
            if (!string.IsNullOrEmpty(Caption))
            {
                top = Card.HeaderSize;
                int x = 16;
                if (!string.IsNullOrEmpty(IconKind))
                {
                    Rectangle box = new Rectangle(16, 13, 22, 22);
                    Gfx.FillRound(g, box, Theme.RadiusChip, Gfx.Alpha(CaptionColor, 32));
                    IconPainter.Draw(g, IconKind, new Rectangle(20, 17, 14, 14), CaptionColor);
                    x = 46;
                }
                using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Caption, Theme.FontBodyBold, b,
                        new Rectangle(x, 12, Math.Max(10, Width - x - 18), 24), sf);
                }
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 16, Card.HeaderSize - 3, Width - 17, Card.HeaderSize - 3);
                }
            }

            if (_items.Count == 0)
            {
                // 数据项为空时不留下大片空白：不少页面要等数据加载完成才 Add 数据项，
                // 这段空窗期若只画标题，就是一张「只有标题的空白卡片」，看起来像界面坏了。
                // 给一句弱化说明，语义与设计的 EmptyState 一致。
                Gfx.DrawTextEllipsis(g, "数据加载中…", Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(16, top + Math.Max(0, (Height - top) / 2 - 10), Math.Max(40, Width - 32), 20));
                return;
            }

            int area = Height - top;
            int colWidth = Math.Max(60, (Width - 32) / _items.Count);

            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                int x = 16 + i * colWidth;

                Gfx.DrawTextEllipsis(g, it.Label, Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(x, top + area / 2 - 22, colWidth - 12, 18));

                Color valueColor = it.ValueColor.IsEmpty ? Theme.TextPrimary : it.ValueColor;
                Gfx.DrawTextEllipsis(g, it.Value, Theme.FontSubTitle, valueColor,
                    new Rectangle(x, top + area / 2 - 2, colWidth - 12, 24));
            }
        }
    }
}
