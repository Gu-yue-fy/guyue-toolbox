using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>自绘复选框单元格，避免系统主题在深色背景下突兀。</summary>
    public class DarkCheckCell : DataGridViewCheckBoxCell
    {
        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds,
            int rowIndex, DataGridViewElementStates elementState, object value, object formattedValue,
            string errorText, DataGridViewCellStyle cellStyle,
            DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            base.Paint(graphics, clipBounds, cellBounds, rowIndex, elementState, value, formattedValue,
                errorText, cellStyle, advancedBorderStyle,
                DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);

            bool isChecked = false;
            try { isChecked = Convert.ToBoolean(value); }
            catch { isChecked = false; }

            int size = 16;
            int x = cellBounds.X + (cellBounds.Width - size) / 2;
            int y = cellBounds.Y + (cellBounds.Height - size) / 2;
            Rectangle box = new Rectangle(x, y, size, size);

            Gfx.EnableSmoothing(graphics);

            Color fill = isChecked ? Theme.Accent : Theme.CardBgAlt;
            Gfx.FillRound(graphics, box, 5, fill);
            Gfx.StrokeRound(graphics, box, 5, isChecked ? Theme.Accent : Theme.BorderStrong, 1.2f);

            if (isChecked)
            {
                using (Pen pen = new Pen(Color.White, 1.9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLines(pen, new Point[]
                    {
                        new Point(box.X + 4, box.Y + 8),
                        new Point(box.X + 7, box.Y + 11),
                        new Point(box.X + 12, box.Y + 5)
                    });
                }
            }
        }
    }

    public class DarkCheckColumn : DataGridViewCheckBoxColumn
    {
        public DarkCheckColumn()
        {
            CellTemplate = new DarkCheckCell();
            Width = 48;
            SortMode = DataGridViewColumnSortMode.NotSortable;
        }
    }

    /// <summary>应用深色主题的 DataGridView。</summary>
    public class DarkGrid : DataGridView
    {
        public DarkGrid()
        {
            // DataGridView 默认关闭双缓冲，反射打开以获得流畅滚动
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.SetProperty,
                null, this, new object[] { true });

            EnableHeadersVisualStyles = false;
            BorderStyle = BorderStyle.None;
            BackgroundColor = Theme.CardBg;
            GridColor = Theme.BorderSoft;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            RowHeadersVisible = false;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            ReadOnly = true;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = false;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            Font = Theme.FontBody;
            RowTemplate.Height = 32;
            ColumnHeadersHeight = 36;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ShowCellToolTips = true;
            ShowEditingIcon = false;
            // 使用系统原生滚动条与滚轮行为
            ScrollBars = ScrollBars.Both;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            ColumnHeadersDefaultCellStyle.BackColor = Theme.GridHeader;
            ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
            ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GridHeader;
            ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.TextMuted;
            ColumnHeadersDefaultCellStyle.Font = Theme.FontSmall;
            ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            DefaultCellStyle.BackColor = Theme.GridRow;
            DefaultCellStyle.ForeColor = Theme.TextPrimary;
            DefaultCellStyle.SelectionBackColor = Theme.GridSelection;
            DefaultCellStyle.SelectionForeColor = Color.White;
            DefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            AlternatingRowsDefaultCellStyle.BackColor = Theme.GridRowAlt;
            AlternatingRowsDefaultCellStyle.ForeColor = Theme.TextPrimary;
            AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.GridSelection;
            AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;

            AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
        }

        // ==============================================================
        // 内置自绘滚动条
        // 关闭系统滚动条后由本控件自己绘制细滚动条并处理滚轮，
        // 这样表格高度可以固定为可视区高度，不再把页面撑长。
        // ==============================================================

        private const int BarW = 6;
        private const int BarInset = 3;
        private const int MinThumb = 32;

        private bool _useOwnScrollbar;
        private int _thumbTop;
        private int _thumbSize;
        private bool _dragBar;
        private bool _hoverBar;
        private int _dragStartY;
        private int _dragStartRow;

        /// <summary>true 时表格高度固定，由内置滚动条负责浏览所有行。</summary>
        public bool UseOwnScrollbar
        {
            get { return _useOwnScrollbar; }
            set { _useOwnScrollbar = value; Invalidate(); }
        }

        private int TotalRows
        {
            get { return VirtualMode ? RowCount : Rows.Count; }
        }

        private int VisibleRows
        {
            get
            {
                try { return DisplayedRowCount(false); }
                catch { return 0; }
            }
        }

        /// <summary>把滚动位置限制在合法范围内。</summary>
        private void ClampFirstRow()
        {
            int total = TotalRows;
            int view = VisibleRows;
            if (total <= 0 || view <= 0) return;

            int maxFirst = total - view;
            if (maxFirst < 0) maxFirst = 0;

            int first = 0;
            try { first = FirstDisplayedScrollingRowIndex; }
            catch { return; }

            if (first < 0) first = 0;
            if (first > maxFirst) first = maxFirst;

            try
            {
                if (FirstDisplayedScrollingRowIndex != first) FirstDisplayedScrollingRowIndex = first;
            }
            catch
            {
            }
        }

        public void ScrollByWheel(int delta)
        {
            if (!_useOwnScrollbar) return;

            int step = Math.Abs(delta) / 40;
            if (step < 1) step = 1;
            if (step > 6) step = 6;

            int first;
            try { first = FirstDisplayedScrollingRowIndex; }
            catch { return; }

            int maxFirst = TotalRows - VisibleRows;
            if (maxFirst < 0) maxFirst = 0;

            int target = delta > 0 ? first - step : first + step;
            if (target < 0) target = 0;
            if (target > maxFirst) target = maxFirst;

            try
            {
                if (FirstDisplayedScrollingRowIndex != target) FirstDisplayedScrollingRowIndex = target;
            }
            catch
            {
            }
            Invalidate();
        }

        private void UpdateThumb()
        {
            _thumbSize = 0;
            _thumbTop = 0;
            if (!_useOwnScrollbar) return;

            int total = TotalRows;
            int view = VisibleRows;
            int trackH = ClientSize.Height - BarInset * 2;
            if (total <= view || view <= 0 || trackH <= 0) return;

            _thumbSize = Math.Max(MinThumb, (int)((double)trackH * view / total));
            if (_thumbSize > trackH) _thumbSize = trackH;

            int maxFirst = total - view;
            int first = 0;
            try { first = FirstDisplayedScrollingRowIndex; }
            catch { }

            int travel = trackH - _thumbSize;
            _thumbTop = BarInset + (maxFirst <= 0 ? 0 : travel * first / maxFirst);
        }

        private bool HitBar(int x)
        {
            return _useOwnScrollbar && x >= ClientSize.Width - BarW - BarInset;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_useOwnScrollbar) return;

            UpdateThumb();
            if (_thumbSize <= 0) return;

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int x = ClientSize.Width - BarW - BarInset;
            Color color = _dragBar
                ? Theme.Accent
                : (_hoverBar ? Theme.BorderStrong : Gfx.Alpha(Theme.BorderStrong, 150));
            Gfx.FillRound(g, new Rectangle(x, _thumbTop, BarW, _thumbSize), BarW / 2, color);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_useOwnScrollbar)
            {
                ScrollByWheel(e.Delta);
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_useOwnScrollbar)
            {
                bool hot = HitBar(e.X) && VisibleRows > 0 && TotalRows > VisibleRows;
                if (hot != _hoverBar) { _hoverBar = hot; Invalidate(); }

                if (_dragBar)
                {
                    int total = TotalRows;
                    int view = VisibleRows;
                    int trackH = ClientSize.Height - BarInset * 2;
                    int travel = trackH - _thumbSize;
                    int maxFirst = total - view;
                    if (travel > 0 && maxFirst > 0)
                    {
                        int delta = e.Y - _dragStartY;
                        int target = _dragStartRow + (int)((double)delta * maxFirst / travel);
                        if (target < 0) target = 0;
                        if (target > maxFirst) target = maxFirst;
                        try { FirstDisplayedScrollingRowIndex = target; }
                        catch { }
                        Invalidate();
                    }
                    return;
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverBar)
            {
                _hoverBar = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_useOwnScrollbar && e.Button == MouseButtons.Left && HitBar(e.X) && _thumbSize > 0)
            {
                _dragBar = true;
                _dragStartY = e.Y;
                try { _dragStartRow = FirstDisplayedScrollingRowIndex; }
                catch { _dragStartRow = 0; }
                Capture = true;
                Invalidate();
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragBar)
            {
                _dragBar = false;
                Capture = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        /// <summary>完整显示所有行所需的自然高度。</summary>
        public int NaturalHeight
        {
            get
            {
                int h = 2;
                if (ColumnHeadersVisible) h += ColumnHeadersHeight;

                if (VirtualMode)
                {
                    // 虚拟模式下行对象是按需生成的，直接用行数 × 行高，避免遍历开销
                    return h + RowCount * RowTemplate.Height;
                }

                for (int i = 0; i < Rows.Count; i++) h += Rows[i].Height;
                return h;
            }
        }

        /// <summary>创建一列文本。</summary>
        public DataGridViewTextBoxColumn AddTextColumn(string header, int width, bool rightAlign)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.Width = width;
            col.ReadOnly = true;
            col.SortMode = DataGridViewColumnSortMode.Programmatic;
            if (rightAlign)
            {
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.DefaultCellStyle.Padding = new Padding(0, 0, 8, 0);
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            Columns.Add(col);
            return col;
        }

        /// <summary>创建一列自适应宽度的文本。</summary>
        public DataGridViewTextBoxColumn AddFillColumn(string header, int minimumWidth)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            col.MinimumWidth = minimumWidth;
            col.ReadOnly = true;
            col.SortMode = DataGridViewColumnSortMode.Programmatic;
            Columns.Add(col);
            return col;
        }

        /// <summary>深色主题下的行悬停高亮。</summary>
        protected override void OnCellMouseEnter(DataGridViewCellEventArgs e)
        {
            base.OnCellMouseEnter(e);
            if (e.RowIndex >= 0 && e.RowIndex < Rows.Count && !Rows[e.RowIndex].Selected)
            {
                Rows[e.RowIndex].DefaultCellStyle.BackColor = Theme.GridHover;
            }
        }

        protected override void OnCellMouseLeave(DataGridViewCellEventArgs e)
        {
            base.OnCellMouseLeave(e);
            if (e.RowIndex >= 0 && e.RowIndex < Rows.Count && !Rows[e.RowIndex].Selected)
            {
                Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.Empty;
            }
        }
    }
}
