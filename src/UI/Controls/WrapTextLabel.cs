using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.UI;

namespace GuyueBox.UI.Controls
{
    /// <summary>
    /// 自动换行正文标签：按控件宽度逐字/逐词换行并自动算出高度（详情栏说明正文用）。
    ///
    /// 曾用名 JustifyLabel（两端对齐版）：中英混排时被拉伸的字词间隙一宽一窄，
    /// 读起来像"字被掰开"，且与工具箱其它文字（自然左对齐）不统一 —— 已去掉两端对齐，
    /// 只保留逐字/逐词换行与自动测高（那套对齐数学也一并删除）。
    /// </summary>
    internal sealed class WrapTextLabel : Control
    {
        private int _lineHeight;
        private int _preferredHeight;
        private float _spaceW = 4;

        public WrapTextLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.TextSecondary;
            Font = Theme.FontSmall;
            AutoSize = false;
            _lineHeight = Font.Height + 4;
        }

        public override string Text
        {
            get { return base.Text; }
            set { if (value != base.Text) { base.Text = value; Recompute(); } }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recompute();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Recompute();
        }

        /// <summary>按当前宽度重测换行后的高度并自适应；调用方据 Height 排布。</summary>
        public void Recompute()
        {
            if (Width <= 0) { _preferredHeight = 0; return; }
            {
                // 用共享的测量画布，不再每次 CreateGraphics()：
                // 本方法由 OnResize 驱动，原先窗口拖动缩放时每一帧都要新建一个 HDC 再销毁。
                Graphics g = MeasureGraphics();
                StringFormat sf = MakeSf();
                try
                {
                    // 空格宽度必须用默认格式测：GenericTypographic 不计尾随空格，
                    // 测出来≈0，中英混排（如「让 Windows 在…」）会挤成一团
                    _spaceW = g.MeasureString(" ", Font).Width;
                    _lineHeight = (int)Math.Ceiling(Font.GetHeight(g)) + 6;
                    int h = 0;
                    string[] paras = (Text ?? "").Split('\n');
                    for (int p = 0; p < paras.Length; p++)
                    {
                        List<Unit> units = Tokenize(paras[p]);
                        // 行宽留 2px 余量：测量(GenericTypographic)与渲染(ClearType)存在
                        // 亚像素差异，行末最后一个汉字实测会被裁掉半边（"资源"只显示"资"）
                        List<List<Unit>> lines = Wrap(g, sf, units, Math.Max(0, Width - 2));
                        h += lines.Count * _lineHeight;
                    }
                    _preferredHeight = h;
                    if (Height != h) Height = h;
                }
                finally
                {
                    sf.Dispose();
                }
            }
            Invalidate();
        }

        /// <summary>
        /// 共享测量画布（1×1 位图上的 Graphics）：测文本只需要一个可用的 HDC，
        /// 每帧新建 Graphics 会持续分配/释放 GDI 句柄。程序只在 UI 线程访问，静态安全。
        /// </summary>
        private static Bitmap _measureBmp;
        private static Graphics _measureGfx;

        private static Graphics MeasureGraphics()
        {
            if (_measureGfx == null)
            {
                _measureBmp = new Bitmap(1, 1);
                _measureGfx = Graphics.FromImage(_measureBmp);
                _measureGfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
            return _measureGfx;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(Text)) return;
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            StringFormat sf = MakeSf();
            try
            {
                _spaceW = g.MeasureString(" ", Font).Width; // 默认格式，含空格自身宽度
                Brush b = GdiCache.Brush(ForeColor);
                float y = 0;
                string[] paras = Text.Split('\n');
                for (int p = 0; p < paras.Length; p++)
                {
                    List<Unit> units = Tokenize(paras[p]);
                    // 与 Recompute 同步留 2px 余量：两处必须同一宽度，否则测高与绘制换行不一致
                    List<List<Unit>> lines = Wrap(g, sf, units, Math.Max(0, Width - 2));
                    for (int li = 0; li < lines.Count; li++)
                    {
                        // 整行一次 DrawString：此前逐 token 绘制，GDI+ 默认 StringFormat
                        // 自带的两侧内边距逐 token 累加，中文被拉出"让 给 前 台 游 戏"式的大空隙。
                        // 换行仍用逐 token 的 Wrap（测量偏保守，只会提前换行、不会溢出右缘）。
                        g.DrawString(JoinLine(lines[li]), Font, b, 0, y, sf);
                        y += _lineHeight;
                    }
                }
            }
            finally
            {
                sf.Dispose();
            }
        }

        private float MeasureUnit(Graphics g, StringFormat sf, string s)
        {
            if (s.Length == 0) return 0;
            return g.MeasureString(s, Font, PointF.Empty, sf).Width;
        }

        private float NaturalGap(Unit a, Unit b)
        {
            return (a.Cjk && b.Cjk) ? 0 : _spaceW;
        }

        private List<List<Unit>> Wrap(Graphics g, StringFormat sf, List<Unit> units, int maxW)
        {
            List<List<Unit>> lines = new List<List<Unit>>();
            List<Unit> cur = new List<Unit>();
            float curW = 0;
            for (int i = 0; i < units.Count; i++)
            {
                float uw = MeasureUnit(g, sf, units[i].Text);
                if (cur.Count == 0)
                {
                    cur.Add(units[i]);
                    curW = uw;
                }
                else
                {
                    float gap = NaturalGap(units[i - 1], units[i]);
                    if (curW + gap + uw <= maxW)
                    {
                        cur.Add(units[i]);
                        curW += gap + uw;
                    }
                    else
                    {
                        lines.Add(cur);
                        cur = new List<Unit>();
                        cur.Add(units[i]);
                        curW = uw;
                    }
                }
            }
            if (cur.Count > 0) lines.Add(cur);
            return lines;
        }

        private struct Unit
        {
            public string Text;
            public bool Cjk;
            public bool Space;   // 该 unit 之前在原文里是否有空格（整行绘制时用于忠实还原）
        }

        private static List<Unit> Tokenize(string para)
        {
            List<Unit> list = new List<Unit>();
            if (string.IsNullOrEmpty(para)) return list;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool has = false;
            bool pendingSpace = false;
            for (int i = 0; i < para.Length; i++)
            {
                char c = para[i];
                if (c == ' ' || c == '\t')
                {
                    FlushLatin(sb, ref has, list, pendingSpace);
                    pendingSpace = true;
                    continue;
                }
                if (IsCjk(c))
                {
                    // 中文字符各自成词：可在任意字间断行
                    FlushLatin(sb, ref has, list, pendingSpace);
                    Unit u;
                    u.Text = c.ToString();
                    u.Space = pendingSpace;
                    u.Cjk = true;
                    pendingSpace = false;
                    list.Add(u);
                }
                else
                {
                    sb.Append(c);
                    has = true;
                }
            }
            FlushLatin(sb, ref has, list, pendingSpace);
            return list;
        }

        private static void FlushLatin(System.Text.StringBuilder sb, ref bool has, List<Unit> list, bool pendingSpace)
        {
            if (has && sb.Length > 0)
            {
                Unit u;
                u.Text = sb.ToString();
                u.Space = pendingSpace;
                u.Cjk = false;
                list.Add(u);
            }
            sb.Length = 0;
            has = false;
        }

        /// <summary>把一行 token 连回整行文本：只在原文有空格的位置还原空格，中文之间不留缝。</summary>
        private static string JoinLine(List<Unit> line)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0 && line[i].Space) sb.Append(' ');
                sb.Append(line[i].Text);
            }
            return sb.ToString();
        }

        private static bool IsCjk(char c)
        {
            if (c >= 0x3000 && c <= 0x30FF) return true;   // CJK 标点 / 假名
            if (c >= 0x3400 && c <= 0x4DBF) return true;   // 扩展 A
            if (c >= 0x4E00 && c <= 0x9FFF) return true;   // 统一表意文字
            if (c >= 0xF900 && c <= 0xFAFF) return true;   // 兼容表意
            if (c >= 0xFF00 && c <= 0xFFEF) return true;   // 全角
            return false;
        }

        private static StringFormat MakeSf()
        {
            // 用默认 StringFormat 而非 GenericTypographic：后者行距偏紧、且会裁掉字形顶部，
            // 段落阅读显压抑。默认行距更舒展透气，保留 NoWrap 与自定义逐行 Wrap 配合，NoClip 防裁切。
            StringFormat f = new StringFormat();
            f.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            return f;
        }
    }
}
