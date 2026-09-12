using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    public sealed class PrivacyView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly List<PrivacyItem> _items = PrivacyCleaner.BuildItems();

        private bool _busy;
        private bool _suppress;
        private AccentButton _scanButton;
        private AccentButton _cleanButton;
        private AccentButton _toggleButton;

        public PrivacyView()
            : base("隐私清理", "清除使用痕迹，保护个人隐私")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "只删除使用记录本身，不影响任何程序功能；列表会随使用自动重建。";

            _summary.Caption = "隐私扫描";
            _summary.IconKind = "shield";
            _summary.CaptionColor = Theme.Purple;

            _scanButton = AddAction("开始扫描", "refresh", ButtonVariant.Primary, OnScanClick, 118);
            _cleanButton = AddAction("清理选中项", "trash", ButtonVariant.Danger, OnCleanClick, 150);
            _toggleButton = AddAction("全选", "check", ButtonVariant.Secondary, OnToggleAllClick, 92);

            BuildGrid();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = false;
            _grid.UseOwnScrollbar = true;
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.AddTextColumn("项目", 180, false);
            _grid.AddFillColumn("说明", 260);
            _grid.AddTextColumn("痕迹数", 90, true);

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += OnCellValueChanged;
        }

        private void BuildLayout()
        {
            AddFull(_notice, 42, 18);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_grid, 300, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            int summaryH = _summary.PreferredHeight;
            _summary.Height = summaryH;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryH;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18;
            int avail = ViewportHeight - used;
            if (avail < 180) avail = 180;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (_grid.Rows.Count == 0)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    PrivacyItem it = _items[i];
                    int idx = _grid.Rows.Add(it.Selected, it.Name, it.Description, "未扫描");
                    _grid.Rows[idx].Tag = it;
                }
                _grid.ClearSelection();
            }
            UpdateSummary();
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppress || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            PrivacyItem it = _grid.Rows[e.RowIndex].Tag as PrivacyItem;
            if (it == null) return;

            bool selected = false;
            try { selected = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[0].Value); }
            catch { }
            it.Selected = selected;
            UpdateSummary();
        }

        private void OnToggleAllClick(object sender, EventArgs e)
        {
            bool anyUnselected = false;
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Selected) { anyUnselected = true; break; }
            }

            _suppress = true;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                _grid.Rows[i].Cells[0].Value = anyUnselected;
                PrivacyItem it = _grid.Rows[i].Tag as PrivacyItem;
                if (it != null) it.Selected = anyUnselected;
            }
            _suppress = false;

            _toggleButton.Text = anyUnselected ? "取消全选" : "全选";
            _toggleButton.FitToText(84);
            LayoutActions();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int count = 0;
            int traces = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Selected) continue;
                count++;
                if (_items[i].Scanned) traces += _items[i].Count;
            }

            _summary.Clear();
            _summary.Add("已选项", count + " 项");
            _summary.Add("痕迹数", traces > 0 ? traces + " 条" : "—",
                traces > 0 ? Theme.Warning : Theme.TextPrimary);
            _summary.Invalidate();
            Relayout();
        }

        // --------------------------------------------------------------
        // 扫描
        // --------------------------------------------------------------

        private void OnScanClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                SetSubtitle("正在扫描…", Theme.Warning);
                return;
            }

            _busy = true;
            _scanButton.Enabled = false;
            _cleanButton.Enabled = false;
            SetSubtitle("正在扫描使用痕迹…", Theme.Warning);

            // 剪贴板必须在 UI 线程访问
            for (int i = 0; i < _items.Count; i++)
            {
                PrivacyItem it = _items[i];
                if (it.Id == "clipboard")
                {
                    it.Count = PrivacyCleaner.ScanClipboard();
                    it.Scanned = true;
                }
                else
                {
                    it.Scanned = false;
                }
                int row = FindRow(it);
                if (row >= 0) _grid.Rows[row].Cells[3].Value = "…";
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    PrivacyItem it = _items[i];
                    if (it.Id == "clipboard") continue;
                    PrivacyCleaner.Scan(it);
                }

                Post(delegate
                {
                    for (int i = 0; i < _grid.Rows.Count; i++)
                    {
                        PrivacyItem it = _grid.Rows[i].Tag as PrivacyItem;
                        if (it == null) continue;
                        _grid.Rows[i].Cells[3].Value = it.Scanned ? it.Count.ToString() : "—";
                    }
                    _busy = false;
                    _scanButton.Enabled = true;
                    _cleanButton.Enabled = true;

                    int traces = 0;
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (_items[i].Selected) traces += _items[i].Count;
                    }
                    SetSubtitle("扫描完成，发现 " + traces + " 条使用痕迹。",
                        traces > 0 ? Theme.Warning : Theme.Success);
                    UpdateSummary();
                });
            });
        }

        // --------------------------------------------------------------
        // 清理
        // --------------------------------------------------------------

        private void OnCleanClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<PrivacyItem> targets = new List<PrivacyItem>();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Selected) targets.Add(_items[i]);
            }
            if (targets.Count == 0)
            {
                Dialog.Info(this, "没有选中项目", "请先勾选要清理的项目。");
                return;
            }

            int traces = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Scanned) traces += targets[i].Count;
            }

            if (!Dialog.Confirm(this, "确认清理",
                "即将清除 " + targets.Count + " 项使用痕迹（共 " + traces + " 条记录）。\r\n\r\n" +
                "这些记录删除后无法恢复，但不影响系统与程序功能。是否继续？"))
            {
                return;
            }

            _busy = true;
            _scanButton.Enabled = false;
            _cleanButton.Enabled = false;
            SetSubtitle("正在清理使用痕迹…", Theme.Warning);

            List<string> errors = new List<string>();

            // 剪贴板在 UI 线程清
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Id != "clipboard") continue;
                string err;
                if (!PrivacyCleaner.CleanClipboard(out err)) errors.Add("剪贴板：" + err);
                else targets[i].Count = 0;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    PrivacyItem it = targets[i];
                    if (it.Id == "clipboard") continue;
                    string err;
                    if (!PrivacyCleaner.Clean(it, out err)) errors.Add(it.Name + "：" + err);
                }

                Post(delegate
                {
                    for (int i = 0; i < _grid.Rows.Count; i++)
                    {
                        PrivacyItem it = _grid.Rows[i].Tag as PrivacyItem;
                        if (it == null || !targets.Contains(it)) continue;
                        _grid.Rows[i].Cells[3].Value = "0";
                    }
                    _busy = false;
                    _scanButton.Enabled = true;
                    _cleanButton.Enabled = true;
                    UpdateSummary();

                    if (errors.Count > 0)
                    {
                        string text = "部分项目清理失败：\r\n";
                        for (int i = 0; i < errors.Count && i < 6; i++) text += "· " + errors[i] + "\r\n";
                        Dialog.Warn(this, "清理完成", text);
                        SetSubtitle("清理完成，部分项目失败。", Theme.Warning);
                    }
                    else
                    {
                        SetSubtitle("已清除所选使用痕迹。", Theme.Success);
                        Dialog.Success(this, "清理完成", "所选使用痕迹已全部清除。");
                    }
                });
            });
        }

        private int FindRow(PrivacyItem it)
        {
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (_grid.Rows[i].Tag == it) return i;
            }
            return -1;
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
