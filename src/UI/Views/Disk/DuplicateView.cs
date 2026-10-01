// 文件说明：重复文件页（虚拟模式，数据收集在 List<DupRow>，CellValueNeeded 时填充）

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class DuplicateView : ViewBase
    {
        private sealed class DupRow
        {
            public string Path;
            public string Size;
            public string ModTime;
            public string Hash;
            public bool Selected;
        }

        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly Panel _pathRow = new Panel();
        private readonly TextBox _pathBox = new TextBox();
        private readonly Button _browseBtn = new Button();
        private readonly Panel _optRow = new Panel();
        private readonly CheckBox _recursive = new CheckBox();
        private readonly Label _progress = new Label();

        private readonly List<DupRow> _rows = new List<DupRow>();
        private bool _scanning;
        private bool _cancel;
        private AccentButton _scanButton;
        private AccentButton _stopButton;
        private AccentButton _keepOneButton;
        private AccentButton _deleteButton;

        public DuplicateView()
            : base("重复文件", "按内容查找重复文件，安全清理释放磁盘空间")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "按文件大小+内容哈希找出完全相同（重复）的文件。删除只移入回收站，可随时恢复。建议先「每组留一份」再删除。";

            _summary.Caption = "重复文件";
            _summary.IconKind = "dup";
            _summary.CaptionColor = Theme.Accent;

            _scanButton = AddAction("开始扫描", "search", ButtonVariant.Primary, OnScanClick, 120);
            _stopButton = AddAction("停止", "stop", ButtonVariant.Secondary, OnStopClick, 92);
            _stopButton.Enabled = false;
            _keepOneButton = AddAction("每组留一份", "check", ButtonVariant.Ghost, OnKeepOneClick, 130);
            _deleteButton = AddAction("删除选中(回收站)", "trash", ButtonVariant.Danger, OnDeleteClick, 150);
            _deleteButton.Enabled = false;

            BuildGrid();
            BuildPathRow();
            BuildOptRow();
            BuildLayout();
        }



        private void BuildGrid()
        {
            _grid.VirtualMode = true;
            _grid.ReadOnly = false;
            _grid.UseOwnScrollbar = true;
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.CheckOnRowClick = true;
            _grid.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _grid.AddFillColumn("文件路径", 200);
            _grid.AddTextColumn("大小", 110, true);
            _grid.AddTextColumn("修改时间", 130, false);
            _grid.AddTextColumn("组(哈希)", 130, false);

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellValuePushed += OnCellValuePushed;
        }

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            DupRow r = _rows[e.RowIndex];

            switch (e.ColumnIndex)
            {
                case 0: e.Value = r.Selected; break;
                case 1: e.Value = r.Path; break;
                case 2: e.Value = r.Size; break;
                case 3: e.Value = r.ModTime; break;
                case 4: e.Value = r.Hash; break;
            }
        }

        private void OnCellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.ColumnIndex != 0) return;
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;

            bool selected = false;
            try { selected = Convert.ToBoolean(e.Value); }
            catch { }
            _rows[e.RowIndex].Selected = selected;
            UpdateDelete();
        }

        private void BuildPathRow()
        {
            _pathRow.BackColor = Theme.WindowBg;
            _pathRow.Height = 36;

            _pathBox.BorderStyle = BorderStyle.None;
            _pathBox.BackColor = Theme.CardBg;
            _pathBox.ForeColor = Theme.TextPrimary;
            _pathBox.Font = Theme.FontBody;
            _pathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _browseBtn.FlatStyle = FlatStyle.Flat;
            _browseBtn.FlatAppearance.BorderSize = 0;
            _browseBtn.BackColor = Theme.CardBg;
            _browseBtn.ForeColor = Theme.TextPrimary;
            _browseBtn.Font = Theme.FontSmall;
            _browseBtn.Text = "浏览…";
            _browseBtn.Click += delegate
            {
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "选择要扫描重复文件的文件夹";
                dlg.SelectedPath = _pathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.SelectedPath;
            };

            _pathRow.Controls.Add(_pathBox);
            _pathRow.Controls.Add(_browseBtn);
            _pathRow.Resize += delegate
            {
                _browseBtn.SetBounds(_pathRow.Width - 86, 3, 80, 30);
                _pathBox.SetBounds(0, 3, _pathRow.Width - 92, 30);
            };
        }

        private void BuildOptRow()
        {
            _optRow.BackColor = Theme.WindowBg;
            _optRow.Height = 30;

            _recursive.AutoSize = true;
            _recursive.BackColor = Theme.WindowBg;
            _recursive.ForeColor = Theme.TextSecondary;
            _recursive.Font = Theme.FontSmall;
            _recursive.Text = "包含子文件夹";
            _recursive.Checked = true;

            _progress.AutoSize = true;
            _progress.ForeColor = Theme.TextMuted;
            _progress.Font = Theme.FontSmall;
            _progress.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            _optRow.Controls.Add(_recursive);
            _optRow.Controls.Add(_progress);
            _optRow.Resize += delegate
            {
                _recursive.SetBounds(0, 6, _recursive.Width, 22);
                LayoutRightLabel(_progress, _optRow.Width, 0, 6, 22);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 8);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_pathRow, 36, 6);
            AddFull(_optRow, 30, 10);
            AddFull(_grid, 300, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateDelete();
        }

        /// <summary>统计条 + 选项行 + 撑满剩余高度的表格（高度账统一由 ViewBase 计算）。</summary>
        private void Relayout()
        {
            LayoutGrid(_grid, _summary, 8 + 36 + 6 + 30 + 10, 220);
        }

        public override void OnActivated()
        {
        }

        // --------------------------------------------------------------

        private void OnScanClick(object sender, EventArgs e)
        {
            if (_scanning) return;
            string root = _pathBox.Text.Trim();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Dialog.Error(this, "路径无效", "请先选择一个存在的文件夹。");
                return;
            }

            _scanning = true;
            _cancel = false;
            _scanButton.Enabled = false;
            _stopButton.Enabled = true;
            _keepOneButton.Enabled = false;
            _deleteButton.Enabled = false;
            _rows.Clear();
            _grid.RowCount = 0;
            SetSubtitle("正在扫描：" + root, Theme.Warning);

            string folder = root;
            bool rec = _recursive.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                // 回投统一走基类 Post（内含 IsDisposed / 句柄判断与延迟投递队列）：
                // 这里原先自带一份 SafePost，与基类实现重复维护，且少了一层守卫。
                List<DuplicateGroup> groups = DuplicateFinder.Find(folder, rec,
                    delegate { return _cancel; },
                    delegate (ScanState s)
                    {
                        Post(delegate
                        {
                            _progress.Text = "已扫描 " + s.FilesScanned + " 个文件…";
                        });
                    });

                Post(delegate
                {
                    _scanning = false;
                    _stopButton.Enabled = false;
                    _scanButton.Enabled = true;
                    _keepOneButton.Enabled = groups.Count > 0;
                    Render(groups);
                    long wasted = 0;
                    for (int i = 0; i < groups.Count; i++) wasted += groups[i].WastedBytes;
                    _progress.Text = "扫描完成：" + groups.Count + " 组重复";

                    _summary.Clear();
                    _summary.Add("重复组", groups.Count + " 组");
                    _summary.Add("可释放", wasted > 0 ? SysInfo.FormatSize(wasted) : "0",
                        wasted > 0 ? Theme.Success : Theme.TextPrimary);
                    _summary.Invalidate();

                    SetSubtitle(groups.Count > 0
                        ? "找到 " + groups.Count + " 组重复文件，可释放 " + SysInfo.FormatSize(wasted) + "。"
                        : "未发现重复文件。", groups.Count > 0 ? Theme.Success : Theme.Warning);
                    Relayout();
                });
            });
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (_scanning) _cancel = true;
        }

        private void Render(List<DuplicateGroup> groups)
        {
            _rows.Clear();
            for (int i = 0; i < groups.Count; i++)
            {
                DuplicateGroup g = groups[i];
                for (int j = 0; j < g.Files.Count; j++)
                {
                    DupRow r = new DupRow();
                    r.Path = g.Files[j];
                    r.Size = g.SizeText;
                    r.ModTime = ModTime(g.Files[j]);
                    r.Hash = g.DisplayHash;
                    r.Selected = false;
                    _rows.Add(r);
                }
            }
            _grid.RowCount = _rows.Count;
            _grid.ClearSelection();
            UpdateDelete();
        }

        private static string ModTime(string p)
        {
            try { return new FileInfo(p).LastWriteTime.ToString("yyyy-MM-dd HH:mm"); }
            catch { return "—"; }
        }

        private void OnKeepOneClick(object sender, EventArgs e)
        {
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int i = 0; i < _rows.Count; i++)
            {
                string hash = _rows[i].Hash;
                if (hash == null) continue;
                bool first;
                if (!seen.TryGetValue(hash, out first))
                {
                    seen[hash] = true;
                    _rows[i].Selected = false; // 保留第一份
                }
                else
                {
                    _rows[i].Selected = true;  // 其余勾选删除
                }
            }
            _grid.Invalidate();
            UpdateDelete();
        }

        private void UpdateDelete()
        {
            int n = CheckedCount();
            _deleteButton.Enabled = !_scanning && n > 0;
            _progress.Text = _scanning ? _progress.Text : (n > 0 ? "已选择 " + n + " 个待删除" : _progress.Text);
        }

        private int CheckedCount()
        {
            int n = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected) n++;
            }
            return n;
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            List<string> paths = new List<string>();
            List<DupRow> kept = new List<DupRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected)
                {
                    string p = _rows[i].Path;
                    if (!string.IsNullOrEmpty(p)) paths.Add(p);
                }
                else
                {
                    kept.Add(_rows[i]);
                }
            }
            if (paths.Count == 0) return;

            if (!Dialog.ConfirmDanger(this, "删除到回收站",
                "把选中的 " + paths.Count + " 个重复文件移入回收站。",
                "可撤销：文件在回收站中可随时还原（清空回收站后不可恢复）。",
                "建议每组至少保留一份副本；磁盘空间要等清空回收站后才真正释放。",
                "移入回收站", false))
                return;

            _rows.Clear();
            _rows.AddRange(kept);
            _grid.RowCount = _rows.Count;
            _grid.ClearSelection();

            long reclaimed = 0;
            int removed = 0;

            bool ok = DuplicateFinder.Recycle(paths.ToArray());
            if (ok)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    try { reclaimed += new FileInfo(paths[i]).Length; } catch { }
                }
                removed = paths.Count;
                Dialog.Success(this, "已移至回收站",
                    "已将 " + removed + " 个文件移入回收站，合计约 " + SysInfo.FormatSize(reclaimed) + "。");
                _keepOneButton.Enabled = _rows.Count > 0;
                UpdateDelete();
            }
            else
            {
                Dialog.Error(this, "删除失败", "无法将所选文件移入回收站，可能文件正被占用或路径无效。");
            }
        }
    }
}