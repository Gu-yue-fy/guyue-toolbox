/* ============================================================
 * 文件说明：GDI 绘制辅助（圆角路径、卡片/圆环仪表/文本绘制、颜色混合与透明度）
 * 项目：古月工具箱（GuyueBox）
 * 设计分工：
 *   - 本文件只负责「绘制动作」，不持有任何 GDI 句柄；
 *   - GDI 对象（画刷/画笔/路径/字符串格式/渐变遮罩）的缓存由 GdiCache 负责；
 *   - 二者同属 GuyueBox.UI，职责分离：Gfx 调 GdiCache 取缓存对象，再画到画布。
 *   此前 Gfx 曾内联在 Theme.cs（主题令牌文件）里，模块边界不清；现独立成文件。
 * ============================================================ */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>绘制辅助。</summary>
    public static class Gfx
    {
        /// <summary>
        /// 开启 WS_EX_COMPOSITED：让控件及其所有子控件统一双缓冲绘制，
        /// 消除滚动 / 切页时子窗口各自擦除重绘导致的白块与闪烁。
        /// ScrollHost 与 ViewBase 等容器控件共用，避免各处重复同一位运算。
        /// </summary>
        public static void EnableComposited(CreateParams cp)
        {
            cp.ExStyle |= 0x02000000;
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)));
                return path;
            }

            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>填充圆角矩形。路径与画刷均走缓存，避免每帧创建 GDI 对象。</summary>
        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.FillPath(GdiCache.Brush(color), path);
            g.Restore(st);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.DrawPath(GdiCache.Pen(color, width), path);
            g.Restore(st);
        }

        /// <summary>卡片风格：填充 + 描边 + 顶部 1px 高光，营造轻微立体感。</summary>
        public static void DrawCard(Graphics g, Rectangle r, int radius, Color fill, Color border, bool highlight)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            try
            {
                g.FillPath(GdiCache.Brush(fill), path);
                if (highlight)
                {
                    // 设计内高光：上缘通亮、向下渐隐（渐变描边），而不是整圈等亮。
                    // 路径是 (0,0) 基准的局部坐标，故渐变矩形同样用局部坐标；
                    // 笔刷本体走按高度缓存的 HighlightPen（这里是最热的绘制路径之一）
                    g.DrawPath(GdiCache.HighlightPen(r.Height), path);
                }
                g.DrawPath(GdiCache.Pen(border, 1f), path);
            }
            finally
            {
                g.Restore(st);
            }
        }

        /// <summary>
        /// 圆环仪表的核心画法：轨道 + 分段渐变进度弧（可选环境光晕、末端落点圆）。
        /// 内存页的 <c>RingGauge</c> 与优化中心的 <c>ModuleHealthCard</c> 共用这一份实现
        /// （此前是两套几乎逐行相同的段式弧算法）。
        /// 之所以分段而不是一条粗弧：单色弧看起来像占位图；相邻段多画 0.8° 覆盖接缝避免断线。
        /// 不设置线帽：缓存的画笔是共享对象，就地改线帽会污染其它调用方。
        /// </summary>
        /// <param name="g">画布</param>
        /// <param name="ring">圆环外接矩形</param>
        /// <param name="thickness">线宽</param>
        /// <param name="sweepDeg">弧长（度）；≤0 时只画轨道</param>
        /// <param name="from">起始色</param>
        /// <param name="to">末端色</param>
        /// <param name="glow">是否叠加低透明度环境光晕（让仪表显得"在工作"）</param>
        /// <param name="endDot">是否在弧末端画实心圆点（设计上的"落点"）</param>
        /// <param name="degreesPerSegment">分段粒度（度）：段数 = ceil(sweepDeg / 该值)</param>
        public static void DrawRingArc(Graphics g, Rectangle ring, float thickness, double sweepDeg,
            Color from, Color to, bool glow, bool endDot, double degreesPerSegment)
        {
            g.DrawEllipse(GdiCache.Pen(Theme.CardBgAlt, thickness), ring);

            if (sweepDeg <= 0) return;
            if (sweepDeg > 360) sweepDeg = 360;

            if (glow)
            {
                g.DrawArc(GdiCache.RoundPen(Gfx.Alpha(from, 42), thickness + 6),
                    ring, -90f, (float)sweepDeg);
            }

            double per = degreesPerSegment < 1 ? 1 : degreesPerSegment;
            int seg = (int)Math.Ceiling(sweepDeg / per);
            if (seg < 1) seg = 1;

            for (int i = 0; i < seg; i++)
            {
                float a0 = -90f + (float)(sweepDeg * i / seg);
                float a1 = -90f + (float)(sweepDeg * (i + 1) / seg);
                Color c = seg <= 1 ? from : Gfx.Blend(from, to, (double)i / (seg - 1));
                g.DrawArc(GdiCache.Pen(c, thickness), ring, a0, (a1 - a0) + 0.8f);
            }

            if (endDot)
            {
                double endRad = (-90.0 + sweepDeg) * Math.PI / 180.0;
                float rr = (ring.Width - thickness) / 2f;
                float cx = ring.X + ring.Width / 2f + (float)Math.Round(Math.Cos(endRad) * rr);
                float cy = ring.Y + ring.Height / 2f + (float)Math.Round(Math.Sin(endRad) * rr);
                g.FillEllipse(GdiCache.Brush(to),
                    cx - thickness / 2f, cy - thickness / 2f, thickness, thickness);
            }
        }

        public static void EnableSmoothing(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>把颜色按比例调亮 / 调暗。</summary>
        public static Color Shade(Color c, double factor)
        {
            int r = Clamp(c.R * factor);
            int g = Clamp(c.G * factor);
            int b = Clamp(c.B * factor);
            return Color.FromArgb(c.A, r, g, b);
        }

        private static int Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (int)v;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// 带 alpha 的颜色插值。<see cref="Blend"/> 会把结果 alpha 固定为 255，
        /// 透明 / 半透明色参与状态过渡时会被拉成不透明（例如 Ghost 按钮凭空出现实心底），
        /// 因此凡是有透明度参与的混合都必须用这个重载。
        /// </summary>
        public static Color BlendArgb(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>带透明度的颜色。</summary>
        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>根据占用率返回语义化颜色。</summary>
        public static Color LoadColor(double percent)
        {
            if (percent < 0) return Theme.Accent;
            if (percent >= 90) return Theme.Danger;
            if (percent >= 70) return Theme.Warning;
            return Theme.Success;
        }

        /// <summary>绘制单行文本，过长时自动省略号。绘制时把矩形上下各留 1px 呼吸空间，
        /// 配合 GdiCache.Ellipsis 的 NoClip，彻底消除 g/y/标点下行被裁掉的排版瑕疵。</summary>
        public static void DrawTextEllipsis(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            Rectangle r = new Rectangle(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2);
            g.DrawString(text, font, GdiCache.Brush(color), r, GdiCache.Ellipsis);
        }

        /// <summary>在指定矩形内居中绘制文本（上下各留 1px 呼吸空间，防下行裁切）。</summary>
        public static void DrawTextCenter(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            Rectangle r = new Rectangle(bounds.X, bounds.Y - 1, bounds.Width, bounds.Height + 2);
            g.DrawString(text, font, GdiCache.Brush(color), r, GdiCache.EllipsisCenter);
        }
    }
}
