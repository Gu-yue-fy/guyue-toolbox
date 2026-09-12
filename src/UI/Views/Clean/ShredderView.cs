using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    public sealed class ShredderView : ViewBase
    {
        private sealed class ShredEntry
        {
            public string Path;
            public bool IsDir;
            public long Size;
        }

        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly ComboBox _passBox = new ComboBox();

        private readonly List<ShredEntry> _items = new List<ShredEntry>();
        private bool _busy;

        private AccentButton _addFileButton;
        private AccentButton _addFolderButton;
        private AccentButton _removeButton;
        private AccentButton _clearButton;
        private AccentButton _shredSelButton;
        private AccentButton _shredAllButton;

        public ShredderView()
            : base("文件粉碎", "用随机数据覆盖后安全删除，防止被恢复")
        {
            _notice.NoticeIcon = "warn";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "被粉碎的文件将难以恢复，请确认无误。默认覆盖 3 次，占用中的文件会自动跳过。";

            _summary.Caption = "待粉碎";
            _summary.IconKind = "trash";
            _summary.CaptionColor = Theme.Danger;

            _addFileButton = AddAction("添加文件", "plus", ButtonVariant.Secondary, OnAddFiles, 116);
            _addFolderButton = AddAction("添加文件夹", "folder", ButtonVariant.Secondary, OnAddFolder, 124);
            _removeButton = AddAction("移除选中", "close", ButtonVariant.Ghost, OnRemove, 110);
            _clearButton = AddAction("清空列表", "trash", ButtonVariant.Ghost, OnClear, 110);
            _shredSelButton = AddAction("粉碎选中项", "trash", ButtonVariant.Danger, OnShredSelected, 130);
            _shredAllButton = AddAction("粉碎全部", "bolt", ButtonVariant.Danger, OnShredAll, 116);

            BuildGrid();
            BuildLayout();
            UpdateActions();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private int Passes
        {
            get
            {
                switch (_passBox.SelectedIndex)
                {
                    case 0: return 1;
                    case 2: return 7;
                    default: return 3;
                }
            }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.MultiSelect = true;
            _grid.AddFillColumn("项目路径", 280);
            _grid.AddTextColumn("类型", 90, false);
            _grid.AddTextColumn("大小", 110, true);

            _grid.CellMouseDoubleClick += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0) return;
                OpenItem(e.RowIndex);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 42, 18);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            FlowLayoutPanel passRow = MakeRow(0, 18);
            Label lbl = new Label();
            lbl.Text = "擦除次数：";
            lbl.ForeColor = Theme.TextSecondary;
            lbl.Font = Theme.FontSmall;
            lbl.AutoSize = true;
            lbl.Height = 22;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            passRow.Controls.Add(lbl);

            _passBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _passBox.Items.Add("快速（覆盖 1 次）");
            _passBox.Items.Add("标准（覆盖 3 次）");
            _passBox.Items.Add("彻底（覆盖 7 次）");
            _passBox.SelectedIndex = 1;
            _passBox.Width = 180;
            _passBox.Height = 26;
            _passBox.FlatStyle = FlatStyle.Flat;
            _passBox.BackColor = Theme.WindowBg;
            _passBox.ForeColor = Theme.TextPrimary;
            _passBox.Font = Theme.FontSmall;
            passRow.Controls.Add(_passBox);
            AddRow(passRow);

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

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18 + 44 + 18;
            int avail = ViewportHeight - used;
            if (avail < 170) avail = 170;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            UpdateSummary();
        }

        // --------------------------------------------------------------
        // 列表维护
        // --------------------------------------------------------------

        private void AddItem(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Path, path, StringComparison.OrdinalIgnoreCase)) return;
            }

            ShredEntry entry = new ShredEntry();
            entry.Path = path;
            entry.IsDir = System.IO.Directory.Exists(path);
            entry.Size = Shredder.Measure(path);

            _items.Add(entry);
            int idx = _grid.Rows.Add(path, entry.IsDir ? "文件夹" : "文件",
                entry.Size > 0 ? SysInfo.FormatSize(entry.Size) : "—");
            _grid.Rows[idx].Tag = entry;
            _grid.ClearSelection();
            UpdateSummary();
        }

        private void OnAddFiles(object sender, EventArgs e)
        {
            if (_busy) return;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Title = "选择要粉碎的文件";
            dlg.CheckFileExists = false;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                for (int i = 0; i < dlg.FileNames.Length; i++) AddItem(dlg.FileNames[i]);
            }
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            if (_busy) return;
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择要粉碎的文件夹";
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
            {
                AddItem(dlg.SelectedPath);
            }
        }

        private void OnRemove(object sender, EventArgs e)
        {
            if (_busy) return;
            RemoveSelected();
        }

        private void OnClear(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_items.Count == 0) return;
            _grid.Rows.Clear();
            _items.Clear();
            UpdateSummary();
            SetSubtitle("已清空列表。", Theme.TextSecondary);
        }

        private void RemoveSelected()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先选择要移除的项目。");
                return;
            }
            for (int i = _grid.SelectedRows.Count - 1; i >= 0; i--)
            {
                int r = _grid.SelectedRows[i].Index;
                if (r < 0 || r >= _items.Count) continue;
                ShredEntry entry = _grid.Rows[r].Tag as ShredEntry;
                if (entry != null) _items.Remove(entry);
                _grid.Rows.RemoveAt(r);
            }
            UpdateSummary();
        }

        private void OpenItem(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _items.Count) return;
            ShredEntry entry = _items[rowIndex];
            if (entry == null) return;
            try
            {
                if (entry.IsDir) Shell.OpenPath(entry.Path);
                else Shell.Run("explorer.exe", "/select,\"" + entry.Path + "\"", 0);
            }
            catch
            {
            }
        }

        private void UpdateSummary()
        {
            long total = 0;
            for (int i = 0; i < _items.Count; i++) total += _items[i].Size;

            _summary.Clear();
            _summary.Add("项目数", _items.Count + " 项");
            _summary.Add("合计大小", SysInfo.FormatSize(total), total > 0 ? Theme.Warning : Theme.TextPrimary);
            _summary.Add("擦除次数", Passes + " 次", Theme.Danger);
            _summary.Invalidate();
            Relayout();
        }

        // --------------------------------------------------------------
        // 粉碎
        // --------------------------------------------------------------

        private void OnShredSelected(object sender, EventArgs e)
        {
            if (_busy) return;
            List<ShredEntry> targets = new List<ShredEntry>();
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                int r = _grid.SelectedRows[i].Index;
                if (r >= 0 && r < _items.Count) targets.Add(_items[r]);
            }
            if (targets.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先选择要粉碎的项目。");
                return;
            }
            ShredCore(targets);
        }

        private void OnShredAll(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_items.Count == 0)
            {
                Dialog.Info(this, "列表为空", "请先添加要粉碎的文件或文件夹。");
                return;
            }
            ShredCore(new List<ShredEntry>(_items));
        }

        private void ShredCore(List<ShredEntry> targets)
        {
            int passes = Passes;
            long total = 0;
            for (int i = 0; i < targets.Count; i++) total += targets[i].Size;

            string message = "即将安全删除 " + targets.Count + " 个项目，合计 " + SysInfo.FormatSize(total) +
                "，使用「覆盖 " + passes + " 次」方式。\r\n\r\n删除后内容将难以恢复，是否继续？";
            if (!Dialog.Confirm(this, "确认粉碎", message)) return;

            _busy = true;
            UpdateActions();
            SetSubtitle("正在粉碎（覆盖 " + passes + " 次）…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Shredder.ShredResult result = new Shredder.ShredResult();
                int done = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    Shredder.ShredResult one = Shredder.Shred(targets[i].Path, passes);
                    result.Bytes += one.Bytes;
                    result.Files += one.Files;
                    result.Errors += one.Errors;
                    for (int k = 0; k < one.ErrorMessages.Count; k++) result.ErrorMessages.Add(one.ErrorMessages[k]);

                    done++;
                    int cur = done;
                    Post(delegate
                    {
                        SetSubtitle("正在粉碎… " + cur + "/" + targets.Count, Theme.Warning);
                    });
                }

                Post(delegate
                {
                    _busy = false;
                    UpdateActions();

                    for (int i = 0; i < targets.Count; i++)
                    {
                        int idx = _items.IndexOf(targets[i]);
                        if (idx >= 0)
                        {
                            _items.RemoveAt(idx);
                            if (idx < _grid.Rows.Count) _grid.Rows.RemoveAt(idx);
                        }
                    }
                    _grid.ClearSelection();
                    UpdateSummary();

                    string text = "已安全删除 " + result.Files + " 个项目，覆盖写入 " +
                        SysInfo.FormatSize(result.Bytes) + "。";
                    if (result.Errors > 0)
                    {
                        text += "\r\n有 " + result.Errors + " 个项目未能删除（可能被占用）：\r\n";
                        for (int i = 0; i < result.ErrorMessages.Count && i < 6; i++)
                        {
                            text += "· " + result.ErrorMessages[i] + "\r\n";
                        }
                    }

                    SetSubtitle("粉碎完成，覆盖写入 " + SysInfo.FormatSize(result.Bytes) + "。", Theme.Success);
                    Dialog.Success(this, "粉碎完成", text);
                });
            });
        }

        private void UpdateActions()
        {
            _addFileButton.Enabled = !_busy;
            _addFolderButton.Enabled = !_busy;
            _removeButton.Enabled = !_busy && _items.Count > 0;
            _clearButton.Enabled = !_busy && _items.Count > 0;
            _shredSelButton.Enabled = !_busy && _items.Count > 0;
            _shredAllButton.Enabled = !_busy && _items.Count > 0;
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
