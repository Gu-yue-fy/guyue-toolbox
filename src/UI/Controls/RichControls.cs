/* ============================================================
 * 文件说明：高信息密度展示控件（设计「工具型 / 模块型」页面的主视觉承载面）：
 *           RingGauge 环形仪表、HeroCard 工具卡、RankStrip 排行条、CoreMatrix 核心矩阵。
 *           这些控件的共同职责是把「一个关键数字 + 它的上下文」画成可一眼读懂的图形，
 *           而不是再用一行「标签 / 数值」——后者是界面显得简陋的主因。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 环形仪表：轨道 + 渐变进度弧 + 中心主数值 / 单位 + 副行 + 状态文案。
    /// 对应设计 <c>mem-hero-ring</c>：进度为渐变弧并在末端收一个圆点，
    /// 中心数值用等宽大字号，副行与状态给出读数上下文。
    /// </summary>
    public class RingGauge : Control
    {
        private double _percent;
        private string _centerText = "--";
        private string _centerUnit = "%";
        private string _subText = "";
        private string _stateText = "";
        private Color _tone = Theme.Accent;
        private Color _toneTo = Color.Empty;
        private int _thickness = 10;

        public RingGauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(200, 200);
        }

        public double Percent
        {
            get { return _percent; }
            set { _percent = value; Invalidate(); }
        }

        /// <summary>中心主数值（如 "39"）。</summary>
        public string CenterText
        {
            get { return _centerText; }
            set { _centerText = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>主数值右侧的单位（如 "%"），按小字号弱化绘制。</summary>
        public string CenterUnit
        {
            get { return _centerUnit; }
            set { _centerUnit = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>中心下方副行（如 "12.6 / 32 GB"）。</summary>
        public string SubText
        {
            get { return _subText; }
            set { _subText = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>副行下方状态文案（如 "运行良好"），使用本仪表的语义色。</summary>
        public string StateText
        {
            get { return _stateText; }
            set { _stateText = value == null ? "" : value; Invalidate(); }
        }

        public Color Tone
        {
            get { return _tone; }
            set { _tone = value; Invalidate(); }
        }

        /// <summary>渐变终点色。为空时用 <see cref="Tone"/> 单色。</summary>
        public Color ToneTo
        {
            get { return _toneTo; }
            set { _toneTo = value; Invalidate(); }
        }

        public int Thickness
        {
            get { return _thickness; }
            set { _thickness = value < 2 ? 2 : value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int t = _thickness;
            int size = Math.Min(Width, Height) - t - 2;
            if (size <= 8) return;

            Rectangle ring = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);

            // 轨道
            g.DrawEllipse(GdiCache.Pen(Theme.CardBgAlt, t), ring);

            double pct = _percent;
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;

            if (pct > 0)
            {
                Color to = _toneTo.IsEmpty ? _tone : _toneTo;
                float sweep = (float)(pct * 3.6);
                // 每段约 12°：段数随弧长自适应，短弧不会白画几十段
                int seg = (int)Math.Ceiling(sweep / 12.0);
                if (seg < 1) seg = 1;

                for (int i = 0; i < seg; i++)
                {
                    float a0 = -90f + sweep * i / seg;
                    float a1 = -90f + sweep * (i + 1) / seg;
                    Color c = seg <= 1 ? _tone : Gfx.Blend(_tone, to, (double)i / (seg - 1));
                    // 相邻段多画 0.8° 覆盖接缝，避免出现断线
                    g.DrawArc(GdiCache.Pen(c, t), ring, a0, (a1 - a0) + 0.8f);
                }

                // 末端圆点：让弧的收尾像设计那样有"落点"
                double endRad = (-90.0 + sweep) * Math.PI / 180.0;
                int rr = (size - t) / 2;
                int cx = ring.X + size / 2 + (int)Math.Round(Math.Cos(endRad) * rr);
                int cy = ring.Y + size / 2 + (int)Math.Round(Math.Sin(endRad) * rr);
                g.FillEllipse(GdiCache.Brush(to), cx - t / 2, cy - t / 2, t, t);
            }

            // 中心：主数值 + 单位（合成一行水平居中）
            SizeF vw = g.MeasureString(_centerText, Theme.FontMetric);
            SizeF uw = g.MeasureString(_centerUnit, Theme.FontBody);
            float midY = ring.Y + size / 2f;
            float startX = ring.X + (size - (vw.Width + uw.Width)) / 2f;

            g.DrawString(_centerText, Theme.FontMetric, GdiCache.Brush(Theme.TextPrimary),
                startX, midY - vw.Height / 2f - 6f);
            g.DrawString(_centerUnit, Theme.FontBody, GdiCache.Brush(Theme.TextSecondary),
                startX + vw.Width - 2f, midY - uw.Height / 2f + 8f);

            if (_subText.Length > 0)
            {
                Gfx.DrawTextCenter(g, _subText, Theme.FontMicro, Theme.TextSecondary,
                    new Rectangle(ring.X, (int)midY + 12, size, 16));
            }

            if (_stateText.Length > 0)
            {
                Gfx.DrawTextCenter(g, _stateText, Theme.FontSmall, _tone,
                    new Rectangle(ring.X, (int)midY + 32, size, 18));
            }
        }
    }

    /// <summary>
    /// 工具型 Hero 卡，对应设计 <c>mem-hero</c>：
    /// 玻璃底 + 环境光晕 + 左侧圆环仪表 + 中部标题 / 说明 / 实时指标 + 右侧圆形主操作。
    /// 用于承载"本页最重要的一件事"（如内存页的一键释放）。
    /// </summary>
    public class HeroCard : RoundPanel
    {
        private readonly RingGauge _gauge = new RingGauge();
        private readonly AccentButton _cta = new AccentButton();

        private string _tagText = "";
        private string _tagIcon = "bolt";
        private string _headline = "";
        private string _subText = "";
        private readonly List<string> _statKeys = new List<string>();
        private readonly List<string> _statValues = new List<string>();

        public HeroCard()
        {
            BackColor = Theme.GlassCardBg;
            Radius = Theme.RadiusCard;
            ShowBorder = true;
            Height = 212;
            Tag = "stretch";

            _gauge.Size = new Size(156, 156);
            _gauge.Thickness = 11;
            Controls.Add(_gauge);

            _cta.Variant = ButtonVariant.Primary;
            _cta.Text = "立即执行";
            _cta.IconKind = "bolt";
            _cta.Size = new Size(116, 116);
            _cta.Radius = 58;
            Controls.Add(_cta);
        }

        /// <summary>内嵌圆环（页面直接更新它的数值）。</summary>
        public RingGauge Gauge
        {
            get { return _gauge; }
        }

        /// <summary>圆形主操作按钮（页面挂 Click）。</summary>
        public AccentButton Action
        {
            get { return _cta; }
        }

        /// <summary>卡左上角小标签（图标 + 文案），用于标明这是"工具型"能力。</summary>
        public string TagText
        {
            get { return _tagText; }
            set { _tagText = value == null ? "" : value; Invalidate(); }
        }

        public string TagIcon
        {
            get { return _tagIcon; }
            set { _tagIcon = value == null ? "" : value; Invalidate(); }
        }

        public string Headline
        {
            get { return _headline; }
            set { _headline = value == null ? "" : value; Invalidate(); }
        }

        public string SubText
        {
            get { return _subText; }
            set { _subText = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>实时指标（键值成对，横排等分，中间 1px 竖线分隔）。</summary>
        public void SetStats(string[] keys, string[] values)
        {
            _statKeys.Clear();
            _statValues.Clear();
            if (keys != null) _statKeys.AddRange(keys);
            if (values != null) _statValues.AddRange(values);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int gh = Math.Min(Height - 36, 172);
            if (gh < 90) gh = 90;
            _gauge.Size = new Size(gh, gh);
            _gauge.Location = new Point(26, (Height - gh) / 2);

            int cs = Math.Min(Height - 60, 124);
            if (cs < 70) cs = 70;
            _cta.Size = new Size(cs, cs);
            _cta.Radius = cs / 2;
            _cta.Location = new Point(Width - cs - 34, (Height - cs) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);

            // 环境光晕：右侧偏上的低 alpha 强调色，制造"光源在卡内"的层次
            GraphicsPath clip = GdiCache.RoundRect(box, Theme.RadiusCard);
            GraphicsState st = g.Save();
            g.SetClip(clip);
            using (SolidBrush glow = new SolidBrush(Gfx.Alpha(_gauge.Tone, 22)))
            {
                g.FillEllipse(glow, Width - 300, -110, 420, 300);
            }
            g.Restore(st);

            int x = _gauge.Right + 26;
            int right = _cta.Left - 26;
            int textW = Math.Max(80, right - x);
            int y = 26;

            // 小标签：图标底 + 文案
            if (_tagText.Length > 0)
            {
                SizeF tw = g.MeasureString(_tagText, Theme.FontSmall);
                int chipW = (int)tw.Width + 34;
                Rectangle chip = new Rectangle(x, y, chipW, 24);
                Gfx.FillRound(g, chip, 12, Gfx.Alpha(_gauge.Tone, 34));
                IconPainter.Draw(g, _tagIcon, new Rectangle(x + 10, y + 6, 12, 12), _gauge.Tone);
                Gfx.DrawTextEllipsis(g, _tagText, Theme.FontSmall, _gauge.Tone,
                    new Rectangle(x + 26, y + 2, chipW - 30, 20));
                y += 34;
            }

            if (_headline.Length > 0)
            {
                g.DrawString(_headline, Theme.FontTitle, GdiCache.Brush(Theme.TextPrimary),
                    new Rectangle(x, y, textW, 30), GdiCache.Ellipsis);
                y += 36;
            }

            if (_subText.Length > 0)
            {
                Gfx.DrawTextEllipsis(g, _subText, Theme.FontSmall, Theme.TextSecondary,
                    new Rectangle(x, y, textW, 20));
            }

            // 实时指标：贴卡片底部横排，等分并保留 1px 分隔线
            int n = Math.Min(_statKeys.Count, _statValues.Count);
            if (n > 0)
            {
                int statH = 44;
                int statY = Height - statH - 24;
                int colW = Math.Max(60, textW / n);
                for (int i = 0; i < n; i++)
                {
                    int sx = x + i * colW;
                    if (i > 0) g.DrawLine(GdiCache.Pen(Theme.RowBorder, 1f), sx - 10, statY + 4, sx - 10, statY + statH - 6);

                    Gfx.DrawTextEllipsis(g, _statKeys[i], Theme.FontMicro, Theme.TextMuted,
                        new Rectangle(sx, statY, colW - 16, 16));
                    g.DrawString(_statValues[i], Theme.FontBodyBold, GdiCache.Brush(Theme.TextPrimary),
                        new Rectangle(sx, statY + 18, colW - 16, 22), GdiCache.Ellipsis);
                }
            }
        }
    }

    /// <summary>
    /// 排行条，对应设计 <c>mem-top-strip</c>：
    /// 单行高度内呈现「名次 + 图标块 + 名称 + 迷你条 + 数值」，
    /// 用于 TOP N 占用排行这类"需要排序感"的列表（比表格更省纵向空间）。
    /// </summary>
    public class RankStrip : RoundPanel
    {
        public sealed class Row
        {
            public int Rank;
            public string Icon = "process";
            public string Name = "";
            public string Value = "";
            /// <summary>迷你条百分比（0~100）。</summary>
            public double Percent;
            public Color Tone = Theme.Accent;
        }

        private readonly List<Row> _rows = new List<Row>();
        private string _title = "占用排行";
        private string _hint = "TOP 5";
        private Rectangle _refreshRect;
        private bool _refreshHover;

        /// <summary>点击右上角刷新按钮。</summary>
        public event EventHandler RefreshClick;

        public RankStrip()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;
            ShowBorder = true;
            Height = 224;
            Tag = "stretch";
            A11y.MakeFocusable(this, AccessibleRole.List);
        }

        public string TitleText
        {
            get { return _title; }
            set { _title = value == null ? "" : value; Invalidate(); }
        }

        public string TitleHint
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; Invalidate(); }
        }

        public void SetRows(List<Row> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            Invalidate();
        }

        public int RowCount
        {
            get { return _rows.Count; }
        }

        /// <summary>内容高度：标题条 + N 行 + 底部留白。</summary>
        public int PreferredHeight
        {
            get { return HeightFor(_rows.Count); }
        }

        /// <summary>
        /// 给定行数时的整体高度。页面应在挂载前用它定稿高度——
        /// 行布局要求"行高在挂载前定稿"，挂载后再改高会与其后区块错位。
        /// </summary>
        public static int HeightFor(int rows)
        {
            if (rows < 1) rows = 1;
            return 46 + rows * 32 + 10;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = _refreshRect.Contains(e.Location);
            if (h != _refreshHover)
            {
                _refreshHover = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate(_refreshRect);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_refreshHover)
            {
                _refreshHover = false;
                Cursor = Cursors.Default;
                Invalidate(_refreshRect);
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _refreshRect.Contains(e.Location))
            {
                EventHandler h = RefreshClick;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // ---- 标题条 ----
            IconPainter.Draw(g, "gauge", new Rectangle(18, 17, 14, 14), Theme.TextSecondary);
            Gfx.DrawTextEllipsis(g, _title, Theme.FontBodyBold, Theme.TextPrimary,
                new Rectangle(40, 14, Math.Max(60, Width - 190), 20));

            if (_hint.Length > 0)
            {
                SizeF hw = g.MeasureString(_hint, Theme.FontMicro);
                int chipW = (int)hw.Width + 16;
                Gfx.FillRound(g, new Rectangle(Width - chipW - 54, 16, chipW, 18), 9, Theme.CardBgAlt);
                Gfx.DrawTextCenter(g, _hint, Theme.FontMicro, Theme.TextSecondary,
                    new Rectangle(Width - chipW - 54, 16, chipW, 18));
            }

            _refreshRect = new Rectangle(Width - 36, 12, 24, 24);
            if (_refreshHover) Gfx.FillRound(g, _refreshRect, 6, Theme.CardBgAlt);
            IconPainter.Draw(g, "refresh", new Rectangle(_refreshRect.X + 6, _refreshRect.Y + 6, 12, 12),
                _refreshHover ? Theme.TextPrimary : Theme.TextMuted);

            if (_rows.Count == 0)
            {
                Gfx.DrawTextEllipsis(g, "暂无数据", Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(18, 54, Math.Max(40, Width - 36), 20));
                return;
            }

            // ---- 排行行 ----
            int rowH = 32;
            int y = 46;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];

                // 名次（等宽，弱化；首位用语义色强调）
                Gfx.DrawTextEllipsis(g, r.Rank.ToString(), Theme.FontMono,
                    r.Rank == 1 ? r.Tone : Theme.TextMuted,
                    new Rectangle(18, y + 6, 20, 20));

                // 图标块
                Rectangle box = new Rectangle(42, y + 4, 24, 24);
                Gfx.FillRound(g, box, Theme.RadiusChip, Gfx.Alpha(r.Tone, 34));
                IconPainter.Draw(g, r.Icon, new Rectangle(box.X + 6, box.Y + 6, 12, 12), r.Tone);

                // 名称 + 迷你条
                int valueW = 84;
                int nameX = box.Right + 10;
                int barW = Math.Max(60, Width - nameX - valueW - 24 - 16);
                Gfx.DrawTextEllipsis(g, r.Name, Theme.FontSmall, Theme.TextPrimary,
                    new Rectangle(nameX, y + 1, barW, 16));

                Rectangle track = new Rectangle(nameX, y + 19, barW, 5);
                Gfx.FillRound(g, track, 2, Theme.CardBgAlt);
                double pct = r.Percent;
                if (pct < 0) pct = 0;
                if (pct > 100) pct = 100;
                int fillW = (int)Math.Round(track.Width * pct / 100.0);
                if (fillW >= 3) Gfx.FillRound(g, new Rectangle(track.X, track.Y, fillW, track.Height), 2, r.Tone);

                // 数值（右对齐、等宽）
                Gfx.DrawTextEllipsis(g, r.Value, Theme.FontMono, Theme.TextSecondary,
                    new Rectangle(Width - valueW - 20, y + 6, valueW, 20));

                y += rowH;
            }
        }
    }

    /// <summary>
    /// 核心矩阵，对应设计 cpu-core 的「P/E 核棋盘」：
    /// 每个逻辑核一个方格，可点选/取消（表示是否允许该核参与调度），
    /// 对外暴露 ulong 亲和性掩码。用于进程的 CPU 亲和性设置。
    /// </summary>
    public class CoreMatrix : Control
    {
        private int _cores = 8;
        private ulong _mask = 0xFF;
        private int _hover = -1;
        private int _focus = -1;
        private const int CellW = 52;
        private const int CellH = 38;

        /// <summary>选择变化（页面据此更新"已选 N 核"与掩码文本）。</summary>
        public event EventHandler SelectionChanged;

        public CoreMatrix()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            A11y.MakeFocusable(this, AccessibleRole.List);
        }

        /// <summary>逻辑核数量（超出 64 的部分不参与掩码）。</summary>
        public int CoreCount
        {
            get { return _cores; }
        }

        /// <summary>当前选中的核掩码（bit i 对应第 i 个逻辑核）。</summary>
        public ulong Mask
        {
            get { return _mask; }
        }

        public int SelectedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _cores && i < 64; i++)
                {
                    if ((_mask & (1UL << i)) != 0) n++;
                }
                return n;
            }
        }

        /// <summary>重设核数并全选（切换目标进程/重新检测时调用）。</summary>
        public void SetCoreCount(int cores)
        {
            if (cores < 1) cores = 1;
            if (cores > 64) cores = 64;
            _cores = cores;
            _mask = cores >= 64 ? ulong.MaxValue : ((1UL << cores) - 1);
            LayoutChanged();
        }

        /// <summary>按外部得到的掩码回填（读取进程当前亲和性后调用）。</summary>
        public void SetMask(ulong mask)
        {
            _mask = mask;
            Invalidate();
        }

        public void SelectAll()
        {
            _mask = _cores >= 64 ? ulong.MaxValue : ((1UL << _cores) - 1);
            LayoutChanged();
        }

        private void LayoutChanged()
        {
            int rows = RowsOf(_cores);
            int want = rows * (CellH + 8) + 6;
            if (Height != want) Height = want;
            Invalidate();
            EventHandler h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private int ColumnsOf(int cores)
        {
            // 尚未参与布局时 Width 为 0：此时若按真实宽度算会得出 1 列（64 行）的荒谬高度，
            // 故先按 12 列预估，等布局完成（OnResize）再按真实宽度收敛。
            int perRow = Width < 80 ? 12 : Math.Max(1, (Width - 4) / (CellW + 8));
            if (perRow > cores) perRow = cores;
            if (perRow > 16) perRow = 16;
            return perRow;
        }

        private int RowsOf(int cores)
        {
            int cols = ColumnsOf(cores);
            return (cores + cols - 1) / cols;
        }

        private int HitTest(Point p, out int index)
        {
            index = -1;
            int cols = ColumnsOf(_cores);
            int cw = CellW + 8;
            int ch = CellH + 8;
            int col = (p.X - 2) / cw;
            int row = (p.Y - 3) / ch;
            if (p.X < 2 || p.Y < 3 || col < 0 || row < 0 || col >= cols) return -1;
            int idx = row * cols + col;
            if (idx < 0 || idx >= _cores) return -1;
            index = idx;
            return idx;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int rows = RowsOf(_cores);
            int want = rows * (CellH + 8) + 6;
            if (Height != want) Height = want;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx;
            HitTest(e.Location, out idx);
            if (idx != _hover)
            {
                _hover = idx;
                Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1)
            {
                _hover = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int idx;
                if (HitTest(e.Location, out idx) >= 0)
                {
                    _focus = idx; // 鼠标点选也同步键盘焦点
                    // 不允许把所有核都取消——那会得到一个无法调度的空掩码
                    ulong bit = 1UL << idx;
                    if ((_mask & bit) != 0 && SelectedCount <= 1) { Invalidate(); return; }
                    _mask = (_mask & bit) != 0 ? (_mask & ~bit) : (_mask | bit);
                    Invalidate();
                    EventHandler h = SelectionChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        /// <summary>键盘：方向键移动焦点，空格/回车切换当前焦点核（无鼠标也可操作）。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int cols = ColumnsOf(_cores);
            int rows = RowsOf(_cores);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
                e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (_focus < 0) _focus = 0;
                int col = _focus % cols;
                int row = _focus / cols;
                if (e.KeyCode == Keys.Left) col = col > 0 ? col - 1 : col;
                else if (e.KeyCode == Keys.Right) col = col < cols - 1 ? col + 1 : col;
                else if (e.KeyCode == Keys.Up) row = row > 0 ? row - 1 : row;
                else if (e.KeyCode == Keys.Down) row = row < rows - 1 ? row + 1 : row;
                int idx = row * cols + col;
                if (idx >= 0 && idx < _cores) { _focus = idx; Invalidate(); }
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                if (_focus >= 0)
                {
                    ulong bit = 1UL << _focus;
                    if ((_mask & bit) != 0 && SelectedCount <= 1) { e.Handled = true; return; }
                    _mask = (_mask & bit) != 0 ? (_mask & ~bit) : (_mask | bit);
                    Invalidate();
                    EventHandler h = SelectionChanged;
                    if (h != null) h(this, EventArgs.Empty);
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int cols = ColumnsOf(_cores);
            int cw = CellW + 8;
            int ch = CellH + 8;

            for (int i = 0; i < _cores; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rectangle cell = new Rectangle(2 + col * cw, 3 + row * ch, CellW, CellH);

                bool on = i < 64 && (_mask & (1UL << i)) != 0;
                bool hover = i == _hover;

                Color fill = on ? Gfx.Alpha(Theme.Accent, hover ? 90 : 60) : Theme.CardBgAlt;
                Gfx.FillRound(g, cell, Theme.RadiusChip, fill);
                Gfx.StrokeRound(g, cell, Theme.RadiusChip,
                    on ? Gfx.Alpha(Theme.Accent, 190) : Theme.BorderSoft, 1f);

                // 键盘焦点环：让 Tab 聚焦后用方向键 + 空格操作的用户能看清当前选中核
                if (i == _focus)
                {
                    Gfx.StrokeRound(g, cell, Theme.RadiusChip, Theme.TextPrimary, 2f);
                }

                Gfx.DrawTextCenter(g, on ? "#" + (i + 1) : "#" + (i + 1),
                    Theme.FontSmall, on ? Theme.TextPrimary : Theme.TextMuted, cell);
            }
        }
    }
}
