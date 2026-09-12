using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SysToolbox.UI
{
    /// <summary>
    /// 全局配色与字体。深色主题，蓝紫作为强调色。
    /// </summary>
    public static class Theme
    {
        // ---------------- 背景层次（由暗到亮） ----------------

        public static readonly Color ChromeBg = Color.FromArgb(15, 17, 21);      // 标题栏 / 侧栏
        public static readonly Color WindowBg = Color.FromArgb(21, 23, 28);      // 页面背景
        public static readonly Color CardBg = Color.FromArgb(29, 33, 40);        // 卡片
        public static readonly Color CardBgAlt = Color.FromArgb(36, 41, 50);     // 卡片内次级区域
        public static readonly Color CardHover = Color.FromArgb(43, 49, 60);
        public static readonly Color SidebarBg = Color.FromArgb(15, 17, 21);

        // ---------------- 描边 ----------------

        public static readonly Color BorderSoft = Color.FromArgb(34, 39, 47);
        public static readonly Color Border = Color.FromArgb(45, 51, 61);
        public static readonly Color BorderStrong = Color.FromArgb(63, 71, 84);

        // ---------------- 文本 ----------------

        public static readonly Color TextPrimary = Color.FromArgb(236, 240, 247);
        public static readonly Color TextSecondary = Color.FromArgb(158, 168, 183);
        public static readonly Color TextMuted = Color.FromArgb(110, 119, 133);

        // ---------------- 强调色（可由设置页切换） ----------------

        public static Color Accent = Color.FromArgb(76, 141, 255);
        public static Color AccentHover = Color.FromArgb(108, 162, 255);
        public static Color AccentPress = Color.FromArgb(56, 118, 226);
        public static Color AccentSoft = Color.FromArgb(38, 60, 102);

        public static readonly Color Success = Color.FromArgb(53, 200, 138);
        public static readonly Color Warning = Color.FromArgb(242, 180, 64);
        public static readonly Color Danger = Color.FromArgb(239, 95, 95);
        public static readonly Color DangerHover = Color.FromArgb(248, 120, 120);
        public static readonly Color Purple = Color.FromArgb(167, 139, 250);
        public static readonly Color Cyan = Color.FromArgb(56, 189, 213);

        /// <summary>可选主题色（索引对应设置页色块）。</summary>
        public static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(76, 141, 255),   // 蓝（默认）
            Color.FromArgb(56, 189, 213),   // 青
            Color.FromArgb(167, 139, 250),  // 紫
            Color.FromArgb(53, 200, 138),   // 绿
            Color.FromArgb(242, 180, 64),   // 橙
            Color.FromArgb(239, 95, 95)     // 红
        };

        /// <summary>应用主题色索引 0-5，派生出悬停/按压/柔和高亮色。</summary>
        public static void ApplyAccent(int index)
        {
            if (index < 0 || index >= Palette.Length) index = 0;
            Color c = Palette[index];
            Accent = c;
            AccentHover = Lighten(c, 0.18f);
            AccentPress = Darken(c, 0.14f);
            Color mixed = Mix(c, WindowBg, 0.45f);
            AccentSoft = Color.FromArgb(38, mixed.R, mixed.G, mixed.B);
        }

        private static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(
                c.A,
                c.R + (int)((255 - c.R) * amount),
                c.G + (int)((255 - c.G) * amount),
                c.B + (int)((255 - c.B) * amount));
        }

        private static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static Color Mix(Color c, Color bg, float amount)
        {
            return Color.FromArgb(
                (int)(c.R * (1 - amount) + bg.R * amount),
                (int)(c.G * (1 - amount) + bg.G * amount),
                (int)(c.B * (1 - amount) + bg.B * amount));
        }

        // ---------------- 表格 ----------------

        public static readonly Color GridHeader = Color.FromArgb(25, 28, 35);
        public static readonly Color GridRow = Color.FromArgb(29, 33, 40);
        public static readonly Color GridRowAlt = Color.FromArgb(33, 37, 45);
        public static readonly Color GridSelection = Color.FromArgb(41, 62, 100);
        public static readonly Color GridHover = Color.FromArgb(38, 43, 53);

        // ---------------- 字体 ----------------

        private static readonly string Family = PickFamily();

        private static string PickFamily()
        {
            string[] candidates = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "Segoe UI" };
            foreach (string c in candidates)
            {
                try
                {
                    using (Font probe = new Font(c, 9F))
                    {
                        if (string.Equals(probe.Name, c, StringComparison.OrdinalIgnoreCase)) return c;
                    }
                }
                catch
                {
                }
            }
            return "Segoe UI";
        }

        // 字体只创建一次并长期复用，避免频繁绘制产生 GDI 句柄泄漏
        public static readonly Font FontMicro = new Font(Family, 8F);
        public static readonly Font FontSmall = new Font(Family, 8.5F);
        public static readonly Font FontBody = new Font(Family, 9.5F);
        public static readonly Font FontBodyBold = new Font(Family, 9.5F, FontStyle.Bold);
        public static readonly Font FontNav = new Font(Family, 10F);
        public static readonly Font FontSubTitle = new Font(Family, 12F, FontStyle.Bold);
        public static readonly Font FontTitle = new Font(Family, 18F, FontStyle.Bold);
        public static readonly Font FontMetric = new Font(Family, 21F, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 9F);

        public static string FontFamilyName
        {
            get { return Family; }
        }
    }

    /// <summary>绘制辅助。</summary>
    public static class Gfx
    {
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
                if (highlight) g.DrawPath(GdiCache.Pen(CardHighlight, 1f), path);
                g.DrawPath(GdiCache.Pen(border, 1f), path);
            }
            finally
            {
                g.Restore(st);
            }
        }

        private static readonly Color CardHighlight = Color.FromArgb(16, 255, 255, 255);

        public static void EnableSmoothing(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
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

        public static string PercentText(double percent)
        {
            if (percent < 0) return "--";
            return percent.ToString("0") + "%";
        }

        /// <summary>绘制单行文本，过长时自动省略号。</summary>
        public static void DrawTextEllipsis(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            g.DrawString(text, font, GdiCache.Brush(color), bounds, GdiCache.Ellipsis);
        }

        /// <summary>在指定矩形内居中绘制文本。</summary>
        public static void DrawTextCenter(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            g.DrawString(text, font, GdiCache.Brush(color), bounds, GdiCache.EllipsisCenter);
        }
    }
}
