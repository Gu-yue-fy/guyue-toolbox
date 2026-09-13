using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class BenchmarkView : ViewBase
    {
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly DarkGrid _metrics = new DarkGrid();
        private readonly DarkGrid _history = new DarkGrid();
        private readonly Label _score = new Label();
        private readonly Label _hint = new Label();

        private bool _busy;
        private bool _loaded;
        private AccentButton _runButton;

        public BenchmarkView()
            : base("性能基准", "量化 CPU / 内存 / 磁盘性能，对比优化前后")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "九项基准测试：CPU 单核/多核浮点、内存顺序读/写、磁盘顺序写/读、AES-256 加密吞吐、GZip 压缩吞吐、磁盘 4K 随机读（IOPS），并给出综合评分。测试约 15 秒，临时文件自动删除。历史记录即本机排行榜（按综合评分排名）。";

            _summary.Caption = "性能基准";
            _summary.IconKind = "gauge";
            _summary.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { LoadHistory(); }, 92);
            _runButton = AddAction("开始测试", "bolt", ButtonVariant.Primary, OnRunClick, 116);
            AddAction("清空历史", "trash", ButtonVariant.Ghost, OnClearClick, 116);

            BuildControls();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildControls()
        {
            _score.AutoSize = false;
            _score.Height = 46;
            _score.Font = Theme.FontMetric;
            _score.ForeColor = Theme.TextPrimary;
            _score.Text = "综合评分 —";
            _score.TextAlign = ContentAlignment.MiddleLeft;

            _hint.AutoSize = false;
            _hint.Height = 20;
            _hint.Font = Theme.FontSmall;
            _hint.ForeColor = Theme.TextMuted;
            _hint.Text = "评分与子项均为相对值：越大越快。";

            _metrics.ReadOnly = true;
            _metrics.UseOwnScrollbar = true;
            _metrics.AddFillColumn("测试项目", 220);
            _metrics.AddTextColumn("数值", 180, false);

            _history.ReadOnly = true;
            _history.UseOwnScrollbar = true;
            _history.AddTextColumn("排名", 50, false);
            _history.AddFillColumn("时间", 160);
            _history.AddTextColumn("综合评分", 96, false);
            _history.AddTextColumn("CPU 多核", 104, false);
            _history.AddTextColumn("磁盘读", 96, false);
            _history.AddTextColumn("AES", 92, false);
            _history.AddTextColumn("4K 随机", 92, false);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_score, 46, 0);
            AddFull(_hint, 20, 10);

            Label m1 = MakeSection("本次结果");
            AddFull(m1, 22, 6);
            AddFull(_metrics, 190, 12);

            Label m2 = MakeSection("历史记录");
            AddFull(m2, 22, 6);
            AddFull(_history, 220, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private Label MakeSection(string text)
        {
            Label lab = new Label();
            lab.AutoSize = false;
            lab.Text = text;
            lab.Font = Theme.FontBodyBold;
            lab.ForeColor = Theme.TextSecondary;
            lab.TextAlign = ContentAlignment.MiddleLeft;
            return lab;
        }

        private void Relayout()
        {
            int summaryH = _summary.PreferredHeight;
            _summary.Height = summaryH;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryH;
            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (!_loaded)
            {
                _loaded = true;
                LoadHistory();
            }
        }

        private void LoadHistory()
        {
            List<BenchmarkResult> list = Benchmark.Load();
            // 排行榜：按综合评分降序，前三名高亮
            List<BenchmarkResult> ranked = new List<BenchmarkResult>(list);
            ranked.Sort(delegate (BenchmarkResult a, BenchmarkResult b) { return b.Score.CompareTo(a.Score); });

            _history.Rows.Clear();
            int shown = Math.Min(15, ranked.Count);
            for (int i = 0; i < shown; i++)
            {
                BenchmarkResult r = ranked[i];
                int idx = _history.Rows.Add(
                    "#" + (i + 1),
                    r.When.ToString("MM-dd HH:mm:ss"),
                    r.Score.ToString("N0"),
                    r.CpuMultiMops.ToString("0.00") + " M",
                    r.DiskReadMBs.ToString("0") + " MB/s",
                    r.AesMBs > 0 ? r.AesMBs.ToString("0") + " MB/s" : "—",
                    r.Disk4kIops > 0 ? r.Disk4kIops.ToString("N0") + " IOPS" : "—");
                if (i < 3) _history.Rows[idx].Cells[2].Style.ForeColor = i == 0 ? Theme.Success : Theme.Warning;
            }
            _history.ClearSelection();
            _summary.Clear();
            _summary.Add("总测试次数", list.Count + " 次");
            if (list.Count > 0) _summary.Add("最高评分", MaxScore(list).ToString("N0"), Theme.Success);
            _summary.Invalidate();
            SetSubtitle(list.Count > 0
                ? "排行榜显示综合评分最高的前 " + shown + " 次测试。"
                : "暂无历史记录，点击「开始测试」。", Theme.TextSecondary);
        }

        private static int MaxScore(List<BenchmarkResult> list)
        {
            int m = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Score > m) m = list[i].Score;
            return m;
        }

        private void OnRunClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("准备基准测试…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                BenchmarkResult r = null;
                string lastStatus = "";
                try
                {
                    r = Benchmark.Run(delegate (string s)
                    {
                        lastStatus = s;
                        Post(delegate { SetSubtitle("正在测试：" + s, Theme.Warning); });
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        _busy = false;
                        SetSubtitle("测试失败：" + ex.Message, Theme.Danger);
                        UpdateActions();
                    });
                    return;
                }

                BenchmarkResult result = r;
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (result != null) ShowResult(result);
                });
            });
        }

        private void ShowResult(BenchmarkResult r)
        {
            _score.Text = "综合评分 " + r.Score.ToString("N0");
            _score.ForeColor = Theme.Success;

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

            SetSubtitle("测试完成，评分 " + r.Score.ToString("N0") + "（" + r.When.ToString("HH:mm:ss") + "）。", Theme.Success);
            LoadHistory();
        }

        private void OnClearClick(object sender, EventArgs e)
        {
            if (!Dialog.Confirm(this, "清空历史", "确定清空全部基准测试历史记录吗？此操作不可撤销。"))
                return;
            Benchmark.ClearHistory();
            LoadHistory();
            SetSubtitle("历史记录已清空。", Theme.Success);
        }

        private void UpdateActions()
        {
            _runButton.Enabled = !_busy;
        }

        private void Post(ThreadStart action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke((MethodInvoker)delegate { action(); });
                }
            }
            catch
            {
            }
        }
    }
}
