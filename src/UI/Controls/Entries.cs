using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{
    /// <summary>圆形图标入口：大圆底 + 图标 + 下方文字，类似管家/火绒的快捷入口。</summary>
    public class RoundEntry : Control
    {
        private bool _hover;
        private bool _press;
        private string _icon = "clean";
        private Color _accent = Theme.Accent;
        private string _caption = "";

        public RoundEntry(string caption, string icon, Color accent)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(96, 104);
            Cursor = Cursors.Hand;
            _caption = caption;
            _icon = icon;
            _accent = accent;
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value == null ? "" : value; Invalidate(); }
        }

        public string IconKind
        {
            get { return _icon; }
            set { _icon = value == null ? "" : value; Invalidate(); }
        }

        public Color AccentColor
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false; _press = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _press = true; Invalidate(); base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _press = false; Invalidate(); base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            if (Parent != null)
            {
                g.FillRectangle(GdiCache.Brush(Parent.BackColor), ClientRectangle);
            }

            int circle = Math.Min(56, Width - 8);
            int cx = (Width - circle) / 2;
            Rectangle r = new Rectangle(cx, 6, circle, circle);

            if (_press)
            {
                Gfx.FillRound(g, r, circle / 2, Gfx.Alpha(_accent, 60));
            }
            else if (_hover)
            {
                Gfx.FillRound(g, r, circle / 2, Gfx.Alpha(_accent, 42));
            }
            else
            {
                Gfx.FillRound(g, r, circle / 2, Gfx.Alpha(_accent, 24));
            }

            Gfx.StrokeRound(g, r, circle / 2, Gfx.Alpha(_accent, _hover ? 170 : 90), 1.5f);

            int iconSize = (int)(circle * 0.44);
            IconPainter.Draw(g, _icon,
                new Rectangle(cx + (circle - iconSize) / 2, 6 + (circle - iconSize) / 2, iconSize, iconSize),
                _hover ? Color.White : _accent);

            Gfx.DrawTextCenter(g, _caption, Theme.FontSmall,
                _hover ? Theme.TextPrimary : Theme.TextSecondary,
                new Rectangle(0, circle + 16, Width, Height - circle - 16));
        }
    }

    /// <summary>健康评分大卡片：左侧环形分数，右侧状态说明与操作按钮。</summary>
    public class HealthCard : RoundPanel
    {
        private int _score = -1;
        private string _status = "尚未体检";
        private string _hint = "点击右侧「一键体检」开始检查系统状态";
        private Color _scoreColor = Theme.TextMuted;

        // 动画状态：得分滚动 + 体检扫描环
        private float _displayScore;      // 0→目标分数的插值
        private float _scanAngle;         // 体检中旋转角
        private Timer _anim;

        // 分数字号较大，字体缓存起来，避免每次绘制创建 GDI 对象
        private static Font _scoreFont;

        private static Font ScoreFont
        {
            get
            {
                if (_scoreFont == null) _scoreFont = new Font(Theme.FontFamilyName, 30F, FontStyle.Bold);
                return _scoreFont;
            }
        }

        public HealthCard()
        {
            BackColor = Theme.CardBg;
            Radius = 12;
            Height = 156;
        }

        /// <summary>得分滚动动画：圆环与数字从 0 缓动到目标值。</summary>
        public void SetScoreAnimated(int score)
        {
            if (!AppSettings.Animations)
            {
                _displayScore = score;
                Invalidate();
                return;
            }
            _score = score;
            StartAnim(() =>
            {
                _displayScore += (score - _displayScore) * 0.22f;
                if (Math.Abs(score - _displayScore) < 0.6f)
                {
                    _displayScore = score;
                    return true;
                }
                return false;
            });
        }

        /// <summary>体检进行中：圆环进入旋转扫描状态。</summary>
        public void SetScanning(bool scanning)
        {
            if (scanning)
            {
                _score = -2; // 扫描态
                StartAnim(() =>
                {
                    _scanAngle = (_scanAngle + 9f) % 360f;
                    return false; // 持续旋转直到外部停止
                });
            }
            else
            {
                _score = -1;
            }
            Invalidate();
        }

        private void StartAnim(Func<bool> tick)
        {
            if (_anim == null)
            {
                _anim = new Timer();
                _anim.Interval = 16;
                _anim.Tick += delegate
                {
                    if (tick())
                    {
                        _anim.Stop();
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

        public int Score
        {
            get { return _score; }
            set { _score = value; _displayScore = value; Invalidate(); }
        }

        public string StatusText
        {
            get { return _status; }
            set { _status = value == null ? "" : value; Invalidate(); }
        }

        public string HintText
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; Invalidate(); }
        }

        public Color ScoreColor
        {
            get { return _scoreColor; }
            set { _scoreColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int ring = 104;
            int left = 30;
            int top = (Height - ring) / 2;
            Rectangle r = new Rectangle(left, top, ring, ring);

            Color color = _score < 0 ? Theme.Border : _scoreColor;

            // 底环
            using (Pen pen = new Pen(Theme.CardBgAlt, 9f))
            {
                g.DrawEllipse(pen, r);
            }

            if (_score == -2)
            {
                // 体检扫描态：底环上旋转高亮弧
                using (Pen pen = new Pen(Theme.Accent, 9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, r, _scanAngle, 95);
                }
                Gfx.DrawTextCenter(g, "···", ScoreFont, Theme.Accent, r);
                Gfx.DrawTextCenter(g, "体检中",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }
            else if (_score >= 0)
            {
                // 得分滚动动画：圆弧与数字随 _displayScore 缓动
                int sweep = (int)(360.0 * Math.Max(0, Math.Min(100, _displayScore)) / 100.0);
                using (Pen pen = new Pen(color, 9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, r, -90, sweep);
                }

                int shown = (int)Math.Round(_displayScore);
                Gfx.DrawTextCenter(g, shown.ToString(), ScoreFont, Theme.TextPrimary, r);
                Gfx.DrawTextCenter(g, "分",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }
            else
            {
                string scoreText = "--";
                Gfx.DrawTextCenter(g, scoreText, ScoreFont, Theme.TextMuted, r);
                Gfx.DrawTextCenter(g, "未体检",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }

            int textLeft = left + ring + 34;
            int textWidth = Math.Max(10, Width - textLeft - 210);

            Gfx.DrawTextEllipsis(g, "系统健康评分", Theme.FontSmall, Theme.TextMuted,
                new Rectangle(textLeft, top + 14, textWidth, 20));

            Gfx.DrawTextEllipsis(g, _status, Theme.FontSubTitle, Theme.TextPrimary,
                new Rectangle(textLeft, top + 38, textWidth, 26));

            Gfx.DrawTextEllipsis(g, _hint, Theme.FontSmall, Theme.TextSecondary,
                new Rectangle(textLeft, top + 70, textWidth, 22));
        }
    }
}
