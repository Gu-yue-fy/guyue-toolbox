﻿/* ============================================================
 * 文件说明：滚动容器：自绘滚动条 + 内容高度自适应，承担所有页面的内容滚动。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.UI.Views;   // 滚轮路由回退到指针所在页面的滚动容器（ViewBase.Scroller）

namespace GuyueBox.UI
{
    /// <summary>
    /// 自绘滚动容器：用一条细长的深色滚动条替代系统原生的 Win32 滚动条，
    /// 与整体深色主题保持一致。滚轮由 MainForm 的 WheelRouter 统一路由。
    /// </summary>
    public class ScrollHost : BufferPanel
    {
        private const int BarWidth = 8;
        private const int BarInset = 3;
        private const int MinThumb = 36;

        private Control _content;
        private int _offset;
        private int _lastAppliedOffset; // 上一帧实际应用的偏移：仅重绘新露出的那条边
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

            // 平滑滚动收敛逻辑收口到 SmoothScroll；此处只声明「读当前偏移 / 应用新偏移」两个回调
            _smooth = new SmoothScroll(
                () => _offset,
                v =>
                {
                    _offset = ClampOffset((int)Math.Round(v));
                    ApplyOffset();
                    UpdateThumb();
                },
                Theme.Motion.ScrollLerp,
                3.0);
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
                Gfx.EnableComposited(cp);
                return cp;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // 页面首次显示时，内容控件可能还没参与过布局，需要强制重排一次
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
                // 换内容时重置滚动目标：旧目标可能远超新内容范围，
                // 留着会让滚轮在错误基准上累加、动画长期追不上目标
                _smooth.Stop();
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

        /// <summary>滚轮增量的零头累积：触控板/精密滚轮会给出 1~20 的小增量，按比例累计才跟手。</summary>
        private double _wheelAccum;

        public void ScrollByWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == 0) return;   // 系统设置里关掉了滚轮滚动，尊重它

            int step;
            if (delta % 120 == 0)
            {
                // 鼠标滚轮：整格（±120 的整数倍）→ 按系统「一次滚动几行」换算
                //（-1 = 一次一屏，N 行按 40px 一行近似；Windows 默认 3 行 → 120px）
                int notches = delta / 120;
                int perNotch = lines < 0 ? Math.Max(40, ClientSize.Height) : lines * 40;
                step = notches * perNotch;
                _wheelAccum = 0;      // 整格进来时清掉零头，避免叠加出多余一格
            }
            else
            {
                // 触控板 / 精密滚轮：给出的是 1~20 的小增量，按像素 1:1 累积才跟手
                //（若也按"满一格才动"，触控板要滑好几下才滚一次，感觉滚不动）
                _wheelAccum += delta;
                step = (int)_wheelAccum;
                if (step == 0) return;
                _wheelAccum -= step;
            }

            // 基准取「动画目标」而非当前位置：连续滚轮要累积，否则快速滚动时每格都从
            // 当前位置重算，会丢掉累积量、越滚越慢。
            // 走 SmoothScroll 指数插值（滚轮一直是瞬跳，与系统/浏览器的平滑过渡不一致）。
            int baseVal = _smooth.IsRunning ? (int)Math.Round(_smooth.Target) : _offset;
            SetOffset(ClampOffset(baseVal - step), true);
        }

        // ---------------- 平滑滚动：滚轮目标值指数插值，消除逐行跳动的生硬感 ----------------
        // 收敛逻辑收口到 SmoothScroll；此处只声明「读当前偏移 / 应用新偏移」两个回调。

        private readonly SmoothScroll _smooth;

        private int ClampOffset(int value)
        {
            int view = ClientSize.Height;
            int max = Math.Max(_contentHeight, view) - view;
            if (max < 0) max = 0;
            if (value < 0) value = 0;
            if (value > max) value = max;
            return value;
        }

        private void SetOffset(int value)
        {
            SetOffset(value, false);
        }

        /// <summary>animate=true 时滚向目标值（指数插值），false 时立即到位（拖动/程序化滚动）。</summary>
        private void SetOffset(int value, bool animate)
        {
            value = ClampOffset(value);
            if (animate)
            {
                // 已停在该目标且正滚向它：避免重复启动（原 _targetOffset 同值早退逻辑）
                if (value == _offset && _smooth.IsRunning && Math.Abs(_smooth.Target - value) < 0.5) return;
                _smooth.AnimateTo(value);
                return;
            }

            _smooth.Stop();
            if (value == _offset) return;

            _offset = value;
            ApplyOffset(); // 内部已按需失效，不要再叠一次整层 Invalidate
            UpdateThumb();
        }

        private void ClampOffset()
        {
            int max = Math.Max(0, _contentHeight - ClientSize.Height);
            if (_offset > max) _offset = max;
            if (_offset < 0) _offset = 0;
        }

        /// <summary>
        /// 应用滚动偏移并按需失效。
        /// 移动子控件不会让容器自动重绘露出区域，必须显式刷新，否则会留白/闪烁；
        /// 但整页刷新等于「每帧把所有行重画一遍」——长列表上正是掉帧根因，
        /// 所以只失效新露出的那条边（下滚露底部、上滚露顶部），
        /// 位移达到一屏（拖动滚动条/程序化跳转）时退回整层刷新以保证不漏刷。
        /// </summary>
        private void ApplyOffset()
        {
            if (_content == null) return;

            int delta = _offset - _lastAppliedOffset;
            _lastAppliedOffset = _offset;
            _content.Location = new Point(0, -_offset);

            int view = ClientSize.Height;
            if (delta == 0 || view <= 0 || Math.Abs(delta) >= view)
            {
                Invalidate();
                _content.Invalidate();
                return;
            }

            Rectangle band = delta > 0
                ? new Rectangle(0, view - delta - 1, ClientSize.Width, delta + 1)
                : new Rectangle(0, 0, ClientSize.Width, -delta + 1);
            Invalidate(band);
            // 内容控件位于 (0, -_offset)，把容器坐标的这条边换算到内容坐标
            _content.Invalidate(new Rectangle(band.X, band.Y + _offset, band.Width, band.Height));
        }

        private void UpdateThumb()
        {
            int view = ClientSize.Height;
            int total = Math.Max(_contentHeight, view);
            // 缩略几何收口到 ScrollGeom（与 DarkGrid 共用）：无溢出时输出 0
            ScrollGeom.Compute(total, view, BarInset, MinThumb, _offset, out _thumbSize, out _thumbTop);
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

        /// <summary>释放平滑滚动（切页销毁控件树后，残留的缓动回调若仍触发会访问已释放的 _content）。</summary>
        protected override void Dispose(bool disposing)
        {
            _smooth.Stop();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 用自身 BackColor 画背景（而不是硬编码页面色）：
            // 侧栏里的 ScrollHost 必须跟侧栏同色（ChromeBg），否则导航项下方会露出一段灰色
            g.FillRectangle(GdiCache.Brush(BackColor), ClientRectangle);

            base.OnPaint(e);

            // 上下渐隐遮罩：内容滚动出视口时自然消隐，比生硬裁切更有层次。
            // 画刷走缓存——滚动时这里每帧都会重绘，新建渐变对象会很浪费
            int fade = 26;
            if (_offset > 0)
            {
                g.FillRectangle(GdiCache.FadeBrush(BackColor, 0, fade, true),
                    0, 0, ClientSize.Width, fade);
            }
            if (_offset < Math.Max(0, _contentHeight - ClientSize.Height))
            {
                int top = ClientSize.Height - fade;
                g.FillRectangle(GdiCache.FadeBrush(BackColor, top, fade, false),
                    0, top, ClientSize.Width, fade);
            }

            if (_thumbSize <= 0) return;

            Gfx.EnableSmoothing(g);
            int x = ClientSize.Width - BarWidth - BarInset;

            // 默认 18% 白、悬停 32% 白；拖拽/悬停仍以主题色强调
            Color thumbColor = _dragging
                ? Theme.Accent
                : (_hoverBar ? Theme.ScrollThumbHover : Theme.ScrollThumb);

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

        /// <summary>该控件自己处理滚轮（系统标准行为），页面滚动不应接管。</summary>
        private static bool OwnsWheel(Control c)
        {
            if (c is TextBoxBase) return true;                  // TextBox / RichTextBox
            if (c is ComboBox) return true;
            if (c is ListBox) return true;
            if (c is ListView) return true;
            if (c is TreeView) return true;
            if (c is NumericUpDown) return true;
            if (c is TrackBar) return true;
            // DarkGrid 继承自 DataGridView，但它有自绘滚动条，必须由下面的路由处理
            if (c is DataGridView && !(c is DarkGrid)) return true;
            return false;
        }

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

            // 指针落在"自带滚轮语义"的控件上时不拦截：文本框滚自己的文本、下拉框/列表滚自己的选项，
            // 这些是系统既有行为。被全局路由抢走会让人感觉"滚轮不正常"（如在多行文本框里滚不动文本）。
            for (Control c = under; c != null; c = c.Parent)
            {
                if (c is ScrollHost) break;      // 到页面滚动容器为止，只检查它内部
                if (OwnsWheel(c)) return false;
            }

            DarkGrid grid = null;
            ScrollHost host = null;
            for (Control c = under; c != null; c = c.Parent)
            {
                if (grid == null && c is DarkGrid && ((DarkGrid)c).UseOwnScrollbar) grid = (DarkGrid)c;
                if (host == null && c is ScrollHost) host = (ScrollHost)c;
            }

            int delta = unchecked((short)((long)m.WParam >> 16));

            // 指针在自带滚动条的表格上时，优先滚动表格；
            // 表格无溢出或已滚到端时穿透给页面，保证整页连续滚动（否则滚轮在表格上会"失灵"）
            if (grid != null)
            {
                if (grid.ScrollByWheel(delta)) return true;
                if (host != null)
                {
                    host.ScrollByWheel(delta);
                    return true;
                }
                return false;
            }

            if (host == null)
            {
                // 指针停在页头工具条 / 通知条等"滚动容器之外"的位置：回退到所在页面的滚动容器。
                // 这些位置系统默认没人处理滚轮，用户会觉得"滚轮在这儿失灵"。
                Control page = under;
                while (page != null && !(page is ViewBase)) page = page.Parent;
                if (page is ViewBase) host = ((ViewBase)page).Scroller;
            }

            if (host == null) return false;

            host.ScrollByWheel(delta);
            return true;
        }
    }
}
