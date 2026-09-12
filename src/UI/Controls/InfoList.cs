using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 以「标签 — 值」形式展示一组信息的面板。
    /// </summary>
    public class InfoList : RoundPanel
    {
        public const int RowHeight = 25;

        private readonly List<string[]> _rows = new List<string[]>();
        private int _labelWidth = 0;

        public string Caption = "";
        public string IconKind = "info";
        public Color CaptionColor = Theme.Accent;

        public InfoList()
        {
            BackColor = Theme.CardBg;
            Radius = 12;
        }

        public int RowCount
        {
            get { return _rows.Count; }
        }

        public void Clear()
        {
            _rows.Clear();
            _labelWidth = 0;
        }

        public void Add(string label, string value)
        {
            _rows.Add(new string[] { label == null ? "" : label, value == null ? "" : value });
        }

        public void Set(string label, string value)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i][0] == label)
                {
                    _rows[i][1] = value == null ? "" : value;
                    Invalidate();
                    return;
                }
            }
            Add(label, value);
        }

        public int HeaderHeight
        {
            get { return string.IsNullOrEmpty(Caption) ? 12 : Card.HeaderSize; }
        }

        public int PreferredHeight
        {
            get { return HeaderHeight + _rows.Count * RowHeight + 12; }
        }

        /// <summary>根据最长标签计算标签列宽度，让值列紧贴其后，避免出现大段空白。</summary>
        private int MeasureLabelWidth(Graphics g)
        {
            if (_labelWidth > 0) return _labelWidth;

            float max = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                float w = g.MeasureString(_rows[i][0], Theme.FontBody).Width;
                if (w > max) max = w;
            }

            int width = (int)Math.Ceiling(max) + 14;
            if (width < 76) width = 76;
            int limit = Math.Max(90, Width / 3);
            if (width > limit) width = limit;
            return width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            if (!string.IsNullOrEmpty(Caption))
            {
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
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Caption, Theme.FontBodyBold, b,
                        new Rectangle(x, 12, Math.Max(10, Width - x - 18), 24), sf);
                }
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    g.DrawLine(p, 16, Card.HeaderSize - 3, Width - 17, Card.HeaderSize - 3);
                }
            }

            int top = HeaderHeight;
            int labelWidth = MeasureLabelWidth(g);
            int valueX = 18 + labelWidth;
            int valueWidth = Math.Max(20, Width - valueX - 20);

            for (int i = 0; i < _rows.Count; i++)
            {
                int y = top + i * RowHeight;
                if (y > Height) break;

                Gfx.DrawTextEllipsis(g, _rows[i][0], Theme.FontBody, Theme.TextMuted,
                    new Rectangle(18, y, Math.Max(10, labelWidth - 6), RowHeight));

                Gfx.DrawTextEllipsis(g, _rows[i][1], Theme.FontBody, Theme.TextPrimary,
                    new Rectangle(valueX, y, valueWidth, RowHeight));

                if (i < _rows.Count - 1)
                {
                    using (Pen p = new Pen(Gfx.Alpha(Theme.BorderSoft, 150)))
                    {
                        g.DrawLine(p, 18, y + RowHeight - 1, Width - 19, y + RowHeight - 1);
                    }
                }
            }
        }
    }
}
