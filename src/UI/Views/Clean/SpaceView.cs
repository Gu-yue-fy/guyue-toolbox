using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 磁盘空间分析：找出指定磁盘里体积最大的文件与占用最多的目录，
    /// 便于定位"空间到底被谁吃了"。
    /// </summary>
    public sealed class SpaceView : ViewBase
    {
        private readonly DarkGrid _fileGrid = new DarkGrid();
        private readonly DarkGrid _dirGrid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly DiskAnalyzer _analyzer = new DiskAnalyzer();
        private DiskAnalyzer.Result _result;

        private string _root = "";
        private bool _busy;
        private AccentButton _runButton;
        private Control _fileCard;
        private Control _dirCard;

        public SpaceView()
            : base("空间分析", "找出磁盘里体积最大的文件与占用最多的目录")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "分析过程只读取文件信息，不会修改或删除任何内容。";

            _summary.Caption = "分析概况";
            _summary.IconKind = "disk";
            _summary.CaptionColor = Theme.Accent;

            AddAction("选择磁盘", "folder", ButtonVariant.Secondary, OnPickDrive, 110);
            _runButton = AddAction("开始分析", "refresh", ButtonVariant.Primary, OnRunClick, 118);
            AddAction("打开所在位置", "folder", ButtonVariant.Ghost, OnOpenLocation, 130);
            AddAction("删除选中文件", "trash", ButtonVariant.Danger, OnDeleteClick, 130);

            BuildGrids();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrids()
        {
            _fileGrid.UseOwnScrollbar = true;
            _fileGrid.AddTextColumn("文件名", 240, false);
            _fileGrid.AddTextColumn("大小", 110, true);
            _fileGrid.AddTextColumn("修改时间", 150, false);
            _fileGrid.AddFillColumn("所在目录", 200);
            _fileGrid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) OnOpenLocation(s, EventArgs.Empty);
            };

            _dirGrid.UseOwnScrollbar = true;
            _dirGrid.AddTextColumn("目录", 380, false);
            _dirGrid.AddTextColumn("占用空间", 120, true);
            _dirGrid.AddFillColumn("占已扫描空间", 160);
            _dirGrid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) OnOpenLocation(s, EventArgs.Empty);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            _fileCard = MakeGridCard("体积最大的文件", "bug", Theme.Danger, _fileGrid);
            AddFull(_fileCard, 220, 18);

            _dirCard = MakeGridCard("占用最多的目录", "folder", Theme.Cyan, _dirGrid);
            AddFull(_dirCard, 220, 0);

            _root = FirstFixedDrive();
            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateSummary();
        }

        private Control MakeGridCard(string title, string icon, Color accent, Control grid)
        {
            Card card = new Card();
            card.BackColor = Theme.CardBg;
            card.Radius = 12;
            card.HeaderText = title;
            card.HeaderIcon = icon;
            card.HeaderIconColor = accent;

            grid.SetBounds(14, Card.HeaderSize + 8, 100, 100);
            card.Controls.Add(grid);

            card.Resize += delegate
            {
                grid.SetBounds(14, Card.HeaderSize + 8,
                    Math.Max(60, card.Width - 28),
                    Math.Max(40, card.Height - Card.HeaderSize - 22));
            };
            return card;
        }

        private void Relayout()
        {
            int summaryHeight = _summary.PreferredHeight;
            _summary.Height = summaryHeight;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryHeight;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryHeight + 18 + 18;
            int avail = ViewportHeight - used;
            if (avail < 240) avail = 240;

            int half = (avail) / 2;
            if (half < 110) half = 110;

            if (_fileCard.Height != half) _fileCard.Height = half;
            if (_dirCard.Height != half) _dirCard.Height = half;

            RefreshLayout();
        }

        private static string FirstFixedDrive()
        {
            try
            {
                List<DiskInfo> disks = SysInfo.GetDisks(true);
                for (int i = 0; i < disks.Count; i++)
                {
                    if (disks[i].Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) return disks[i].Name;
                }
                if (disks.Count > 0) return disks[0].Name;
            }
            catch
            {
            }
            return "C:\\";
        }

        public override void OnActivated()
        {
            if (string.IsNullOrEmpty(_root)) _root = FirstFixedDrive();
            UpdateSummary();
        }

        // --------------------------------------------------------------

        private void OnPickDrive(object sender, EventArgs e)
        {
            if (_busy) return;

            List<string> options = new List<string>();
            try
            {
                List<DiskInfo> disks = SysInfo.GetDisks(true);
                for (int i = 0; i < disks.Count; i++)
                {
                    DiskInfo d = disks[i];
                    options.Add(d.Name.TrimEnd('\\') + "   (" + d.Label +
                        "，剩余 " + SysInfo.FormatSize(d.FreeBytes) + ")");
                }
            }
            catch
            {
            }
            if (options.Count == 0) options.Add("C:");

            string picked = Chooser.ChooseOne(this, "选择要分析的磁盘", options);
            if (string.IsNullOrEmpty(picked)) return;

            int idx = picked.IndexOf(' ');
            string drive = idx > 0 ? picked.Substring(0, idx) : picked;
            if (!drive.EndsWith("\\")) drive += "\\";

            _root = drive;
            SetSubtitle("已选择 " + drive + "，点击「开始分析」。", Theme.TextSecondary);
            UpdateSummary();
        }

        private void OnRunClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                _analyzer.Cancel();
                SetSubtitle("正在停止…", Theme.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root))
            {
                Dialog.Info(this, "目录不存在", "请先选择一个有效的磁盘。");
                return;
            }

            _busy = true;
            _runButton.Text = "停止分析";
            _runButton.Variant = ButtonVariant.Secondary;
            _runButton.FitToText(118);
            LayoutActions();

            _fileGrid.Rows.Clear();
            _dirGrid.Rows.Clear();
            SetSubtitle("正在分析 " + _root + " …", Theme.Warning);

            string root = _root;

            ThreadPool.QueueUserWorkItem(delegate
            {
                DiskAnalyzer.Result r = null;
                try
                {
                    r = _analyzer.Analyze(root, 200, 120, 100L * 1024 * 1024, 8);
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        _busy = false;
                        RestoreRunButton();
                        SetSubtitle("分析失败：" + ex.Message, Theme.Danger);
                    });
                    return;
                }

                Post(delegate
                {
                    _busy = false;
                    RestoreRunButton();
                    _result = r;
                    RenderResult(r, root);

                    SetSubtitle(r.Cancelled
                        ? "已停止（已扫描 " + r.FileCount + " 个文件）。"
                        : "分析完成：" + r.FileCount + " 个文件、" + r.DirectoryCount +
                          " 个目录，用时 " + r.ElapsedMs + " ms。",
                        r.Cancelled ? Theme.Warning : Theme.Success);
                });
            });
        }

        private void RestoreRunButton()
        {
            _runButton.Text = "开始分析";
            _runButton.Variant = ButtonVariant.Primary;
            _runButton.FitToText(118);
            LayoutActions();
        }

        private void RenderResult(DiskAnalyzer.Result r, string root)
        {
            _fileGrid.Rows.Clear();
            for (int i = 0; i < r.Files.Count; i++)
            {
                DiskAnalyzer.Entry e = r.Files[i];
                string dir = "";
                try { dir = Path.GetDirectoryName(e.PathText); }
                catch { }

                int idx = _fileGrid.Rows.Add(
                    e.NameText,
                    e.SizeText,
                    e.Modified == DateTime.MinValue ? "—" : e.Modified.ToString("yyyy-MM-dd HH:mm"),
                    dir);
                _fileGrid.Rows[idx].Tag = e;
            }

            _dirGrid.Rows.Clear();
            for (int i = 0; i < r.Directories.Count; i++)
            {
                DiskAnalyzer.Entry e = r.Directories[i];
                double pct = r.ScannedBytes > 0 ? e.Size * 100.0 / r.ScannedBytes : 0;

                int idx = _dirGrid.Rows.Add(
                    e.PathText,
                    e.SizeText,
                    pct.ToString("0.0") + " %");
                _dirGrid.Rows[idx].Tag = e;
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            DiskAnalyzer.Result r = _result;

            _summary.Clear();
            _summary.Add("分析目标", string.IsNullOrEmpty(_root) ? "未选择" : _root);
            if (r == null)
            {
                _summary.Add("状态", "尚未分析");
                _summary.Add("已扫描", "0 个文件");
                _summary.Add("大文件占用", "—");
            }
            else
            {
                long topSum = 0;
                for (int i = 0; i < r.Files.Count; i++) topSum += r.Files[i].Size;

                _summary.Add("已扫描", r.FileCount + " 个文件 / " + r.DirectoryCount + " 个目录", Theme.TextPrimary);
                _summary.Add("大文件合计", SysInfo.FormatSize(topSum), Theme.Warning);
                _summary.Add("用时", (r.ElapsedMs / 1000.0).ToString("0.0") + " 秒" +
                    (r.ErrorCount > 0 ? "（" + r.ErrorCount + " 处无权限）" : ""));
            }
            _summary.Invalidate();
            Relayout();
        }

        // --------------------------------------------------------------

        private DiskAnalyzer.Entry SelectedEntry()
        {
            Control focused = _fileGrid.Focused ? (Control)_fileGrid : (Control)_dirGrid;
            DataGridView grid = focused as DataGridView;
            if (grid == null || grid.CurrentRow == null) return null;

            DiskAnalyzer.Entry e = grid.CurrentRow.Tag as DiskAnalyzer.Entry;
            if (e == null)
            {
                grid = grid == _fileGrid ? _dirGrid : _fileGrid;
                if (grid.CurrentRow != null) e = grid.CurrentRow.Tag as DiskAnalyzer.Entry;
            }
            return e;
        }

        private void OnOpenLocation(object sender, EventArgs e)
        {
            DiskAnalyzer.Entry entry = SelectedEntry();
            if (entry == null)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择一行。");
                return;
            }

            string target = entry.IsDirectory ? entry.PathText : entry.PathText;
            try
            {
                if (entry.IsDirectory)
                {
                    if (Directory.Exists(target)) Shell.OpenPath(target);
                    else Dialog.Warn(this, "无法打开", "目录不存在：" + target);
                }
                else
                {
                    if (File.Exists(target)) Shell.OpenSelect(target);
                    else Dialog.Warn(this, "无法定位", "文件不存在：" + target);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "打开失败", ex.Message);
            }
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            DiskAnalyzer.Entry entry = SelectedEntry();
            if (entry == null)
            {
                Dialog.Info(this, "未选择", "请先在文件列表中选择一行。");
                return;
            }
            if (entry.IsDirectory)
            {
                Dialog.Info(this, "不支持", "为避免误删，这里不支持删除目录。请在「打开所在位置」后手工处理。");
                return;
            }

            string path = entry.PathText;
            if (!File.Exists(path))
            {
                Dialog.Warn(this, "文件不存在", path);
                return;
            }

            if (!Dialog.Confirm(this, "删除文件",
                "确定要删除这个文件吗？\r\n\r\n" + path + "\r\n大小：" + entry.SizeText +
                "\r\n\r\n此操作不可撤销，且不会进入回收站。"))
                return;

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                SetSubtitle("已删除：" + Path.GetFileName(path), Theme.Success);

                if (_result != null)
                {
                    for (int i = 0; i < _result.Files.Count; i++)
                    {
                        if (_result.Files[i].PathText == path) { _result.Files.RemoveAt(i); break; }
                    }
                    if (_result != null) RenderResult(_result, _root);
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "删除失败",
                    "无法删除该文件。\r\n\r\n常见原因：文件正在被占用，或权限不足。\r\n\r\n" + ex.Message);
            }
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
