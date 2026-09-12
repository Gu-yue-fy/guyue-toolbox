using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    public sealed class ProcessView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatCard _cardCount = new StatCard();
        private readonly StatCard _cardCpu = new StatCard();
        private readonly StatCard _cardMem = new StatCard();
        private readonly StatCard _cardHot = new StatCard();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly ProcManager _manager = new ProcManager();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        private List<ProcInfo> _processes = new List<ProcInfo>();
        private bool _busy;
        private bool _autoRefresh = true;
        private int _sortColumn = -1;
        private bool _sortAscending = true;
        private AccentButton _autoButton;
        private AccentButton _releaseButton;

        public ProcessView()
            : base("进程管理", "查看正在运行的程序与资源占用")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "说明：选中进程后可调整「优先级」（高优先级响应更快但抢占其他程序，建议只给游戏设「高」）与「核心绑定」（把进程固定到指定核心，避免跨核迁移；系统关键进程请勿改动）。结束进程可能导致数据丢失，csrss/winlogon 等系统关键进程无法结束也请勿尝试。";

            _cardCount.IconKind = "process";
            _cardCpu.IconKind = "cpu";
            _cardMem.IconKind = "memory";
            _cardHot.IconKind = "gauge";
            _cardCount.AccentColor = Theme.Purple;
            _cardCpu.AccentColor = Theme.Accent;
            _cardMem.AccentColor = Theme.Cyan;
            _cardHot.AccentColor = Theme.Warning;
            _cardCount.SetData("进程总数", "--", -1, "", Theme.Purple);
            _cardCpu.SetData("CPU 占用", "--", -1, "", Theme.Accent);
            _cardMem.SetData("内存占用", "--", -1, "", Theme.Cyan);
            _cardHot.SetData("高占用进程", "--", -1, "CPU ≥ 10% 或内存 ≥ 500 MB", Theme.Warning);

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Refresh(true); }, 92);
            _autoButton = AddAction("暂停自动刷新", "clock", ButtonVariant.Ghost, OnToggleAuto, 132);
            AddAction("结束进程", "close", ButtonVariant.Danger, OnKillClick, 110);
            _releaseButton = AddAction("释放内存", "memory", ButtonVariant.Ghost, OnReleaseMemory, 110);

            BuildContextMenu();

            BuildGrid();
            BuildLayout();

            _timer.Interval = 3000;
            _timer.Tick += delegate { Refresh(false); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrid()
        {
            // 虚拟模式：不再为每个进程创建行对象，刷新时只换数据，
            // 这是消除"进程页卡顿"的关键。
            _grid.VirtualMode = true;
            _grid.UseOwnScrollbar = true;
            _grid.MultiSelect = true;
            _grid.AddTextColumn("进程名", 190, false);
            _grid.AddTextColumn("PID", 70, true);
            _grid.AddTextColumn("CPU", 78, true);
            _grid.AddTextColumn("内存", 98, true);
            _grid.AddTextColumn("累计 CPU 时间", 124, true);
            _grid.AddTextColumn("描述", 170, false);
            _grid.AddFillColumn("可执行文件路径", 200);

            _grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) OnOpenLocation(s, EventArgs.Empty);
            };
            _grid.ColumnHeaderMouseClick += OnColumnHeaderClick;
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellFormatting += OnCellFormatting;
        }

        private List<ProcInfo> _sorted = new List<ProcInfo>();

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _sorted.Count) return;
            ProcInfo p = _sorted[e.RowIndex];

            switch (e.ColumnIndex)
            {
                case 0: e.Value = p.Name; break;
                case 1: e.Value = p.Pid.ToString(); break;
                case 2: e.Value = p.CpuPercent <= 0 ? "—" : p.CpuPercent.ToString("0.0") + " %"; break;
                case 3: e.Value = p.MemoryText; break;
                case 4: e.Value = FormatCpuTime(p.CpuTime); break;
                case 5: e.Value = string.IsNullOrEmpty(p.Description) ? "—" : p.Description; break;
                case 6: e.Value = string.IsNullOrEmpty(p.Path) ? "（无访问权限）" : p.Path; break;
            }
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _sorted.Count) return;

            // 关键：只在确实需要变色时才写 CellStyle。
            // 每次赋值都会触发样式克隆，是表格绘制变慢的主要原因。
            ProcInfo p = _sorted[e.RowIndex];
            Color c = Color.Empty;

            if (e.ColumnIndex == 2)
            {
                if (p.CpuPercent >= 40) c = Theme.Danger;
                else if (p.CpuPercent >= 10) c = Theme.Warning;
            }
            else if (e.ColumnIndex == 3)
            {
                if (p.WorkingSet > 1024L * 1024 * 1024) c = Theme.Warning;
            }
            else if (e.ColumnIndex == 0)
            {
                if (!p.Responding) c = Theme.Danger;
            }

            if (c != Color.Empty)
            {
                e.CellStyle.ForeColor = c;
                e.CellStyle.SelectionForeColor = Color.White;
                e.FormattingApplied = true;
            }
        }

        private void OnColumnHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            if (_sortColumn == e.ColumnIndex) _sortAscending = !_sortAscending;
            else { _sortColumn = e.ColumnIndex; _sortAscending = true; }
            Render();
        }

        private int _hotFilter; // 0=全部 1=高CPU 2=高内存
        private readonly AccentButton[] _hotChips = new AccentButton[3];

        private void BuildLayout()
        {
            AddFull(_notice, 42, 14);

            // ① 状态卡行
            FlowLayoutPanel cards = MakeRowFixed(128, 14);
            cards.Controls.Add(_cardCount);
            cards.Controls.Add(_cardCpu);
            cards.Controls.Add(_cardMem);
            cards.Controls.Add(_cardHot);
            AddRow(cards);

            // ② 筛选行：搜索 + 高占用快捷过滤
            FlowLayoutPanel searchRow = MakeRow(0, 10);
            Label sl = new Label();
            sl.Text = "筛选：";
            sl.ForeColor = Theme.TextSecondary;
            sl.Font = Theme.FontBody;
            sl.AutoSize = true;
            sl.Margin = new Padding(0, 0, 8, 0);
            searchRow.Controls.Add(sl);
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.Font = Theme.FontBody;
            _searchBox.Size = new Size(220, 28);
            _searchBox.TextChanged += delegate { Render(); };
            searchRow.Controls.Add(_searchBox);
            string[] chipTexts = new string[] { "全部", "高 CPU", "高内存" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                AccentButton chip = new AccentButton();
                chip.Text = chipTexts[i];
                chip.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                chip.Height = 30;
                chip.FitToText(88);
                chip.Margin = new Padding(8, 0, 0, 0);
                chip.Click += delegate
                {
                    _hotFilter = idx;
                    for (int k = 0; k < _hotChips.Length; k++)
                    {
                        _hotChips[k].Variant = k == idx ? ButtonVariant.Primary : ButtonVariant.Ghost;
                    }
                    Render();
                };
                _hotChips[i] = chip;
                searchRow.Controls.Add(chip);
            }
            AddRow(searchRow);

            AddFull(_grid, 340, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 14 + 128 + 14 + 30 + 10;
            int avail = ViewportHeight - used;
            if (avail < 190) avail = 190;

            // 表格高度固定为可视区高度，由内置滚动条浏览所有行，
            // 避免把页面撑到几千像素导致整页滚动时反复重绘。
            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (_autoRefresh && !_timer.Enabled) _timer.Start();
            Refresh(true);
        }

        public override void OnDeactivated()
        {
            _timer.Stop();
        }

        private void OnToggleAuto(object sender, EventArgs e)
        {
            _autoRefresh = !_autoRefresh;
            _autoButton.Text = _autoRefresh ? "暂停自动刷新" : "开启自动刷新";
            _autoButton.FitToText(96);
            if (_autoRefresh) _timer.Start();
            else _timer.Stop();
            LayoutActions();
            SetSubtitle(_autoRefresh ? "已开启自动刷新（每 3 秒）" : "已暂停自动刷新", Theme.TextSecondary);
        }

        // --------------------------------------------------------------

        private void Refresh(bool manual)
        {
            if (_busy || !Visible) return;
            _busy = true;
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ProcInfo> list = null;
                try
                {
                    list = _manager.Snapshot();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (list == null) return;
                    _processes = list;
                    Render();
                    if (manual) SetSubtitle("已刷新，" + _processes.Count + " 个进程。", Theme.TextSecondary);
                });
            });
        }

        private readonly TextBox _searchBox = new TextBox();

        private void Render()
        {
            // 记录当前选中的进程，刷新后按 PID 还原
            HashSet<int> selectedPids = new HashSet<int>();
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                int idx = _grid.SelectedRows[i].Index;
                if (idx >= 0 && idx < _sorted.Count) selectedPids.Add(_sorted[idx].Pid);
            }

            double totalCpu = 0;
            long totalMem = 0;
            for (int i = 0; i < _processes.Count; i++)
            {
                totalCpu += _processes[i].CpuPercent;
                totalMem += _processes[i].WorkingSet;
            }

            List<ProcInfo> sorted = new List<ProcInfo>(_processes);
            SortList(sorted);

            // 筛选：高占用快捷过滤 + 按名称/PID 过滤（不区分大小写）
            string q = _searchBox.Text.Trim();
            List<ProcInfo> kept = new List<ProcInfo>();
            for (int i = 0; i < sorted.Count; i++)
            {
                ProcInfo p = sorted[i];
                if (_hotFilter == 1 && p.CpuPercent < 10) continue;
                if (_hotFilter == 2 && p.WorkingSet < 1024L * 1024 * 500) continue;
                if (q.Length > 0 &&
                    p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    !p.Pid.ToString().Contains(q))
                {
                    continue;
                }
                kept.Add(p);
            }
            sorted = kept;
            _sorted = sorted;

            // 记住滚动位置：进程数量变化会导致行数变化，
            // 不还原的话用户正在浏览的位置会被弹回顶部。
            int firstRow = 0;
            try { firstRow = _grid.FirstDisplayedScrollingRowIndex; }
            catch { }

            // 虚拟模式下只更新行数，不创建任何行对象
            _grid.SuspendLayout();
            if (_grid.RowCount != sorted.Count) _grid.RowCount = sorted.Count;
            if (selectedPids.Count > 0)
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (selectedPids.Contains(sorted[i].Pid)) _grid.Rows[i].Selected = true;
                }
            }
            else
            {
                _grid.ClearSelection();
            }
            _grid.ResumeLayout();

            if (firstRow > 0)
            {
                int maxFirst = sorted.Count - 1;
                if (firstRow > maxFirst) firstRow = maxFirst;
                try { _grid.FirstDisplayedScrollingRowIndex = firstRow; }
                catch { }
            }

            _grid.Invalidate();

            // 状态卡
            int hotCount = 0;
            for (int i = 0; i < _processes.Count; i++)
            {
                if (_processes[i].CpuPercent >= 10 || _processes[i].WorkingSet >= 1024L * 1024 * 500) hotCount++;
            }
            _cardCount.SetData("进程总数", _processes.Count.ToString(), -1,
                "显示 " + sorted.Count + " 个", Theme.Purple);
            _cardCpu.SetData("CPU 占用", totalCpu.ToString("0.0") + " %",
                Math.Min(100, totalCpu), totalCpu >= 70 ? "负载较高" : "正常",
                totalCpu >= 70 ? Theme.Warning : Theme.Accent);
            long totalMemMb = totalMem / (1024 * 1024);
            ulong physTotal = 0;
            try { physTotal = SysInfo.GetMemory().TotalBytes; }
            catch { }
            double memPercent = physTotal > 0
                ? totalMemMb * 100.0 / (physTotal / (1024 * 1024))
                : -1;
            _cardMem.SetData("内存占用", SysInfo.FormatSize(totalMem),
                memPercent, "", Theme.Cyan);
            _cardHot.SetData("高占用进程", hotCount.ToString() + " 个", hotCount > 0 ? 100 : -1,
                "CPU ≥ 10% 或内存 ≥ 500 MB", Theme.Warning);

            Relayout();
        }

        private void SortList(List<ProcInfo> list)
        {
            Comparison<ProcInfo> cmp;
            switch (_sortColumn)
            {
                case 1:
                    cmp = delegate (ProcInfo a, ProcInfo b) { return a.Pid.CompareTo(b.Pid); };
                    break;
                case 2:
                    cmp = delegate (ProcInfo a, ProcInfo b) { return a.CpuPercent.CompareTo(b.CpuPercent); };
                    break;
                case 3:
                    cmp = delegate (ProcInfo a, ProcInfo b) { return a.WorkingSet.CompareTo(b.WorkingSet); };
                    break;
                case 4:
                    cmp = delegate (ProcInfo a, ProcInfo b) { return a.CpuTime.CompareTo(b.CpuTime); };
                    break;
                case 5:
                    cmp = delegate (ProcInfo a, ProcInfo b)
                    {
                        return string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase);
                    };
                    break;
                case 0:
                    cmp = delegate (ProcInfo a, ProcInfo b)
                    {
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    };
                    break;
                default:
                    cmp = delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); };
                    break;
            }

            list.Sort(cmp);
            if (_sortColumn >= 0 && !_sortAscending) list.Reverse();
        }

        private static string FormatCpuTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            if (t.TotalMinutes >= 1) return t.Minutes + ":" + t.Seconds.ToString("00");
            return t.Seconds + "." + (t.Milliseconds / 100) + " s";
        }

        // --------------------------------------------------------------

        private void OnReleaseMemory(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在整理内存工作集…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                long before = 0;
                long after = 0;
                int touched = 0;
                try { touched = ProcManager.ReleaseMemory(out before, out after); }
                catch { before = after = 0; }

                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    long freed = after - before;
                    if (freed < 0) freed = 0;
                    SetSubtitle("已整理进程工作集，释放约 " + SysInfo.FormatSize(freed) +
                        " 可用内存（触及 " + touched + " 个进程）。",
                        Theme.Success);
                    Refresh(true);
                });
            });
        }

        private void UpdateActions()
        {
            _autoButton.Enabled = !_busy;
            _releaseButton.Enabled = !_busy;
        }

        private void OnOpenLocation(object sender, EventArgs e)
        {
            ProcInfo p = CurrentProcess();
            if (p == null)
            {
                Dialog.Info(this, "未选择", "请先选择一个进程。");
                return;
            }
            if (string.IsNullOrEmpty(p.Path))
            {
                Dialog.Warn(this, "无法定位", "该进程的路径不可访问（通常是系统或受保护进程）。");
                return;
            }
            try
            {
                Shell.Run("explorer.exe", "/select,\"" + p.Path + "\"", 0);
            }
            catch
            {
            }
        }

        private ProcInfo CurrentProcess()
        {
            if (_grid.CurrentRow == null) return null;
            int idx = _grid.CurrentRow.Index;
            if (idx < 0 || idx >= _sorted.Count) return null;
            return _sorted[idx];
        }

        private void OnCopyPath(object sender, EventArgs e)
        {
            ProcInfo p = CurrentProcess();
            if (p == null)
            {
                Dialog.Info(this, "未选择", "请先选择一个进程。");
                return;
            }
            string text = string.IsNullOrEmpty(p.Path) ? (p.Name + "  (PID " + p.Pid + ")") : p.Path;
            try
            {
                Clipboard.SetText(text);
                SetSubtitle("已复制：" + text, Theme.Success);
            }
            catch
            {
                Dialog.Warn(this, "复制失败", "无法访问系统剪贴板。");
            }
        }

        // ---------------- 优先级与核心绑定（右键菜单，任务管理器式） ----------------

        /// <summary>构建右键菜单：结束 / 进程树 / 优先级 / 核心绑定 / 打开位置。</summary>
        private void BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("结束任务", null, delegate { OnKillClick(null, null); });
            menu.Items.Add("结束进程树", null, delegate { OnKillTreeClick(null, null); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem prio = new ToolStripMenuItem("优先级");
            AddPrioItem(prio, "低", System.Diagnostics.ProcessPriorityClass.Idle);
            AddPrioItem(prio, "低于正常", System.Diagnostics.ProcessPriorityClass.BelowNormal);
            AddPrioItem(prio, "正常", System.Diagnostics.ProcessPriorityClass.Normal);
            AddPrioItem(prio, "高于正常", System.Diagnostics.ProcessPriorityClass.AboveNormal);
            AddPrioItem(prio, "高", System.Diagnostics.ProcessPriorityClass.High);
            menu.Items.Add(prio);

            ToolStripMenuItem aff = new ToolStripMenuItem("核心绑定");
            aff.DropDownItems.Add("前半核心", null, delegate { SetAffinity(true); });
            aff.DropDownItems.Add("后半核心", null, delegate { SetAffinity(false); });
            aff.DropDownItems.Add("全部核心", null, delegate { OnAffinityAll(null, null); });
            menu.Items.Add(aff);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开文件位置", null, delegate { OnOpenLocation(null, null); });
            menu.Items.Add("复制路径", null, delegate { OnCopyPath(null, null); });

            _grid.ContextMenuStrip = menu;
        }

        private void AddPrioItem(ToolStripMenuItem parent, string text, System.Diagnostics.ProcessPriorityClass cls)
        {
            parent.DropDownItems.Add(text, null, delegate { SetPriority(cls); });
        }

        /// <summary>设置选中进程的优先级（RealTime 档刻意不提供）。</summary>
        private void SetPriority(System.Diagnostics.ProcessPriorityClass cls)
        {
            ProcInfo p = SelectedSingle();
            if (p == null) { Dialog.Info(this, "未选择", "请先选择一个进程。"); return; }
            try
            {
                using (System.Diagnostics.Process proc = System.Diagnostics.Process.GetProcessById(p.Pid))
                {
                    proc.PriorityClass = cls;
                    SetSubtitle("「" + p.Name + "」优先级已设为 " + cls + "。提升优先级会让该进程更抢占 CPU，建议只给游戏设「高」。", Theme.Success);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "调整失败", "无法调整「" + p.Name + "」的优先级：\r\n" + ex.Message +
                    "\r\n\r\n系统关键进程与需要更高权限的进程无法调整。");
            }
        }

        /// <summary>结束进程树（含全部子进程）。</summary>
        private void OnKillTreeClick(object sender, EventArgs e)
        {
            ProcInfo p = SelectedSingle();
            if (p == null) { Dialog.Info(this, "未选择", "请先选择一个进程。"); return; }
            if (!Dialog.Confirm(this, "结束进程树",
                "将结束「" + p.Name + "」及其全部子进程（PID " + p.Pid + "）。\r\n未保存的数据会丢失，是否继续？")) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r = Shell.Run("taskkill.exe", "/PID " + p.Pid + " /T /F", 15000);
                Post(delegate
                {
                    if (r.Ok) SetSubtitle("已结束进程树：" + p.Name, Theme.Success);
                    else SetSubtitle("结束进程树失败：" + (r.Error.Length > 0 ? r.Error.Trim() : "未知原因"), Theme.Danger);
                    Refresh(true);
                });
            });
        }

        /// <summary>优先级档位（刻意不含 RealTime——会把系统线程全部压住，极易假死）。</summary>
        private static readonly System.Diagnostics.ProcessPriorityClass[] PrioOrder = new System.Diagnostics.ProcessPriorityClass[]
        {
            ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal,
            ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal,
            ProcessPriorityClass.High
        };

        private ProcInfo SelectedSingle()
        {
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                int idx = _grid.SelectedRows[i].Index;
                if (idx >= 0 && idx < _sorted.Count) return _sorted[idx];
            }
            return null;
        }

        private void OnPriorityUp(object sender, EventArgs e) { AdjustPriority(1); }
        private void OnPriorityDown(object sender, EventArgs e) { AdjustPriority(-1); }

        private void AdjustPriority(int dir)
        {
            ProcInfo p = SelectedSingle();
            if (p == null) { Dialog.Info(this, "未选择", "请先选择一个进程。"); return; }

            ProcessPriorityClass current;
            try
            {
                using (Process proc = Process.GetProcessById(p.Pid))
                {
                    current = proc.PriorityClass;

                    int idx = Array.IndexOf(PrioOrder, current);
                    if (idx < 0) idx = 2; // 未知档位按 Normal 处理
                    int next = Math.Max(0, Math.Min(PrioOrder.Length - 1, idx + dir));
                    if (next == idx)
                    {
                        SetSubtitle(dir > 0 ? "已到最高可用档（「高」——再高会威胁系统响应，不支持）。" :
                            "已到最低档（「低」）。", Theme.Warning);
                        return;
                    }

                    proc.PriorityClass = PrioOrder[next];
                    SetSubtitle("「" + p.Name + "」优先级已调整为：" + PrioOrder[next] +
                        "。提升优先级会让该进程更抢占 CPU，建议只给游戏设「高」。", Theme.Success);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "调整失败", "无法调整「" + p.Name + "」的优先级：\r\n" + ex.Message +
                    "\r\n\r\n系统关键进程与需要更高权限的进程无法调整。");
            }
        }

        private void OnAffinityFirstHalf(object sender, EventArgs e) { SetAffinity(true); }
        private void OnAffinitySecondHalf(object sender, EventArgs e) { SetAffinity(false); }

        /// <summary>把选中进程绑定到前半/后半逻辑核心，减少跨核迁移带来的延迟。</summary>
        private void SetAffinity(bool firstHalf)
        {
            ProcInfo p = SelectedSingle();
            if (p == null) { Dialog.Info(this, "未选择", "请先选择一个进程。"); return; }

            int cores = Environment.ProcessorCount;
            if (cores < 2)
            {
                Dialog.Info(this, "不适用", "本机只有单个逻辑核心，无法做核心绑定。");
                return;
            }

            int half = cores / 2;
            long mask = 0;
            for (int i = 0; i < cores; i++)
            {
                bool inRange = firstHalf ? i < half : i >= cores - half;
                if (inRange) mask |= 1L << i;
            }

            try
            {
                using (Process proc = Process.GetProcessById(p.Pid))
                {
                    proc.ProcessorAffinity = (IntPtr)mask;
                    SetSubtitle("「" + p.Name + "」已绑定到" + (firstHalf ? "前 " + half : "后 " + half) +
                        " 个逻辑核心（掩码 0x" + mask.ToString("X") + "）。作用即时生效；把游戏绑定到物理核所在的核心可减少调度迁移。", Theme.Success);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "绑定失败", "无法设置「" + p.Name + "」的核心绑定：\r\n" + ex.Message +
                    "\r\n\r\n系统关键进程无法绑定；单核进程也无法更改。");
            }
        }

        private void OnAffinityAll(object sender, EventArgs e)
        {
            ProcInfo p = SelectedSingle();
            if (p == null) { Dialog.Info(this, "未选择", "请先选择一个进程。"); return; }

            long mask = Environment.ProcessorCount >= 64 ? long.MaxValue : (1L << Environment.ProcessorCount) - 1;
            try
            {
                using (Process process = Process.GetProcessById(p.Pid))
                {
                    process.ProcessorAffinity = (IntPtr)mask;
                    SetSubtitle("「" + p.Name + "」已恢复使用全部核心。", Theme.Success);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "恢复失败", ex.Message);
            }
        }

        private void OnKillClick(object sender, EventArgs e)
        {
            if (_busy) return;
            List<ProcInfo> targets = new List<ProcInfo>();
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                int idx = _grid.SelectedRows[i].Index;
                if (idx >= 0 && idx < _sorted.Count) targets.Add(_sorted[idx]);
            }

            if (targets.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择要结束的进程。");
                return;
            }

            string names = "";
            for (int i = 0; i < targets.Count && i < 8; i++)
            {
                names += "· " + targets[i].Name + "  (PID " + targets[i].Pid + ")\r\n";
            }
            if (targets.Count > 8) names += "· …等共 " + targets.Count + " 个进程\r\n";

            if (!Dialog.Confirm(this, "结束进程",
                "确定要结束以下进程吗？\r\n\r\n" + names + "\r\n未保存的数据将会丢失。"))
                return;

            int ok = 0;
            int failed = 0;
            int self = System.Diagnostics.Process.GetCurrentProcess().Id;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Pid == self) { failed++; continue; }
                if (ProcManager.Kill(targets[i].Pid, true)) ok++;
                else failed++;
            }

            SetSubtitle("已结束 " + ok + " 个进程" + (failed > 0 ? "，" + failed + " 个失败。" : "。"),
                failed > 0 ? Theme.Warning : Theme.Success);

            if (failed > 0)
            {
                Dialog.Warn(this, "部分失败",
                    "成功结束 " + ok + " 个进程，另有 " + failed +
                    " 个进程无法结束。\r\n\r\n系统关键进程需要管理员权限，或受系统保护无法终止。");
            }

            Refresh(true);
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
