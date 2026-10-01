using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 专门的跑分性能测试：一轮压力测试后换算为 0-100 标准化分项分与综合等级，
    /// 并和内置参考机型做横向对比。区别于「电源与性能」里的综合基准页（那页是原始指标 + 前后对比）。
    /// </summary>
    public sealed class PerfRunView : ViewBase
    {
        // -------- 分项得分行（自绘：标签 + 原始值 + 分数 + 条形） --------
        private sealed class MetricRow : Control
        {
            public string Label = "";
            public string Detail = "";
            public int Score;

            public MetricRow()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Height = 46;
                Tag = "stretch";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                int labelW = Width - 72;
                if (labelW < 40) labelW = 40;
                Gfx.DrawTextEllipsis(g, Label, Theme.FontBodyBold, Theme.TextPrimary, new Rectangle(0, 3, labelW, 18));
                Gfx.DrawTextEllipsis(g, Detail, Theme.FontSmall, Theme.TextMuted, new Rectangle(0, 23, labelW, 16));
                Gfx.DrawTextCenter(g, Score.ToString(), Theme.FontMetric, Tone(), new Rectangle(Width - 68, 0, 68, 30));

                int barY = 41;
                int barW = Width - 8;
                Gfx.FillRound(g, new Rectangle(0, barY, barW, 5), 2, Theme.CardBgAlt);
                int s = Score;
                if (s < 0) s = 0;
                if (s > 100) s = 100;
                int fill = (int)Math.Round(barW * s / 100.0);
                if (fill >= 2) Gfx.FillRound(g, new Rectangle(0, barY, fill, 5), 2, Tone());
            }

            private Color Tone()
            {
                if (Score >= 70) return Theme.Success;
                if (Score >= 36) return Theme.Accent;
                return Theme.Warning;
            }
        }

        // -------- 综合跑分环（自绘环形 + 中心数字） --------
        private sealed class ScoreRing : Control
        {
            public int Score;

            public ScoreRing()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Width = 196;
                Height = 196;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                int size = Math.Min(Width, Height) - 10;
                Rectangle ring = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                float th = size * 0.11f;
                int v = Score;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                double sweep = 360.0 * v / 100.0;
                Color c = Tone();
                Gfx.DrawRingArc(g, ring, th, sweep, c, c, true, true, 3.0);
                Gfx.DrawTextCenter(g, v.ToString(), Theme.FontMetric, c,
                    new Rectangle(ring.X, ring.Y + size / 2 - 26, size, 40));
                Gfx.DrawTextCenter(g, "综合跑分", Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(ring.X, ring.Y + size / 2 + 14, size, 18));
            }

            private Color Tone()
            {
                if (Score >= 70) return Theme.Success;
                if (Score >= 36) return Theme.Accent;
                return Theme.Warning;
            }
        }

        private readonly ScoreRing _ring = new ScoreRing();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly ProgressStrip _progress = new ProgressStrip();
        private readonly FlowLayoutPanel _scores = new FlowLayoutPanel();
        private readonly DarkGrid _compare = new DarkGrid();
        private readonly DarkGrid _history = new DarkGrid();
        private AccentButton _runButton;
        /// <summary>九项原始测试指标表（融合自原「性能测试」页：保留可核对的实测值）。</summary>
        private readonly DarkGrid _metrics = new DarkGrid();
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _loaded;
        private bool _cancel;

        public PerfRunView()
            : base("性能跑分", "一键跑分：标准化分数 · 性能等级 · 机型对比 · 原始指标 · 前后对比")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "一键跑分会对 CPU、内存、磁盘、加密、压缩与图形渲染做一轮压力测试，并换算为 0-100 标准化分数与综合等级（入门办公 / 主流家用 / 高性能游戏 / 高端工作站 / 发烧旗舰）。分数基于内置参考基准，用于横向比较与本机前后对比，测试约 15 秒。";

            _summary.Caption = "性能跑分";
            _summary.IconKind = "gauge";
            _summary.CaptionColor = Theme.Accent;

            _runButton = AddAction("开始跑分", "bolt", ButtonVariant.Primary, OnRunClick, 120);
            AddAction("前后对比", "gauge", ButtonVariant.Secondary, OnCompareClick, 130);
            AddAction("清空历史", "trash", ButtonVariant.Danger, OnClearClick, 120);

            BuildControls();
            BuildLayout();
        }

        private void BuildControls()
        {
            _progress.Visible = false;

            _scores.FlowDirection = FlowDirection.TopDown;
            _scores.WrapContents = false;
            _scores.AutoScroll = false;
            _scores.BackColor = Color.Transparent;

            _metrics.ReadOnly = true;
            _metrics.UseOwnScrollbar = true;
            _metrics.AddTextColumn("测试项", 180, false);
            _metrics.AddFillColumn("实测值", 240);

            _compare.ReadOnly = true;
            _compare.UseOwnScrollbar = true;
            _compare.AddTextColumn("机型", 150, false);
            _compare.AddTextColumn("综合跑分", 96, false);
            _compare.AddFillColumn("配置 / 定位", 220);

            _history.ReadOnly = true;
            _history.UseOwnScrollbar = true;
            _history.AddTextColumn("时间", 150, false);
            _history.AddTextColumn("综合跑分", 96, false);
            _history.AddTextColumn("CPU 多核", 104, false);
            _history.AddTextColumn("磁盘读", 100, false);
            _history.AddTextColumn("图形", 96, false);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 56, 12);

            TableLayoutPanel head = new TableLayoutPanel();
            head.ColumnCount = 2;
            head.RowCount = 1;
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));
            head.Controls.Add(_ring, 0, 0);
            head.Controls.Add(_summary, 1, 0);
            head.Tag = "stretch";
            _ring.Dock = DockStyle.Fill;
            _summary.Dock = DockStyle.Fill;
            AddFull(head, 196, 12);

            AddFull(_progress, 46, 8);

            AddFull(MakeSection("分项标准化得分（0-100）"), 22, 6);
            AddFull(_scores, 10 * 47, 12);

            AddFull(MakeSection("原始测试指标"), 22, 6);
            // DarkGrid 实际行高 32 + 表头 36：此前按行高 26 估算，10 行放不下 →
            // 表格内部出滚动条，看全部分项结果得靠滚轮。按真实行高给足即全量可见。
            AddFull(_metrics, 36 + 10 * 32 + 2, 12);

            AddFull(MakeSection("机型对比（估算基准）"), 22, 6);
            // 本机 + 4 档参考机型 = 5 行，同理给足高度，表内不再滚动
            AddFull(_compare, 36 + 5 * 32 + 2, 12);

            AddFull(MakeSection("历史跑分记录"), 22, 6);
            AddFull(_history, 180, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private Label MakeSection(string text)
        {
            Label lab = new Label();
            lab.AutoSize = false;
            lab.Height = 22;
            lab.Text = text;
            lab.Font = Theme.FontBodyBold;
            lab.ForeColor = Theme.TextSecondary;
            lab.TextAlign = ContentAlignment.MiddleLeft;
            return lab;
        }

        private void Relayout()
        {
            int sh = _summary.PreferredHeight;
            if (sh > 0) _summary.Height = sh;
            int w = _scores.ClientSize.Width;
            if (w > 0)
            {
                foreach (Control c in _scores.Controls) c.Width = w;
            }
            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (!_loaded)
            {
                _loaded = true;
                LoadCompare(0, "—");
                LoadHistory();
            }
        }

        private void LoadCompare(int total, string grade)
        {
            _compare.Rows.Clear();
            int idx = _compare.Rows.Add("本机", total.ToString(), grade);
            _compare.Rows[idx].Cells[0].Style.ForeColor = Theme.TextPrimary;
            _compare.Rows[idx].Cells[1].Style.ForeColor = Theme.Success;
            AddCmp("入门办公机", "30", "双核 / 集显 / 机械硬盘");
            AddCmp("主流家用机", "52", "四核 / SATA SSD");
            AddCmp("高性能游戏机", "73", "八核 / NVMe / 独显");
            AddCmp("高端工作站", "89", "十六核 / PCIe 4.0");
            _compare.ClearSelection();
        }

        private void AddCmp(string name, string score, string desc)
        {
            int idx = _compare.Rows.Add(name, score, desc);
            _compare.Rows[idx].Cells[1].Style.ForeColor = Theme.TextSecondary;
        }

        private void LoadHistory()
        {
            List<string[]> rows = ReadHistory();
            _history.Rows.Clear();
            int shown = Math.Min(8, rows.Count);
            for (int i = 0; i < shown; i++)
            {
                string[] f = rows[i];
                _history.Rows.Add(f[0], f[1], f[2], f[3], f[4]);
            }
            _history.ClearSelection();
        }

        private void OnRunClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _cancel = false;
            UpdateActions();

            _progress.Visible = true;
            _progress.Begin("正在跑分",
                new string[] { "CPU 单核…", "CPU 多核…", "内存读写…", "磁盘读写…", "AES 加密吞吐…", "GZip 压缩吞吐…", "磁盘 4K 随机读…", "图形渲染…" }, false);

            ThreadPool.QueueUserWorkItem(delegate
            {
                BenchmarkResult r = null;
                double gfxFps = 0;
                try
                {
                    r = Benchmark.Run(delegate (string s)
                    {
                        string st = s;
                        Post(delegate { _progress.SetPhaseByName(st, 60); SetSubtitle("正在测试：" + st, Theme.Warning); });
                    }, delegate { return _cancel; });

                    if (r == null)
                    {
                        Post(delegate { FinishCancel(); });
                        return;
                    }

                    Post(delegate { _progress.SetPhaseByName("图形渲染…", 60); SetSubtitle("图形渲染…", Theme.Warning); });
                    gfxFps = GraphicsFps();
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        _busy = false;
                        _progress.Finish();
                        SetSubtitle("测试失败：" + ex.Message, Theme.Danger);
                        UpdateActions();
                    });
                    return;
                }

                BenchmarkResult res = r;
                double gfx = gfxFps;
                Post(delegate { ShowResult(res, gfx); });
            });
        }

        private void FinishCancel()
        {
            _busy = false;
            _progress.Finish();
            SetSubtitle("已取消跑分。", Theme.TextSecondary);
            UpdateActions();
        }

        /// <summary>图形渲染压测：用 GDI+ 大量抗锯齿矢量绘制（圆 + 线），计时平均帧率。</summary>
        private static double GraphicsFps()
        {
            try
            {
                int w = 900, h = 600;
                Stopwatch sw = Stopwatch.StartNew();
                int frames = 0;
                while (sw.ElapsedMilliseconds < 1500)
                {
                    using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.FromArgb(20, 20, 30));
                        for (int i = 0; i < 400; i++)
                        {
                            int x = (i * 97) % w;
                            int y = (i * 131) % h;
                            int rad = 20 + (i % 40);
                            using (Brush br = new SolidBrush(Color.FromArgb(60, (i * 7) & 255, (i * 13) & 255, 255 - ((i * 5) & 255))))
                            {
                                g.FillEllipse(br, x, y, rad, rad);
                            }
                        }
                        using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
                        {
                            for (int i = 0; i < 200; i++)
                            {
                                int x1 = (i * 53) % w;
                                int y1 = (i * 71) % h;
                                int x2 = (i * 29) % w;
                                int y2 = (i * 17) % h;
                                g.DrawLine(p, x1, y1, x2, y2);
                            }
                        }
                    }
                    frames++;
                }
                sw.Stop();
                double sec = sw.Elapsed.TotalSeconds;
                if (sec <= 0) return 0;
                return frames / sec;
            }
            catch
            {
                return 0;
            }
        }

        private void ShowResult(BenchmarkResult r, double gfxFps)
        {
            int cores = r.CoreCount;
            if (cores <= 0) cores = Environment.ProcessorCount;

            int cpuS = Norm(r.CpuSingleMops, 2.8);
            int cpuM = Norm(r.CpuMultiMops, 2.8 * cores);
            int memR = Norm(r.MemReadMBs, 32000);
            int memW = Norm(r.MemWriteMBs, 22000);
            int diskR = Norm(r.DiskReadMBs, 3500);
            int diskW = Norm(r.DiskWriteMBs, 3000);
            int iops = Norm(r.Disk4kIops, 500000);
            int aes = Norm(r.AesMBs, 4000);
            int gzip = Norm(r.GzipMBs, 900);
            int gfx = 0;
            if (gfxFps > 0)
            {
                double avgMs = 1000.0 / gfxFps;
                gfx = Norm(10.0, avgMs); // 10ms/帧 为满分基准
            }

            double total = cpuS * 0.10 + cpuM * 0.22 + memR * 0.08 + memW * 0.06
                        + diskR * 0.12 + diskW * 0.10 + iops * 0.12 + aes * 0.06 + gzip * 0.04 + gfx * 0.10;
            int tot = (int)Math.Round(total);
            if (tot < 0) tot = 0;
            if (tot > 100) tot = 100;
            string grade = GradeFor(tot);

            _ring.Score = tot;
            _ring.Invalidate();

            _summary.Clear();
            _summary.Add("综合跑分", tot.ToString(), ToneFor(tot));
            _summary.Add("性能等级", grade, ToneFor(tot));
            _summary.Add("逻辑核心", cores.ToString() + " 核");
            _summary.Invalidate();

            _scores.Controls.Clear();
            AddScore("CPU 单核浮点", r.CpuSingleMops.ToString("0.00") + " M ops/s", cpuS);
            AddScore("CPU 多核浮点", r.CpuMultiMops.ToString("0.00") + " M ops/s", cpuM);
            AddScore("内存顺序读", r.MemReadMBs.ToString("0") + " MB/s", memR);
            AddScore("内存顺序写", r.MemWriteMBs.ToString("0") + " MB/s", memW);
            AddScore("磁盘顺序读", r.DiskReadMBs.ToString("0") + " MB/s", diskR);
            AddScore("磁盘顺序写", r.DiskWriteMBs.ToString("0") + " MB/s", diskW);
            AddScore("磁盘 4K 随机读", r.Disk4kIops.ToString("N0") + " IOPS", iops);
            AddScore("AES-256 加密", r.AesMBs.ToString("0") + " MB/s", aes);
            AddScore("GZip 压缩", r.GzipMBs.ToString("0") + " MB/s", gzip);
            AddScore("图形渲染", gfxFps.ToString("0.0") + " fps", gfx);
            Relayout();

            FillMetrics(r);
            LoadCompare(tot, grade);
            Benchmark.Save(r);          // 写入公共历史库，供「前后对比」取完整指标
            SaveHistory(r, tot, gfxFps);
            LoadHistory();

            _busy = false;
            _progress.Finish();
            SetSubtitle("跑分完成：综合 " + tot.ToString() + "（" + grade + "）", Theme.Success);
            UpdateActions();
        }

        private void AddScore(string label, string detail, int score)
        {
            MetricRow row = new MetricRow();
            row.Label = label;
            row.Detail = detail;
            row.Score = score;
            row.Width = _scores.ClientSize.Width;
            _scores.Controls.Add(row);
        }

        private static int Norm(double actual, double reference)
        {
            if (reference <= 0) return 0;
            double s = 100.0 * actual / reference;
            if (s < 0) s = 0;
            if (s > 100) s = 100;
            return (int)Math.Round(s);
        }

        private static string GradeFor(int total)
        {
            if (total >= 88) return "发烧旗舰";
            if (total >= 72) return "高端工作站";
            if (total >= 54) return "高性能游戏";
            if (total >= 36) return "主流家用";
            return "入门办公";
        }

        private static Color ToneFor(int total)
        {
            if (total >= 70) return Theme.Success;
            if (total >= 36) return Theme.Accent;
            return Theme.Warning;
        }

        // ---------------- 历史持久化 ----------------

        private static string HistPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GuyueBox", "speedrun.csv");
            }
        }

        private static void SaveHistory(BenchmarkResult r, int total, double gfxFps)
        {
            try
            {
                string dir = Path.GetDirectoryName(HistPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string line = string.Join("|",
                    r.When.ToString("yyyy-MM-dd HH:mm:ss"),
                    total.ToString(),
                    r.CpuMultiMops.ToString("0.00", CultureInfo.InvariantCulture),
                    r.DiskReadMBs.ToString("0", CultureInfo.InvariantCulture),
                    gfxFps.ToString("0.0", CultureInfo.InvariantCulture));
                File.AppendAllText(HistPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static List<string[]> ReadHistory()
        {
            List<string[]> list = new List<string[]>();
            try
            {
                if (!File.Exists(HistPath)) return list;
                string[] lines = File.ReadAllLines(HistPath);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    string[] f = line.Split('|');
                    if (f.Length < 5) continue;
                    list.Add(new string[] { f[0], f[1], f[2], f[3], f[4] });
                }
            }
            catch
            {
            }
            return list;
        }

        /// <summary>九项原始指标（融合自原「性能测试」页）：标准化分数之外，保留可核实的实测值。</summary>
        private void FillMetrics(BenchmarkResult r)
        {
            _metrics.Rows.Clear();
            _metrics.Rows.Add("CPU 单核（浮点）", r.CpuSingleMops.ToString("0.00") + " M ops/s");
            _metrics.Rows.Add("CPU 多核（浮点）", r.CpuMultiMops.ToString("0.00") + " M ops/s");
            _metrics.Rows.Add("内存顺序读", r.MemReadMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("内存顺序写", r.MemWriteMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("磁盘顺序写", r.DiskWriteMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("磁盘顺序读", r.DiskReadMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("AES-256 加密吞吐", r.AesMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("GZip 压缩吞吐", r.GzipMBs.ToString("0") + " MB/s");
            _metrics.Rows.Add("磁盘 4K 随机读", r.Disk4kIops.ToString("N0") + " IOPS");
            _metrics.Rows.Add("逻辑处理器", r.CoreCount.ToString() + " 核");
            _metrics.ClearSelection();
        }

        /// <summary>
        /// 前后对比（融合自原「性能测试」页）：把最近两次测试的九个子项与综合评分做 diff 与百分比变化，
        /// 回答"优化后到底哪一项变快了、变化多少"。不足两次记录时给出提示。
        /// </summary>
        private void OnCompareClick(object sender, EventArgs e)
        {
            List<BenchmarkResult> list = Benchmark.Load();
            if (list.Count < 2)
            {
                SetSubtitle("至少需要两次跑分记录才能对比（当前 " + list.Count + " 次）。", Theme.TextSecondary);
                Dialog.Info(this, "无法对比", "至少需要两次跑分记录。\r\n请再跑一次分，系统会自动保留历史用于前后对比。");
                return;
            }

            BenchmarkResult newer = list[list.Count - 1];
            BenchmarkResult older = list[list.Count - 2];

            string text = "综合评分：" + older.Score.ToString("N0") + " → " + newer.Score.ToString("N0") +
                "（" + Delta(older.Score, newer.Score) + "）\r\n\r\n";

            text += Line("CPU 单核", older.CpuSingleMops, newer.CpuSingleMops, "M ops/s", "0.00");
            text += Line("CPU 多核", older.CpuMultiMops, newer.CpuMultiMops, "M ops/s", "0.00");
            text += Line("内存读", older.MemReadMBs, newer.MemReadMBs, "MB/s", "0");
            text += Line("内存写", older.MemWriteMBs, newer.MemWriteMBs, "MB/s", "0");
            text += Line("磁盘读", older.DiskReadMBs, newer.DiskReadMBs, "MB/s", "0");
            text += Line("磁盘写", older.DiskWriteMBs, newer.DiskWriteMBs, "MB/s", "0");
            text += Line("AES 加密", older.AesMBs, newer.AesMBs, "MB/s", "0");
            text += Line("GZip 压缩", older.GzipMBs, newer.GzipMBs, "MB/s", "0");
            text += Line("4K 随机读", older.Disk4kIops, newer.Disk4kIops, "IOPS", "N0");

            Dialog.Info(this, "前后对比（最近两次跑分）", text);
        }

        private static string Line(string name, double oldVal, double newVal, string unit, string fmt)
        {
            string arrow = newVal >= oldVal ? "↑" : "↓";
            return name + "：" + oldVal.ToString(fmt) + " → " + newVal.ToString(fmt) + " " + unit +
                "  " + arrow + " " + Delta(oldVal, newVal) + "\r\n";
        }

        private static string Delta(double oldVal, double newVal)
        {
            if (oldVal == 0) return "—";
            double pct = (newVal - oldVal) * 100.0 / oldVal;
            return (pct >= 0 ? "+" : "") + pct.ToString("0.0") + "%";
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            if (!Dialog.ConfirmDanger(this, "清空跑分历史",
                "删除全部跑分记录。",
                "不可撤销：CSV 记录直接删除，没有备份。",
                "只影响本页的历史与对比基线；不影响已应用的优化项。",
                "清空", true))
                return;
            try { if (File.Exists(HistPath)) File.Delete(HistPath); }
            catch { }
            // 同时清掉公共历史库（%APPDATA%\GuyueBox\benchmarks.csv）：
            // 「前后对比」读的是它，只删本页 CSV 会让对比基线还在（确认框里承诺的是两者都清）
            try { Benchmark.ClearHistory(); }
            catch { }
            LoadHistory();
            SetSubtitle("跑分历史已清空。", Theme.Success);
        }

        private void UpdateActions()
        {
            _runButton.Enabled = !_busy;
            _runButton.Text = _busy ? "跑分中…" : "开始跑分";
        }
    }
}
