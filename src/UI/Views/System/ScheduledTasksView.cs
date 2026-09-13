using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class ScheduledTasksView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly List<ScheduledTask> _all = new List<ScheduledTask>();
        private bool _busy;
        private bool _loaded;
        private AccentButton _disableButton;
        private AccentButton _sortButton;
        private int _sortMode;

        private void OnSortClick(object sender, EventArgs e)
        {
            _sortMode = (_sortMode + 1) % 2;
            _sortButton.Text = _sortMode == 0 ? "排序：名称" : "排序：已禁用优先";
            _sortButton.Invalidate();
            Load();
        }
        private AccentButton _enableButton;

        public ScheduledTasksView()
            : base("计划任务", "管理系统计划任务，禁用无用定时项")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Cyan;
            _notice.NoticeText = "列出系统计划任务（含 Microsoft 内置项）。可禁用不常用或可疑的定时任务以加快开机、减少后台活动。禁用通过 schtasks 实现，可随时启用还原。系统任务需管理员权限才能修改。";

            _summary.Caption = "计划任务";
            _summary.IconKind = "task";
            _summary.CaptionColor = Theme.Cyan;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            _sortButton = AddAction("排序：名称", "sort", ButtonVariant.Secondary, OnSortClick, 120);
            _disableButton = AddAction("禁用选中", "close", ButtonVariant.Danger, OnDisableClick, 120);
            _enableButton = AddAction("启用选中", "check", ButtonVariant.Primary, OnEnableClick, 120);

            BuildGrid();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.ColumnClickSort = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("任务名", 260);
            _grid.AddTextColumn("状态", 110, false);
            _grid.AddTextColumn("下次运行", 190, false);

            _grid.SelectionChanged += delegate { UpdateActions(); };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 42, 18);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_grid, 320, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private void Relayout()
        {
            int summaryH = _summary.PreferredHeight;
            _summary.Height = summaryH;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryH;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18;
            int avail = ViewportHeight - used;
            if (avail < 200) avail = 200;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();
            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (!_loaded) Load();
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            _loaded = true;
            SetSubtitle("正在读取计划任务…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ScheduledTask> result = ScheduledTasks.List();
                Post(delegate
                {
                    _busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    _grid.Rows.Clear();

                    // 排序：0=名称 1=已禁用优先
                    List<ScheduledTask> ordered = new List<ScheduledTask>(_all);
                    if (_sortMode == 0) ordered.Sort(delegate (ScheduledTask a, ScheduledTask b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
                    else ordered.Sort(delegate (ScheduledTask a, ScheduledTask b)
                    {
                        if (a.Enabled != b.Enabled) return a.Enabled ? 1 : -1;
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });

                    for (int i = 0; i < ordered.Count; i++)
                    {
                        ScheduledTask t = ordered[i];
                        int idx = _grid.Rows.Add(t.Name, t.Status, t.NextRun);
                        _grid.Rows[idx].Tag = t;
                        _grid.Rows[idx].Cells[1].Style.ForeColor = t.Enabled ? Theme.TextPrimary : Theme.Warning;
                    }
                    _grid.ClearSelection();

                    int disabled = 0;
                    for (int i = 0; i < result.Count; i++) if (!result[i].Enabled) disabled++;

                    _summary.Clear();
                    _summary.Add("任务数", result.Count + " 个");
                    _summary.Add("已禁用", disabled + " 个", disabled > 0 ? Theme.Warning : Theme.TextPrimary);
                    _summary.Invalidate();

                    SetSubtitle(result.Count > 0 ? "已读取 " + result.Count + " 个计划任务。" : "未读取到计划任务。",
                        Theme.Success);
                    Relayout();
                });
            });
        }

        private ScheduledTask Selected
        {
            get
            {
                if (_grid.SelectedRows.Count == 0) return null;
                return _grid.SelectedRows[0].Tag as ScheduledTask;
            }
        }

        private void UpdateActions()
        {
            ScheduledTask t = Selected;
            _disableButton.Enabled = t != null && t.Enabled && !_busy;
            _enableButton.Enabled = t != null && !t.Enabled && !_busy;
        }

        private void OnDisableClick(object sender, EventArgs e)
        {
            ScheduledTask t = Selected;
            if (t == null || !t.Enabled) return;
            if (!Dialog.Confirm(this, "禁用任务", "确定要禁用计划任务「" + t.Name + "」吗？\r\n（可随时启用还原）"))
                return;
            Apply(t, false);
        }

        private void OnEnableClick(object sender, EventArgs e)
        {
            ScheduledTask t = Selected;
            if (t == null || t.Enabled) return;
            Apply(t, true);
        }

        private void Apply(ScheduledTask t, bool enable)
        {
            _busy = true;
            UpdateActions();
            SetSubtitle((enable ? "正在启用 " : "正在禁用 ") + t.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ScheduledTasks.Set(t, enable, out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok) { SetSubtitle((enable ? "已启用：" : "已禁用：") + t.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, enable ? "启用失败" : "禁用失败", "操作失败：\r\n" + error); SetSubtitle("操作失败", Theme.Danger); }
                });
            });
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
