using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SysToolbox.UI
{
    /// <summary>
    /// 自绘滚动容器：用一条细长的深色滚动条替代系统原生的 Win32 滚动条，
    /// 与整体深色主题保持一致。滚轮由 MainForm 的 WheelRouter 统一路由。
    /// </summary>
    public class ScrollHost : BufferPanel
    {
        private const int BarWidth = 6;
        private const int BarInset = 3;
        private const int MinThumb = 36;

        private Control _content;
        private int _offset;
        private int _contentHeight;
        private int _thumbTop;
        private int _thumbSize;
        private bool _dragging;
        private bool _hoverBar;
        private int _dragStartY;
        private int _dragStartOffset;

        public ScrollHost()
        {
            BackColor = Theme.WindowBg;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        /// <summary>
        /// WS_EX_COMPOSITED：让容器及其所有子控件统一双缓冲绘制。
        /// 没有它，滚动时每个子窗口各自擦除再重绘，会出现白块和闪烁。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;
                return cp;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 页面首次显示时，内容控件此前可能从未参与布局，需要强制重排一次
            if (Visible) Relayout(true);
        }

        /// <summary>被滚动的内容控件。</summary>
        public Control Content
        {
            get { return _content; }
        }

        public int ContentHeight
        {
            get { return _contentHeight; }
        }

        public int ScrollOffset
        {
            get { return _offset; }
        }

        public int ViewportHeight
        {
            get { return ClientSize.Height; }
        }

        public void SetContent(Control content)
        {
            if (_content != null)
            {
                Controls.Remove(_content);
                _content.Dispose();
                _content = null;
            }

            _content = content;
            if (_content != null)
            {
                _content.Location = new Point(0, 0);
                Controls.Add(_content);
            }

            _offset = 0;
            Relayout();
        }

        /// <summary>重新计算内容高度与滚动条，内容尺寸变化后需要调用。</summary>
        public void Relayout()
        {
            Relayout(false);
        }

        /// <summary>forcePerformLayout 为 true 时无条件重排（页面首次显示时用）。</summary>
        public void Relayout(bool forcePerformLayout)
        {
            if (_content == null || _content.IsDisposed) return;

            int width = Math.Max(0, ClientSize.Width - BarWidth - BarInset);
            bool widthChanged = width > 0 && _content.Width != width;
            if (widthChanged) _content.Width = width;

            // 只在宽度变化或被强制时重排。子控件增删时 FlowLayoutPanel 自身已会排布。
            if (widthChanged || forcePerformLayout) _content.PerformLayout();

            int measured = MeasureContent();
            if (measured < 0) measured = 0;

            if (_contentHeight != measured)
            {
                _contentHeight = measured;
                if (_content.Height != measured) _content.Height = measured;
            }

            ClampOffset();
            ApplyOffset();
            UpdateThumb();
            Invalidate();
        }

        /// <summary>内容控件的自然高度。</summary>
        private int MeasureContent()
        {
            int bottom = _content.Padding.Top;
            for (int i = 0; i < _content.Controls.Count; i++)
            {
                Control c = _content.Controls[i];
                if (!c.Visible) continue;
                int b = c.Bottom + c.Margin.Bottom + _content.Padding.Bottom;
                if (b > bottom) bottom = b;
            }
            return bottom;
        }

        // ---------------------------------------------------------------
        // 滚动
        // ---------------------------------------------------------------

        public void ScrollByWheel(int delta)
        {
            int step = Math.Abs(delta) / 2;
            if (step < 40) step = 40;
            if (delta > 0) SetOffset(_offset - step);
            else SetOffset(_offset + step);
        }

        public void ScrollToTop()
        {
            SetOffset(0);
        }

        private void SetOffset(int value)
        {
            int max = Math.Max(0, _contentHeight - ClientSize.Height);
            if (value < 0) value = 0;
            if (value > max) value = max;
            if (value == _offset) return;

            _offset = value;
            ApplyOffset();
            UpdateThumb();
            Invalidate();
        }

        private void ClampOffset()
        {
            int max = Math.Max(0, _contentHeight - ClientSize.Height);
            if (_offset > max) _offset = max;
            if (_offset < 0) _offset = 0;
        }

        private void ApplyOffset()
        {
            if (_content == null) return;
            _content.Location = new Point(0, -_offset);

            // 移动子控件不会自动让容器重绘露出区域，必须显式刷新，
            // 否则滚动时会留下空白或闪烁。
            Invalidate();
            _content.Invalidate();
        }

        private void UpdateThumb()
        {
            int view = ClientSize.Height;
            int total = Math.Max(_contentHeight, view);

            if (total <= view || view <= 0)
            {
                _thumbSize = 0;
                _thumbTop = 0;
                return;
            }

            _thumbSize = Math.Max(MinThumb, (int)((double)view * view / total));
            if (_thumbSize > view - BarInset * 2) _thumbSize = view - BarInset * 2;

            int travel = view - BarInset * 2 - _thumbSize;
            int max = total - view;
            _thumbTop = BarInset + (max <= 0 ? 0 : (int)((double)_offset * travel / max));
        }

        private bool HitBar(int x)
        {
            return x >= ClientSize.Width - BarWidth - BarInset;
        }

        // ---------------------------------------------------------------
        // 布局 & 绘制
        // ---------------------------------------------------------------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = HitBar(e.X) && _thumbSize > 0;
            if (hot != _hoverBar)
            {
                _hoverBar = hot;
                Invalidate();
            }

            if (_dragging)
            {
                int view = ClientSize.Height;
                int total = Math.Max(_contentHeight, view);
                int travel = view - BarInset * 2 - _thumbSize;
                int max = total - view;
                if (travel > 0 && max > 0)
                {
                    int delta = e.Y - _dragStartY;
                    SetOffset(_dragStartOffset + (int)((double)delta * max / travel));
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverBar = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _thumbSize > 0 && HitBar(e.X))
            {
                _dragging = true;
                _dragStartY = e.Y;
                _dragStartOffset = _offset;
                Capture = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.WindowBg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            base.OnPaint(e);

            // 上下渐隐遮罩：内容滚动出视口时自然消隐，比生硬裁切更有层次
            int fade = 26;
            if (_offset > 0)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(0, 0, 1, fade), Theme.WindowBg, Color.Transparent, 90f))
                {
                    g.FillRectangle(b, 0, 0, ClientSize.Width, fade);
                }
            }
            if (_offset < Math.Max(0, _contentHeight - ClientSize.Height))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(0, ClientSize.Height - fade, 1, fade), Color.Transparent, Theme.WindowBg, 90f))
                {
                    g.FillRectangle(b, 0, ClientSize.Height - fade, ClientSize.Width, fade);
                }
            }

            if (_thumbSize <= 0) return;

            Gfx.EnableSmoothing(g);
            int x = ClientSize.Width - BarWidth - BarInset;

            Color thumbColor = _dragging
                ? Theme.Accent
                : (_hoverBar ? Theme.BorderStrong : Gfx.Alpha(Theme.BorderStrong, 150));

            Gfx.FillRound(g, new Rectangle(x, _thumbTop, BarWidth, _thumbSize),
                BarWidth / 2, thumbColor);
        }
    }

    /// <summary>
    /// 全应用范围的滚轮路由：把 WM_MOUSEWHEEL 转发给指针下方的 ScrollHost。
    /// 指针位于 DataGridView 上时不做拦截，保留列表自身的滚动行为。
    /// </summary>
    public sealed class WheelRouter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;

            int x = unchecked((short)(long)m.LParam);
            int y = unchecked((short)((long)m.LParam >> 16));

            Control under = null;
            try
            {
                IntPtr hwnd = WindowFromPoint(new Point(x, y));
                if (hwnd == IntPtr.Zero) return false;
                under = Control.FromHandle(hwnd);
            }
            catch
            {
                return false;
            }

            if (under == null) return false;

            DarkGrid grid = null;
            ScrollHost host = null;
            for (Control c = under; c != null; c = c.Parent)
            {
                if (grid == null && c is DarkGrid && ((DarkGrid)c).UseOwnScrollbar) grid = (DarkGrid)c;
                if (host == null && c is ScrollHost) host = (ScrollHost)c;
            }

            int delta = unchecked((short)((long)m.WParam >> 16));

            // 指针在自带滚动条的表格上时，优先滚动表格
            if (grid != null)
            {
                grid.ScrollByWheel(delta);
                return true;
            }

            if (host == null) return false;

            host.ScrollByWheel(delta);
            return true;
        }
    }
}
