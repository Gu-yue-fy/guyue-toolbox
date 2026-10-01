using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>驱动一览：枚举已安装驱动，未数字签名的标红提示。</summary>
    public sealed class DriverView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _stats = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly List<DriverInfo> _drivers = new List<DriverInfo>();
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _loaded;

        public DriverView()
            : base("驱动一览", "已安装驱动清单，未数字签名的驱动会标红提示")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "未数字签名的驱动可能引发稳定性问题；第三方驱动（如显卡、芯片组）属正常，仅在需排查问题时关注。";

            _stats.Caption = "驱动";
            _stats.IconKind = "pc";
            _stats.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, OnRefresh, 110);

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
            _grid.AddTextColumn("设备", 280, false);
            _grid.AddTextColumn("厂商", 200, false);
            _grid.AddTextColumn("版本", 120, false);
            _grid.AddTextColumn("日期", 110, false);
            _grid.AddFillColumn("备注", 160);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 48, 12);
            FlowLayoutPanel statRow = MakeRow(0, 12);
            statRow.Controls.Add(_stats);
            AddRow(statRow);
            AddFull(_grid, 300, 0);
            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            // 提示条 48+12 + 状态行 ~36
            LayoutGrid(_grid, _stats, 96, 240);
        }

        private void UpdateStats()
        {
            _stats.Clear();
            _stats.Add("驱动总数", _drivers.Count.ToString());
            int unsigned = 0;
            for (int i = 0; i < _drivers.Count; i++) if (!_drivers[i].IsSigned) unsigned++;
            _stats.Add("未签名", unsigned.ToString(), unsigned > 0 ? Theme.Danger : Theme.Success);
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
            SetSubtitle("正在枚举驱动…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DriverInfo> list = DriverScanner.Scan();
                Post(delegate { ShowDrivers(list); });
            });
        }

        private void ShowDrivers(List<DriverInfo> list)
        {
            _busy = false;
            SetButtonsEnabled(true);
            _drivers.Clear();
            _grid.Rows.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                DriverInfo d = list[i];
                int idx = _grid.Rows.Add(d.DeviceName, d.Provider, d.Version, d.Date, d.Note);
                if (!string.IsNullOrEmpty(d.Note))
                {
                    _grid.Rows[idx].DefaultCellStyle.ForeColor =
                        d.Note == "未数字签名" ? Theme.Danger : Theme.Warning;
                }
            }
            _drivers.AddRange(list);
            _grid.ClearSelection();
            UpdateStats();
            if (list.Count == 0) SetSubtitle("未枚举到驱动，可能缺少权限。", Theme.Warning);
            else SetSubtitle("共 " + list.Count + " 个驱动，未签名 " + CountUnsigned(list) + " 个。", Theme.TextSecondary);
        }

        private int CountUnsigned(List<DriverInfo> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (!list[i].IsSigned) n++;
            return n;
        }
    }
}
