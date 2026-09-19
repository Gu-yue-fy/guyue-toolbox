using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 区块标题，对齐设计 OverviewSectionTitle 的写法：
    /// 4px 强调竖条 + 标题（16px SemiBold）+ 说明（11px 弱化文字）。
    ///
    /// 这是设计全站统一的"分区语言"——用竖条与留白表达层级，
    /// 而不是给每个区块套一张卡片（那会形成"卡片套卡片"的噪音）。
    /// </summary>
    public class SectionTitle : Control
    {
        private string _title = "";
        private string _hint = "";
        private string _right = "";
        private Color _tone = Theme.Accent;

        public SectionTitle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Height = 44;
            Tag = "stretch";
        }

        public string TitleText
        {
            get { return _title; }
            set { _title = value == null ? "" : value; Invalidate(); }
        }

        public string HintText
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; Invalidate(); }
        }

        /// <summary>右侧附注（如「共 6 项」）。</summary>
        public string RightText
        {
            get { return _right; }
            set { _right = value == null ? "" : value; Invalidate(); }
        }

        public Color Tone
        {
            get { return _tone; }
            set { _tone = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);
            g.FillRectangle(GdiCache.Brush(Theme.WindowBg), ClientRectangle);

            // 4px 强调竖条（与标题首行等高）：自上而下由实到虚 + 背后一层柔光。
            // 平涂的小色块在深色底上几乎没有存在感，渐变+光晕才让它成为"层级标记"。
            using (SolidBrush halo = new SolidBrush(Gfx.Alpha(_tone, 28)))
            {
                g.FillRectangle(halo, -2, 2, 8, 24);
            }
            const int barSegs = 10;
            for (int i = 0; i < barSegs; i++)
            {
                int y0 = 4 + 20 * i / barSegs;
                int y1 = 4 + 20 * (i + 1) / barSegs;
                double t = 1.0 - (double)i / (barSegs - 1);
                g.FillRectangle(GdiCache.Brush(Gfx.Alpha(_tone, 70 + (int)(t * 185))), 0, y0, 4, y1 - y0);
            }

            Gfx.DrawTextEllipsis(g, _title, Theme.FontSubTitle, Theme.TextPrimary,
                new Rectangle(14, 2, Math.Max(40, Width - 200), 24));

            if (_hint.Length > 0)
            {
                Gfx.DrawTextEllipsis(g, _hint, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(14, 26, Math.Max(40, Width - 200), 16));
            }

            if (_right.Length > 0)
            {
                Gfx.DrawTextEllipsis(g, _right, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(Math.Max(40, Width - 190), 8, 180, 18));
            }
        }
    }

    /// <summary>
    /// 数据带，对齐设计 OverviewPage 的「数据带」：
    /// 一个圆角容器内**等分若干格**，格与格之间用 1px 竖线分隔（不是各自独立的卡片）。
    /// 每格自上而下：3px 竖条 + 标签 / 等宽大数值 / 进度条 + 状态 / 弱化补充。
    /// 这是概览页唯一的强主视觉承载面。
    /// </summary>
    public class MetricBand : RoundPanel
    {
        public sealed class Cell
        {
            public string Label = "";
            public string Value = "--";
            public string Status = "";
            public string Extra = "";
            /// <summary>进度百分比；小于 0 时不绘制进度条。</summary>
            public double Percent = -1;
            /// <summary>该格的语义色（竖条 / 进度条）。</summary>
            public Color Tone = Theme.Accent;
        }

        private readonly List<Cell> _cells = new List<Cell>();

        /// <summary>每格最小高度（设计 OverviewMetricCell MinHeight = 166）。</summary>
        public const int CellMinHeight = 166;

        public MetricBand()
        {
            BackColor = Theme.CardBgAlt;
            Radius = Theme.RadiusCard;
            Height = CellMinHeight;
            Tag = "stretch";
            ShowBorder = true;
        }

        public void SetCells(Cell[] cells)
        {
            _cells.Clear();
            if (cells != null) _cells.AddRange(cells);
            Invalidate();
        }

        public void UpdateCell(int index, string value, double percent, string status, string extra)
        {
            if (index < 0 || index >= _cells.Count) return;
            Cell c = _cells[index];
            c.Value = value == null ? "--" : value;
            c.Percent = percent;
            if (status != null) c.Status = status;
            if (extra != null) c.Extra = extra;
            Invalidate();
        }

        /// <summary>设置某格的语义色（如按占用率在绿/琥珀/红之间切换）。</summary>
        public void SetTone(int index, Color tone)
        {
            if (index < 0 || index >= _cells.Count) return;
            if (_cells[index].Tone == tone) return;
            _cells[index].Tone = tone;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_cells.Count == 0) return;
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int n = _cells.Count;
            // 内缩 1px 避免压住卡片自身的玻璃描边
            int left = 1;
            int usable = Math.Max(n, Width - 2);
            int colW = usable / n;

            for (int i = 0; i < n; i++)
            {
                Cell c = _cells[i];
                int x = left + i * colW;
                int w = (i == n - 1) ? (left + usable - x) : colW;

                // 格间 1px 竖线（设计 Overview.Hairline）
                if (i > 0)
                {
                    g.DrawLine(GdiCache.Pen(Theme.RowBorder, 1f), x, 16, x, Height - 17);
                }

                Rectangle box = new Rectangle(x + 20, 18, Math.Max(40, w - 40), Height - 36);

                // 3px 竖条 + 标签
                g.FillRectangle(GdiCache.Brush(c.Tone), box.X, box.Y + 3, 3, 16);
                Gfx.DrawTextEllipsis(g, c.Label, Theme.FontSmall, Theme.TextSecondary,
                    new Rectangle(box.X + 12, box.Y, Math.Max(30, box.Width - 12), 20));

                // 等宽大数值（设计用 Token.FontFamily.Mono / 30px）
                int valueTop = box.Y + 34;
                Gfx.DrawTextEllipsis(g, c.Value, Theme.FontMetric, Theme.TextPrimary,
                    new Rectangle(box.X, valueTop, box.Width, 36));

                // 进度条（8px、两端圆角）+ 状态
                int barTop = valueTop + 46;
                if (c.Percent >= 0 && barTop + 8 < Height - 20)
                {
                    Rectangle track = new Rectangle(box.X, barTop, box.Width, 8);
                    Gfx.FillRound(g, track, 4, Theme.CardBg);
                    double pct = c.Percent;
                    if (pct > 100) pct = 100;
                    if (pct < 0) pct = 0;
                    int fillW = (int)Math.Round(track.Width * pct / 100.0);
                    if (fillW >= 4)
                    {
                        Gfx.FillRound(g, new Rectangle(track.X, track.Y, fillW, track.Height), 4, c.Tone);
                    }
                }

                if (c.Status.Length > 0)
                {
                    Gfx.DrawTextEllipsis(g, c.Status, Theme.FontSmall, Theme.TextSecondary,
                        new Rectangle(box.X, barTop + 16, box.Width, 20));
                }

                if (c.Extra.Length > 0)
                {
                    Gfx.DrawTextEllipsis(g, c.Extra, Theme.FontMicro, Theme.TextMuted,
                        new Rectangle(box.X, barTop + 38, box.Width, 18));
                }
            }
        }
    }
}
