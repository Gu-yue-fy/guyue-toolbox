using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GuyueBox.UI
{
    /// <summary>
    /// 用矢量绘制界面图标，避免依赖任何图标字体或图片资源。
    /// </summary>
    public static class IconPainter
    {
        /// <summary>
        /// 分段渐变绘制：把图标区域切成横向色带，逐带用插值色重绘，
        /// 无需改动任何具体图标的绘制逻辑即可获得线性渐变效果。
        /// </summary>
        public static void DrawGradient(Graphics g, string kind, Rectangle r, Color top, Color bottom)
        {
            const int steps = 6;
            try
            {
                for (int i = 0; i < steps; i++)
                {
                    float t = steps == 1 ? 0 : i / (float)(steps - 1);
                    Color c = Color.FromArgb(
                        top.A + (int)((bottom.A - top.A) * t),
                        top.R + (int)((bottom.R - top.R) * t),
                        top.G + (int)((bottom.G - top.G) * t),
                        top.B + (int)((bottom.B - top.B) * t));
                    Rectangle band = new Rectangle(r.X, r.Y + r.Height * i / steps, r.Width, r.Height / steps + 1);
                    g.SetClip(band);
                    Draw(g, kind, r, c);
                }
                g.ResetClip();
            }
            catch
            {
            }
        }

        public static void Draw(Graphics g, string kind, Rectangle r, Color color)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (r.Width <= 0 || r.Height <= 0) return;

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float size = Math.Min(r.Width, r.Height);
            float left = r.X + (r.Width - size) / 2f;
            float top = r.Y + (r.Height - size) / 2f;
            RectangleF box = new RectangleF(left, top, size, size);

            // 画笔与画刷走全局缓存，避免每次绘制都创建 GDI 对象
            Pen pen = GdiCache.RoundPen(color, Math.Max(1.6f, size / 11f));
            SolidBrush brush = GdiCache.Brush(color);
            {
                switch (kind)
                {
                        case "dashboard": DrawDashboard(g, pen, brush, box); break;
                        case "clean": DrawClean(g, pen, box); break;
                        case "startup": DrawStartup(g, pen, brush, box); break;
                        case "process": DrawProcess(g, pen, brush, box); break;
                        case "tune": DrawTune(g, pen, brush, box); break;
                        case "network": DrawNetwork(g, pen, box); break;
                        case "shield": DrawShield(g, pen, box); break;
                        case "recycle": DrawRecycle(g, pen, box); break;
                        case "temp": DrawTemp(g, pen, box); break;
                        case "image": DrawImage(g, pen, brush, box); break;
                        case "globe": DrawGlobe(g, pen, box); break;
                        case "bug": DrawBug(g, pen, box); break;
                        case "list": DrawList(g, pen, brush, box); break;
                        case "bolt": DrawBolt(g, brush, box); break;
                        case "share": DrawShare(g, pen, brush, box); break;
                        case "check": DrawCheck(g, pen, box); break;
                        case "close": DrawClose(g, pen, box); break;
                        case "min": DrawMin(g, pen, box); break;
                        case "max": DrawMax(g, pen, box); break;
                        case "restore": DrawRestore(g, pen, box); break;
                        case "refresh": DrawRefresh(g, pen, box); break;
                        case "admin": DrawAdmin(g, pen, brush, box); break;
                        case "info": DrawInfo(g, pen, brush, box); break;
                        case "trash": DrawTrash(g, pen, box); break;
                        case "folder": DrawFolder(g, pen, box); break;
                        case "cpu": DrawCpu(g, pen, box); break;
                        case "memory": DrawMemory(g, pen, box); break;
                        case "disk": DrawDisk(g, pen, brush, box); break;
                        case "apps": DrawApps(g, pen, box); break;
                        case "search": DrawSearch(g, pen, box); break;
                        case "plus": DrawPlus(g, pen, box); break;
                        case "history": DrawHistory(g, pen, box); break;
                        case "doc": DrawDoc(g, pen, box); break;
                        case "feature": DrawFeature(g, pen, box); break;
                        case "dup": DrawDup(g, pen, box); break;
                        case "play": DrawPlay(g, pen, brush, box); break;
                        case "stop": DrawStop(g, brush, box); break;
                        case "services": DrawServices(g, pen, box); break;
                        case "clock": DrawClock(g, pen, box); break;
                        case "lock": DrawLock(g, pen, brush, box); break;
                        case "power": DrawPower(g, pen, box); break;
                        case "menu": DrawMenu(g, pen, box); break;
                        case "task": DrawTask(g, pen, box); break;
                        case "gauge": DrawGauge(g, pen, box); break;
                        case "user": DrawUser(g, pen, box); break;
                        case "edit": DrawEdit(g, pen, box); break;
                        case "warn": DrawWarn(g, pen, box); break;
                    }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawDashboard(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            SolidBrush solid = brush as SolidBrush;
            Color baseColor = solid != null ? solid.Color : Color.White;

            float q = b.Width * 0.40f;
            float r = q * 0.30f;
            float x2 = b.Right - q;
            float y2 = b.Bottom - q;

            Gfx.FillRound(g, ToRect(b.Left, b.Top, q), (int)r, baseColor);
            Gfx.FillRound(g, ToRect(x2, b.Top, q), (int)r, Gfx.Alpha(baseColor, 130));
            Gfx.FillRound(g, ToRect(b.Left, y2, q), (int)r, Gfx.Alpha(baseColor, 130));
            Gfx.FillRound(g, ToRect(x2, y2, q), (int)r, baseColor);
        }

        private static Rectangle ToRect(float x, float y, float size)
        {
            return new Rectangle((int)Math.Round(x), (int)Math.Round(y),
                Math.Max(1, (int)Math.Round(size)), Math.Max(1, (int)Math.Round(size)));
        }

        private static void DrawClean(Graphics g, Pen pen, RectangleF b)
        {
            // 扫帚
            g.DrawLine(pen, b.Left + b.Width * 0.72f, b.Top + b.Height * 0.12f,
                b.Left + b.Width * 0.30f, b.Top + b.Height * 0.62f);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new PointF[]
                {
                    new PointF(b.Left + b.Width * 0.14f, b.Top + b.Height * 0.96f),
                    new PointF(b.Left + b.Width * 0.40f, b.Top + b.Height * 0.70f),
                    new PointF(b.Left + b.Width * 0.66f, b.Top + b.Height * 0.90f),
                    new PointF(b.Left + b.Width * 0.36f, b.Bottom)
                });
                using (SolidBrush br = new SolidBrush(Color.FromArgb(90, pen.Color)))
                {
                    g.FillPath(br, p);
                }
                g.DrawPath(pen, p);
            }
        }

        private static void DrawStartup(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            g.DrawLine(pen, b.Left + b.Width * 0.5f, b.Top, b.Left + b.Width * 0.5f, b.Top + b.Height * 0.68f);
            g.DrawLine(pen, b.Left + b.Width * 0.24f, b.Top + b.Height * 0.30f,
                b.Left + b.Width * 0.5f, b.Top);
            g.DrawLine(pen, b.Left + b.Width * 0.76f, b.Top + b.Height * 0.30f,
                b.Left + b.Width * 0.5f, b.Top);
            g.DrawLine(pen, b.Left + b.Width * 0.16f, b.Bottom - b.Height * 0.10f,
                b.Right - b.Width * 0.16f, b.Bottom - b.Height * 0.10f);
        }

        private static void DrawProcess(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float h = b.Height * 0.16f;
            float y = b.Top + b.Height * 0.10f;
            for (int i = 0; i < 3; i++)
            {
                g.FillRectangle(brush, b.Left, y + i * (b.Height * 0.32f), b.Width * 0.18f, h);
                g.DrawLine(pen, b.Left + b.Width * 0.30f, y + i * (b.Height * 0.32f) + h / 2,
                    b.Right, y + i * (b.Height * 0.32f) + h / 2);
            }
        }

        private static void DrawTune(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float rad = b.Width * 0.28f;

            // 齿轮：外圈 + 8 个齿 + 中心孔
            using (Pen tooth = new Pen(pen.Color, pen.Width * 1.5f))
            {
                tooth.StartCap = LineCap.Round;
                tooth.EndCap = LineCap.Round;
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4.0 + Math.PI / 8.0;
                    float x1 = cx + (float)(Math.Cos(a) * rad * 1.18f);
                    float y1 = cy + (float)(Math.Sin(a) * rad * 1.18f);
                    float x2 = cx + (float)(Math.Cos(a) * rad * 1.62f);
                    float y2 = cy + (float)(Math.Sin(a) * rad * 1.62f);
                    g.DrawLine(tooth, x1, y1, x2, y2);
                }
            }

            g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                g.FillEllipse(br, cx - rad * 0.36f, cy - rad * 0.36f, rad * 0.72f, rad * 0.72f);
            }
        }

        private static void DrawNetwork(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                float radius = b.Width * (0.16f + i * 0.20f);
                g.DrawArc(pen, cx - radius, b.Top + b.Height * 0.34f - radius, radius * 2, radius * 2, 205, 130);
            }
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                g.FillEllipse(br, cx - b.Width * 0.07f, b.Top + b.Height * 0.76f, b.Width * 0.14f, b.Height * 0.14f);
            }
        }

        private static void DrawShield(Graphics g, Pen pen, RectangleF b)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new PointF[]
                {
                    new PointF(b.Left + b.Width * 0.5f, b.Top),
                    new PointF(b.Right, b.Top + b.Height * 0.20f),
                    new PointF(b.Right, b.Top + b.Height * 0.58f),
                    new PointF(b.Left + b.Width * 0.5f, b.Bottom),
                    new PointF(b.Left, b.Top + b.Height * 0.58f),
                    new PointF(b.Left, b.Top + b.Height * 0.20f)
                });
                g.DrawPath(pen, p);
            }
        }

        private static void DrawRecycle(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.52f;
            float r = b.Width * 0.34f;
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 40, 110);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 165, 110);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 290, 100);
        }

        private static void DrawTemp(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left + b.Width * 0.12f, b.Top + b.Height * 0.22f, b.Width * 0.76f, b.Height * 0.66f);
            g.DrawRectangle(pen, b.Left + b.Width * 0.34f, b.Top + b.Height * 0.06f, b.Width * 0.32f, b.Height * 0.16f);
            g.DrawLine(pen, b.Left + b.Width * 0.38f, b.Top + b.Height * 0.44f, b.Left + b.Width * 0.62f, b.Top + b.Height * 0.44f);
            g.DrawLine(pen, b.Left + b.Width * 0.38f, b.Top + b.Height * 0.64f, b.Left + b.Width * 0.62f, b.Top + b.Height * 0.64f);
        }

        private static void DrawImage(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left, b.Top + b.Height * 0.12f, b.Width, b.Height * 0.76f);
            using (SolidBrush br = new SolidBrush(Color.FromArgb(90, pen.Color)))
            {
                g.FillEllipse(br, b.Left + b.Width * 0.16f, b.Top + b.Height * 0.24f, b.Width * 0.18f, b.Height * 0.18f);
            }
            g.DrawLines(pen, new PointF[]
            {
                new PointF(b.Left + b.Width * 0.08f, b.Top + b.Height * 0.82f),
                new PointF(b.Left + b.Width * 0.38f, b.Top + b.Height * 0.48f),
                new PointF(b.Left + b.Width * 0.62f, b.Top + b.Height * 0.74f),
                new PointF(b.Left + b.Width * 0.76f, b.Top + b.Height * 0.60f),
                new PointF(b.Left + b.Width * 0.94f, b.Top + b.Height * 0.82f)
            });
        }

        private static void DrawGlobe(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float rx = b.Width * 0.46f;
            g.DrawEllipse(pen, cx - rx, cy - rx, rx * 2, rx * 2);
            g.DrawLine(pen, cx - rx, cy, cx + rx, cy);
            g.DrawEllipse(pen, cx - rx * 0.45f, cy - rx, rx * 0.9f, rx * 2);
        }

        private static void DrawBug(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.56f;
            float rx = b.Width * 0.24f;
            float ry = b.Height * 0.30f;
            g.DrawEllipse(pen, cx - rx, cy - ry, rx * 2, ry * 2);
            g.DrawLine(pen, cx - rx, cy - ry * 0.4f, b.Left, b.Top + b.Height * 0.10f);
            g.DrawLine(pen, cx + rx, cy - ry * 0.4f, b.Right, b.Top + b.Height * 0.10f);
            g.DrawLine(pen, cx - rx, cy, b.Left, cy);
            g.DrawLine(pen, cx + rx, cy, b.Right, cy);
            g.DrawLine(pen, cx - rx, cy + ry * 0.6f, b.Left, b.Bottom);
            g.DrawLine(pen, cx + rx, cy + ry * 0.6f, b.Right, b.Bottom);
        }

        private static void DrawList(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            for (int i = 0; i < 4; i++)
            {
                float y = b.Top + b.Height * (0.08f + i * 0.28f);
                g.FillRectangle(brush, b.Left, y, b.Width * 0.10f, b.Height * 0.10f);
                g.DrawLine(pen, b.Left + b.Width * 0.22f, y + b.Height * 0.05f, b.Right, y + b.Height * 0.05f);
            }
        }

        private static void DrawBolt(Graphics g, Brush brush, RectangleF b)
        {
            PointF[] pts = new PointF[]
            {
                new PointF(b.Left + b.Width * 0.58f, b.Top),
                new PointF(b.Left + b.Width * 0.16f, b.Top + b.Height * 0.58f),
                new PointF(b.Left + b.Width * 0.46f, b.Top + b.Height * 0.58f),
                new PointF(b.Left + b.Width * 0.38f, b.Bottom),
                new PointF(b.Left + b.Width * 0.86f, b.Top + b.Height * 0.40f),
                new PointF(b.Left + b.Width * 0.54f, b.Top + b.Height * 0.40f)
            };
            g.FillPolygon(brush, pts);
        }

        private static void DrawShare(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float r = b.Width * 0.16f;
            float x1 = b.Left + r, y1 = b.Top + b.Height * 0.5f;
            float x2 = b.Right - r, y2 = b.Top + r;
            float x3 = b.Right - r, y3 = b.Bottom - r;
            g.DrawLine(pen, x1 + r * 0.8f, y1, x2 - r * 0.8f, y2);
            g.DrawLine(pen, x1 + r * 0.8f, y1, x3 - r * 0.8f, y3);
            g.DrawLine(pen, x2, y2 + r, x3, y3 - r);
            g.FillEllipse(brush, x1 - r, y1 - r, r * 2, r * 2);
            g.FillEllipse(brush, x2 - r, y2 - r, r * 2, r * 2);
            g.FillEllipse(brush, x3 - r, y3 - r, r * 2, r * 2);
        }

        private static void DrawCheck(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawLines(pen, new PointF[]
            {
                new PointF(b.Left + b.Width * 0.14f, b.Top + b.Height * 0.52f),
                new PointF(b.Left + b.Width * 0.40f, b.Top + b.Height * 0.78f),
                new PointF(b.Left + b.Width * 0.88f, b.Top + b.Height * 0.20f)
            });
        }

        private static void DrawClose(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawLine(pen, b.Left, b.Top, b.Right, b.Bottom);
            g.DrawLine(pen, b.Right, b.Top, b.Left, b.Bottom);
        }

        private static void DrawMin(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawLine(pen, b.Left, b.Top + b.Height * 0.5f, b.Right, b.Top + b.Height * 0.5f);
        }

        private static void DrawMax(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left, b.Top, b.Width, b.Height);
        }

        private static void DrawRestore(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left, b.Top + b.Height * 0.22f, b.Width * 0.72f, b.Height * 0.72f);
            g.DrawLines(pen, new PointF[]
            {
                new PointF(b.Left + b.Width * 0.26f, b.Top + b.Height * 0.22f),
                new PointF(b.Left + b.Width * 0.26f, b.Top),
                new PointF(b.Right, b.Top),
                new PointF(b.Right, b.Top + b.Height * 0.74f),
                new PointF(b.Left + b.Width * 0.74f, b.Top + b.Height * 0.74f)
            });
        }

        private static void DrawRefresh(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float r = b.Width * 0.40f;
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 40, 260);
            g.DrawLines(pen, new PointF[]
            {
                new PointF(cx + r * 0.62f, cy - r * 0.92f),
                new PointF(cx + r * 0.98f, cy - r * 0.52f),
                new PointF(cx + r * 0.46f, cy - r * 0.24f)
            });
        }

        private static void DrawAdmin(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            // 盾牌 + 勾
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddPolygon(new PointF[]
                {
                    new PointF(b.Left + b.Width * 0.5f, b.Top),
                    new PointF(b.Right, b.Top + b.Height * 0.20f),
                    new PointF(b.Right, b.Top + b.Height * 0.58f),
                    new PointF(b.Left + b.Width * 0.5f, b.Bottom),
                    new PointF(b.Left, b.Top + b.Height * 0.58f),
                    new PointF(b.Left, b.Top + b.Height * 0.20f)
                });
                using (SolidBrush br = new SolidBrush(Color.FromArgb(70, pen.Color)))
                {
                    g.FillPath(br, p);
                }
                g.DrawPath(pen, p);
            }
        }

        private static void DrawInfo(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float r = b.Width * 0.46f;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.FillEllipse(brush, cx - r * 0.10f, cy - r * 0.46f, r * 0.20f, r * 0.20f);
            g.DrawLine(pen, cx, cy - r * 0.10f, cx, cy + r * 0.46f);
        }

        private static void DrawTrash(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left + b.Width * 0.16f, b.Top + b.Height * 0.26f, b.Width * 0.68f, b.Height * 0.68f);
            g.DrawLine(pen, b.Left, b.Top + b.Height * 0.18f, b.Right, b.Top + b.Height * 0.18f);
            g.DrawRectangle(pen, b.Left + b.Width * 0.34f, b.Top, b.Width * 0.32f, b.Height * 0.16f);
        }

        private static void DrawFolder(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawLines(pen, new PointF[]
            {
                new PointF(b.Left, b.Bottom - b.Height * 0.06f),
                new PointF(b.Left, b.Top + b.Height * 0.16f),
                new PointF(b.Left + b.Width * 0.40f, b.Top + b.Height * 0.16f),
                new PointF(b.Left + b.Width * 0.50f, b.Top + b.Height * 0.34f),
                new PointF(b.Right, b.Top + b.Height * 0.34f),
                new PointF(b.Right, b.Bottom - b.Height * 0.06f),
                new PointF(b.Left, b.Bottom - b.Height * 0.06f)
            });
        }

        private static void DrawCpu(Graphics g, Pen pen, RectangleF b)
        {
            float inset = b.Width * 0.22f;
            g.DrawRectangle(pen, b.Left + inset, b.Top + inset, b.Width - inset * 2, b.Height - inset * 2);
            for (int i = 0; i < 3; i++)
            {
                float off = b.Width * (0.34f + i * 0.16f);
                g.DrawLine(pen, b.Left + off, b.Top, b.Left + off, b.Top + inset);
                g.DrawLine(pen, b.Left + off, b.Bottom - inset, b.Left + off, b.Bottom);
                g.DrawLine(pen, b.Left, b.Top + off, b.Left + inset, b.Top + off);
                g.DrawLine(pen, b.Right - inset, b.Top + off, b.Right, b.Top + off);
            }
        }

        private static void DrawMemory(Graphics g, Pen pen, RectangleF b)
        {
            g.DrawRectangle(pen, b.Left, b.Top + b.Height * 0.22f, b.Width, b.Height * 0.56f);
            for (int i = 0; i < 3; i++)
            {
                g.DrawRectangle(pen, b.Left + b.Width * (0.10f + i * 0.28f), b.Top + b.Height * 0.36f,
                    b.Width * 0.18f, b.Height * 0.28f);
            }
        }

        private static void DrawDisk(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float cy = b.Top + b.Height * 0.5f;
            g.DrawEllipse(pen, b.Left + b.Width * 0.08f, cy - b.Height * 0.42f, b.Width * 0.84f, b.Height * 0.84f);
            g.FillEllipse(brush, b.Left + b.Width * 0.42f, cy - b.Height * 0.08f, b.Width * 0.16f, b.Height * 0.16f);
        }

        private static void DrawClock(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float r = b.Width * 0.46f;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawLine(pen, cx, cy, cx, cy - r * 0.58f);
            g.DrawLine(pen, cx, cy, cx + r * 0.46f, cy);
        }

        private static void DrawApps(Graphics g, Pen pen, RectangleF b)
        {
            float s = b.Width * 0.36f;
            float right = b.Right - s;
            float bottom = b.Bottom - s;
            Gfx.StrokeRound(g, ToRect(b.Left, b.Top, s), 3, pen.Color, pen.Width);
            Gfx.StrokeRound(g, ToRect(right, b.Top, s), 3, pen.Color, pen.Width);
            Gfx.StrokeRound(g, ToRect(b.Left, bottom, s), 3, pen.Color, pen.Width);
            Gfx.StrokeRound(g, ToRect(right, bottom, s), 3, pen.Color, pen.Width);
        }

        private static void DrawSearch(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.42f;
            float cy = b.Top + b.Height * 0.42f;
            float r = b.Width * 0.30f;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawLine(pen, cx + r * 0.70f, cy + r * 0.70f,
                b.Right - b.Width * 0.06f, b.Bottom - b.Height * 0.06f);
        }

        private static void DrawPlus(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float l = b.Width * 0.30f;
            g.DrawLine(pen, cx - l, cy, cx + l, cy);
            g.DrawLine(pen, cx, cy - l, cx, cy + l);
        }

        private static void DrawHistory(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float r = b.Width * 0.38f;
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 210, 200);
            g.DrawLine(pen, cx, cy, cx, cy - r * 0.55f);
            g.DrawLine(pen, cx, cy, cx + r * 0.42f, cy);
        }

        private static void DrawPlay(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.46f;
            float cy = b.Top + b.Height * 0.5f;
            float s = b.Height * 0.30f;
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                g.FillPolygon(br, new PointF[]
                {
                    new PointF(cx - s, cy - s),
                    new PointF(cx - s, cy + s),
                    new PointF(cx + s * 1.3f, cy)
                });
            }
        }

        private static void DrawDup(Graphics g, Pen pen, RectangleF b)
        {
            float s = b.Width * 0.40f;
            int y = (int)(b.Top + b.Height * 0.12f);
            Gfx.StrokeRound(g, new Rectangle((int)b.Left, y, (int)s, (int)s), 4, pen.Color, pen.Width);
            Gfx.StrokeRound(g, new Rectangle((int)(b.Right - s), y, (int)s, (int)s), 4, pen.Color, pen.Width);
        }

        private static void DrawFeature(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float step = b.Width * 0.26f;
            float r = b.Width * 0.07f;
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        g.FillEllipse(br, cx + i * step - r, cy + j * step - r, r * 2, r * 2);
                    }
                }
            }
        }

        private static void DrawMenu(Graphics g, Pen pen, RectangleF b)
        {
            float x = b.Left + b.Width * 0.22f;
            float w = b.Width * 0.56f;
            float y1 = b.Top + b.Height * 0.30f;
            float y2 = b.Top + b.Height * 0.50f;
            float y3 = b.Top + b.Height * 0.70f;
            float t = Math.Max(1.4f, pen.Width);
            g.DrawLine(pen, x, y1, x + w, y1);
            g.DrawLine(pen, x, y2, x + w, y2);
            g.DrawLine(pen, x, y3, x + w, y3);
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                float d = b.Width * 0.10f;
                g.FillRectangle(br, x - d, y1 - t, d, t * 2);
                g.FillRectangle(br, x - d, y2 - t, d, t * 2);
                g.FillRectangle(br, x - d, y3 - t, d, t * 2);
            }
        }

        private static void DrawTask(Graphics g, Pen pen, RectangleF b)
        {
            float x = b.Left + b.Width * 0.24f;
            float w = b.Width * 0.52f;
            float y = b.Top + b.Height * 0.20f;
            float h = b.Height * 0.60f;
            float t = Math.Max(1.4f, pen.Width);
            g.DrawRectangle(pen, x, y, w, h);
            float lineY = y + h * 0.30f;
            float boxX = x + w * 0.16f;
            float boxS = h * 0.22f;
            g.DrawRectangle(pen, boxX, lineY - boxS * 0.5f, boxS, boxS);
            g.DrawLine(pen, x + w * 0.42f, lineY, x + w * 0.82f, lineY);
            float lineY2 = y + h * 0.62f;
            g.DrawRectangle(pen, boxX, lineY2 - boxS * 0.5f, boxS, boxS);
            g.DrawLine(pen, x + w * 0.42f, lineY2, x + w * 0.82f, lineY2);
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                g.FillEllipse(br, boxX, lineY - boxS * 0.5f, boxS, boxS);
            }
        }

        private static void DrawGauge(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.58f;
            float r = b.Width * 0.34f;
            using (Pen p = new Pen(pen.Color, Math.Max(1.6f, pen.Width * 1.2f)))
            {
                g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, 180, 180);
                g.DrawLine(p, cx - r * 0.5f, cy + r * 0.55f, cx + r * 0.5f, cy + r * 0.55f);
            }
            g.DrawLine(pen, cx, cy, cx + r * 0.55f, cy - r * 0.5f);
            g.DrawLine(pen, cx, cy - r * 0.1f, cx, cy + r * 0.1f);
        }

        private static void DrawUser(Graphics g, Pen pen, RectangleF b)
        {
            // 人形：头 + 肩
            float cx = b.Left + b.Width * 0.5f;
            float headR = b.Width * 0.19f;
            float headCy = b.Top + b.Height * 0.30f;
            g.DrawEllipse(pen, cx - headR, headCy - headR, headR * 2, headR * 2);
            using (Pen p = new Pen(pen.Color, Math.Max(1.6f, pen.Width * 1.2f)))
            {
                g.DrawArc(p,
                    b.Left + b.Width * 0.14f, b.Top + b.Height * 0.55f,
                    b.Width * 0.72f, b.Height * 0.52f, 180, 180);
            }
        }

        private static void DrawEdit(Graphics g, Pen pen, RectangleF b)
        {
            // 铅笔：斜杆 + 笔尖
            float x1 = b.Left + b.Width * 0.72f;
            float y1 = b.Top + b.Height * 0.16f;
            float x2 = b.Left + b.Width * 0.30f;
            float y2 = b.Top + b.Height * 0.72f;
            using (Pen p = new Pen(pen.Color, Math.Max(1.6f, pen.Width * 1.3f)))
            {
                g.DrawLine(p, x1, y1, x2, y2);
            }
            g.DrawLine(pen, x2, y2, b.Left + b.Width * 0.22f, b.Bottom - b.Height * 0.14f);
            g.DrawLine(pen, b.Left + b.Width * 0.22f, b.Bottom - b.Height * 0.14f, x2 + b.Width * 0.13f, y2 - b.Height * 0.05f);
            g.DrawLine(pen, x1 - b.Width * 0.06f, y1 + b.Height * 0.05f, x1 + b.Width * 0.12f, y1 + b.Height * 0.18f);
        }

        private static void DrawWarn(Graphics g, Pen pen, RectangleF b)
        {
            // 警告三角 + 叹号
            using (Pen p = new Pen(pen.Color, Math.Max(1.6f, pen.Width * 1.2f)))
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(new PointF[]
                {
                    new PointF(b.Left + b.Width * 0.5f, b.Top + b.Height * 0.10f),
                    new PointF(b.Right - b.Width * 0.08f, b.Bottom - b.Height * 0.14f),
                    new PointF(b.Left + b.Width * 0.08f, b.Bottom - b.Height * 0.14f)
                });
                g.DrawPath(p, path);
                float cx = b.Left + b.Width * 0.5f;
                g.DrawLine(p, cx, b.Top + b.Height * 0.38f, cx, b.Top + b.Height * 0.62f);
                g.DrawLine(p, cx, b.Top + b.Height * 0.74f, cx, b.Top + b.Height * 0.78f);
            }
        }

        private static void DrawDoc(Graphics g, Pen pen, RectangleF b)
        {
            float x = b.Left + b.Width * 0.22f;
            float y = b.Top + b.Height * 0.16f;
            float w = b.Width * 0.56f;
            float h = b.Height * 0.68f;
            g.DrawRectangle(pen, x, y, w, h);
            float lx = x + w * 0.18f;
            float rx = x + w * 0.82f;
            for (int i = 1; i <= 3; i++)
            {
                float ly = y + h * (0.26f + i * 0.18f);
                g.DrawLine(pen, lx, ly, rx, ly);
            }
        }

        private static void DrawStop(Graphics g, Brush brush, RectangleF b)
        {
            using (SolidBrush br = new SolidBrush(penColor(brush)))
            {
                float s = b.Width * 0.28f;
                g.FillRectangle(br, b.Left + b.Width * 0.5f - s, b.Top + b.Height * 0.5f - s, s * 2, s * 2);
            }
        }

        private static Color penColor(Brush brush)
        {
            SolidBrush sb = brush as SolidBrush;
            return sb != null ? sb.Color : Color.White;
        }

        private static void DrawServices(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float rad = b.Width * 0.22f;
            g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            using (Pen tooth = new Pen(pen.Color, pen.Width * 1.4f))
            {
                tooth.StartCap = LineCap.Round;
                tooth.EndCap = LineCap.Round;
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3.0;
                    float x1 = cx + (float)(Math.Cos(a) * rad * 1.15f);
                    float y1 = cy + (float)(Math.Sin(a) * rad * 1.15f);
                    float x2 = cx + (float)(Math.Cos(a) * rad * 1.6f);
                    float y2 = cy + (float)(Math.Sin(a) * rad * 1.6f);
                    g.DrawLine(tooth, x1, y1, x2, y2);
                }
            }
            using (SolidBrush br = new SolidBrush(pen.Color))
            {
                g.FillEllipse(br, cx - rad * 0.34f, cy - rad * 0.34f, rad * 0.68f, rad * 0.68f);
            }
        }

        private static void DrawLock(Graphics g, Pen pen, Brush brush, RectangleF b)
        {
            g.DrawArc(pen, b.Left + b.Width * 0.24f, b.Top, b.Width * 0.52f, b.Height * 0.62f, 180, 180);
            g.FillRectangle(brush, b.Left + b.Width * 0.14f, b.Top + b.Height * 0.46f, b.Width * 0.72f, b.Height * 0.54f);
        }

        private static void DrawPower(Graphics g, Pen pen, RectangleF b)
        {
            float cx = b.Left + b.Width * 0.5f;
            float cy = b.Top + b.Height * 0.5f;
            float r = b.Width * 0.44f;
            g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -60, 300);
            g.DrawLine(pen, cx, b.Top, cx, cy - r * 0.10f);
        }
    }
}
