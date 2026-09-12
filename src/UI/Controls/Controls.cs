﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;

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
        private int _radius = 12;
        private Color _borderColor = Theme.Border;
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
            set { _borderColor = value; Invalidate(); }
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
                Gfx.FillRound(g, box, 6, Gfx.Alpha(_iconColor, 32));
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
        Success
    }

    /// <summary>完全自绘的扁平按钮。</summary>
    public class AccentButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private ButtonVariant _variant = ButtonVariant.Secondary;
        private int _radius = 7;
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
        }

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
            _pressed = false;
            Invalidate();
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        private void ResolveColors(out Color fill, out Color text, out Color border)
        {
            border = Color.Empty;
            switch (_variant)
            {
                case ButtonVariant.Primary:
                    fill = _pressed ? Theme.AccentPress : (_hover ? Theme.AccentHover : Theme.Accent);
                    text = Color.White;
                    break;
                case ButtonVariant.Danger:
                    fill = _pressed ? Gfx.Shade(Theme.Danger, 0.85) : (_hover ? Theme.DangerHover : Theme.Danger);
                    text = Color.White;
                    break;
                case ButtonVariant.Success:
                    fill = _pressed ? Gfx.Shade(Theme.Success, 0.85) : (_hover ? Gfx.Shade(Theme.Success, 1.12) : Theme.Success);
                    text = Color.FromArgb(10, 28, 18);
                    break;
                case ButtonVariant.Ghost:
                    fill = _pressed ? Theme.CardBgAlt : (_hover ? Gfx.Alpha(Theme.Border, 90) : Color.Transparent);
                    text = _hover ? Theme.TextPrimary : Theme.TextSecondary;
                    border = _hover ? Theme.BorderStrong : Color.Empty;
                    break;
                default:
                    fill = _pressed ? Gfx.Shade(Theme.CardBgAlt, 0.92) : (_hover ? Theme.CardHover : Theme.CardBgAlt);
                    text = _hover ? Theme.TextPrimary : Theme.TextSecondary;
                    border = _hover ? Theme.BorderStrong : Theme.Border;
                    break;
            }

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
            ResolveColors(out fill, out text, out border);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (fill.A > 0)
            {
                Gfx.FillRound(g, r, _radius, fill);
            }
            if (border != Color.Empty && border.A > 0)
            {
                Gfx.StrokeRound(g, r, _radius, border, 1f);
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
        }

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
            if (_filled)
            {
                Gfx.FillRound(g, r, Height / 2, _badgeColor);
            }
            else
            {
                Gfx.FillRound(g, r, Height / 2, Gfx.Alpha(_badgeColor, 30));
                Gfx.StrokeRound(g, r, Height / 2, Gfx.Alpha(_badgeColor, 110), 1f);
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

    /// <summary>主题化的信息提示条。</summary>
    public class NoticeBar : RoundPanel
    {
        private string _text = "";
        private Color _accent = Theme.Warning;
        private string _icon = "info";
        private bool _clickable;

        public NoticeBar()
        {
            BackColor = Theme.CardBg;
            Radius = 9;
            Height = 42;
            CornerColor = Theme.WindowBg;
            BorderColor = Theme.Border;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            BorderColor = Gfx.Alpha(_accent, 70);
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 左侧色条
            using (GraphicsPath p = Gfx.RoundRect(new Rectangle(0, 12, 3, Height - 24), 2))
            using (SolidBrush b = new SolidBrush(_accent))
            {
                g.FillPath(b, p);
            }

            IconPainter.Draw(g, _icon, new Rectangle(14, (Height - 15) / 2, 15, 15), _accent);

            Rectangle textRect = new Rectangle(38, 0, Math.Max(10, Width - 52), Height);
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(Theme.TextSecondary))
                {
                    g.DrawString(_text, Theme.FontBody, b, textRect, sf);
                }
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

        private double _shownPercent = -1;   // 进度条当前显示值（向 Percent 缓动）
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
            MetricText = metric;
            Percent = percent;
            FooterText = footer;
            AccentColor = accent;
            StartBarAnim();
            Invalidate();
        }

        /// <summary>进度条从当前显示值缓动到目标值（关闭动画或首帧则直接到位）。</summary>
        private void StartBarAnim()
        {
            if (!AppSettings.Animations || Percent < 0 || _shownPercent < 0 ||
                Math.Abs(_shownPercent - Percent) < 0.5)
            {
                _shownPercent = Percent;
                return;
            }
            if (_anim == null)
            {
                _anim = new System.Windows.Forms.Timer { Interval = 16 };
                _anim.Tick += delegate
                {
                    double diff = Percent - _shownPercent;
                    if (Math.Abs(diff) < 0.6)
                    {
                        _shownPercent = Percent;
                        _anim.Stop();
                    }
                    else
                    {
                        _shownPercent += diff * 0.25; // 指数缓动
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 图标底
            Rectangle iconBox = new Rectangle(16, 14, 28, 28);
            Gfx.FillRound(g, iconBox, 8, Gfx.Alpha(AccentColor, 36));
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
            public Color ValueColor = Theme.TextPrimary;
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

        public void Add(string label, string value)
        {
            Add(label, value, Theme.TextPrimary);
        }

        public void Add(string label, string value, Color valueColor)
        {
            Item it = new Item();
            it.Label = label == null ? "" : label;
            it.Value = value == null ? "" : value;
            it.ValueColor = valueColor;
            _items.Add(it);
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
                    Gfx.FillRound(g, box, 6, Gfx.Alpha(CaptionColor, 32));
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

            if (_items.Count == 0) return;

            int area = Height - top;
            int colWidth = Math.Max(60, (Width - 32) / _items.Count);

            for (int i = 0; i < _items.Count; i++)
            {
                Item it = _items[i];
                int x = 16 + i * colWidth;

                Gfx.DrawTextEllipsis(g, it.Label, Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(x, top + area / 2 - 22, colWidth - 12, 18));

                Gfx.DrawTextEllipsis(g, it.Value, Theme.FontSubTitle, it.ValueColor,
                    new Rectangle(x, top + area / 2 - 2, colWidth - 12, 24));
            }
        }
    }
}
