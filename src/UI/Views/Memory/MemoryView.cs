// 内存优化页：实时占用 / 一键释放 / 占用排行

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class MemoryView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly HeroCard _hero = new HeroCard();
        private readonly RankStrip _rank = new RankStrip();
        private readonly SectionTitle _secDetail = new SectionTitle();
        private readonly InfoList _detail = new InfoList();
        private readonly ComboBox _autoBox = new ComboBox();
        private readonly TextBox _warnBox = new TextBox();
        private readonly System.Windows.Forms.Timer _autoTimer;

        private readonly ProcManager _procs = new ProcManager();
        /// <summary>与排行行一一对应的进程（点行时据此定位）。</summary>
        private readonly List<ProcInfo> _topList = new List<ProcInfo>();

        private const int RankTop = 8;
        /// <summary>明细行数（页高在挂载前按此定稿）。</summary>
        private const int DetailRows = 8;

        private bool _busy;
        private bool _releasing;
        private bool _loaded;

        private int _autoMinutes;
        private int _warnPercent = 85;
        private DateTime _lastAuto = DateTime.MinValue;
        private bool _warned;   // 阈值提醒只弹一次，占用回落后复位

        public MemoryView()
            : base("内存优化", "实时占用 / 明细拆分 / 一键与定时释放 / 占用排行")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "释放只回收待机缓存与各进程的空闲工作集，不结束任何进程、不改系统设置；"
                + "正在被使用的内存不受影响，随后会自动回填。";

            _hero.TagIcon = "bolt";
            _hero.TagText = "即时清理工具";
            _hero.Headline = "一键释放被占用的内存";
            _hero.SubText = "回收待机列表与空闲工作集，实测释放量在完成后显示。";
            _hero.Gauge.CenterUnit = "%";
            _hero.Gauge.CenterText = "--";
            _hero.Gauge.StateText = "读取中";
            _hero.Action.Text = "立即清理";
            _hero.Action.IconKind = "bolt";
            _hero.Action.Click += OnReleaseClick;

            // 排行条自带标题与刷新按钮（设计 mem-top-strip 的写法），
            // 页面不再另加区块标题，避免出现两个「占用排行」
            _rank.TitleText = "占用排行";
            _rank.TitleHint = "TOP " + RankTop + " · 可点";
            _rank.RefreshClick += delegate { Load(); };
            _rank.RowClick += OnRankRowClick;

            _secDetail.TitleText = "内存明细";
            _secDetail.HintText = "物理内存 / 系统缓存 / 内核池 / 提交量";
            _secDetail.Tone = Theme.Success;

            _detail.Caption = "内存明细";
            _detail.IconKind = "memory";
            _detail.CaptionColor = Theme.Success;
            _detail.EmptyText = "正在读取内存信息…";

            // 自动释放 / 阈值提醒（两项都持久化在 HKCU\Software\GuyueBox\Settings）
            _autoMinutes = AppSettings.MemAutoReleaseMinutes;
            _warnPercent = AppSettings.MemWarnPercent;

            AddFull(_notice, 34, 12);
            AddFull(_hero, 212, Theme.GapSection);
            // 高度在这里一次定稿（排行条按最大行数、明细卡按固定行数）：
            // 行布局要求"行高在挂载前定稿"，取到数据后再改高会与其后区块错位。
            AddFull(_rank, RankStrip.HeightFor(RankTop), Theme.GapSection);
            AddRow(BuildAutoRow());
            AddFull(_secDetail, 44, 0);
            AddFull(_detail, 46 + DetailRows * InfoList.RowHeight + 12, 0);

            AddAction("一键释放", "bolt", ButtonVariant.Primary, OnReleaseClick, 124);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);

            // 定时释放：60 秒检查一次"距上次自动释放是否已达间隔"，页面不可见时直接跳过
            _autoTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            _autoTimer.Tick += OnAutoTick;
            _autoTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _autoTimer != null)
            {
                _autoTimer.Stop();
                _autoTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// 把忙碌状态暴露给基类：BusyOverlay 遮罩、状态栏忙碌指示与 UI 探针的
        /// 「等到不忙再截图」都依赖它（读取内存/进程、释放内存都是异步的）。
        /// </summary>
        public override bool IsBusy
        {
            get { return _busy || _releasing; }
        }

        public override void OnActivated()
        {
            // 首次进入才读数据；每次切回都重扫进程代价太大
            if (!_loaded) Load();
        }

        // ==============================================================
        // 工具行：自动释放间隔 + 占用提醒阈值
        // ==============================================================

        private FlowLayoutPanel BuildAutoRow()
        {
            FlowLayoutPanel row = MakeRowFixed(34, 10);
            row.Controls.Add(MakeLabel("自动释放", 64, true));

            _autoBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoBox.Size = new Size(120, 30);
            _autoBox.Items.Add("关闭");
            _autoBox.Items.Add("每 15 分钟");
            _autoBox.Items.Add("每 30 分钟");
            _autoBox.Items.Add("每 60 分钟");
            _autoBox.SelectedIndex = IndexOfMinutes(_autoMinutes);
            _autoBox.SelectedIndexChanged += delegate { ApplyAutoSelection(); };
            _autoBox.Margin = new Padding(0, 2, 12, 0);
            row.Controls.Add(_autoBox);

            row.Controls.Add(MakeLabel("占用阈值", 76, true));
            _warnBox.BorderStyle = BorderStyle.FixedSingle;
            _warnBox.Font = Theme.FontBody;
            _warnBox.Size = new Size(52, 30);
            _warnBox.Text = _warnPercent.ToString();
            _warnBox.Margin = new Padding(0, 2, 4, 0);
            _warnBox.Leave += delegate { ApplyWarnThreshold(); };
            _warnBox.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyWarnThreshold(); }
            };
            row.Controls.Add(ThemeInput.Wrap(_warnBox));
            row.Controls.Add(MakeLabel("%", 22, false));

            row.Controls.Add(MakeLabel("超阈值时在本页提醒（0 = 关闭）", 260, false));
            return row;
        }

        private static int IndexOfMinutes(int minutes)
        {
            if (minutes >= 60) return 3;
            if (minutes >= 30) return 2;
            if (minutes >= 15) return 1;
            return 0;
        }

        private static int MinutesOfIndex(int index)
        {
            switch (index)
            {
                case 1: return 15;
                case 2: return 30;
                case 3: return 60;
                default: return 0;
            }
        }

        private void ApplyAutoSelection()
        {
            int minutes = MinutesOfIndex(_autoBox.SelectedIndex);
            if (minutes == _autoMinutes) return;
            _autoMinutes = minutes;
            _lastAuto = DateTime.Now;   // 从改动的这一刻重新计时，避免刚开就立刻释放
            AppSettings.MemAutoReleaseMinutes = minutes;
            SetSubtitle(minutes > 0
                ? "已开启定时释放：每 " + minutes + " 分钟自动整理一次（仅在本页可见时执行）。"
                : "已关闭定时释放。",
                Theme.TextSecondary);
        }

        private void ApplyWarnThreshold()
        {
            int v;
            if (!int.TryParse(_warnBox.Text.Trim(), out v) || v < 0 || v > 99)
            {
                _warnBox.Text = _warnPercent.ToString();
                Dialog.Info(this, "数值无效", "请输入 0 – 99 之间的百分比（0 表示关闭占用提醒）。");
                return;
            }
            if (v == _warnPercent) return;
            _warnPercent = v;
            _warned = false;
            AppSettings.MemWarnPercent = v;
            SetSubtitle(v > 0 ? "占用超过 " + v + "% 时会在本页提醒。" : "已关闭占用提醒。", Theme.TextSecondary);
        }

        private static Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = bold ? Theme.TextPrimary : Theme.TextMuted;
            l.Font = bold ? Theme.FontBodyBold : Theme.FontBody;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Size = new Size(width, 30);
            l.Margin = new Padding(0, 0, 8, 0);
            return l;
        }

        // ==============================================================
        // 数据加载
        // ==============================================================

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取内存信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                MemoryInfo m = null;
                MemoryPerf perf = null;
                List<ProcInfo> procs = null;
                try
                {
                    m = SysInfo.GetMemory();
                    perf = SysInfo.GetMemoryPerf();
                    procs = _procs.Snapshot();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    if (m == null)
                    {
                        SetSubtitle("读取内存信息失败。", Theme.Danger);
                        return;
                    }
                    _loaded = true;
                    bool warned = Render(m, perf, procs);
                    if (!warned) SetSubtitle("内存信息已更新。", Theme.Success);
                });
            });
        }

        /// <summary>渲染全部区块；返回是否命中占用阈值（命中时副标题由调用点让位给警示）。</summary>
        private bool Render(MemoryInfo m, MemoryPerf perf, List<ProcInfo> procs)
        {
            double pct = m.UsedPercent;

            _hero.Gauge.Percent = pct;
            _hero.Gauge.Tone = Gfx.LoadColor(pct);
            _hero.Gauge.ToneTo = pct >= 70 ? Gfx.Shade(Gfx.LoadColor(pct), 1.25) : Theme.Cyan;
            _hero.Gauge.CenterText = pct.ToString("0");
            _hero.Gauge.SubText = SysInfo.FormatSize(m.UsedBytes) + " / " + SysInfo.FormatSize(m.TotalBytes);
            _hero.Gauge.StateText = pct >= 90 ? "内存紧张" : (pct >= 75 ? "占用偏高" : "运行良好");

            _hero.SetStats(
                new string[] { "可用", "页面文件", "提交上限" },
                new string[] {
                    SysInfo.FormatSize(m.AvailBytes),
                    m.PageFileAvail > 0 ? SysInfo.FormatSize(m.PageFileTotal - m.PageFileAvail) : "--",
                    SysInfo.FormatSize(m.PageFileTotal)
                });

            // ---- 占用排行（同时维护"行号 → 进程"映射，供点击联动）----
            List<ProcInfo> top = null;
            _topList.Clear();
            if (procs != null && procs.Count > 0)
            {
                List<ProcInfo> sorted = new List<ProcInfo>(procs);
                sorted.Sort(delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); });

                long max = sorted[0].WorkingSet;
                if (max <= 0) max = 1;

                top = new List<ProcInfo>();
                int n = sorted.Count < RankTop ? sorted.Count : RankTop;
                for (int i = 0; i < n; i++) top.Add(sorted[i]);

                List<RankStrip.Row> rows = new List<RankStrip.Row>();
                for (int i = 0; i < top.Count; i++)
                {
                    ProcInfo p = top[i];
                    RankStrip.Row r = new RankStrip.Row();
                    r.Rank = i + 1;
                    r.Icon = IconOf(p.Name);
                    r.Name = p.Name + "（" + p.Pid + "）";
                    r.Value = SysInfo.FormatSize(p.WorkingSet);
                    r.Percent = (double)p.WorkingSet * 100.0 / max;
                    r.Tone = i == 0 ? Theme.Danger : (i < 3 ? Theme.Warning : Theme.Accent);
                    rows.Add(r);
                    _topList.Add(p);
                }
                _rank.SetRows(rows);
            }
            else
            {
                _rank.SetRows(null);
            }

            // ---- 明细（物理 / 缓存 / 内核池 / 提交 / 页面文件 / 进程线程句柄）----
            _detail.Clear();
            _detail.Add("物理内存总量", SysInfo.FormatSize(m.TotalBytes));
            _detail.Add("已用", SysInfo.FormatSize(m.UsedBytes) + "（" + pct.ToString("0.0") + "%）");
            _detail.Add("可用", SysInfo.FormatSize(m.AvailBytes));
            _detail.Add("系统缓存", perf != null && perf.SystemCache > 0
                ? SysInfo.FormatSize(perf.SystemCache) + "（释放主要回收这部分）"
                : "未读取到");
            _detail.Add("内核内存池", perf != null && (perf.KernelPaged > 0 || perf.KernelNonpaged > 0)
                ? "分页 " + SysInfo.FormatSize(perf.KernelPaged) + " / 非分页 " + SysInfo.FormatSize(perf.KernelNonpaged)
                : "未读取到");
            _detail.Add("已提交 / 上限", perf != null && perf.CommitLimit > 0
                ? SysInfo.FormatSize(perf.CommitTotal) + " / " + SysInfo.FormatSize(perf.CommitLimit)
                : "未读取到");
            _detail.Add("页面文件", m.PageFileTotal > 0
                ? SysInfo.FormatSize(m.PageFileTotal - m.PageFileAvail) + " 已用 / " + SysInfo.FormatSize(m.PageFileTotal) + " 总量"
                : "未启用或读取失败");
            _detail.Add("进程 / 线程 / 句柄", perf != null && perf.ProcessCount > 0
                ? perf.ProcessCount + " / " + perf.ThreadCount + " / " + perf.HandleCount
                : "未读取到");
            _detail.Invalidate();

            // ---- 阈值提醒（只弹一次；回落到阈值以下 5 个百分点后复位）----
            bool warned = false;
            if (_warnPercent > 0 && pct >= _warnPercent)
            {
                warned = true;
                if (!_warned)
                {
                    _warned = true;
                    SetSubtitle("内存占用 " + pct.ToString("0") + "% 已达提醒阈值（" + _warnPercent + "%），建议一键释放。", Theme.Danger);
                    Toast("内存占用偏高",
                        "当前 " + pct.ToString("0") + "% ≥ 阈值 " + _warnPercent + "%；可点「一键释放」回收缓存。",
                        ToastKind.Danger);
                }
            }
            else if (pct < _warnPercent - 5)
            {
                _warned = false;
            }
            return warned;
        }

        /// <summary>按进程名的用途给出图标名（用于排行行的图标块）。</summary>
        private static string IconOf(string name)
        {
            string n = (name == null ? "" : name).ToLowerInvariant();
            if (n.IndexOf("chrome") >= 0 || n.IndexOf("edge") >= 0 || n.IndexOf("firefox") >= 0) return "globe";
            if (n.IndexOf("code") >= 0 || n.IndexOf("devenv") >= 0) return "tune";
            if (n.IndexOf("explorer") >= 0 || n.IndexOf("dwm") >= 0) return "apps";
            if (n.IndexOf("svchost") >= 0 || n.IndexOf("service") >= 0) return "services";
            if (n.IndexOf("game") >= 0 || n.IndexOf("steam") >= 0) return "play";
            return "process";
        }

        // ==============================================================
        // 排行行点击：查看该进程内存详情 / 跳进程管理页
        // ==============================================================

        private void OnRankRowClick(object sender, EventArgs e)
        {
            int idx = _rank.SelectedIndex;
            if (idx < 0 || idx >= _topList.Count) return;
            ProcInfo p = _topList[idx];

            string action = Chooser.ChooseOne(this, p.Name + "（PID " + p.Pid + "）",
                new List<string> { "查看内存详情", "去进程管理页", "取消" });
            if (action == null || action == "取消") return;

            if (action == "去进程管理页")
            {
                MainForm mf = MainForm.Current;
                if (mf != null) mf.NavigateTo("process");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("进程：" + p.Name);
            sb.AppendLine("PID：" + p.Pid);
            sb.AppendLine("工作集（物理内存）：" + SysInfo.FormatSize(p.WorkingSet));
            sb.AppendLine("私有内存：" + SysInfo.FormatSize(p.PrivateBytes));
            sb.AppendLine("CPU 占用：" + p.CpuPercentText);
            if (p.CpuTime > TimeSpan.Zero) sb.AppendLine("累计 CPU 时间：" + p.CpuTime.ToString(@"hh\:mm\:ss"));
            sb.AppendLine("路径：" + (string.IsNullOrEmpty(p.Path) ? "—" : p.Path));
            if (!string.IsNullOrEmpty(p.Description)) sb.AppendLine("说明：" + p.Description);
            sb.AppendLine("响应状态：" + (p.Responding ? "正常" : "无响应"));
            sb.AppendLine();
            sb.AppendLine("需要结束该进程时请到「进程管理」页操作（那里会二次确认并显示占用）。");
            Dialog.Output(this, "进程内存详情", sb.ToString());
        }

        // ==============================================================
        // 释放（手动 / 定时）
        // ==============================================================

        private void OnReleaseClick(object sender, EventArgs e)
        {
            DoRelease(false);
        }

        private void OnAutoTick(object sender, EventArgs e)
        {
            if (_autoMinutes <= 0 || _releasing || _busy) return;
            if (!Visible) return;   // 页面不在前台时不做任何事
            if ((DateTime.Now - _lastAuto).TotalMinutes < _autoMinutes) return;
            _lastAuto = DateTime.Now;
            DoRelease(true);
        }

        private void DoRelease(bool auto)
        {
            if (_releasing) return;
            _releasing = true;
            _hero.Action.Enabled = false;
            SetSubtitle(auto ? "正在定时自动释放内存…" : "正在释放内存，请稍候…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                long before = 0;
                long after = 0;
                int touched = 0;
                string error = null;
                try
                {
                    touched = ProcManager.ReleaseMemory(out before, out after);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    _releasing = false;
                    _hero.Action.Enabled = true;

                    if (error != null)
                    {
                        SetSubtitle("释放失败：" + error, Theme.Danger);
                        return;
                    }

                    double freedMb = (after - before) / 1048576.0;
                    _lastAuto = DateTime.Now;
                    SetSubtitle((auto ? "定时释放完成：" : "") + (freedMb > 0
                        ? "已释放 " + freedMb.ToString("0.0") + " MB（处理 " + touched + " 个进程）。"
                        : "已整理 " + touched + " 个进程的工作集，当前没有可回收的额外内存。"),
                        Theme.Success);

                    // 释放是"看得见结果"的操作：把释放量与下一步一起播报
                    Toast(freedMb > 0 ? (auto ? "定时释放 " : "已释放 ") + freedMb.ToString("0.0") + " MB" : "内存已整理",
                        "处理了 " + touched + " 个进程的工作集，占用量会被系统按需重新填充。",
                        freedMb > 0 ? ToastKind.Success : ToastKind.Info);
                    Load();
                });
            });
        }
    }
}
