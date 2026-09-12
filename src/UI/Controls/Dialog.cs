using System;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 与整体风格一致的对话框，替代系统 MessageBox。
    /// </summary>
    public static class Dialog
    {
        public static void Info(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, "info", Theme.Accent, false, false);
        }

        public static void Success(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, "check", Theme.Success, false, false);
        }

        public static void Warn(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, "info", Theme.Warning, false, false);
        }

        public static void Error(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, "close", Theme.Danger, false, false);
        }

        public static void Output(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, "list", Theme.Accent, false, true);
        }

        public static bool Confirm(IWin32Window owner, string title, string text)
        {
            return Show(owner, title, text, "info", Theme.Warning, true, false);
        }

        public static string Input(IWin32Window owner, string title, string label, string defaultValue)
        {
            using (DialogForm f = new DialogForm())
            {
                f.Setup(title, "", "info", Theme.Accent, DialogKind.Input);
                f.SetInput(label, defaultValue);
                DialogResult r = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                return r == DialogResult.OK ? f.InputValue : null;
            }
        }

        private static bool Show(IWin32Window owner, string title, string text, string icon,
            Color accent, bool confirm, bool mono)
        {
            using (DialogForm f = new DialogForm())
            {
                f.Setup(title, text == null ? "" : text, icon, accent,
                    confirm ? DialogKind.Confirm : (mono ? DialogKind.Output : DialogKind.Message));
                DialogResult r = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                return r == DialogResult.OK;
            }
        }
    }

    public enum DialogKind
    {
        Message,
        Confirm,
        Output,
        Input
    }

    internal sealed class DialogForm : Form
    {
        private string _title = "";
        private string _text = "";
        private string _icon = "info";
        private Color _accent = Theme.Accent;
        private DialogKind _kind = DialogKind.Message;

        private TextBox _input;
        private string _inputLabel = "";
        private string _inputDefault = "";

        private const int BodyTop = 74;
        private const int Edge = 22;
        private const int ButtonH = 36;

        public string InputValue
        {
            get { return _input == null ? "" : _input.Text; }
        }

        public DialogForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.CardBg;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = Theme.FontBody;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else if (e.KeyCode == Keys.Enter && _kind != DialogKind.Output)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
        }

        public void Setup(string title, string text, string icon, Color accent, DialogKind kind)
        {
            _title = title;
            _text = text;
            _icon = icon;
            _accent = accent;
            _kind = kind;
            BuildLayout();
        }

        public void SetInput(string label, string defaultValue)
        {
            _inputLabel = label;
            _inputDefault = defaultValue;
            BuildLayout();
        }

        private void BuildLayout()
        {
            SuspendLayout();

            // 清理旧控件
            for (int i = Controls.Count - 1; i >= 0; i--)
            {
                Controls[i].Dispose();
            }
            Controls.Clear();
            _input = null;

            bool isOutput = _kind == DialogKind.Output;
            bool isInput = _kind == DialogKind.Input;

            // 自适应屏幕工作区（上限 90%），小屏/高分屏缩放下不再超出屏幕
            Rectangle wa = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1280, 720);

            int width = Math.Min(isOutput ? 720 : 480, wa.Width - 40);
            int height;

            if (isOutput) height = 520;
            else if (isInput) height = 250;
            else height = 200;
            height = Math.Min(height, wa.Height - 40);
            if (height < 160) height = 160;

            // 文本区
            int textHeight = height - BodyTop - ButtonH - Edge * 2;
            if (textHeight < 50) textHeight = 50;

            if (isInput)
            {
                Label lbl = new Label();
                lbl.Text = _inputLabel;
                lbl.ForeColor = Theme.TextSecondary;
                lbl.BackColor = Color.Transparent;
                lbl.Font = Theme.FontBody;
                lbl.SetBounds(Edge, BodyTop, width - Edge * 2, 20);
                Controls.Add(lbl);

                _input = new TextBox();
                _input.Text = _inputDefault;
                _input.BorderStyle = BorderStyle.FixedSingle;
                _input.BackColor = Theme.WindowBg;
                _input.ForeColor = Theme.TextPrimary;
                _input.Font = Theme.FontBody;
                _input.SetBounds(Edge, BodyTop + 26, width - Edge * 2, 28);
                Controls.Add(_input);

                textHeight = 0;
            }
            else
            {
                TextBox box = new TextBox();
                box.Text = _text;
                box.Multiline = true;
                box.ReadOnly = true;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Theme.CardBg;
                box.ForeColor = Theme.TextSecondary;
                box.Font = isOutput ? Theme.FontMono : Theme.FontBody;
                box.ScrollBars = isOutput ? ScrollBars.Both : ScrollBars.Vertical;
                box.WordWrap = !isOutput;
                box.TabStop = false;
                box.SetBounds(Edge, BodyTop, width - Edge * 2, textHeight);
                Controls.Add(box);
            }

            // 按钮
            int by = height - ButtonH - Edge;

            if (_kind == DialogKind.Confirm)
            {
                AccentButton ok = MakeButton("确定", ButtonVariant.Primary, width - Edge - 100, by, 100);
                ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                AccentButton cancel = MakeButton("取消", ButtonVariant.Ghost, width - Edge - 100 - 108, by, 100);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(ok);
                Controls.Add(cancel);
            }
            else if (isInput)
            {
                AccentButton ok2 = MakeButton("确定", ButtonVariant.Primary, width - Edge - 100, by, 100);
                ok2.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                AccentButton cancel2 = MakeButton("取消", ButtonVariant.Ghost, width - Edge - 100 - 108, by, 100);
                cancel2.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(ok2);
                Controls.Add(cancel2);
            }
            else if (isOutput)
            {
                AccentButton copy = MakeButton("复制", ButtonVariant.Secondary, Edge, by, 92);
                copy.Click += delegate { CopyToClipboard(); };
                AccentButton close = MakeButton("关闭", ButtonVariant.Primary, width - Edge - 100, by, 100);
                close.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                Controls.Add(copy);
                Controls.Add(close);
            }
            else
            {
                AccentButton close = MakeButton("知道了", ButtonVariant.Primary, width - Edge - 110, by, 110);
                close.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                Controls.Add(close);
            }

            // 标题栏按钮（关闭）
            CaptionButton x = new CaptionButton("close");
            x.HoverColor = Theme.Danger;
            x.SetBounds(width - 44, 0, 44, 32);
            x.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(x);

            ClientSize = new Size(width, height);
            ResumeLayout(false);
        }

        private AccentButton MakeButton(string text, ButtonVariant variant, int x, int y, int w)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.Variant = variant;
            b.SetBounds(x, y, w, ButtonH);
            return b;
        }

        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(_text);
            }
            catch
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            using (SolidBrush b = new SolidBrush(Theme.CardBg))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            using (Pen p = new Pen(Theme.BorderStrong, 1f))
            {
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            // 顶部色条
            using (SolidBrush b = new SolidBrush(_accent))
            {
                g.FillRectangle(b, 0, 0, Width, 3);
            }

            IconPainter.Draw(g, _icon, new Rectangle(Edge, 22, 20, 20), _accent);

            using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
            {
                Rectangle r = new Rectangle(Edge + 30, 18, Width - Edge * 2 - 46, 28);
                Gfx.DrawTextEllipsis(g, _title, Theme.FontSubTitle, Theme.TextPrimary, r);
            }

            if (_kind != DialogKind.Input)
            {
                using (Pen p = new Pen(Theme.Border))
                {
                    g.DrawLine(p, Edge, 62, Width - Edge, 62);
                }
            }
        }

        // 支持拖动：仅顶部标题区域可拖动，避免影响按钮点击
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && m.Result.ToInt32() == HTCLIENT)
            {
                int sx = unchecked((short)(long)m.LParam);
                int sy = unchecked((short)((long)m.LParam >> 16));
                Point p = PointToClient(new Point(sx, sy));
                if (p.Y <= 46) m.Result = (IntPtr)HTCAPTION;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_input != null)
            {
                _input.Focus();
                _input.SelectAll();
            }
        }
    }
}
