// 计划任务页：列出系统计划任务（含内置项），可禁用/启用

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 计划任务：列出系统计划任务（含 Microsoft 内置项），可禁用/启用。
    /// 通过 schtasks 实现，原计划定义不会被删除，随时可还原。
    /// </summary>
    public sealed class ScheduledTasksView : GridPageView
    {
        private readonly List<ScheduledTask> _all = new List<ScheduledTask>();
        private readonly EmptyState _empty = new EmptyState();
        private AccentButton _disableButton;
        private AccentButton _enableButton;

        public ScheduledTasksView()
            : base("计划任务", "管理系统计划任务，禁用无用定时项")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Cyan;
            Notice.NoticeText = "列出系统计划任务（含 Microsoft 内置项）。可禁用不常用或可疑的定时任务以加快开机、减少后台活动。禁用通过 schtasks 实现，可随时启用还原。系统任务需管理员权限才能修改。";

            Summary.Caption = "计划任务";
            Summary.IconKind = "task";
            Summary.CaptionColor = Theme.Cyan;

            // 排序不走绑死的按钮：点列头（名称/状态等）即可排序（GridPageView 默认 ColumnClickSort）
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            _disableButton = AddAction("禁用选中", "ban", ButtonVariant.Danger, OnDisableClick, 120);
            _enableButton = AddAction("启用选中", "check", ButtonVariant.Primary, OnEnableClick, 120);

            InitializeGridPage();
        }

        protected override void BuildColumns()
        {
            Grid.AddFillColumn("任务名", 260);
            Grid.AddTextColumn("状态", 110, false);
            Grid.AddTextColumn("下次运行", 190, false);
        }

        /// <summary>
        /// 空态卡与表格共用同一块位置（借 GridPageView 的"表格前附加行"钩子）：
        /// 没有可显示内容时隐藏表格、只显示空态卡，避免留下一片空白让用户以为程序没反应。
        /// </summary>
        protected override void AddExtraLayoutRows()
        {
            _empty.Visible = false;
            AddFull(_empty, 150, Theme.GapSection);
        }

        /// <summary>显示空态/失败卡，动作按钮承担"重试"。</summary>
        private void ShowEmpty(string title, string sub, string actionText)
        {
            _empty.SetState(title, sub, actionText, "refresh", delegate { Load(); });
            _empty.Visible = true;
            Grid.Visible = false;
            Relayout();
        }

        /// <summary>恢复表格显示（成功取到数据后调用）。</summary>
        private void ShowGrid()
        {
            _empty.Visible = false;
            Grid.Visible = true;
            Relayout();
        }

        protected override void Load()
        {
            if (Busy) return;
            Busy = true;
            Loaded = true;
            SetSubtitle("正在读取计划任务…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                List<ScheduledTask> result = ScheduledTasks.List(out error);
                Post(delegate
                {
                    Busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    Grid.Rows.Clear();

                    // 读取失败与"零条任务"必须区分：前者给原因 + 重试，后者给"确实没有"的说明
                    if (error.Length > 0)
                    {
                        Summary.Clear();
                        Summary.Invalidate();
                        ShowEmpty("读取计划任务失败",
                            error + "\r\n若为权限问题，请以管理员身份运行本程序后重试。", "重试");
                        SetSubtitle("读取计划任务失败：" + error, Theme.Danger);
                        return;
                    }

                    // 默认按名称排序；点列头（名称/状态等）随时改排序
                    List<ScheduledTask> ordered = new List<ScheduledTask>(_all);
                    ordered.Sort(delegate (ScheduledTask a, ScheduledTask b)
                    {
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });

                    for (int i = 0; i < ordered.Count; i++)
                    {
                        ScheduledTask t = ordered[i];
                        int idx = Grid.Rows.Add(t.Name, t.StatusText, t.NextRun);
                        Grid.Rows[idx].Tag = t;
                        Grid.Rows[idx].Cells[1].Style.ForeColor = t.Enabled ? Theme.TextPrimary : Theme.Warning;
                    }
                    Grid.ClearSelection();

                    int disabled = 0;
                    for (int i = 0; i < result.Count; i++) if (!result[i].Enabled) disabled++;

                    Summary.Clear();
                    Summary.Add("任务数", result.Count + " 个");
                    Summary.Add("已禁用", disabled + " 个", disabled > 0 ? Theme.Warning : Theme.TextPrimary);
                    Summary.Invalidate();

                    if (result.Count == 0)
                    {
                        ShowEmpty("没有可管理的计划任务",
                            "未枚举到任何计划任务；若刚做过系统更改，可点「刷新」重新读取。", "刷新");
                        SetSubtitle("未读取到计划任务（不是读取失败）。", Theme.Warning);
                        return;
                    }

                    ShowGrid();
                    SetSubtitle("已读取 " + result.Count + " 个计划任务。", Theme.Success);
                    Relayout();
                });
            });
        }

        protected override void UpdateActions()
        {
            ScheduledTask t = SelectedRow<ScheduledTask>();
            _disableButton.Enabled = t != null && t.Enabled && !Busy;
            _enableButton.Enabled = t != null && !t.Enabled && !Busy;
        }

        private void OnDisableClick(object sender, EventArgs e)
        {
            ScheduledTask t = SelectedRow<ScheduledTask>();
            if (t == null || !t.Enabled) return;

            // 计划任务改动是系统级写操作：先过统一管理员门禁，而不是等 schtasks 报"拒绝访问"
            if (!EnsureElevated("禁用计划任务需要管理员权限。")) return;

            if (!Dialog.ConfirmDanger(this, "禁用计划任务",
                "禁用计划任务「" + t.Name + "」，它不再按计划自动运行。",
                "可撤销：可随时重新启用，原计划定义不会被删除。",
                "若该任务属于系统维护或更新检查，禁用后相关自动流程不再执行。",
                "禁用", false))
                return;
            Apply(t, false);
        }

        private void OnEnableClick(object sender, EventArgs e)
        {
            ScheduledTask t = SelectedRow<ScheduledTask>();
            if (t == null || t.Enabled) return;
            if (!EnsureElevated("启用计划任务需要管理员权限。")) return;
            Apply(t, true);
        }

        private void Apply(ScheduledTask t, bool enable)
        {
            Busy = true;
            UpdateActions();
            SetSubtitle((enable ? "正在启用 " : "正在禁用 ") + t.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ScheduledTasks.Set(t, enable, out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok)
                    {
                        SetSubtitle((enable ? "已启用：" : "已禁用：") + t.Name, Theme.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, enable ? "启用失败" : "禁用失败", "操作失败：\r\n" + error);
                        SetSubtitle("操作失败", Theme.Danger);
                    }
                });
            });
        }
    }
}
