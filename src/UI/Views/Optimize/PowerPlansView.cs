using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class PowerPlansView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly List<PowerPlan> _all = new List<PowerPlan>();
        private bool _busy;
        private bool _loaded;
        private AccentButton _applyButton;
        private AccentButton _ultimateButton;

        public PowerPlansView()
            : base("电源计划", "切换性能/节能方案，释放硬件潜力")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Success;
            _notice.NoticeText = "电源计划决定 CPU 频率、节能节流与核心调度策略。追求极限性能可选「高性能」或「终极性能」；笔记本日常使用「平衡」更省电。切换可随时改回。";

            _summary.Caption = "电源计划";
            _summary.IconKind = "power";
            _summary.CaptionColor = Theme.Success;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(true); }, 92);
            AddAction("推荐方案", "shield", ButtonVariant.Primary, OnRecommendClick, 130);
            _applyButton = AddAction("应用此计划", "power", ButtonVariant.Secondary, OnApplyClick, 130);
            _ultimateButton = AddAction("启用终极性能", "bolt", ButtonVariant.Ghost, OnUltimateClick, 130);

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
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("计划名称", 200);
            _grid.AddTextColumn("GUID", 300, false);
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
            if (!_loaded) Load(false);
        }

        private void Load(bool force)
        {
            if (_busy) return;
            _busy = true;
            _loaded = true;
            SetSubtitle("正在读取电源计划…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<PowerPlan> result = PowerPlans.List();
                Post(delegate
                {
                    _busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    _grid.Rows.Clear();
                    for (int i = 0; i < result.Count; i++)
                    {
                        PowerPlan p = result[i];
                        int idx = _grid.Rows.Add(p.Name, p.Guid, p.Active ? "使用中" : "—");
                        _grid.Rows[idx].Tag = p;
                        if (p.Active) _grid.Rows[idx].Cells[2].Style.ForeColor = Theme.Success;
                    }
                    _grid.ClearSelection();

                    int activeCount = 0;
                    for (int i = 0; i < result.Count; i++) if (result[i].Active) activeCount++;

                    _summary.Clear();
                    _summary.Add("方案总数", result.Count + " 个");
                    _summary.Add("当前激活", activeCount > 0 ? "1 个" : "无", Theme.Success);
                    _summary.Invalidate();

                    SetSubtitle(result.Count > 0 ? "已读取 " + result.Count + " 个电源计划。" : "未读取到电源计划。",
                        Theme.Success);
                    Relayout();
                });
            });
        }

        private PowerPlan Selected
        {
            get
            {
                if (_grid.SelectedRows.Count == 0) return null;
                return _grid.SelectedRows[0].Tag as PowerPlan;
            }
        }

        private void UpdateActions()
        {
            PowerPlan p = Selected;
            _applyButton.Enabled = p != null && !p.Active && !_busy;
            _ultimateButton.Enabled = !_busy;
        }

        /// <summary>
        /// 硬件感知的电源计划推荐：
        ///  笔记本（存在电池）→「平衡」，性能与续航均衡；
        ///  台式机（无电池）→「高性能」，保持 CPU 高频与调度余量，游戏/重载首选。
        /// </summary>
        private void OnRecommendClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_all.Count == 0) { SetSubtitle("电源计划尚未读取，请先点「刷新」。", Theme.Warning); return; }

            var ps = SystemInformation.PowerStatus;
            // 无法识别电池状态时按笔记本处理（保守，避免给笔记本推台式机方案）
            bool laptop = ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                          ps.BatteryChargeStatus != BatteryChargeStatus.Unknown;

            string want = laptop ? "平衡" : "高性能";
            PowerPlan target = null;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].Name.Contains(want)) { target = _all[i]; break; }
            }

            if (target == null)
            {
                SetSubtitle("系统中没有「" + want + "」计划，可直接选用列表中的其他方案。", Theme.Warning);
                return;
            }

            if (target.Active)
            {
                SetSubtitle("当前已在使用推荐的「" + target.Name + "」计划，无需更改。", Theme.Success);
                return;
            }

            _busy = true;
            UpdateActions();
            SetSubtitle("正在应用推荐方案：" + target.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = PowerPlans.SetActive(target.Guid, out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok)
                    {
                        SetSubtitle("已按推荐应用「" + target.Name + "」" +
                            (laptop ? "（笔记本：均衡之选）。" : "（台式机：性能首选）。"), Theme.Success);
                        Load(true);
                    }
                    else
                    {
                        Dialog.Error(this, "应用失败", "无法切换电源计划：\r\n" + error);
                        SetSubtitle("应用失败", Theme.Danger);
                    }
                });
            });
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            PowerPlan p = Selected;
            if (p == null) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在应用 " + p.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = PowerPlans.SetActive(p.Guid, out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok) { SetSubtitle("已应用：" + p.Name, Theme.Success); Load(true); }
                    else { Dialog.Error(this, "应用失败", "无法切换电源计划：\r\n" + error); SetSubtitle("应用失败", Theme.Danger); }
                });
            });
        }

        private void OnUltimateClick(object sender, EventArgs e)
        {
            if (!Dialog.Confirm(this, "启用终极性能",
                "将创建（如不存在）并激活「终极性能」电源计划。\r\n该计划会移除节能节流，桌面/服务器更稳定，但笔记本续航可能下降。\r\n\r\n是否继续？"))
                return;

            _busy = true;
            UpdateActions();
            SetSubtitle("正在启用终极性能…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = PowerPlans.EnsureUltimate(out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok) { Dialog.Success(this, "已启用", "「终极性能」计划已创建并激活。重启后完全生效。"); SetSubtitle("已启用终极性能计划", Theme.Success); Load(true); }
                    else { Dialog.Error(this, "启用失败", "无法启用终极性能计划：\r\n" + error); SetSubtitle("启用失败", Theme.Danger); }
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
