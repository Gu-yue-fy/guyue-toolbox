using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GuyueBox.UI
{
    /// <summary>
    /// 缓存 SolidBrush / Pen / StringFormat / GraphicsPath。
    /// 界面里这些对象在每帧绘制中被反复创建，是卡顿与 GDI 句柄堆积的主要来源。
    /// 颜色与尺寸组合数量有限，缓存后命中率很高。
    /// </summary>
    internal static class GdiCache
    {
        private const int PathCacheLimit = 320;

        private static readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private static readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private static readonly Dictionary<string, GraphicsPath> _paths = new Dictionary<string, GraphicsPath>();

        private static StringFormat _ellipsis;
        private static StringFormat _ellipsisCenter;
        private static StringFormat _center;
        private static StringFormat _nearMiddle;

        private static readonly object _lock = new object();

        public static SolidBrush Brush(Color c)
        {
            int key = c.ToArgb();
            lock (_lock)
            {
                SolidBrush b;
                if (_brushes.TryGetValue(key, out b)) return b;
                b = new SolidBrush(c);
                _brushes[key] = b;
                return b;
            }
        }

        public static Pen Pen(Color c, float width)
        {
            long key = ((long)c.ToArgb() << 12) | (long)(width * 10f);
            lock (_lock)
            {
                Pen p;
                if (_pens.TryGetValue(key, out p)) return p;
                p = new Pen(c, width);
                _pens[key] = p;
                return p;
            }
        }

        /// <summary>带圆头线帽的画笔，用于图标绘制。</summary>
        public static Pen RoundPen(Color c, float width)
        {
            Pen p = Pen(c, width);
            if (p.StartCap != LineCap.Round)
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
            }
            return p;
        }

        /// <summary>缓存圆角矩形路径，避免每帧重新构造 GraphicsPath。</summary>
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            string key = r.Width + "x" + r.Height + ":" + radius;
            lock (_lock)
            {
                GraphicsPath p;
                if (_paths.TryGetValue(key, out p)) return p;

                if (_paths.Count > PathCacheLimit)
                {
                    foreach (GraphicsPath stale in _paths.Values) stale.Dispose();
                    _paths.Clear();
                }

                p = Gfx.RoundRect(new Rectangle(0, 0, Math.Max(1, r.Width), Math.Max(1, r.Height)), radius);
                _paths[key] = p;
                return p;
            }
        }

        /// <summary>单行 + 省略号，左对齐、垂直居中。</summary>
        public static StringFormat Ellipsis
        {
            get
            {
                if (_ellipsis == null)
                {
                    StringFormat f = new StringFormat();
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Alignment = StringAlignment.Near;
                    f.LineAlignment = StringAlignment.Center;
                    _ellipsis = f;
                }
                return _ellipsis;
            }
        }

        /// <summary>单行 + 省略号，居中。</summary>
        public static StringFormat EllipsisCenter
        {
            get
            {
                if (_ellipsisCenter == null)
                {
                    StringFormat f = new StringFormat();
                    f.Trimming = StringTrimming.EllipsisCharacter;
                    f.FormatFlags = StringFormatFlags.NoWrap;
                    f.Alignment = StringAlignment.Center;
                    f.LineAlignment = StringAlignment.Center;
                    _ellipsisCenter = f;
                }
                return _ellipsisCenter;
            }
        }

        /// <summary>水平居中 + 垂直居中，无省略号。</summary>
        public static StringFormat Center
        {
            get
            {
                if (_center == null)
                {
                    StringFormat f = new StringFormat();
                    f.Alignment = StringAlignment.Center;
                    f.LineAlignment = StringAlignment.Center;
                    _center = f;
                }
                return _center;
            }
        }

        /// <summary>左对齐 + 垂直居中，无省略号。</summary>
        public static StringFormat NearMiddle
        {
            get
            {
                if (_nearMiddle == null)
                {
                    StringFormat f = new StringFormat();
                    f.Alignment = StringAlignment.Near;
                    f.LineAlignment = StringAlignment.Center;
                    _nearMiddle = f;
                }
                return _nearMiddle;
            }
        }
    }
}
