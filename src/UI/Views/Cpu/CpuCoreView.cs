// CPU 核心调度页：把进程绑定到指定核心或调整优先级（即时属性，不写注册表、无需还原）

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class CpuCoreView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly MetricBand _band = new MetricBand();

        private readonly SectionTitle _secProc = new SectionTitle();
        private readonly DarkGrid _grid = new DarkGrid();

        private readonly SectionTitle _secCore = new SectionTitle();
        private readonly CoreMatrix _matrix = new CoreMatrix();
        private readonly FlowLayoutPanel _presetRow = MakeRowFixed(34, 8);
        private readonly FlowLayoutPanel _prioRow = MakeRowFixed(34, Theme.GapSection);
        private readonly AccentButton[] _presetButtons = new AccentButton[7];
        private readonly AccentButton[] _prioButtons = new AccentButton[6];

        private readonly InfoList _state = new InfoList();

        private readonly ProcManager _procs = new ProcManager();
        private readonly SysInfo.CpuLoadMeter _meter = new SysInfo.CpuLoadMeter();

        // 预设档：全核 / 性能核 / 能效核 / NUMA0 / 前半 / 后半 / 单最快
        private readonly string[] _presetNames = new string[]
            { "全核", "性能核", "能效核", "NUMA 0", "前半核", "后半核", "单最快" };

        private int _targetPid = -1;
        private string _targetName = "";
        private int _prioLevel = 2;
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _loaded;

        public CpuCoreView()
            : base("CPU 核心调度", "把进程绑定到指定核心，或调整它的调度优先级")
        {
            _notice.NoticeIcon = "warn";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "亲和性与优先级是进程的即时属性，重启后由系统重置，不写注册表也不用还原。"
                + "普通使用无需修改；仅在需要把游戏/渲染进程固定到特定核心，或把后台进程赶到能效核时使用。";

            _secProc.TitleText = "目标进程";
            _secProc.HintText = "选中一行后，在下方为它设置核心与优先级";
            _secProc.Tone = Theme.Accent;

            _grid.Columns.Add("c0", "进程名");
            _grid.Columns.Add("c1", "PID");
            _grid.Columns.Add("c2", "CPU");
            _grid.Columns.Add("c3", "内存");
            _grid.Columns.Add("c4", "优先级");
            _grid.Columns[0].Width = 240;
            _grid.Columns[1].Width = 90;
            _grid.Columns[2].Width = 90;
            _grid.Columns[3].Width = 120;
            _grid.Columns[4].Width = 110;
            _grid.Height = 268;
            _grid.ClearSelection();
            _grid.SelectionChanged += delegate { OnRowSelected(); };

            _secCore.TitleText = "核心矩阵";
            _secCore.HintText = "点方格取消该核（蓝=性能核 / 青=能效核）；至少要保留一个核心";
            _secCore.Tone = Theme.Cyan;

            _matrix.SelectionChanged += delegate { RefreshCoreHint(); };

            for (int i = 0; i < _presetButtons.Length; i++)
            {
                AccentButton b = new AccentButton();
                b.Text = _presetNames[i];
                b.Variant = ButtonVariant.Secondary;
                b.Height = 30;
                b.NaturalWidth = 96;
                int index = i;
                b.Click += delegate { ApplyPreset(index); };
                _presetButtons[i] = b;
                _presetRow.Controls.Add(b);
            }

            for (int i = 0; i < _prioButtons.Length; i++)
            {
                AccentButton b = new AccentButton();
                b.Text = ProcManager.PriorityNames[i];
                b.Variant = i == _prioLevel ? ButtonVariant.Primary : ButtonVariant.Secondary;
                b.Height = 30;
                int level = i;
                b.Click += delegate { SelectPriority(level); };
                _prioButtons[i] = b;
                _prioRow.Controls.Add(b);
            }

            _state.Caption = "当前设置";
            _state.IconKind = "tune";
            _state.CaptionColor = Theme.Cyan;
            _state.EmptyText = "尚未选择进程——请在上方列表里点一行。";

            AddFull(_notice, 34, 12);
            AddFull(_band, MetricBand.CellMinHeight, Theme.GapSection);
            AddFull(_secProc, 44, 0);
            AddFull(_grid, 268, Theme.GapSection);
            AddFull(_secCore, 44, 0);
            // 多核时矩阵需要更高的纵向空间：按逻辑核数预给 4 行高度（约 46px/行）
            AddFull(_matrix, 4 * 46 + 10, Theme.GapTight);
            AddRow(_presetRow);
            AddRow(_prioRow);
            AddFull(_state, 46 + 3 * InfoList.RowHeight + 12, 0);

            // （强绑模式与持久化规则功能已按产品决策移除：
            //   后台每 3 秒扫描进程并自动改亲和性的行为过于侵入，普通用户感知不到收益，
            //   保留的"即时绑核 + 优先级"已覆盖手动调度需求。）

            AddAction("应用设置", "check", ButtonVariant.Primary, OnApply, 124);
            AddAction("恢复全核", "undo", ButtonVariant.Secondary, delegate
            {
                _matrix.SetSelectedGlobals(AllGlobalIndices());
                RefreshCoreHint();
            }, 120);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);

            _band.SetCells(new MetricBand.Cell[] {
                NewCell("CPU 负载", "--", "正在采样", "", Theme.Accent, true),
                NewCell("物理核心", "--", "核心数", "由系统报告", Theme.Purple, false),
                NewCell("逻辑核心", "--", "线程数", "可分派单元", Theme.Cyan, false),
                NewCell("处理器组", "--", "NUMA / 组", "跨组调度开销", Theme.Success, false),
                NewCell("架构", "--", "处理器类型", "", Theme.Warning, false)
            });
        }

        private static MetricBand.Cell NewCell(string label, string value, string status, string extra, Color tone, bool withBar)
        {
            MetricBand.Cell c = new MetricBand.Cell();
            c.Label = label;
            c.Value = value;
            c.Status = status;
            c.Extra = extra;
            c.Tone = tone;
            c.Percent = withBar ? 0 : -1;
            return c;
        }

        public override void OnActivated()
        {
            if (!_loaded) Load();
        }

        // ==============================================================
        // 加载
        // ==============================================================

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取 CPU 与进程信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemSnapshot snap = null;
                List<ProcInfo> procs = null;
                Dictionary<int, int> prios = null;
                CpuAffinityMask sysMask = null;
                try
                {
                    double load = _meter.Sample();
                    snap = SysInfo.Capture(true, load);
                    procs = _procs.Snapshot();
                    prios = CollectPriorities(procs, 40);
                    sysMask = AffinityApi.SystemAffinity();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    _loaded = true;
                    if (snap == null)
                    {
                        SetSubtitle("读取 CPU 信息失败。", Theme.Danger);
                        return;
                    }
                    Render(snap, procs, sysMask, prios);
                    SetSubtitle("已更新：共 " + (procs == null ? 0 : procs.Count) + " 个进程。", Theme.Success);
                });
            });
        }

        private static Dictionary<int, int> CollectPriorities(List<ProcInfo> procs, int topN)
        {
            Dictionary<int, int> map = new Dictionary<int, int>();
            if (procs == null || procs.Count == 0) return map;

            List<ProcInfo> sorted = new List<ProcInfo>(procs);
            sorted.Sort(delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); });
            int n = sorted.Count < topN ? sorted.Count : topN;
            for (int i = 0; i < n; i++)
            {
                try { map[sorted[i].Pid] = ProcManager.GetPriority(sorted[i].Pid); }
                catch { }
            }
            return map;
        }

        private void Render(SystemSnapshot snap, List<ProcInfo> procs, CpuAffinityMask sysMask, Dictionary<int, int> prios)
        {
            int logical = CpuTopologyEx.LogicalCount;
            int physical = snap.CpuCores > 0 ? snap.CpuCores : logical;
            double load = snap.CpuLoadPercent;
            bool hybrid = CpuTopologyEx.IsHybrid;
            int groups = CpuTopologyEx.GroupCount;

            _band.UpdateCell(0, load < 0 ? "--" : load.ToString("0") + "%", load, "当前占用", snap.CpuName);
            _band.SetTone(0, Gfx.LoadColor(load));
            _band.UpdateCell(1, physical.ToString(), -1, "物理核心",
                hybrid ? "大小核（异构）" : "同构");
            _band.UpdateCell(2, logical.ToString(), -1, "逻辑核心", "处理器组感知后可绑任意核");
            _band.UpdateCell(3, groups.ToString(), -1, "处理器组", groups > 1 ? "跨组机器：绑定可减少迁移" : "单组");
            _band.UpdateCell(4, hybrid ? "大小核" : "同构", -1, "处理器架构",
                hybrid ? "P 核 + E 核混合" : "所有核心同构");

            // ---- 进程列表（按内存取前 40 个，避免一屏塞入上千行）----
            _grid.Rows.Clear();
            if (procs != null && procs.Count > 0)
            {
                List<ProcInfo> sorted = new List<ProcInfo>(procs);
                sorted.Sort(delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); });

                int n = sorted.Count < 40 ? sorted.Count : 40;
                for (int i = 0; i < n; i++)
                {
                    ProcInfo p = sorted[i];
                    int prio = ProcManager.GetPriority(p.Pid);
                    int idx = _grid.Rows.Add(p.Name, p.Pid, p.CpuPercentText, SysInfo.FormatSize(p.WorkingSet),
                        prio >= 0 ? ProcManager.PriorityNames[prio] : "—");
                    _grid.Rows[idx].Tag = p;
                }
            }
            _grid.ClearSelection();

            // ---- 核心矩阵：按真实逻辑核心数初始化（不再钳制 64 核），并标出 P/E 核 ----
            bool[] perf = new bool[logical];
            IList<LogicalCore> order = CpuTopologyEx.Cores;
            for (int i = 0; i < order.Count && i < logical; i++) perf[i] = order[i].IsPerformanceCore;
            _matrix.SetCoreCount(logical);
            _matrix.SetCoreMeta(perf);
            _matrix.SetSelectedGlobals(AllGlobalIndices());
            RefreshCoreHint();

            _state.Set("目标进程", "未选择");
            _state.Set("当前亲和性", "—");
            _state.Set("当前优先级", "—");
        }

        /// <summary>返回 [0..logical-1] 全部全局序号（用于「全核」/初始化全选）。</summary>
        private List<int> AllGlobalIndices()
        {
            List<int> all = new List<int>();
            int n = CpuTopologyEx.LogicalCount;
            for (int i = 0; i < n; i++) all.Add(i);
            return all;
        }

        private void RefreshCoreHint()
        {
            _secCore.RightText = "已选 " + _matrix.SelectedCount + " / " + _matrix.CoreCount + " 核";
        }

        // ==============================================================
        // 选择目标进程
        // ==============================================================

        private void OnRowSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;

            ProcInfo p = _grid.SelectedRows[0].Tag as ProcInfo;
            if (p == null) return;

            _targetPid = p.Pid;
            _targetName = p.Name;

            int prio = ProcManager.GetPriority(p.Pid);
            if (prio >= 0) SelectPriority(prio);

            _state.Set("目标进程", p.Name + "（PID " + p.Pid + "）");
            _state.Set("当前亲和性", "读取中…");
            _state.Set("当前优先级", prio >= 0 ? ProcManager.PriorityNames[prio] : "—");

            int pid = p.Pid;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                CpuAffinityMask mask = AffinityApi.GetProcessAffinity(pid, out error);
                Post(delegate
                {
                    if (_targetPid != pid) return; // 期间又换了目标进程
                    if (mask.IsEmpty && error.Length > 0)
                    {
                        _state.Set("当前亲和性", error);
                    }
                    else
                    {
                        _state.Set("当前亲和性", MaskText(mask));
                        MaskToMatrix(mask);
                        RefreshCoreHint();
                    }
                });
            });
        }

        /// <summary>把 CpuAffinityMask 映射到核心矩阵的全局序号选择。</summary>
        private void MaskToMatrix(CpuAffinityMask mask)
        {
            _matrix.SetSelectedGlobals(mask.ToGlobalList(CpuTopologyEx.Cores));
        }

        /// <summary>把核心矩阵的当前选择合成 CpuAffinityMask。</summary>
        private CpuAffinityMask MatrixToMask()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            IList<LogicalCore> order = CpuTopologyEx.Cores;
            foreach (int g in _matrix.SelectedGlobals()) m.SetByGlobal(g, order);
            return m;
        }

        private static string MaskText(CpuAffinityMask mask)
        {
            List<int> on = mask.ToGlobalList(CpuTopologyEx.Cores);
            if (on.Count == 0) return "—";
            List<string> parts = new List<string>();
            foreach (int g in on) parts.Add("#" + (g + 1));
            if (parts.Count > 8) return parts.Count + " 个核心（" + parts[0] + " … " + parts[parts.Count - 1] + "）";
            return string.Join("、", parts.ToArray());
        }

        private void SelectPriority(int level)
        {
            _prioLevel = level;
            for (int i = 0; i < _prioButtons.Length; i++)
            {
                _prioButtons[i].Variant = i == level ? ButtonVariant.Primary : ButtonVariant.Secondary;
            }
        }

        private void ApplyPreset(int index)
        {
            if (index < 0 || index >= _presetButtons.Length) return;
            CpuAffinityMask m;
            switch (index)
            {
                case 0: m = AffinityPresets.All(); break;
                case 1: m = AffinityPresets.PerformanceCores(); break;
                case 2: m = AffinityPresets.EfficientCores(); break;
                case 3: m = AffinityPresets.NumaNode(0); break;
                case 4: m = AffinityPresets.FirstHalf(); break;
                case 5: m = AffinityPresets.SecondHalf(); break;
                default: m = AffinityPresets.SingleFastest(); break;
            }
            MaskToMatrix(m);
            RefreshCoreHint();
        }

        // ==============================================================
        // 应用
        // ==============================================================

        private void OnApply(object sender, EventArgs e)
        {
            if (_targetPid <= 0)
            {
                SetSubtitle("请先在上方列表中选择一个进程。", Theme.Warning);
                return;
            }

            int pid = _targetPid;
            string name = _targetName;
            CpuAffinityMask mask = MatrixToMask();
            int level = _prioLevel;

            if (mask.IsEmpty)
            {
                SetSubtitle("请至少在核心矩阵里保留一个核心。", Theme.Warning);
                return;
            }

            SetSubtitle("正在应用设置…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string errAff, errPrio;
                bool okAff = AffinityApi.SetProcessAffinity(pid, mask, out errAff);
                bool okPrio = ProcManager.SetPriority(pid, level, out errPrio);

                Post(delegate
                {
                    if (!okAff && !okPrio)
                    {
                        SetSubtitle("设置失败：" + (errAff.Length > 0 ? errAff : errPrio), Theme.Danger);
                        return;
                    }
                    string msg = name + "：";
                    msg += okAff ? "亲和性已设为 " + mask.CountCores() + " 核" : "亲和性设置失败（" + errAff + "）";
                    msg += "，";
                    msg += okPrio ? "优先级已设为「" + ProcManager.PriorityNames[level] + "」" : "优先级设置失败（" + errPrio + "）";
                    SetSubtitle(msg, okAff && okPrio ? Theme.Success : Theme.Warning);

                    _state.Set("当前亲和性", MaskText(mask));
                    _state.Set("当前优先级", ProcManager.PriorityNames[level]);
                });
            });
        }

    }
}
