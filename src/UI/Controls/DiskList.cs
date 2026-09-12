using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SysToolbox.UI
{
    /// <summary>
    /// 磁盘使用情况面板：每块磁盘一行「卷标 · 使用条 · 可用空间」。
    /// </summary>
    public class DiskList : RoundPanel
    {
        public const int RowHeight = 38;

        public sealed class DiskItem
        {
            public string Name = "";
            public string Label = "";
            public long Total;
            public long Free;
            public bool Removable;
        }

        private readonly List<DiskItem> _items = new List<DiskItem>();

        public string Caption = "磁盘使用情况";
        public string IconKind = "disk";
        public Color CaptionColor = Theme.Success;

        public DiskList()
        {
            BackColor = Theme.CardBg;
            Radius = 12;
        }

        public void Clear()
        {
            _items.Clear();
        }

        public void Add(DiskItem item)
        {
            if (item != null) _items.Add(item);
        }

        public int PreferredHeight
        {
            get { return Card.HeaderSize + Math.Max(1, _items.Count) * RowHeight + 10; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

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

            int top = Card.HeaderSize;

            if (_items.Count == 0)
            {
                Gfx.DrawTextEllipsis(g, "未检测到本地磁盘", Theme.FontBody, Theme.TextMuted,
                    new Rectangle(18, top + 6, Width - 36, 24));
                return;
            }

            // 卷标列宽按最长项自适应
            float labelMax = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                string label = DisplayName(_items[i]);
                float w = g.MeasureString(label, Theme.FontBody).Width;
                if (w > labelMax) labelMax = w;
            }
            int labelWidth = (int)Math.Ceiling(labelMax) + 12;
            if (labelWidth < 90) labelWidth = 90;
            int limit = Math.Max(120, Width / 2);
            if (labelWidth > limit) labelWidth = limit;

            int barX = 18 + labelWidth;
            int rightWidth = 168;
            int barWidth = Width - barX - rightWidth - 20;
            if (barWidth < 60) barWidth = 60;

            for (int i = 0; i < _items.Count; i++)
            {
                DiskItem d = _items[i];
                int y = top + i * RowHeight;

                Gfx.DrawTextEllipsis(g, DisplayName(d), Theme.FontBody, Theme.TextPrimary,
                    new Rectangle(18, y, labelWidth - 8, RowHeight));

                double used = d.Total > 0 ? (d.Total - d.Free) * 100.0 / d.Total : 0;
                if (used < 0) used = 0;
                if (used > 100) used = 100;
                Color color = Gfx.LoadColor(used);

                Rectangle track = new Rectangle(barX, y + RowHeight / 2 - 3, barWidth, 6);
                Gfx.FillRound(g, track, 3, Theme.CardBgAlt);

                int w = (int)(track.Width * used / 100.0);
                if (w < 6) w = 6;
                Gfx.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), 3, color);

                string right = SysInfoText(d) + "   " + used.ToString("0") + "% 已用";
                Gfx.DrawTextEllipsis(g, right, Theme.FontSmall, Theme.TextSecondary,
                    new Rectangle(barX + barWidth + 12, y, rightWidth - 12, RowHeight));
            }
        }

        private static string DisplayName(DiskItem d)
        {
            string name = (d.Label == null ? "" : d.Label.Trim());
            if (name.Length == 0) name = d.Removable ? "可移动磁盘" : "本地磁盘";
            return name + " " + d.Name.TrimEnd('\\');
        }

        private static string SysInfoText(DiskItem d)
        {
            return Core.SysInfo.FormatSize(d.Free) + " 可用";
        }
    }
}
