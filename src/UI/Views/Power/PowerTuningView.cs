using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 高级电源设置：Windows 默认隐藏的处理器电源管理与节能项（项 / 当前值 AC+DC / 提示），
    /// 写入**当前活动计划**；支持按 AMD / Intel 平台推荐值一键应用与恢复参数默认。
    /// 与「电源计划」同属「电源计划」大功能，各占一条页签。
    /// </summary>
    public sealed class PowerTuningView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly List<PowerSetting> _settings = new List<PowerSetting>();
        private readonly List<string> _ac = new List<string>();
        private readonly List<string> _dc = new List<string>();

        private string _activeGuid = "";
        private string _activeName = "";
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }

        public PowerTuningView()
            : base("高级电源设置", "Windows 默认隐藏的处理器电源与节能项，写入当前所选计划")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Success;
            _notice.NoticeText = "Windows 默认隐藏的电源项（处理器 / 硬盘 / PCIe / 睡眠 / 显示 / 电池等子组），写入**当前活动计划**（在上一条页签「电源计划」里切换）。"
                + "本机没有的项会自动隐藏。可一键套用 AMD / Intel 平台推荐值，或「恢复参数默认」还原计划出厂值；写入后立即重新激活计划以生效。";

            _summary.Caption = "高级电源设置";
            _summary.IconKind = "bolt";
            _summary.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            // 厂商预设是一组并列的「套用」动作，不该两个都当主按钮（主次倒置）；
            // 主操作压在同一行的「应用设置」类动作上。
            AddAction("AMD 推荐参数", "cpu", ButtonVariant.Secondary, delegate { RunVendorPreset(true); }, 150);
            AddAction("Intel 推荐参数", "cpu", ButtonVariant.Secondary, delegate { RunVendorPreset(false); }, 150);
            AddAction("恢复参数默认", "undo", ButtonVariant.Ghost, OnResetClick, 140);

            BuildGrid();
            BuildLayout();
        }

        public override void OnActivated()
        {
            base.OnActivated();
            // 每次进入都重读：当前计划可能在上一条页签「电源计划」里被切过
            if (!_busy) Load();
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.VirtualMode = true;
            _grid.AddFillColumn("设置项", 200);
            _grid.AddTextColumn("当前值 (接通电源)", 160, false);
            _grid.AddTextColumn("当前值 (电池)", 120, false);
            _grid.AddTextColumn("提示", 240, false);
            _grid.CellValueNeeded += OnCellValueNeeded;
        }

        private void BuildLayout()
        {
            AddFull(_notice, 78, 12);

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
            if (e.RowIndex < 0 || e.RowIndex >= _settings.Count) return;
            PowerSetting s = _settings[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = s.Name; break;
                case 1: e.Value = ValText(e.RowIndex, true, s); break;
                case 2: e.Value = ValText(e.RowIndex, false, s); break;
                case 3: e.Value = s.Tip; break;
            }
        }

        private string ValText(int rowIndex, bool ac, PowerSetting s)
        {
            List<string> src = ac ? _ac : _dc;
            if (rowIndex >= src.Count) return "—";
            string raw = src[rowIndex];
            if (string.IsNullOrEmpty(raw)) return "—";

            int v;
            if (int.TryParse(raw, out v)) return PowerTuning.Describe(s, v);
            return raw;
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取高级电源设置…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string activeGuid = "";
                string activeName = "";
                List<PowerPlan> plans = PowerPlans.List();
                for (int i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].Active) continue;
                    activeGuid = plans[i].Guid;
                    activeName = plans[i].Name;
                    break;
                }

                List<PowerSetting> settings = new List<PowerSetting>();
                List<string> ac = new List<string>();
                List<string> dc = new List<string>();
                string error = null;
                try
                {
                    PowerTuning.EnsureRevealed();
                    if (activeGuid.Length > 0) settings = PowerTuning.AvailableSettings(activeGuid, out ac, out dc);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    _busy = false;
                    _activeGuid = activeGuid;
                    _activeName = activeName;
                    _settings.Clear();
                    _settings.AddRange(settings);
                    _ac.Clear();
                    _ac.AddRange(ac);
                    _dc.Clear();
                    _dc.AddRange(dc);

                    _grid.RowCount = _settings.Count;
                    _grid.ClearSelection();
                    _grid.Invalidate();
                    UpdateSummary();
                    if (error != null) Dialog.Error(this, "读取高级电源设置失败", error);
                });
            });
        }

        private void UpdateSummary()
        {
            _summary.Clear();
            _summary.Add("当前计划", _activeName.Length > 0 ? _activeName : "—");
            _summary.Add("可调项", _settings.Count + " 项");
            _summary.Add("写入目标", "当前所选计划", Theme.Accent);
            _summary.Invalidate();

            SetSubtitle(_activeName.Length > 0
                ? "当前计划：" + _activeName + "，可调项 " + _settings.Count + " 项。"
                : "未检测到活动电源计划。",
                _activeName.Length > 0 ? Theme.Success : Theme.Warning);
        }

        /// <summary>按 AMD / Intel 平台推荐值写入（amd=true 用 AMD 一列）。</summary>
        private void RunVendorPreset(bool amd)
        {
            if (_busy) return;
            string planGuid = ActiveGuid();
            if (planGuid.Length == 0)
            {
                Dialog.Info(this, "无活动计划", "未能确定当前活动电源计划。");
                return;
            }

            if (!Dialog.ConfirmDanger(this, "应用推荐参数",
                "按 " + (amd ? "AMD" : "Intel") + " 平台的推荐值写入当前计划的全部高级电源设置。",
                "部分项会关闭节能策略，笔记本请注意续航。",
                "写入后立即重新激活计划以便生效。",
                "写入", false))
                return;

            _busy = true;
            SetSubtitle("正在写入推荐参数…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> fails = new List<string>();
                int done = 0;

                for (int i = 0; i < _settings.Count; i++)
                {
                    PowerSetting s = _settings[i];
                    if (s == null) continue;

                    int want = amd ? s.AmdValue : s.IntelValue;
                    int current;
                    int unusedDc;
                    if (!PowerTuning.TryRead(planGuid, s, out current, out unusedDc)) continue;
                    if (current == want) continue;

                    string err;
                    if (PowerTuning.Set(planGuid, s, want, want, out err)) done++;
                    else fails.Add(s.Name + "：" + err);
                }

                Post(delegate
                {
                    _busy = false;
                    string text = "已调整 " + done + " 项。";
                    if (fails.Count > 0)
                    {
                        text += "\r\n\r\n以下项写入失败（可能是该项在本机不可写）：\r\n";
                        for (int i = 0; i < fails.Count && i < 6; i++) text += "· " + fails[i] + "\r\n";
                    }
                    SetSubtitle("推荐参数应用完成，已调整 " + done + " 项。",
                        done > 0 ? Theme.Success : Theme.TextSecondary);
                    Dialog.Success(this, "推荐参数已应用", text);
                    Load();
                });
            });
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            if (_busy) return;

            if (!Dialog.ConfirmDanger(this, "恢复参数默认",
                "把全部电源计划的高级设置恢复为 Windows 出厂默认值。",
                "此操作不可撤销。",
                "仅影响处理器电源管理与节能相关的隐藏项，不会删除你的电源计划。",
                "恢复", true))
                return;

            if (ActiveGuid().Length == 0)
            {
                Dialog.Info(this, "无活动计划", "未能确定当前活动电源计划。");
                return;
            }

            _busy = true;
            SetSubtitle("正在恢复默认值…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/restoredefaultschemes", 30000, isChange: true);
                bool ok = r != null && r.Ok;
                string err = ok ? "" : (r == null ? "执行失败" : r.All.Trim());

                Post(delegate
                {
                    _busy = false;
                    if (!ok)
                    {
                        SetSubtitle("恢复失败：" + err, Theme.Danger);
                        Dialog.Error(this, "恢复默认失败", err);
                        return;
                    }
                    SetSubtitle("已恢复全部计划的出厂默认参数。", Theme.Success);
                    Load();
                });
            });
        }

        private string ActiveGuid()
        {
            if (_activeGuid.Length > 0) return _activeGuid;
            List<PowerPlan> plans = PowerPlans.List();
            for (int i = 0; i < plans.Count; i++)
            {
                if (plans[i].Active) return plans[i].Guid;
            }
            return "";
        }
    }
}
