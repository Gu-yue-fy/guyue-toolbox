using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// N 卡设置：把 NVIDIA 控制面板常用 3D / 电源设置写成预设，一键切换。
    /// 仅对 NVIDIA 显卡可用；电源管理项写 HKLM 需管理员，3D 全局项写 HKCU 无需管理员。
    /// 与「显卡伪装」同属「显卡」大功能，各占一条页签。
    /// </summary>
    public sealed class NvidiaCplView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly List<AccentButton> _presetButtons = new List<AccentButton>();

        // 逐项编辑行：选中任意设置项后单独调值 / 恢复该项默认（此前只能整套应用预设）
        private readonly Label _editCaption = new Label();
        private readonly ComboBox _editCombo = new ComboBox();
        private readonly AccentButton _applyItem = new AccentButton();
        private readonly AccentButton _resetItem = new AccentButton();
        private readonly FlowLayoutPanel _editRow = MakeRowFixed(34, 8);
        private NvidiaCpl.SettingDef _selected;
        private Dictionary<string, int> _cur = new Dictionary<string, int>();
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _nvidia;
        private AccentButton _restoreButton;

        public NvidiaCplView()
            : base("N 卡设置", "把 N 卡控制面板常用 3D / 电源设置写成预设，一键切换（电源管理需管理员）")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Cyan;
            _notice.NoticeText = "NVIDIA 控制面板常用设置（电源管理模式、低延迟、垂直同步、纹理过滤等）的预设切换。"
                + "「电源管理模式」写入 HKLM 需管理员权限；3D 设置写入当前用户，无需管理员。部分项改动后请在 NVIDIA 控制面板中重新打开或重启驱动生效，可一键还原。";

            for (int i = 0; i < NvidiaCpl.Presets.Count; i++)
            {
                NvidiaCpl.Preset p = NvidiaCpl.Presets[i];
                string icon = (i == 0) ? "bolt" : (i == 1 ? "gauge" : "tune");
                ButtonVariant variant = (i == 0) ? ButtonVariant.Primary : ButtonVariant.Secondary;
                AccentButton b = AddAction(p.Name, icon, variant, OnPresetClick, 130);
                b.Tag = p;
                _presetButtons.Add(b);
            }
            _restoreButton = AddAction("还原默认", "undo", ButtonVariant.Ghost, OnRestoreClick, 130);

            BuildGrid();
            BuildLayout();
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("设置项", 200);
            _grid.AddFillColumn("当前值", 160);
            _grid.AddFillColumn("说明", 320);
            _grid.SelectionChanged += delegate { OnRowSelected(); };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 84, 12);
            FlowLayoutPanel sumRow = MakeRow(0, 12);
            sumRow.Controls.Add(_summary);
            AddRow(sumRow);
            // 表格高度按真实行高给足（表头 36 + 行 32）：此前固定 200px，10 项放不下出表内滚动条
            AddFull(_grid, GridHeight(), 0);
            BuildEditorRow();
            AddRow(_editRow);
            Body.Resize += delegate { Relayout(); };
            Relayout();
            Load();
        }

        /// <summary>表格恰好容纳全部设置项的高度。</summary>
        private static int GridHeight()
        {
            return 36 + NvidiaCpl.Settings.Count * 32 + 2;
        }

        private void BuildEditorRow()
        {
            _editCaption.Text = "选中一项设置后在此单独调整";
            _editCaption.ForeColor = Theme.TextMuted;
            _editCaption.AutoSize = true;
            _editRow.Controls.Add(_editCaption);

            _editCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _editCombo.Width = 180;
            _editCombo.SelectedIndexChanged += delegate
            {
                _applyItem.Enabled = _selected != null && _editCombo.SelectedIndex >= 0 && !_busy;
            };
            _editRow.Controls.Add(_editCombo);

            _applyItem.Text = "应用此项";
            _applyItem.IconKind = "check";
            _applyItem.Variant = ButtonVariant.Primary;
            _applyItem.Height = 30;
            _applyItem.NaturalWidth = 104;
            _applyItem.Enabled = false;
            _applyItem.Click += delegate { OnApplyItemClick(); };
            _editRow.Controls.Add(_applyItem);

            _resetItem.Text = "恢复此项默认";
            _resetItem.IconKind = "undo";
            _resetItem.Variant = ButtonVariant.Ghost;
            _resetItem.Height = 30;
            _resetItem.NaturalWidth = 124;
            _resetItem.Enabled = false;
            _resetItem.Click += delegate { OnResetItemClick(); };
            _editRow.Controls.Add(_resetItem);
        }

        /// <summary>行选中：填入当前值与可选档位（来自条目定义的 Labels，全部是驱动认识的值）。</summary>
        private void OnRowSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;
            NvidiaCpl.SettingDef s = _grid.SelectedRows[0].Tag as NvidiaCpl.SettingDef;
            if (s == null) return;
            _selected = s;

            int cur;
            _cur.TryGetValue(s.Name, out cur);

            _editCaption.Text = "「" + s.Caption + "」当前：" + NvidiaCpl.Friendly(s, cur);
            _editCaption.ForeColor = Theme.TextSecondary;

            _editCombo.Items.Clear();
            int sel = -1, i = 0;
            foreach (KeyValuePair<int, string> kv in s.Labels)
            {
                _editCombo.Items.Add(kv.Value);
                if (kv.Key == cur) sel = i;
                i++;
            }
            if (sel >= 0) _editCombo.SelectedIndex = sel;
            _resetItem.Enabled = !_busy && _nvidia;
            _applyItem.Enabled = sel >= 0 && !_busy && _nvidia;
        }

        private static int ValueAt(NvidiaCpl.SettingDef s, int index)
        {
            int i = 0;
            foreach (KeyValuePair<int, string> kv in s.Labels)
            {
                if (i++ == index) return kv.Key;
            }
            return 0;
        }

        private void OnApplyItemClick()
        {
            if (_selected == null || _busy || !_nvidia) return;
            if (_editCombo.SelectedIndex < 0) return;
            NvidiaCpl.SettingDef s = _selected;
            int value = ValueAt(s, _editCombo.SelectedIndex);
            string label = _editCombo.Text;
            if (s.IsPm && !EnsureElevated("电源管理类设置写入系统注册表需要管理员权限。")) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在设置「" + s.Caption + "」…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = NvidiaCpl.SetItem(s, value, out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已设置", "「" + s.Caption + "」已设为 " + label + "；重启驱动或在控制面板重新打开后生效。", ToastKind.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "设置失败", error);
                        SetSubtitle("设置失败", Theme.Danger);
                    }
                });
            });
        }

        private void OnResetItemClick()
        {
            if (_selected == null || _busy || !_nvidia) return;
            NvidiaCpl.SettingDef s = _selected;
            if (s.IsPm && !EnsureElevated("电源管理类设置写入系统注册表需要管理员权限。")) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在恢复「" + s.Caption + "」的驱动默认…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = NvidiaCpl.ResetItem(s, out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已恢复", "「" + s.Caption + "」已交回驱动默认。", ToastKind.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "恢复失败", error);
                        SetSubtitle("恢复失败", Theme.Danger);
                    }
                });
            });
        }

        private void Relayout() { LayoutGrid(_grid, _summary, 0, GridHeight()); }

        public override void OnActivated() { if (_grid.Rows.Count == 0) Load(); }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在检测 N 卡与当前设置…", Theme.Warning);
            UpdateActions();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool nv = NvidiaCpl.IsNvidia();
                Dictionary<string, int> cur = nv ? NvidiaCpl.ReadCurrent() : new Dictionary<string, int>();
                Post(delegate
                {
                    _busy = false;
                    _nvidia = nv;
                    _cur = cur;
                    _grid.Rows.Clear();
                    if (nv)
                    {
                        for (int i = 0; i < NvidiaCpl.Settings.Count; i++)
                        {
                            NvidiaCpl.SettingDef s = NvidiaCpl.Settings[i];
                            int v = cur.ContainsKey(s.Name) ? cur[s.Name] : 0;
                            int idx = _grid.Rows.Add(s.Caption, NvidiaCpl.Friendly(s, v), s.Desc);
                            _grid.Rows[idx].Tag = s;
                        }
                    }
                    _summary.Clear();
                    _summary.Caption = nv ? "检测到 NVIDIA 显卡" : "未检测到 NVIDIA 显卡";
                    _summary.IconKind = "gpu";
                    _summary.CaptionColor = nv ? Theme.Success : Theme.Danger;
                    _summary.Add("设置项", NvidiaCpl.Settings.Count + " 项");
                    _summary.Add("预设数量", NvidiaCpl.Presets.Count + " 个");
                    _summary.Invalidate();
                    SetSubtitle(nv ? "可应用以下预设。" : "本机无 NVIDIA 显卡，此功能不可用。", nv ? Theme.Success : Theme.Danger);
                    UpdateActions();
                });
            });
        }

        private void OnPresetClick(object sender, EventArgs e)
        {
            AccentButton b = sender as AccentButton;
            if (b == null) return;
            NvidiaCpl.Preset p = b.Tag as NvidiaCpl.Preset;
            if (p == null || _busy || !_nvidia) return;
            if (!EnsureElevated("「电源管理模式」写入系统注册表需要管理员权限。")) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在应用「" + p.Name + "」…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = NvidiaCpl.Apply(p, out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已应用", "已切换为「" + p.Name + "」预设；重启驱动或在控制面板中重新打开生效。", ToastKind.Success);
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "应用失败", "应用预设失败：\r\n" + error);
                        SetSubtitle("应用失败", Theme.Danger);
                    }
                });
            });
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            if (_busy || !_nvidia) return;
            if (!EnsureElevated("还原系统注册表需要管理员权限。")) return;
            _busy = true;
            UpdateActions();
            SetSubtitle("正在还原默认…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = NvidiaCpl.Restore(out error);
                Post(delegate
                {
                    _busy = false;
                    UpdateActions();
                    if (ok)
                    {
                        Toast("已还原", "已清除本工具写入的 N 卡设置，恢复驱动默认。", ToastKind.Success);
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
            bool en = _nvidia && !_busy;
            for (int i = 0; i < _presetButtons.Count; i++) _presetButtons[i].Enabled = en;
            _restoreButton.Enabled = en;
            bool canEdit = en && _selected != null;
            _applyItem.Enabled = canEdit && _editCombo.SelectedIndex >= 0;
            _resetItem.Enabled = canEdit;
        }
    }
}
