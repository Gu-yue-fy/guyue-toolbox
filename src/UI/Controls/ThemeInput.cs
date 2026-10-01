using System;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    public sealed class ThemeInput : Panel
    {
        /// <summary>前置图标区宽度：图标 14 + 左右留白 10。</summary>
        private const int IconPad = 24;

        private readonly TextBox _box;
        private readonly string _icon;
        private readonly int _radius;
        private readonly int _designWidth;
        private bool _focused;

        /// <summary>内部输入框：事件绑定、Text 读写照常用它（外壳只负责画边框与图标）。</summary>
        public TextBox Box { get { return _box; } }

        /// <summary>
        /// 创建时的设计宽度（含 1px 边框与可能的图标区）。行内布局以它为准、而不是当前 Width——
        /// 否则窗口很窄时被等分压窄一次后，后续布局会把"被压窄的宽度"当基准，永远回不到设计宽度。
        /// </summary>
        public int DesignWidth { get { return _designWidth; } }

        /// <summary>转发到内部输入框的文本，方便调用方直接读写。</summary>
        public override string Text
        {
            get { return _box == null ? "" : _box.Text; }
            set { if (_box != null) _box.Text = value; }
        }

        public ThemeInput(TextBox box)
            : this(box, "")
        {
        }

        /// <param name="icon">前置图标名（IconPainter 支持的 case，如 "search"）；空串 = 不带图标。</param>
        public ThemeInput(TextBox box, string icon)
        {
            _box = box;
            _icon = icon == null ? "" : icon;
            _radius = Theme.RadiusButton;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            BackColor = Theme.WindowBg;   // 圆角之外露出的是页面底色，不是边框色
            Margin = box.Margin;
            // 高度规整：单行框各页面传 28~30 不等，统一收敛到 26（外壳 28），
            // 与同排按钮同一档，不会"高出一头"。多行框不动：高度是内容设计值，压了会看不到内容。
            if (!box.Multiline && box.Height > 26) box.Height = 26;

            int leftPad = _icon.Length > 0 ? IconPad : 0;
            // 外壳比输入框多出 1px 边框（左右各 1）与图标区
            _designWidth = box.Width + 2 + leftPad;
            Size = new Size(_designWidth, box.Height + 2);

            box.BorderStyle = BorderStyle.None;      // 去掉系统边框（它才是"白框/黑框"的来源）
            // 用次级卡背景而非卡背景：与页面背景形成微妙区分，输入框才"找得到"
            box.BackColor = Theme.CardBgAlt;
            box.ForeColor = Theme.TextPrimary;
            box.Location = new Point(1 + leftPad, 1);
            box.Margin = Padding.Empty;
            box.GotFocus += delegate { _focused = true; Invalidate(); };
            box.LostFocus += delegate { _focused = false; Invalidate(); };

            Controls.Add(box);
        }

        /// <summary>把一个已建好的 TextBox 包成主题化输入框（页面里改一行即可接入）。</summary>
        public static ThemeInput Wrap(TextBox box)
        {
            return new ThemeInput(box);
        }

        /// <summary>包成带放大镜的搜索框（全站搜索框统一长相）。</summary>
        public static ThemeInput WrapSearch(TextBox box)
        {
            return new ThemeInput(box, "search");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 先铺输入框底色（圆角内），再描边：这样圆角处不会露出外壳底色
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Gfx.FillRound(e.Graphics, r, _radius, Theme.CardBgAlt);
            // 未聚焦用强边界色（Border 太弱导致深色下"找不到"输入框），聚焦用强调色
            Gfx.StrokeRound(e.Graphics, r, _radius, _focused ? Theme.Accent : Theme.BorderStrong, 1f);

            if (_icon.Length > 0)
            {
                IconPainter.Draw(e.Graphics, _icon,
                    new Rectangle(8, (Height - 14) / 2, 14, 14), _focused ? Theme.Accent : Theme.TextMuted);
            }

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _box != null)
            {
                // 外壳由调用方一起释放；这里只解掉引用，避免内部输入框被重复释放
                Controls.Remove(_box);
            }
            base.Dispose(disposing);
        }
    }
}
