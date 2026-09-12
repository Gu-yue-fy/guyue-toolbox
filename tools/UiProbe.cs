using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.UI;

namespace GuyueBox.Test
{
    /// <summary>
    /// 界面探针：**不显示任何窗口**，通过 DrawToBitmap 离屏渲染每个页面，
    /// 并捕获所有未处理异常。完全无副作用、不干扰用户。
    /// </summary>
    internal static class UiProbe
    {
        private static readonly List<string> Errors = new List<string>();
        private static readonly List<string> Trace = new List<string>();

        private static bool FullMode;

        [STAThread]
        private static int Main(string[] args)
        {
            FullMode = args != null && args.Length > 0 && args[0] == "full";
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe");
            try { outDir = Path.GetFullPath(outDir); } catch { }
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Record("ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Record("UnhandledException", e.ExceptionObject as Exception);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm form = new MainForm();
            form.ClientSize = new Size(1280, 800);
            // 显示在屏幕外：Visible 状态正常、GDI 渲染正常，但用户无法点到
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            Rectangle vs = SystemInformation.VirtualScreen;
            form.Location = new Point(vs.Right + 400, vs.Bottom + 400);
            form.Show();
            Pump(2200);

            Trace.Add("主窗体已创建（屏幕外），位置 " + form.Location.X + "," + form.Location.Y +
                "，尺寸 " + form.Width + "x" + form.Height + "，IsDisposed=" + form.IsDisposed);

            // 动态枚举页面模块：目录加页后探针零改动，自动覆盖新页
            IList<PageModule> modules = form.Modules;
            string[] keys = new string[modules.Count];
            string[] names = new string[modules.Count];
            for (int i = 0; i < modules.Count; i++)
            {
                keys[i] = modules[i].Key;
                names[i] = (i + 1) + "-" + modules[i].Name;
            }
            Trace.Add("页面模块数（动态）： " + keys.Length);

            // 功能注册数量检查（装配数应与模块目录一致）
            try
            {
                Trace.Add("已注册功能总数：" + form.CountFeatures());
                if (form.CountFeatures() != modules.Count) Record("功能注册", new Exception("装配数应与模块目录一致 " + modules.Count + "，实际 " + form.CountFeatures()));
            }
            catch (Exception ex)
            {
                Record("功能注册", ex);
            }

            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                try
                {
                    form.NavigateTo(key);
                }
                catch (Exception ex)
                {
                    Record("NavigateTo(" + key + ")", ex);
                    continue;
                }

                if (!FullMode) Trace.Add("切换到 " + key + " 后立即：" + DescribeVisible(form));

                Pump(FullMode ? 2600 : 450);
                if (FullMode)
                {
                    Trace.Add("切换到 " + key + " 稳定后：" + DescribeVisible(form));
                    Trace.Add("        加载体检：" + InspectLayout(form));
                    Trace.Add("        数据体检：" + InspectData(form));
                }

                if (FullMode)
                {
                    try
                    {
                        Bitmap bmp = Capture(form);
                        string file = Path.Combine(outDir, names[i] + ".png");
                        bmp.Save(file, ImageFormat.Png);
                        bmp.Dispose();
                        Trace.Add("已截图 " + names[i]);
                    }
                    catch (Exception ex)
                    {
                        Record("Capture(" + key + ")", ex);
                    }
                }
            }

            if (FullMode)
            {
                // 小窗口布局检查（仅完整模式）
                try
                {
                    form.NavigateTo("dashboard");
                    form.ClientSize = new Size(960, 620);
                    form.PerformLayout();
                    Pump(1500);
                    Bitmap small = Capture(form);
                    small.Save(Path.Combine(outDir, "7-小窗口.png"), ImageFormat.Png);
                    small.Dispose();

                    form.NavigateTo("optimize");
                    Pump(1200);
                    Bitmap opt = Capture(form);
                    opt.Save(Path.Combine(outDir, "8-小窗口-优化.png"), ImageFormat.Png);
                    opt.Dispose();
                    Trace.Add("已截图 7/8 小窗口");
                }
                catch (Exception ex)
                {
                    Record("小窗口截图", ex);
                }

                // 像素级空白检测：切页 + 滚动后，内容区是否真的画出了东西
                TraceCoverage(form, keys, names);

                // 交互加载测试：垃圾清理的「开始扫描」
                TraceScanFlow(form);

                // 缩放后重排测试
                TraceResizeFlow(form);

                // 绘制性能基准：每页连续整屏绘制，取平均耗时
                Benchmark(form, keys, names);
            }
            else
            {
                Trace.Add("快速模式：跳过截图 / 像素覆盖 / 基准测试");
            }

            try { form.Dispose(); }
            catch (Exception ex) { Record("Dispose", ex); }

            WriteReport(outDir);
            return Errors.Count == 0 ? 0 : 2;
        }

        /// <summary>整屏绘制性能基准。DrawToBitmap 会走完整的 OnPaint 路径。</summary>
        private static void Benchmark(MainForm form, string[] keys, string[] names)
        {
            Trace.Add("");
            Trace.Add("---- 整屏绘制耗时（每页 12 次取样，单位 ms）----");

            using (Bitmap bmp = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
            {
                Rectangle rect = new Rectangle(0, 0, form.Width, form.Height);

                for (int i = 0; i < keys.Length; i++)
                {
                    form.NavigateTo(keys[i]);
                    Pump(1400);

                    double total = 0;
                    double worst = 0;
                    const int rounds = 12;
                    for (int r = 0; r < rounds; r++)
                    {
                        long t0 = DateTime.Now.Ticks;
                        form.DrawToBitmap(bmp, rect);
                        double ms = (DateTime.Now.Ticks - t0) / 10000.0;
                        total += ms;
                        if (ms > worst) worst = ms;
                    }

                    Trace.Add("  " + names[i].PadRight(16) + " 平均 " +
                        (total / rounds).ToString("0.0") + " ms   最慢 " + worst.ToString("0.0") + " ms");
                }
            }
        }

        /// <summary>检查当前可见页的滚动内容与布局是否正常。</summary>
        private static string InspectLayout(Form form)
        {
            try
            {
                System.Reflection.FieldInfo fiContent = typeof(MainForm).GetField("_content",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Control content = fiContent.GetValue(form) as Control;
                if (content == null) return "(无 _content)";

                Control view = null;
                for (int i = 0; i < content.Controls.Count; i++)
                {
                    if (content.Controls[i].Visible) { view = content.Controls[i]; break; }
                }
                if (view == null) return "(无可见页面)";

                // 找到页面里的 ScrollHost
                ScrollHost host = null;
                System.Reflection.FieldInfo[] fields = view.GetType().BaseType.GetFields(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (System.Reflection.FieldInfo f in fields)
                {
                    if (f.FieldType == typeof(ScrollHost))
                    {
                        host = f.GetValue(view) as ScrollHost;
                        break;
                    }
                }
                if (host == null) return "(找不到 ScrollHost)";

                Control body = host.Content;
                if (body == null) return "(内容为空)";

                int lastBottom = 0;
                int availW = body.ClientSize.Width - body.Padding.Left - body.Padding.Right;
                int badWidth = 0;
                for (int i = 0; i < body.Controls.Count; i++)
                {
                    Control c = body.Controls[i];
                    if (!c.Visible) continue;
                    if (c.Bottom + c.Margin.Bottom > lastBottom) lastBottom = c.Bottom + c.Margin.Bottom;
                    // 宽度体检：stretch 控件应铺满可用宽度（允许 8px 余量），防止行宽塌陷漏检
                    if (availW > 0 && (c.Tag as string) == "stretch" &&
                        Math.Abs(c.Width - availW) > 8) badWidth++;
                }

                string widthTag = badWidth > 0 ? "!! " + badWidth + " 个行宽塌陷 !!" : "宽度OK";
                return string.Format("视口={0} 内容高度={1} 期望≥{2} 子控件={3} 滚动位置={4} {5} {6}",
                    host.ViewportHeight, host.ContentHeight, lastBottom + body.Padding.Bottom,
                    body.Controls.Count, host.ScrollOffset,
                    host.ContentHeight < lastBottom ? "!! 内容被截断 !!" : "OK", widthTag);
            }
            catch (Exception ex)
            {
                return "(体检失败: " + ex.Message + ")";
            }
        }

        /// <summary>检查当前可见页的数据是否真的加载出来了。</summary>
        private static string InspectData(Form form)
        {
            try
            {
                System.Reflection.FieldInfo fiContent = typeof(MainForm).GetField("_content",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Control content = fiContent.GetValue(form) as Control;
                if (content == null) return "(无 _content)";

                Control view = null;
                for (int i = 0; i < content.Controls.Count; i++)
                {
                    if (content.Controls[i].Visible) { view = content.Controls[i]; break; }
                }
                if (view == null) return "(无可见页面)";

                StringBuilder sb = new StringBuilder();

                // 状态文本（页面副标题）
                System.Type viewType = view.GetType();
                while (viewType != null && viewType.Name != "ViewBase") viewType = viewType.BaseType;

                System.Reflection.FieldInfo fiSub = viewType == null ? null :
                    viewType.GetField("_subtitle",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fiSub != null)
                {
                    string sub = fiSub.GetValue(view) as string;
                    sb.Append("状态[").Append(sub == null ? "" : sub).Append("] ");
                }

                List<Control> all = new List<Control>();
                CollectDescendants(view, all);

                int gridCount = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    Control c = all[i];
                    if (c is DarkGrid)
                    {
                        DarkGrid g = (DarkGrid)c;
                        int n = g.VirtualMode ? g.RowCount : g.Rows.Count;
                        sb.Append("表格").Append(++gridCount).Append("=").Append(n).Append("行 ");
                    }
                    else if (c is StatCard)
                    {
                        StatCard s = (StatCard)c;
                        sb.Append(s.CaptionText).Append("=").Append(s.MetricText).Append(" ");
                    }
                    else if (c is InfoList)
                    {
                        sb.Append(((InfoList)c).Caption).Append("=")
                            .Append(((InfoList)c).RowCount).Append("项 ");
                    }
                    else if (c is DiskList)
                    {
                        sb.Append("磁盘=").Append(((DiskList)c).PreferredHeight).Append("px ");
                    }
                    else if (c is ToggleSwitch)
                    {
                        sb.Append("开关");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "(数据体检失败: " + ex.Message + ")";
            }
        }

        /// <summary>
        /// 统计内容区的"非背景色像素占比"，用来客观判断页面是不是空的。
        /// 同时模拟滚动到中间和底部，检查滚动后内容是否还在。
        /// </summary>
        private static void TraceCoverage(MainForm form, string[] keys, string[] names)
        {
            Trace.Add("");
            Trace.Add("---- 像素覆盖率检测（内容区非背景色占比，过低说明空白）----");

            using (Bitmap bmp = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
            {
                Rectangle rect = new Rectangle(0, 0, form.Width, form.Height);
                Rectangle area = new Rectangle(260, 120, Math.Max(10, form.Width - 300),
                    Math.Max(10, form.Height - 220));

                for (int i = 0; i < keys.Length; i++)
                {
                    form.NavigateTo(keys[i]);
                    Pump(2200);

                    form.DrawToBitmap(bmp, rect);
                    double top = Coverage(bmp, area);

                    // 滚到中间
                    ScrollHost host = FindHost(form);
                    double middle = top, bottom = top;
                    string pos = "（无需滚动）";
                    if (host != null && host.ContentHeight > host.ViewportHeight + 20)
                    {
                        // 负 delta = 向下滚（与滚轮方向一致）
                        int max = host.ContentHeight - host.ViewportHeight;

                        host.ScrollToTop();
                        Pump(120);
                        host.ScrollByWheel(-max / 2);
                        Pump(400);
                        form.DrawToBitmap(bmp, rect);
                        middle = Coverage(bmp, area);

                        host.ScrollByWheel(-max);
                        Pump(400);
                        form.DrawToBitmap(bmp, rect);
                        bottom = Coverage(bmp, area);

                        pos = "滚动位置 0→" + host.ScrollOffset + "/最大" + max;

                        host.ScrollToTop();
                        Pump(200);
                    }

                    string flag = (top < 6.0 || middle < 6.0 || bottom < 6.0) ? "  !! 疑似空白 !!" : "";
                    Trace.Add("  " + names[i].PadRight(16) +
                        " 顶部 " + top.ToString("0.0") + "%  中间 " + middle.ToString("0.0") +
                        "%  底部 " + bottom.ToString("0.0") + "%  " + pos + flag);
                }
            }
        }

        private static ScrollHost FindHost(Form form)
        {
            try
            {
                System.Reflection.FieldInfo fi = typeof(MainForm).GetField("_content",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Control content = fi.GetValue(form) as Control;
                Control view = null;
                for (int i = 0; i < content.Controls.Count; i++)
                {
                    if (content.Controls[i].Visible) { view = content.Controls[i]; break; }
                }
                if (view == null) return null;

                System.Type t = view.GetType();
                while (t != null)
                {
                    System.Reflection.FieldInfo f = t.GetField("_scroll",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (f != null) return f.GetValue(view) as ScrollHost;
                    t = t.BaseType;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>区域中与背景色差异明显的像素占比（%）。</summary>
        private static double Coverage(Bitmap bmp, Rectangle area)
        {
            int x0 = Math.Max(0, area.Left);
            int y0 = Math.Max(0, area.Top);
            int x1 = Math.Min(bmp.Width, area.Right);
            int y1 = Math.Min(bmp.Height, area.Bottom);
            if (x1 <= x0 || y1 <= y0) return 0;

            int diff = 0;
            int total = 0;

            for (int y = y0; y < y1; y += 3)
            {
                for (int x = x0; x < x1; x += 3)
                {
                    Color c = bmp.GetPixel(x, y);
                    total++;
                    int d = Math.Abs(c.R - 21) + Math.Abs(c.G - 23) + Math.Abs(c.B - 28);
                    if (d > 14) diff++;
                }
            }

            return total == 0 ? 0 : diff * 100.0 / total;
        }

        /// <summary>模拟点击「开始扫描」，检查扫描能否走完并回填结果。</summary>
        private static void TraceScanFlow(MainForm form)
        {
            Trace.Add("");
            Trace.Add("---- 扫描流程测试 ----");
            try
            {
                form.NavigateTo("clean");
                Pump(2000);

                System.Reflection.FieldInfo fiContent = typeof(MainForm).GetField("_content",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Control content = fiContent.GetValue(form) as Control;

                Control view = null;
                for (int i = 0; i < content.Controls.Count; i++)
                {
                    if (content.Controls[i].Visible) { view = content.Controls[i]; break; }
                }

                System.Reflection.MethodInfo mi = view.GetType().GetMethod("OnScanClick",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (mi == null)
                {
                    Trace.Add("  (找不到 OnScanClick)");
                    return;
                }

                long t0 = DateTime.Now.Ticks;
                mi.Invoke(view, new object[] { null, EventArgs.Empty });

                string state = "";
                int waited = 0;
                while (waited < 90000)
                {
                    Application.DoEvents();
                    Thread.Sleep(60);
                    waited += 60;

                    System.Type vt = view.GetType();
                    while (vt != null && vt.Name != "ViewBase") vt = vt.BaseType;
                    System.Reflection.FieldInfo fs = vt.GetField("_subtitle",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    state = fs.GetValue(view) as string;
                    if (state != null && (state.StartsWith("扫描完成") || state.StartsWith("正在取消"))) break;
                }

                double sec = (DateTime.Now.Ticks - t0) / 10000000.0;
                Trace.Add("  耗时 " + sec.ToString("0.0") + " 秒，状态：" + state);

                // 检查每行的空间列是否已回填
                List<Control> all = new List<Control>();
                CollectDescendants(view, all);
                for (int i = 0; i < all.Count; i++)
                {
                    DarkGrid g = all[i] as DarkGrid;
                    if (g == null) continue;
                    int pending = 0;
                    for (int r = 0; r < g.Rows.Count; r++)
                    {
                        object v = g.Rows[r].Cells[4].Value;
                        string s = v == null ? "" : v.ToString();
                        if (s == "未扫描" || s == "扫描中" || s == "…") pending++;
                    }
                    Trace.Add("  未回填行数 = " + pending + " / " + g.Rows.Count);
                }
            }
            catch (Exception ex)
            {
                Record("扫描流程", ex);
                Trace.Add("  扫描流程异常：" + ex.Message);
            }
        }

        /// <summary>改变窗口尺寸后，内容高度是否重新计算。</summary>
        private static void TraceResizeFlow(MainForm form)
        {
            Trace.Add("");
            Trace.Add("---- 缩放重排测试 ----");
            try
            {
                form.NavigateTo("optimize");
                Pump(1800);
                string before = InspectLayout(form);

                form.ClientSize = new Size(1040, 700);
                Pump(1200);
                string after = InspectLayout(form);

                Trace.Add("  缩放前：" + before);
                Trace.Add("  缩放后：" + after);

                form.ClientSize = new Size(1280, 800);
                Pump(800);
                Trace.Add("  还原后：" + InspectLayout(form));
            }
            catch (Exception ex)
            {
                Record("缩放重排", ex);
            }
        }

        private static void CollectDescendants(Control parent, List<Control> list)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                list.Add(c);
                if (c.Controls.Count > 0 && !(c is DarkGrid)) CollectDescendants(c, list);
            }
        }

        private static Bitmap Capture(Form form)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            return bmp;
        }

        private static string DescribeVisible(Form form)
        {
            try
            {
                System.Reflection.FieldInfo fi = typeof(MainForm).GetField("_content",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fi == null) return "(找不到 _content)";
                Control content = fi.GetValue(form) as Control;
                if (content == null) return "(content 为 null)";

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < content.Controls.Count; i++)
                {
                    Control c = content.Controls[i];
                    if (c.Visible) sb.Append(c.GetType().Name + " ");
                }
                if (sb.Length == 0) sb.Append("(无可见页面)");
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                return "(反射失败: " + ex.Message + ")";
            }
        }

        private static void WriteReport(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== UI 探针报告 =====");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("---- 执行轨迹 ----");
            for (int i = 0; i < Trace.Count; i++) sb.AppendLine("  " + Trace[i]);
            sb.AppendLine();
            sb.AppendLine("---- 捕获到的异常（" + Errors.Count + " 条）----");
            if (Errors.Count == 0) sb.AppendLine("  （无）");
            for (int i = 0; i < Errors.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine("[" + (i + 1) + "] " + Errors[i]);
            }

            string report = Path.Combine(outDir, "report.txt");
            File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
            Console.WriteLine(sb.ToString());
        }

        private static void Record(string where, Exception ex)
        {
            if (ex == null) return;
            string text = where + " :: " + ex.GetType().FullName + ": " + ex.Message;
            if (ex.InnerException != null)
            {
                text += " | 内部异常: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
            }
            text += Environment.NewLine + "      " + ex.StackTrace;
            if (!Errors.Contains(text)) Errors.Add(text);
        }

        private static void Pump(int ms)
        {
            int waited = 0;
            while (waited < ms)
            {
                Application.DoEvents();
                Thread.Sleep(40);
                waited += 40;
            }
        }
    }
}
