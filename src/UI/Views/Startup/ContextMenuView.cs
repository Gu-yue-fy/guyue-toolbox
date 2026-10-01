// 右键菜单页：精简资源管理器右键菜单，可禁用/启用扩展项

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 右键菜单：列出资源管理器右键菜单扩展项，可禁用/启用。
    /// 禁用靠重命名注册表键实现（标记而非删除），因此随时可还原。
    /// </summary>
    public sealed class ContextMenuView : GridPageView
    {
        private readonly EmptyState _empty = new EmptyState();
        private AccentButton _disableButton;
        private AccentButton _enableButton;

        public ContextMenuView()
            : base("右键菜单", "精简资源管理器右键菜单，移除多余项")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Cyan;
            Notice.NoticeText = "列出资源管理器右键菜单中的扩展项，可禁用不常用的条目以精简菜单。禁用通过重命名注册表项实现，可随时启用还原，不影响系统功能。HKLM 项需管理员权限。";

            Summary.Caption = "右键菜单";
            Summary.IconKind = "menu";
            Summary.CaptionColor = Theme.Cyan;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            _disableButton = AddAction("禁用选中", "ban", ButtonVariant.Danger, OnDisableClick, 120);
            _enableButton = AddAction("启用选中", "check", ButtonVariant.Primary, OnEnableClick, 120);

            InitializeGridPage();
        }

        protected override void BuildColumns()
        {
            Grid.AddFillColumn("菜单项", 200);
            Grid.AddTextColumn("位置", 160, false);
            Grid.AddTextColumn("状态", 90, false);
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
            SetSubtitle("正在读取右键菜单…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                List<ContextEntry> result = GuyueBox.Core.ContextMenu.List(out error);
                Post(delegate
                {
                    Busy = false;
                    Grid.Rows.Clear();

                    // 某些注册表位置（HKLM）非管理员读不到：必须给出原因，而不是显示"没有条目"
                    if (error.Length > 0)
                    {
                        Summary.Clear();
                        Summary.Invalidate();
                        ShowEmpty("读取右键菜单失败",
                            error + "\r\nHKLM 下的菜单项需要管理员权限；请以管理员身份运行后重试。", "重试");
                        SetSubtitle("读取右键菜单失败：" + error, Theme.Danger);
                        return;
                    }

                    for (int i = 0; i < result.Count; i++)
                    {
                        ContextEntry e = result[i];
                        int idx = Grid.Rows.Add(e.Name, e.Location, e.Enabled ? "启用" : "已禁用");
                        Grid.Rows[idx].Tag = e;
                        Grid.Rows[idx].Cells[2].Style.ForeColor = e.Enabled ? Theme.TextPrimary : Theme.TextMuted;
                    }
                    Grid.ClearSelection();

                    int disabled = 0;
                    for (int i = 0; i < result.Count; i++) if (!result[i].Enabled) disabled++;

                    Summary.Clear();
                    Summary.Add("菜单项", result.Count + " 个");
                    Summary.Add("已禁用", disabled + " 个", disabled > 0 ? Theme.Warning : Theme.TextPrimary);
                    Summary.Invalidate();

                    if (result.Count == 0)
                    {
                        ShowEmpty("没有扫描到右键菜单项",
                            "当前系统中没有第三方右键菜单扩展；安装新软件后重新扫描即可看到。", "刷新");
                        SetSubtitle("未读取到右键菜单项（不是读取失败）。", Theme.Warning);
                        return;
                    }

                    ShowGrid();
                    SetSubtitle("已读取 " + result.Count + " 个右键菜单项。", Theme.Success);
                    Relayout();
                });
            });
        }

        protected override void UpdateActions()
        {
            ContextEntry e = SelectedRow<ContextEntry>();
            _disableButton.Enabled = e != null && e.Enabled && !Busy;
            _enableButton.Enabled = e != null && !e.Enabled && !Busy;
        }

        private void OnDisableClick(object sender, EventArgs e)
        {
            ContextEntry en = SelectedRow<ContextEntry>();
            if (en == null || !en.Enabled) return;

            // 只有 HKLM 位置需要管理员（HKCU 属于当前用户，标准权限即可），避免无谓地要求提权
            if (NeedsAdmin(en) && !EnsureElevated("修改 HKLM 下的右键菜单项需要管理员权限。")) return;

            if (!Dialog.ConfirmDanger(this, "禁用右键菜单项",
                "禁用右键菜单项「" + en.Name + "」，它不再出现在右键菜单里。",
                "可撤销：随时回到本页重新启用，注册表键只是标记而非删除。",
                "只影响右键菜单的显示；对应程序本身不受影响。",
                "禁用", false))
                return;
            Apply(en, false);
        }

        private void OnEnableClick(object sender, EventArgs e)
        {
            ContextEntry en = SelectedRow<ContextEntry>();
            if (en == null || en.Enabled) return;
            if (NeedsAdmin(en) && !EnsureElevated("修改 HKLM 下的右键菜单项需要管理员权限。")) return;
            Apply(en, true);
        }

        /// <summary>该项是否位于 HKLM（机器级注册表，写入需要管理员）。</summary>
        private static bool NeedsAdmin(ContextEntry en)
        {
            return en != null && en.FullPath != null &&
                en.FullPath.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
        }

        private void Apply(ContextEntry en, bool enable)
        {
            Busy = true;
            UpdateActions();
            SetSubtitle((enable ? "正在启用 " : "正在禁用 ") + en.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = GuyueBox.Core.ContextMenu.Set(en, enable, out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok)
                    {
                        SetSubtitle((enable ? "已启用：" : "已禁用：") + en.Name, Theme.Success);
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
