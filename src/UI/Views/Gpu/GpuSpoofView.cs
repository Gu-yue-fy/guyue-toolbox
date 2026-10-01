using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 显卡伪装：把当前显卡在系统里显示的名称伪装为其它型号（用于兼容性 / 隐私），
    /// 不改变实际驱动能力。与「N卡设置」同属「显卡」大功能，各占一条页签。
    /// </summary>
    public sealed class GpuSpoofView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly List<GpuSpoof.GpuEntry> _entries = new List<GpuSpoof.GpuEntry>();
        private string _selectedName = "";
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private AccentButton _applyButton;
        private AccentButton _restoreButton;

        public GpuSpoofView()
            : base("显卡伪装", "把显卡在系统里显示的名称伪装为其它型号（兼容性 / 隐私），不改变实际驱动能力")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Cyan;
            _notice.NoticeText = "仅修改系统显示的显卡名称（用于让只识别特定厂商 / 型号的游戏或软件放行，或隐藏真实型号），"
                + "并不改变实际驱动能力。需管理员权限；修改后请重启或重新扫描设备才能生效；可一键还原。";

            AddAction("选择型号", "list", ButtonVariant.Secondary, OnPickClick, 120);
            _applyButton = AddAction("应用伪装", "check", ButtonVariant.Primary, OnApplyClick, 120);
            _restoreButton = AddAction("还原真实型号", "undo", ButtonVariant.Ghost, OnRestoreClick, 140);

            BuildGrid();
            BuildLayout();
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddTextColumn("实例", 70, false);
            _grid.AddFillColumn("当前显示名称", 220);
            _grid.AddFillColumn("真实驱动名", 220);
            _grid.AddTextColumn("状态", 90, false);
            _grid.SelectionChanged += delegate { };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 56, 12);

            FlowLayoutPanel sumRow = MakeRow(0, 12);
            sumRow.Controls.Add(_summary);
            AddRow(sumRow);

            AddFull(_grid, 200, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            Load();
        }

        private void Relayout()
        {
            LayoutGrid(_grid, _summary, 0, 200);
        }

        public override void OnActivated()
        {
            if (_entries.Count == 0) Load();
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在检测显卡…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<GpuSpoof.GpuEntry> list = GpuSpoof.Detect();
                bool spoofed = GpuSpoof.IsSpoofed();
                Post(delegate
                {
                    _busy = false;
                    _entries.Clear();
                    for (int i = 0; i < list.Count; i++) _entries.Add(list[i]);

                    _grid.Rows.Clear();
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        GpuSpoof.GpuEntry e = _entries[i];
                        int idx = _grid.Rows.Add(e.SubKey, e.CurrentName, e.DriverDesc, spoofed ? "已伪装" : "真实");
                        _grid.Rows[idx].Tag = e;
                    }
                    _grid.ClearSelection();

                    _summary.Clear();
                    _summary.Caption = "检测到 " + _entries.Count + " 张显卡";
                    _summary.IconKind = "gpu";
                    _summary.CaptionColor = Theme.Accent;
                    _summary.Add("伪装状态", spoofed ? "已伪装" : "未伪装");
                    _summary.Add("当前型号", _selectedName.Length > 0 ? _selectedName : "（未选择）");
                    _summary.Invalidate();
                    Relayout();   // 摘要内容是在载入后才填的：不重排高度，卡片底行会被裁掉

                    SetSubtitle(spoofed ? "当前处于伪装状态。" : "检测到 " + _entries.Count + " 张显卡，可伪装。",
                        spoofed ? Theme.Warning : Theme.Success);
                    UpdateActions();
                });
            });
        }

        private static string[] Presets()
        {
            return new string[]
            {
                "NVIDIA GeForce RTX 4090",
                "NVIDIA GeForce RTX 4080",
                "NVIDIA GeForce RTX 3070",
                "AMD Radeon RX 7900 XTX",
                "AMD Radeon RX 6800 XT",
                "Intel Arc A770 Graphics",
                "自定义…"
            };
        }

        private void OnPickClick(object sender, EventArgs e)
        {
            string pick = Chooser.ChooseOne(this, "选择伪装型号", new List<string>(Presets()));
            if (pick == null) return;
            if (pick == "自定义…")
            {
                string custom = Dialog.Input(this, "自定义型号", "请输入要显示的显卡名称：", _selectedName);
                if (string.IsNullOrWhiteSpace(custom)) return;
                _selectedName = custom.Trim();
            }
            else
            {
                _selectedName = pick;
            }
            _summary.Clear();
            _summary.Caption = "检测到 " + _entries.Count + " 张显卡";
            _summary.IconKind = "gpu";
            _summary.CaptionColor = Theme.Accent;
            _summary.Add("伪装状态", GpuSpoof.IsSpoofed() ? "已伪装" : "未伪装");
            _summary.Add("当前型号", _selectedName);
            _summary.Invalidate();
            Relayout();   // 同上：摘要内容变化后重排高度
            UpdateActions();
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_entries.Count == 0) { SetSubtitle("尚未检测到显卡。", Theme.Warning); return; }
            if (_selectedName.Length == 0) { SetSubtitle("请先「选择型号」。", Theme.Warning); return; }
            if (!EnsureElevated("修改显卡显示名称需要管理员权限。")) return;

            _busy = true;
            UpdateActions();
            SetSubtitle("正在写入伪装名称…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = GpuSpoof.Spoof(_selectedName, out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已伪装", "显卡已显示为「" + _selectedName + "」；重启或重新扫描设备后生效。", ToastKind.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "写入失败", "显卡伪装写入失败：\r\n" + error);
                        SetSubtitle("写入失败", Theme.Danger);
                    }
                });
            });
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (!GpuSpoof.IsSpoofed()) { SetSubtitle("当前未处于伪装状态，无需还原。", Theme.Warning); return; }
            if (!EnsureElevated("还原显卡真实名称需要管理员权限。")) return;

            _busy = true;
            UpdateActions();
            SetSubtitle("正在还原真实名称…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = GpuSpoof.Restore(out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已还原", "显卡已恢复真实名称。", ToastKind.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "还原失败", "还原失败：\r\n" + error);
                        SetSubtitle("还原失败", Theme.Danger);
                    }
                });
            });
        }

        private void UpdateActions()
        {
            _applyButton.Enabled = _entries.Count > 0 && _selectedName.Length > 0 && !_busy;
            _restoreButton.Enabled = !_busy;
        }
    }
}
