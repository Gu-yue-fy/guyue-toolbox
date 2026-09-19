/* ============================================================
 * 文件说明：CPU 核心调度页。按设计「CPU 核心管理」画面组织：
 *           顶部 CPU 信息带 + 目标进程列表 + 核心矩阵（亲和性）+ 快速预设与优先级。
 *           设置的是进程的即时属性（亲和性 / 优先级）：进程重启后由系统重置，
 *           不写注册表、不产生备份，因此也不需要"还原"。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

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
        private readonly AccentButton[] _presetButtons = new AccentButton[4];
        private readonly AccentButton[] _prioButtons = new AccentButton[6];

        private readonly InfoList _state = new InfoList();

        private readonly ProcManager _procs = new ProcManager();
        private readonly SysInfo.CpuLoadMeter _meter = new SysInfo.CpuLoadMeter();

        private readonly int[] _presetCores = new int[] { 0, 4, 8, 1 }; // 0 = 全核
        private readonly string[] _presetNames = new string[] { "全核", "前 4 核", "前 8 核", "单核" };

        private int _targetPid = -1;
        private string _targetName = "";
        private int _prioLevel = 2;
        private ulong _systemMask;
        private bool _busy;
        private bool _loaded;

        public CpuCoreView()
            : base("CPU 核心调度", "把进程绑定到指定核心，或调整它的调度优先级")
        {
            _notice.NoticeIcon = "warn";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "亲和性与优先级是进程的即时属性，重启后由系统重置，不写注册表也不用还原。"
                + "普通使用无需修改；仅在需要把游戏/渲染进程固定到特定核心时使用。";

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
            // 不要设 Dock：Dock=Top 会让表格脱离行布局、压住其后的区块
            // （行布局只按 Tag="stretch" 定宽，高度由 AddFull 指定）
            _grid.Height = 268;
            _grid.ClearSelection();
            _grid.SelectionChanged += delegate { OnRowSelected(); };

            _secCore.TitleText = "核心矩阵";
            _secCore.HintText = "点方格取消该核；至少要保留一个核心";
            _secCore.Tone = Theme.Cyan;

            _matrix.SelectionChanged += delegate { RefreshCoreHint(); };

            for (int i = 0; i < _presetButtons.Length; i++)
            {
                AccentButton b = new AccentButton();
                b.Text = _presetNames[i];
                b.Variant = ButtonVariant.Secondary;
                b.Height = 30;
                b.NaturalWidth = 90;
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
            AddFull(_matrix, 88, Theme.GapTight);
            AddRow(_presetRow);
            AddRow(_prioRow);
            // 高度按固定 3 行定稿（行布局要求挂载前定稿，挂载后改高会与下一区块错位）
            AddFull(_state, 46 + 3 * InfoList.RowHeight + 12, 0);

            AddAction("应用设置", "check", ButtonVariant.Primary, OnApply, 124);
            AddAction("恢复全核", "refresh", ButtonVariant.Secondary, delegate
            {
                _matrix.SelectAll();
                ApplyPreset(0);
            }, 120);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);

            _band.SetCells(new MetricBand.Cell[] {
                NewCell("CPU 负载", "--", "正在采样", "", Theme.Accent, true),
                NewCell("物理核心", "--", "核心数", "由系统报告", Theme.Purple, false),
                NewCell("逻辑核心", "--", "线程数", "可分派单元", Theme.Cyan, false),
                NewCell("架构", "--", "处理器类型", "", Theme.Success, false)
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
                ulong sysMask = 0;
                try
                {
                    double load = _meter.Sample();
                    // deep=true：物理核心数（CpuCores）只有深度采集才会填充，
                    // 用浅采集会退化成"物理核 = 逻辑核"，把 16 核 32 线程报成 32 核
                    snap = SysInfo.Capture(true, load);
                    procs = _procs.Snapshot();
                    // 优先级要逐进程 OpenProcess，放后台取齐，渲染时直接查表
                    prios = CollectPriorities(procs, 40);
                    sysMask = ProcManager.SystemAffinityMask();
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

        /// <summary>
        /// 取内存占用前 topN 个进程的优先级：每个进程都要一次 OpenProcess + GetPriorityClass，
        /// 因此在后台线程一次性取齐，避免渲染时逐行在 UI 线程发系统调用。
        /// </summary>
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

        private void Render(SystemSnapshot snap, List<ProcInfo> procs, ulong sysMask, Dictionary<int, int> prios)
        {
            _systemMask = sysMask;

            int logical = snap.CpuThreads > 0 ? snap.CpuThreads : Environment.ProcessorCount;
            int physical = snap.CpuCores > 0 ? snap.CpuCores : logical;
            double load = snap.CpuLoadPercent;

            _band.UpdateCell(0, load < 0 ? "--" : load.ToString("0") + "%", load, "当前占用", snap.CpuName);
            _band.SetTone(0, Gfx.LoadColor(load));
            _band.UpdateCell(1, physical.ToString(), -1, "物理核心",
                CpuTopology.IsHybrid ? "大小核（异构）" : "同构");
            _band.UpdateCell(2, logical.ToString(), -1, "逻辑核心", "可用于亲和性设置");
            _band.UpdateCell(3, CpuTopology.IsHybrid ? "大小核" : "同构", -1, "处理器架构",
                CpuTopology.IsHybrid ? "P 核 + E 核混合" : "所有核心同构");

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

            // ---- 核心矩阵：按系统掩码里可用的核数初始化 ----
            int usable = 0;
            for (int i = 0; i < 64; i++)
            {
                if (sysMask == 0 || (sysMask & (1UL << i)) != 0) usable++;
            }
            if (sysMask == 0) usable = logical;
            if (usable < 1) usable = 1;
            _matrix.SetCoreCount(usable);
            RefreshCoreHint();

            _state.Set("目标进程", "未选择");
            _state.Set("当前亲和性", "—");
            _state.Set("当前优先级", "—");
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

            string maskText = "读取中…";
            _state.Set("目标进程", p.Name + "（PID " + p.Pid + "）");
            _state.Set("当前亲和性", maskText);
            _state.Set("当前优先级", prio >= 0 ? ProcManager.PriorityNames[prio] : "—");

            int pid = p.Pid;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                ulong mask = ProcManager.GetAffinity(pid, out error);
                Post(delegate
                {
                    if (_targetPid != pid) return; // 期间又换了目标进程
                    if (mask == 0)
                    {
                        _state.Set("当前亲和性", error.Length > 0 ? error : "读取失败");
                    }
                    else
                    {
                        _state.Set("当前亲和性", MaskText(mask));
                        _matrix.SetMask(SubsetOf(mask));
                        RefreshCoreHint();
                    }
                });
            });
        }

        /// <summary>把进程掩码限制到本机可用核上（跨机器/跨处理器组时可能越界）。</summary>
        private ulong SubsetOf(ulong mask)
        {
            if (_systemMask == 0) return mask;
            ulong m = mask & _systemMask;
            return m == 0 ? mask : m;
        }

        private static string MaskText(ulong mask)
        {
            List<string> on = new List<string>();
            for (int i = 0; i < 64; i++)
            {
                if ((mask & (1UL << i)) != 0) on.Add("#" + (i + 1));
            }
            if (on.Count == 0) return "—";
            if (on.Count > 8) return on.Count + " 个核心（" + on[0] + " … " + on[on.Count - 1] + "）";
            return string.Join("、", on.ToArray());
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

            int cores = _presetCores[index];
            if (cores <= 0)
            {
                _matrix.SelectAll();
            }
            else
            {
                ulong mask = 0;
                int limit = cores < _matrix.CoreCount ? cores : _matrix.CoreCount;
                for (int i = 0; i < limit; i++) mask |= 1UL << i;
                _matrix.SetMask(mask);
            }
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
            ulong mask = _matrix.Mask;
            int level = _prioLevel;
            SetSubtitle("正在应用设置…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string errAff, errPrio;
                bool okAff = ProcManager.SetAffinity(pid, mask, out errAff);
                bool okPrio = ProcManager.SetPriority(pid, level, out errPrio);

                Post(delegate
                {
                    if (!okAff && !okPrio)
                    {
                        SetSubtitle("设置失败：" + (errAff.Length > 0 ? errAff : errPrio), Theme.Danger);
                        return;
                    }
                    string msg = name + "：";
                    msg += okAff ? "亲和性已设为 " + _matrix.SelectedCount + " 核" : "亲和性设置失败（" + errAff + "）";
                    msg += "，";
                    msg += okPrio ? "优先级已设为「" + ProcManager.PriorityNames[level] + "」" : "优先级设置失败（" + errPrio + "）";
                    SetSubtitle(msg, okAff && okPrio ? Theme.Success : Theme.Warning);

                    _state.Set("当前亲和性", MaskText(mask));
                    _state.Set("当前优先级", ProcManager.PriorityNames[level]);
                });
            });
        }    }
}
