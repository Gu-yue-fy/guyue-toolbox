﻿/* ============================================================
 * 文件说明：GDI 对象缓存（画刷/画笔/格式/路径/遮罩），防止句柄堆积
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

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
        // 画刷/画笔也要设上限：渐变仪表的每段颜色随进度连续变化，
        // 颜色组合理论上无限，没有上限就会稳定泄漏 GDI 句柄（长会话下会耗尽）
        private const int BrushCacheLimit = 512;
        private const int PenCacheLimit = 256;

        private static readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        private static readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private static readonly Dictionary<string, GraphicsPath> _paths = new Dictionary<string, GraphicsPath>();

        private static StringFormat _ellipsis;
        private static StringFormat _ellipsisCenter;
        private static StringFormat _center;
        private static StringFormat _nearMiddle;
        private static StringFormat _farMiddle;

        private static readonly object _lock = new object();

        public static SolidBrush Brush(Color c)
        {
            int key = c.ToArgb();
            lock (_lock)
            {
                SolidBrush b;
                if (_brushes.TryGetValue(key, out b)) return b;

                if (_brushes.Count > BrushCacheLimit)
                {
                    foreach (SolidBrush stale in _brushes.Values) stale.Dispose();
                    _brushes.Clear();
                }

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

                if (_pens.Count > PenCacheLimit)
                {
                    foreach (Pen stale in _pens.Values) stale.Dispose();
                    _pens.Clear();
                }

                p = new Pen(c, width);
                _pens[key] = p;
                return p;
            }
        }

        private static readonly Dictionary<long, Pen> _roundPens = new Dictionary<long, Pen>();

        /// <summary>
        /// 带圆头线帽的画笔（图标 / 圆环用）。
        ///
        /// 此前它直接改写 <see cref="Pen"/> 那个**共享实例**的线帽：同一 (颜色,线宽) 只要有一处
        /// 先走 RoundPen，之后所有拿到同一支笔的绘制都会变成圆头 ——
        /// 例如 Gfx 里明确假设"无圆帽"的分段圆弧（端点会鼓包、接缝变胖）、卡片描边。
        /// 现在圆头只写在自己的一份缓存上，两个缓存互不影响。
        /// </summary>
        public static Pen RoundPen(Color c, float width)
        {
            long key = ((long)c.ToArgb() << 12) | (long)(width * 10f);
            lock (_lock)
            {
                Pen p;
                if (_roundPens.TryGetValue(key, out p)) return p;

                if (_roundPens.Count > PenCacheLimit)
                {
                    foreach (Pen stale in _roundPens.Values) stale.Dispose();
                    _roundPens.Clear();
                }

                p = new Pen(c, width);
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                _roundPens[key] = p;
                return p;
            }
        }

        private static readonly Dictionary<int, Pen> _highlightPens = new Dictionary<int, Pen>();

        /// <summary>
        /// 卡片顶部高光笔：竖向「上缘通亮 → 向下渐隐」的渐变描边。
        /// 渐变尺寸随卡片高度变化，故按高度缓存——否则每张卡片每次绘制都要新建
        /// LinearGradientBrush + Pen，一屏十几张卡片就是几十个 GDI 对象。
        /// </summary>
        public static Pen HighlightPen(int height)
        {
            if (height < 1) height = 1;
            lock (_lock)
            {
                Pen p;
                if (_highlightPens.TryGetValue(height, out p)) return p;

                if (_highlightPens.Count > 64)
                {
                    foreach (Pen stale in _highlightPens.Values) stale.Dispose();
                    _highlightPens.Clear();
                }

                using (LinearGradientBrush grad = new LinearGradientBrush(
                    new Rectangle(0, 0, 1, height), Theme.CardHighlight, Color.Transparent, 90f))
                {
                    // Pen 内部会克隆画刷，因此渐变画刷可以随即释放
                    p = new Pen(grad, 1f);
                }
                _highlightPens[height] = p;
                return p;
            }
        }

        private static readonly Dictionary<long, LinearGradientBrush> _fadeBrushes =
            new Dictionary<long, LinearGradientBrush>();

        /// <summary>
        /// 竖向渐隐遮罩画刷（实色 → 透明）。
        /// 注意 GDI+ 的渐变矩形是「世界坐标」而非画刷自身坐标，故 y 必须参与缓存键，
        /// 否则底部遮罩会被画到错误的纵向位置。
        /// </summary>
        public static LinearGradientBrush FadeBrush(Color solid, int y, int height, bool fromTop)
        {
            if (height < 1) height = 1;
            long key = ((long)solid.ToArgb() << 33) ^ ((long)y << 12) ^ (long)height ^ (fromTop ? 1L : 0L);
            lock (_lock)
            {
                LinearGradientBrush b;
                if (_fadeBrushes.TryGetValue(key, out b)) return b;

                if (_fadeBrushes.Count > 32)
                {
                    foreach (LinearGradientBrush stale in _fadeBrushes.Values) stale.Dispose();
                    _fadeBrushes.Clear();
                }

                Rectangle grad = new Rectangle(0, y, 1, height);
                b = fromTop
                    ? new LinearGradientBrush(grad, solid, Color.Transparent, 90f)
                    : new LinearGradientBrush(grad, Color.Transparent, solid, 90f);
                _fadeBrushes[key] = b;
                return b;
            }
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
                    // NoWrap（单行省略号）+ NoClip（防止 g/y/标点等下行被 GDI+ 默认裁掉 1~2px）
                    f.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
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
                    f.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
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

        /// <summary>右对齐 + 垂直居中，无省略号。</summary>
        public static StringFormat FarMiddle
        {
            get
            {
                if (_farMiddle == null)
                {
                    StringFormat f = new StringFormat();
                    f.Alignment = StringAlignment.Far;
                    f.LineAlignment = StringAlignment.Center;
                    _farMiddle = f;
                }
                return _farMiddle;
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
