using System;
using System.Collections.Generic;
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
        /// <summary>
        /// 点行任意位置（名称/说明等列）都切换第 0 列的勾选状态。
        /// 勾选型列表页开启——不用再精确点中 16px 的小复选框。
        /// </summary>
        public bool CheckOnRowClick { get; set; }

        /// <summary>点击列头按该列排序（非绑定表格的手动排序，行 Tag 与勾选值随行保留）。</summary>
        public bool ColumnClickSort { get; set; }

        private int _sortCol = -1;
        private bool _sortAsc = true;

        /// <summary>重新应用当前列排序（页面筛选后重建了行时调用，保持用户点选的排序）。</summary>
        public void ReapplySort()
        {
            if (_sortCol >= 0) SortRowsByColumn(_sortCol, _sortAsc);
        }

        /// <summary>按指定列对当前行排序（字符串比较，纯数字列按数值）。</summary>
        public void SortRowsByColumn(int col, bool ascending)
        {
            if (col < 0 || col >= Columns.Count || Rows.Count == 0) return;
            int n = Columns.Count;
            List<object[]> data = new List<object[]>();
            List<object> tags = new List<object>();
            foreach (DataGridViewRow r in Rows)
            {
                object[] vals = new object[n];
                for (int i = 0; i < n; i++) vals[i] = r.Cells[i].Value;
                data.Add(vals);
                tags.Add(r.Tag);
            }
            int dir = ascending ? 1 : -1;
            data.Sort(delegate(object[] a, object[] b)
            {
                object va = a[col], vb = b[col];
                if (IsNumeric(va) && IsNumeric(vb))
                {
                    double da = Convert.ToDouble(va);
                    double db = Convert.ToDouble(vb);
                    return da.CompareTo(db) * dir;
                }
                string sa = Convert.ToString(va);
                string sb = Convert.ToString(vb);
                return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase) * dir;
            });
            Rows.Clear();
            for (int i = 0; i < data.Count; i++)
            {
                int idx = Rows.Add(data[i]);
                Rows[idx].Tag = tags[i];
            }
        }

        private static bool IsNumeric(object o)
        {
            return o is sbyte || o is byte || o is short || o is ushort ||
                   o is int || o is uint || o is long || o is ulong ||
                   o is float || o is double || o is decimal;
        }

        public DarkGrid()
        {
            // DataGridView 默认关闭双缓冲，反射打开以获得流畅滚动
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.SetProperty,
                null, this, new object[] { true });

            EnableHeadersVisualStyles = false;
            BorderStyle = BorderStyle.None;
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
            RowTemplate.Height = 32;
            ColumnHeadersHeight = 36;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ShowCellToolTips = true;
            ShowEditingIcon = false;
            // 关闭系统原生滚动条：由内置自绘细滚动条负责浏览（见 UseOwnScrollbar）。
            // 否则原生条会与自绘条在右侧重叠，且会抢占列宽。
            ScrollBars = ScrollBars.None;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            // 列头点击排序（ColumnClickSort 开启时在处理器内生效）
            ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (!ColumnClickSort) return;
                int col = e.ColumnIndex;
                if (col < 0) return;
                if (col == _sortCol) _sortAsc = !_sortAsc;
                else { _sortCol = col; _sortAsc = true; }
                SortRowsByColumn(col, _sortAsc);
            };

            AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
            AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;

            // 整行悬停高亮：DataGridView 没有内建"整行 hover"，
            // 用 CellFormatting 在绘制时给悬停行着色——比逐行改样式省，也不破坏斑马纹。
            // 已选中行保持选中色，不被悬停色覆盖。
            CellMouseEnter += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex == _hoverRow) return;
                int old = _hoverRow;
                _hoverRow = e.RowIndex;
                if (old >= 0) InvalidateRow(old);
                InvalidateRow(e.RowIndex);
            };
            CellMouseLeave += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                int old = _hoverRow;
                _hoverRow = -1;
                if (old >= 0) InvalidateRow(old);
            };
            CellFormatting += delegate (object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex != _hoverRow || e.CellStyle == null) return;
                if (Rows[e.RowIndex].Selected) return;
                e.CellStyle.BackColor = Theme.GridHover;
            };

            ApplyTheme();
        }

        /// <summary>当前悬停行索引（-1 = 无）。CellFormatting 据此着色。</summary>
        private int _hoverRow = -1;

        /// <summary>
        /// 把当前配色方案套用到表格样式。构造时调用一次；切换深/浅配色后
        /// 由 ThemeSkin 再次调用——DataGridView 的颜色是写进样式对象的，
        /// 不像自绘控件那样每帧读 Theme，必须显式重刷。
        /// </summary>
        public void ApplyTheme()
        {
            try
            {
                BackgroundColor = Theme.CardBg;
                GridColor = Theme.RowBorder; // 行分隔用 Row.Border（设计 #334155），比 BorderSoft 更清晰
                Font = Theme.FontBody;

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

                Invalidate();
            }
            catch
            {
            }
        }

        /// <summary>
        /// 重映射「逐行逐格显式设置过」的前景色 / 背景色。
        /// 各页面填充数据时常写 cell.Style.ForeColor = Theme.TextPrimary 之类，这些值
        /// 优先于 DefaultCellStyle，ApplyTheme 覆盖不到——切到浅色方案后就成了白底白字。
        /// 故由 ThemeSkin 在切方案时显式再做一次 旧色→新色 映射。
        /// </summary>
        internal void RemapRowStyles(Color[] backFrom, Color[] backTo, Color[] foreFrom, Color[] foreTo)
        {
            try
            {
                for (int r = 0; r < Rows.Count; r++)
                {
                    DataGridViewRow row = Rows[r];
                    if (row == null) continue;

                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        DataGridViewCell cell = row.Cells[c];
                        if (cell == null || !cell.HasStyle) continue; // 未显式设样式者跟随 DefaultCellStyle

                        DataGridViewCellStyle st = cell.Style;
                        st.ForeColor = MapSchemeColor(st.ForeColor, foreFrom, foreTo);
                        st.BackColor = MapSchemeColor(st.BackColor, backFrom, backTo);
                    }
                }

                Invalidate();
            }
            catch
            {
                // 表格可能正在重建行，忽略——下一次切主题会再试
            }
        }

        private static Color MapSchemeColor(Color c, Color[] from, Color[] to)
        {
            if (c.IsEmpty || from == null || to == null) return c; // IsEmpty = 继承，不处理
            int cur = c.ToArgb();
            int n = Math.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
            {
                if (cur == from[i].ToArgb()) return to[i];
            }
            return c;
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

        private System.Windows.Forms.Timer _smoothTimer;
        private int _scrollTarget;

        /// <summary>true 时表格高度固定，由内置滚动条负责浏览所有行。</summary>
        public bool UseOwnScrollbar
        {
            get { return _useOwnScrollbar; }
            set
            {
                _useOwnScrollbar = value;
                ScrollBars = value ? ScrollBars.None : ScrollBars.Both;
                Invalidate();
            }
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

        /// <summary>
        /// 滚轮滚动表格，采用缓动平滑（与页面 ScrollHost 一致的手感）。
        /// 返回是否真的能滚动：表格无溢出或已滚到端时返回 false，
        /// 调用方（WheelRouter）可把这次滚轮让给页面继续滚——整页才能连续流畅滚动。
        /// </summary>
        public bool ScrollByWheel(int delta)
        {
            if (!_useOwnScrollbar) return false;

            int first;
            try { first = FirstDisplayedScrollingRowIndex; }
            catch { return false; }

            int maxFirst = Math.Max(0, TotalRows - VisibleRows);
            int step = Math.Abs(delta) / 40;
            if (step < 1) step = 1;
            if (step > 6) step = 6;

            int target = delta > 0 ? first - step : first + step;
            target = Math.Max(0, Math.Min(maxFirst, target));

            bool moved = target != first;
            if (moved)
            {
                _scrollTarget = target;
                EnsureSmoothTimer();
            }
            return moved;
        }

        /// <summary>首次滚动时创建并启动缓动定时器（帧率 12ms）。</summary>
        private void EnsureSmoothTimer()
        {
            if (_smoothTimer == null)
            {
                _smoothTimer = new System.Windows.Forms.Timer();
                _smoothTimer.Interval = 12;
                _smoothTimer.Tick += delegate { SmoothStep(); };
            }
            if (!_smoothTimer.Enabled) _smoothTimer.Start();
        }

        /// <summary>每帧把首行缓动逼近目标（指数插值），消除逐行跳动的生硬感。</summary>
        private void SmoothStep()
        {
            int actual;
            try { actual = FirstDisplayedScrollingRowIndex; }
            catch { StopSmoothTimer(); return; }

            double diff = _scrollTarget - actual;
            if (Math.Abs(diff) < 0.06)
            {
                int snap = (int)Math.Round((double)_scrollTarget);
                if (actual != snap) { try { FirstDisplayedScrollingRowIndex = snap; } catch { } }
                StopSmoothTimer();
                return;
            }
            int next = (int)Math.Round(actual + diff * 0.35);
            if (next != actual) { try { FirstDisplayedScrollingRowIndex = next; } catch { } }
        }

        private void StopSmoothTimer()
        {
            if (_smoothTimer != null) _smoothTimer.Stop();
        }

        /// <summary>释放缓动定时器（切页销毁控件树后，Win32 定时器若仍触发会访问已释放的行集合）。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _smoothTimer != null)
            {
                _smoothTimer.Stop();
                _smoothTimer.Dispose();
                _smoothTimer = null;
            }
            base.Dispose(disposing);
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

        /// <summary>CheckOnRowClick 模式：点击行内任意列都切换首列勾选（复选列自身走原生行为）。</summary>
        protected override void OnCellClick(DataGridViewCellEventArgs e)
        {
            base.OnCellClick(e);
            if (!CheckOnRowClick || e.RowIndex < 0) return;
            if (e.ColumnIndex == 0) return;                       // 复选列点击已是原生切换
            if (Columns.Count == 0 || !(Columns[0] is DarkCheckColumn)) return;
            if (Rows[e.RowIndex].Cells[0].ReadOnly) return;

            bool cur = false;
            try { cur = Convert.ToBoolean(Rows[e.RowIndex].Cells[0].Value); }
            catch { }

            Rows[e.RowIndex].Cells[0].Value = !cur;               // 标脏
            if (IsCurrentCellDirty)
            {
                CommitEdit(DataGridViewDataErrorContexts.Commit); // 与点复选框同一条提交路径
            }
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
