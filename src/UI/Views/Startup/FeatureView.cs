using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>Windows 可选功能（组件）枚举与启停，需管理员。</summary>
    public sealed class FeatureView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _stats = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly List<FeatureInfo> _features = new List<FeatureInfo>();
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _loaded;

        public FeatureView()
            : base("可选功能", "启用 / 禁用 Windows 可选功能（组件），需管理员")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "此处列出系统可选功能。启用/禁用是系统标准操作且可逆；部分功能（如 Hyper-V、Containers）变更后需重启生效。";

            _stats.Caption = "功能";
            _stats.IconKind = "feature";
            _stats.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, OnRefresh, 110);
            AddAction("启用选中", "check", ButtonVariant.Primary, OnEnable, 120);
            AddAction("禁用选中", "ban", ButtonVariant.Danger, OnDisable, 120);

            BuildGrid();
            BuildLayout();
        }



        public override void OnActivated()
        {
            if (!_loaded) { _loaded = true; OnRefresh(null, EventArgs.Empty); }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddTextColumn("功能名称", 440, false);
            _grid.AddFillColumn("状态", 140);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 56, 12);
            FlowLayoutPanel statRow = MakeRow(0, 12);
            statRow.Controls.Add(_stats);
            AddRow(statRow);
            AddFull(_grid, 300, 0);
            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            LayoutGrid(_grid, _stats, 56 + 12 + 36, 240);
        }

        private void UpdateStats()
        {
            _stats.Clear();
            _stats.Add("功能总数", _features.Count.ToString());
            int enabled = 0;
            for (int i = 0; i < _features.Count; i++) if (_features[i].State == "Enabled") enabled++;
            _stats.Add("已启用", enabled.ToString(), enabled > 0 ? Theme.Success : Theme.TextSecondary);
            _stats.Invalidate();
        }

        private void SetButtonsEnabled(bool on)
        {
            for (int i = 0; i < Actions.Count; i++) Actions[i].Enabled = on;
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetButtonsEnabled(false);
            SetSubtitle("正在列出可选功能…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<FeatureInfo> list = FeatureManager.List();
                Post(delegate { ShowFeatures(list); });
            });
        }

        private void ShowFeatures(List<FeatureInfo> list)
        {
            _busy = false;
            SetButtonsEnabled(true);
            _features.Clear();
            _grid.Rows.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                FeatureInfo f = list[i];
                int idx = _grid.Rows.Add(f.Name, f.State);
                if (f.State == "Enabled") _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Success;
                else if (f.State != "Disabled") _grid.Rows[idx].DefaultCellStyle.ForeColor = Theme.Warning;
            }
            _features.AddRange(list);
            _grid.ClearSelection();
            UpdateStats();
            if (list.Count == 0) SetSubtitle("未列出功能，可能缺少管理员权限。", Theme.Warning);
            else SetSubtitle("共 " + list.Count + " 个可选功能。", Theme.TextSecondary);
        }

        private void OnEnable(object sender, EventArgs e) { Apply(true); }
        private void OnDisable(object sender, EventArgs e) { Apply(false); }

        private void Apply(bool enable)
        {
            if (_busy) return;
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先在列表里选择要" + (enable ? "启用" : "禁用") + "的功能。");
                return;
            }
            List<string> names = new List<string>();
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                object v = _grid.SelectedRows[i].Cells[0].Value;
                if (v != null) names.Add(v.ToString());
            }
            if (names.Count == 0) return;

            string verb = enable ? "启用" : "禁用";
            string preview = string.Join("\r\n", names.GetRange(0, Math.Min(names.Count, 8)));
            if (names.Count > 8) preview += "\r\n…";
            if (!Dialog.Confirm(this, verb + "功能", "将" + verb + " " + names.Count + " 个功能：\r\n" + preview +
                "\r\n部分功能变更后需重启生效。")) return;

            _busy = true;
            SetButtonsEnabled(false);
            SetSubtitle("正在" + verb + "功能…", Theme.Warning);
            List<string> copy = names;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                List<string> errors = new List<string>();
                for (int i = 0; i < copy.Count; i++)
                {
                    if (FeatureManager.Set(copy[i], enable)) ok++;
                    else errors.Add(copy[i]);
                }
                Post(delegate { AfterApply(ok, errors, verb); });
            });
        }

        private void AfterApply(int ok, List<string> errors, string verb)
        {
            _busy = false;
            SetButtonsEnabled(true);
            if (errors.Count == 0) SetSubtitle("已" + verb + " " + ok + " 个功能。", Theme.Success);
            else SetSubtitle(verb + "完成 " + ok + " 个，失败 " + errors.Count + " 个（可能需管理员权限）。", Theme.Warning);
            OnRefresh(null, EventArgs.Empty);
        }
    }
}
