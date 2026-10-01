/* UI/Views/Optimize/PowerPlansView.cs — 电源计划页（硬件感知推荐）。 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 电源计划：列出本机全部电源计划，选中后切换（双击行同效），
    /// 并给「高性能 / 平衡 / 终极性能」三个常用计划的快捷入口。
    /// 与「高级电源设置」同属「电源计划」大功能，各占一条页签。
    /// </summary>
    public sealed class PowerPlansView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly List<PowerPlan> _plans = new List<PowerPlan>();

        private bool _busy;
        private bool _loaded;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }

        public PowerPlansView()
            : base("电源计划", "选择并切换电源计划：计划决定 CPU 频率与节能节流策略")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Success;
            _notice.NoticeText = "选中一行后点「应用选中计划」（或双击该行）即切换到该计划；高性能 / 平衡 / 终极性能是三个常用计划的快捷入口。"
                + "「终极性能」默认隐藏，首次使用会先克隆出实例再激活；全部改动随时可切回其它计划。";

            _summary.Caption = "电源计划";
            _summary.IconKind = "power";
            _summary.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            AddAction("应用选中计划", "power", ButtonVariant.Primary, OnApplyClick, 140);
            AddAction("高性能", "bolt", ButtonVariant.Secondary, delegate { SwitchKnown(PowerPlans.SchemeHighPerformance, "高性能"); }, 110);
            AddAction("平衡", "gauge", ButtonVariant.Secondary, delegate { SwitchKnown(PowerPlans.SchemeBalanced, "平衡"); }, 92);
            AddAction("终极性能", "bolt", ButtonVariant.Ghost, OnUltimateClick, 130);

            BuildGrid();
            BuildLayout();
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (!_loaded) { _loaded = true; Load(); }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.VirtualMode = true;
            _grid.AddFillColumn("计划名称", 220);
            _grid.AddTextColumn("GUID", 280, false);
            _grid.AddTextColumn("状态", 90, false);
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellMouseDoubleClick += delegate { OnApplyClick(null, EventArgs.Empty); };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 72, 12);

            FlowLayoutPanel row = MakeRow(0, 14);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_grid, 240, 0);
            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout() { LayoutGrid(_grid, _summary, 0, 200); }

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _plans.Count) return;
            PowerPlan p = _plans[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = p.Name; break;
                case 1: e.Value = p.Guid; break;
                case 2: e.Value = p.Active ? "当前使用" : ""; break;
            }
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取电源计划…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<PowerPlan> plans = PowerPlans.List();
                Post(delegate
                {
                    _busy = false;
                    _plans.Clear();
                    _plans.AddRange(plans);
                    _grid.RowCount = _plans.Count;
                    _grid.ClearSelection();
                    _grid.Invalidate();
                    UpdateSummary();
                });
            });
        }

        private void UpdateSummary()
        {
            string activeName = "";
            for (int i = 0; i < _plans.Count; i++)
            {
                if (_plans[i].Active) { activeName = _plans[i].Name; break; }
            }

            _summary.Clear();
            _summary.Add("当前计划", activeName.Length > 0 ? activeName : "—");
            _summary.Add("可用计划", _plans.Count + " 个");
            _summary.Add("作用范围", "全局（所有用户）");
            _summary.Invalidate();

            SetSubtitle(activeName.Length > 0
                ? "当前计划：" + activeName + "，共 " + _plans.Count + " 个可用计划。"
                : "未检测到活动电源计划。",
                activeName.Length > 0 ? Theme.Success : Theme.Warning);
        }

        private int SelectedPlanIndex()
        {
            if (_grid.CurrentCell == null) return -1;
            int idx = _grid.CurrentCell.RowIndex;
            if (idx < 0 || idx >= _plans.Count) return -1;
            return idx;
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            int idx = SelectedPlanIndex();
            if (idx < 0)
            {
                Dialog.Info(this, "未选择计划", "请先在上方列表中点选一个电源计划。");
                return;
            }
            SwitchTo(_plans[idx].Guid, _plans[idx].Name);
        }

        private void SwitchKnown(string guid, string name)
        {
            SwitchTo(guid, name);
        }

        private void OnUltimateClick(object sender, EventArgs e)
        {
            // 「终极性能」默认不存在，需先克隆出实例再激活
            if (!Dialog.ConfirmDanger(this, "启用终极性能",
                "终极性能会关闭几乎所有节能策略，CPU 长期维持高频，耗电与发热明显增加。",
                "笔记本用户请勿在使用电池供电时启用。",
                "该计划默认隐藏，本操作会先克隆再激活（幂等：已存在则直接激活）。",
                "启用", false))
                return;

            _busy = true;
            SetSubtitle("正在启用终极性能…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                bool ok;
                try
                {
                    ok = EnableUltimate(out err);
                }
                catch (Exception ex)
                {
                    ok = false;
                    err = ex.Message;
                }

                Post(delegate
                {
                    _busy = false;
                    if (!ok)
                    {
                        SetSubtitle("启用失败：" + err, Theme.Danger);
                        Dialog.Error(this, "启用终极性能失败", err);
                        return;
                    }
                    SetSubtitle("已切换到「终极性能」。", Theme.Success);
                    Load();
                });
            });
        }

        /// <summary>克隆终极性能计划（若不存在）并激活。</summary>
        private static bool EnableUltimate(out string error)
        {
            error = "";
            List<PowerPlan> plans = PowerPlans.List();
            for (int i = 0; i < plans.Count; i++)
            {
                if (string.Equals(plans[i].Guid, PowerPlans.SchemeUltimate, StringComparison.OrdinalIgnoreCase))
                {
                    return PowerPlans.SetActive(plans[i].Guid, out error);
                }
            }

            Shell.Result dup = Shell.Run("powercfg.exe", "/duplicatescheme " + PowerPlans.SchemeUltimate, 20000, isChange: true);
            if (!dup.Ok)
            {
                // 已存在时会报错，此时直接激活即可
                return PowerPlans.SetActive(PowerPlans.SchemeUltimate, out error);
            }
            return PowerPlans.SetActive(PowerPlans.SchemeUltimate, out error);
        }

        private void SwitchTo(string guid, string name)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在切换到「" + name + "」…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                bool ok = PowerPlans.SetActive(guid, out err);

                Post(delegate
                {
                    _busy = false;
                    if (!ok)
                    {
                        SetSubtitle("切换失败：" + err, Theme.Danger);
                        Dialog.Error(this, "切换电源计划失败", err);
                        return;
                    }
                    SetSubtitle("已切换到「" + name + "」。", Theme.Success);
                    Load();
                });
            });
        }
    }
}
