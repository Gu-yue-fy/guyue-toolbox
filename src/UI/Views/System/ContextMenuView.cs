using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class ContextMenuView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly List<ContextEntry> _all = new List<ContextEntry>();
        private bool _busy;
        private bool _loaded;
        private AccentButton _disableButton;
        private AccentButton _enableButton;

        public ContextMenuView()
            : base("右键菜单", "精简资源管理器右键菜单，移除多余项")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Cyan;
            _notice.NoticeText = "列出资源管理器右键菜单中的扩展项，可禁用不常用的条目以精简菜单。禁用通过重命名注册表项实现，可随时启用还原，不影响系统功能。HKLM 项需管理员权限。";

            _summary.Caption = "右键菜单";
            _summary.IconKind = "menu";
            _summary.CaptionColor = Theme.Cyan;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
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
            _grid.AddFillColumn("菜单项", 200);
            _grid.AddTextColumn("位置", 160, false);
            _grid.AddTextColumn("状态", 90, false);

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
            LayoutGrid(_grid, _summary, 0, 200);
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
            SetSubtitle("正在读取右键菜单…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ContextEntry> result = GuyueBox.Core.ContextMenu.List();
                Post(delegate
                {
                    _busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    _grid.Rows.Clear();
                    for (int i = 0; i < result.Count; i++)
                    {
                        ContextEntry e = result[i];
                        int idx = _grid.Rows.Add(e.Name, e.Location, e.Enabled ? "启用" : "已禁用");
                        _grid.Rows[idx].Tag = e;
                        _grid.Rows[idx].Cells[2].Style.ForeColor = e.Enabled ? Theme.TextPrimary : Theme.TextMuted;
                    }
                    _grid.ClearSelection();

                    int disabled = 0;
                    for (int i = 0; i < result.Count; i++) if (!result[i].Enabled) disabled++;

                    _summary.Clear();
                    _summary.Add("菜单项", result.Count + " 个");
                    _summary.Add("已禁用", disabled + " 个", disabled > 0 ? Theme.Warning : Theme.TextPrimary);
                    _summary.Invalidate();

                    SetSubtitle(result.Count > 0 ? "已读取 " + result.Count + " 个右键菜单项。" : "未读取到右键菜单项。",
                        Theme.Success);
                    Relayout();
                });
            });
        }

        private ContextEntry Selected
        {
            get { return SelectedFrom<ContextEntry>(_grid); }
        }

        private void UpdateActions()
        {
            ContextEntry e = Selected;
            _disableButton.Enabled = e != null && e.Enabled && !_busy;
            _enableButton.Enabled = e != null && !e.Enabled && !_busy;
        }

        private void OnDisableClick(object sender, EventArgs e)
        {
            ContextEntry en = Selected;
            if (en == null || !en.Enabled) return;
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
            ContextEntry en = Selected;
            if (en == null || en.Enabled) return;
            Apply(en, true);
        }

        private void Apply(ContextEntry en, bool enable)
        {
            _busy = true;
            UpdateActions();
            SetSubtitle((enable ? "正在启用 " : "正在禁用 ") + en.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = GuyueBox.Core.ContextMenu.Set(en, enable, out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok) { SetSubtitle((enable ? "已启用：" : "已禁用：") + en.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, enable ? "启用失败" : "禁用失败", "操作失败：\r\n" + error); SetSubtitle("操作失败", Theme.Danger); }
                });
            });
        }    }
}
