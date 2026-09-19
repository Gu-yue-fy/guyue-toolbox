using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 危险操作确认：确认弹窗必须说清三件事——「将发生什么 / 是否可撤销 / 影响范围」，
        /// 并让确认键说清动作本身（如「删除」而不是「确定」）。
        ///
        /// 与 <see cref="Confirm"/> 的区别不只是样式：红色确认键能让人在点之前就分清
        /// 危险与普通操作；「是否可撤销」单独成行，因为它是用户决策时最先看的一句。
        /// 不可撤销时该行用警告色，可撤销时用安全色。
        /// </summary>
        /// <param name="consequence">点下去会发生什么（动作与直接后果）。</param>
        /// <param name="reversible">能否还原、怎么还原；不可还原要明说。</param>
        /// <param name="scope">会影响到的范围（哪些文件/服务/功能，个人数据是否受影响）。</param>
        /// <param name="confirmLabel">确认键文字：写动作名（删除 / 禁用 / 还原），不要写"确定"。</param>
        /// <param name="irreversible">true 表示不可撤销——该行改用危险色强调。</param>
        public static bool ConfirmDanger(IWin32Window owner, string title,
            string consequence, string reversible, string scope, string confirmLabel, bool irreversible)
        {
            using (DialogForm f = new DialogForm())
            {
                f.SetupDanger(title, consequence, reversible, scope,
                    string.IsNullOrEmpty(confirmLabel) ? "确定" : confirmLabel, irreversible);
                DialogResult r = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                return r == DialogResult.OK;
            }
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

        /// <summary>
        /// 详情说明（设计"详情抽屉"的桌面实现）：四段式解释 + 显式声明可逆性。
        /// 空段自动跳过——宁少一段，也不拿"无"占位。
        /// </summary>
        public static void Detail(IWin32Window owner, DetailInfo info)
        {
            if (info == null) return;

            List<string> labels = new List<string>();
            List<string> texts = new List<string>();
            List<Color> tones = new List<Color>();

            Add(labels, texts, tones, "这是什么", info.What, Theme.TextPrimary);
            Add(labels, texts, tones, "为什么会这样", info.Why, Theme.TextSecondary);
            Add(labels, texts, tones, "怎么处理", info.How, Theme.TextPrimary);
            Add(labels, texts, tones, "风险与影响", info.Risk, info.RiskTone ? Theme.Warning : Theme.TextSecondary);
            Add(labels, texts, tones, "是否可撤销", info.Reversible, info.Irreversible ? Theme.Warning : Theme.Success);

            using (DialogForm f = new DialogForm())
            {
                f.SetupDetail(info.Title, info.Icon, info.Accent,
                    labels.ToArray(), texts.ToArray(), tones.ToArray());
                if (owner == null) f.ShowDialog(); else f.ShowDialog(owner);
            }
        }

        /// <summary>
        /// 任意分段的说明弹窗：段数与标题由调用方给定（如"逐项对比"的每个测试项一段）。
        /// 与 <see cref="Detail"/> 共用同一套分段渲染，不另造控件。
        /// </summary>
        public static void Sections(IWin32Window owner, string title, string icon, Color accent,
            string[] labels, string[] texts, Color[] tones)
        {
            using (DialogForm f = new DialogForm())
            {
                f.SetupDetail(title, icon, accent, labels, texts, tones);
                if (owner == null) f.ShowDialog(); else f.ShowDialog(owner);
            }
        }

        private static void Add(List<string> labels, List<string> texts, List<Color> tones,
            string label, string text, Color tone)
        {
            if (string.IsNullOrEmpty(text)) return;
            labels.Add(label);
            texts.Add(text);
            tones.Add(tone);
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
        Input,
        /// <summary>危险操作确认：三段式信息 + 语义色确认键。</summary>
        Danger,
        /// <summary>详情说明：多段式解释（这是什么/为什么/怎么处理/风险）。</summary>
        Detail
    }

    /// <summary>
    /// 详情抽屉的内容载体：四段式解释。
    /// 段位固定为「这是什么 / 为什么会这样 / 怎么处理 / 风险与影响 / 是否可撤销」，
    /// 空段自动跳过——宁少一段，也不写"无"来占位置。
    /// </summary>
    public sealed class DetailInfo
    {
        public string Title = "";
        public string Icon = "info";
        public Color Accent = Theme.Accent;
        public string What = "";
        public string Why = "";
        public string How = "";
        public string Risk = "";
        public string Reversible = "";
        /// <summary>风险段是否用警告色（有副作用时）。</summary>
        public bool RiskTone;
        /// <summary>不可撤销时「是否可撤销」段用警告色。</summary>
        public bool Irreversible;
    }

    internal sealed class DialogForm : Form
    {
        private string _title = "";
        private string _text = "";
        private string _icon = "info";
        private Color _accent = Theme.Accent;
        private DialogKind _kind = DialogKind.Message;

        // 分段内容（危险确认 3 段 / 详情 4~5 段共用）：每项为「标签, 正文, 颜色索引」
        private readonly List<string[]> _sections = new List<string[]>();
        private readonly List<Color> _sectionTones = new List<Color>();
        private string _dConfirmLabel = "确定";
        private bool _dIrreversible;

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

        /// <summary>配置为危险确认：三段式内容 + 语义色确认键（按钮文字写动作名）。</summary>
        public void SetupDanger(string title, string consequence, string reversible,
            string scope, string confirmLabel, bool irreversible)
        {
            _title = title;
            _text = "";
            _icon = "warn";
            // 颜色跟随可撤销性：不可撤销=红，可撤销=琥珀
            // （设计语义：红只用于真正回不去的操作，滥用会让红色失去警示力）
            _accent = irreversible ? Theme.Danger : Theme.Warning;
            _kind = DialogKind.Danger;
            _sections.Clear();
            _sectionTones.Clear();
            AddSection("将发生", consequence, Theme.TextPrimary);
            AddSection("是否可撤销", reversible, irreversible ? Theme.Warning : Theme.Success);
            AddSection("影响范围", scope, Theme.TextSecondary);
            _dConfirmLabel = confirmLabel;
            _dIrreversible = irreversible;
            BuildLayout();
        }

        /// <summary>
        /// 配置为详情说明：按「这是什么 / 为什么 / 怎么处理 / 风险与影响 / 是否可撤销」分段呈现。
        /// 目的与危险确认相反——不是拦住用户，而是让用户看得懂再决定。
        /// </summary>
        public void SetupDetail(string title, string accentIcon, Color accent,
            string[] labels, string[] texts, Color[] tones)
        {
            _title = title;
            _text = "";
            _icon = string.IsNullOrEmpty(accentIcon) ? "info" : accentIcon;
            _accent = accent;
            _kind = DialogKind.Detail;
            _sections.Clear();
            _sectionTones.Clear();
            if (labels != null && texts != null)
            {
                int n = Math.Min(labels.Length, texts.Length);
                for (int i = 0; i < n; i++)
                {
                    Color tone = (tones != null && i < tones.Length) ? tones[i] : Theme.TextSecondary;
                    AddSection(labels[i], texts[i], tone);
                }
            }
            _dConfirmLabel = "知道了";
            BuildLayout();
        }

        private void AddSection(string label, string text, Color tone)
        {
            _sections.Add(new string[] { label == null ? "" : label, text == null ? "" : text });
            _sectionTones.Add(tone);
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
            bool isDanger = _kind == DialogKind.Danger;
            // 危险确认与详情都用「分段自绘」：内容行数不定，高度必须按内容测算
            bool isSectioned = isDanger || _kind == DialogKind.Detail;

            // 自适应屏幕工作区（上限 90%），小屏/高分屏缩放下不再超出屏幕
            Rectangle wa = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1280, 720);

            // 分段内容的正文更长，给更宽的版心
            int width = Math.Min(isOutput ? 720 : (isSectioned ? 520 : 480), wa.Width - 40);
            int height;

            if (isOutput) height = 520;
            else if (isInput) height = 250;
            else if (isSectioned) height = BodyTop + MeasureSections(width - Edge * 2) + 16 + ButtonH + Edge;
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
            else if (!isSectioned)
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

            if (_kind == DialogKind.Danger)
            {
                // 确认键用语义色 + 动作名：颜色在"点之前"就分出危险等级，
                // 文字写清"点下去做什么"，避免"确定/不过脑子点"。
                AccentButton ok = MakeButton(_dConfirmLabel,
                    _dIrreversible ? ButtonVariant.Danger : ButtonVariant.Warning,
                    width - Edge - 116, by, 116);
                ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                AccentButton cancel = MakeButton("取消", ButtonVariant.Ghost, width - Edge - 116 - 108, by, 100);
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
                Controls.Add(ok);
                Controls.Add(cancel);
            }
            else if (_kind == DialogKind.Detail)
            {
                // 详情只是"看明白"，不需要确认语义：只给一个关闭键
                AccentButton close2 = MakeButton("知道了", ButtonVariant.Primary, width - Edge - 110, by, 110);
                close2.Click += delegate { DialogResult = DialogResult.OK; Close(); };
                Controls.Add(close2);
            }
            else if (_kind == DialogKind.Confirm)
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

        /// <summary>
        /// 测算三段式内容所需高度。用 TextRenderer 而非 GDI+ 的 MeasureString：
        /// 测量与实际绘制必须是同一套排版引擎，否则换行位置不同、高度会算错。
        /// </summary>
        private int MeasureSections(int width)
        {
            int h = 0;
            for (int i = 0; i < _sections.Count; i++)
            {
                h += SectionHeight(width, _sections[i][1]);
            }
            return h;
        }

        private static int SectionHeight(int width, string text)
        {
            Size sz = TextRenderer.MeasureText(text == null ? "" : text, Theme.FontBody,
                new Size(Math.Max(60, width), int.MaxValue), TextFormatFlags.WordBreak);
            return 18 + Math.Max(18, sz.Height) + 12; // 小标签 18 + 正文 + 段间距
        }

        /// <summary>绘制一段「小标签 + 正文」，返回下一段的起始 y。</summary>
        private static int DrawSection(Graphics g, int x, int y, int width, string label, string text, Color textColor)
        {
            Gfx.DrawTextEllipsis(g, label, Theme.FontMicro, Theme.TextMuted,
                new Rectangle(x, y, Math.Max(40, width), 16));

            string body = text == null ? "" : text;
            Size sz = TextRenderer.MeasureText(body, Theme.FontBody,
                new Size(Math.Max(60, width), int.MaxValue), TextFormatFlags.WordBreak);
            int bodyH = Math.Max(18, sz.Height);
            TextRenderer.DrawText(g, body, Theme.FontBody,
                new Rectangle(x, y + 18, Math.Max(40, width), bodyH), textColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            return y + 18 + bodyH + 12;
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

            if (_kind == DialogKind.Danger || _kind == DialogKind.Detail)
            {
                int y = BodyTop;
                int w = Width - Edge * 2;
                for (int i = 0; i < _sections.Count; i++)
                {
                    Color tone = i < _sectionTones.Count ? _sectionTones[i] : Theme.TextSecondary;
                    y = DrawSection(g, Edge, y, w, _sections[i][0], _sections[i][1], tone);
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
