using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class CleanerView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly JunkScanner _scanner = new JunkScanner();
        private readonly List<JunkCategory> _categories = JunkScanner.BuildDefaultCategories();

        private bool _suppress;
        private bool _busy;
        private AccentButton _scanButton;
        private AccentButton _cleanButton;
        private AccentButton _toggleButton;

        public CleanerView()
            : base("垃圾清理", "扫描并删除系统中不再需要的临时文件")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "勾选要清理的项目后点击「清理选中项」。正在被占用的文件会自动跳过，不会影响系统稳定性。";

            _summary.Caption = "扫描结果";
            _summary.IconKind = "list";
            _summary.CaptionColor = Theme.Accent;

            _scanButton = AddAction("开始扫描", "refresh", ButtonVariant.Primary, OnScanClick, 118);
            _cleanButton = AddAction("清理选中项", "trash", ButtonVariant.Danger, OnCleanClick, 150);
            AddAction("一键清理（推荐项）", "bolt", ButtonVariant.Primary, OnQuickClean, 150);
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
            // 文本列在 AddTextColumn/AddFillColumn 内已设列级只读，
            // 网格级保持可编辑才能让勾选列响应点击
            _grid.ReadOnly = false;
            _grid.UseOwnScrollbar = true;
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.CheckOnRowClick = true;;
            _grid.AddTextColumn("类别", 200, false);
            _grid.AddFillColumn("说明", 240);
            _grid.AddTextColumn("文件数", 90, true);
            _grid.AddTextColumn("占用空间", 110, true);

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.CellMouseDoubleClick += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0) return;
                OpenCategoryFolder(e.RowIndex);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_grid, 320, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            int summaryHeight = _summary.PreferredHeight;
            _summary.Height = summaryHeight;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryHeight;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryHeight + 18;
            int avail = ViewportHeight - used;
            if (avail < 190) avail = 190;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (_grid.Rows.Count == 0) Populate();
            UpdateSummary();
        }

        private void Populate()
        {
            _suppress = true;
            _grid.Rows.Clear();
            for (int i = 0; i < _categories.Count; i++)
            {
                JunkCategory c = _categories[i];
                int idx = _grid.Rows.Add(c.Selected, c.Name, c.Description, "—", "未扫描");
                _grid.Rows[idx].Tag = c;
                if (c.Advanced)
                {
                    _grid.Rows[idx].Cells[2].Style.ForeColor = Theme.TextMuted;
                }
            }
            _suppress = false;
            _grid.ClearSelection();
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppress || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            JunkCategory c = _grid.Rows[e.RowIndex].Tag as JunkCategory;
            if (c == null) return;

            bool selected = false;
            try { selected = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[0].Value); }
            catch { }
            c.Selected = selected;
            UpdateSummary();
        }

        private void OnToggleAllClick(object sender, EventArgs e)
        {
            bool anyUnselected = false;
            for (int i = 0; i < _categories.Count; i++)
            {
                if (!_categories[i].Selected) { anyUnselected = true; break; }
            }

            _suppress = true;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                _grid.Rows[i].Cells[0].Value = anyUnselected;
                JunkCategory c = _grid.Rows[i].Tag as JunkCategory;
                if (c != null) c.Selected = anyUnselected;
            }
            _suppress = false;

            _toggleButton.Text = anyUnselected ? "取消全选" : "全选";
            _toggleButton.FitToText(84);
            LayoutActions();
            UpdateSummary();
        }

        // --------------------------------------------------------------
        // 扫描
        // --------------------------------------------------------------

        private void OnScanClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                _scanner.Cancel();
                SetSubtitle("正在取消扫描…", Theme.Warning);
                return;
            }

            _busy = true;
            _scanner.Reset();
            _scanButton.Text = "取消扫描";
            _scanButton.Variant = ButtonVariant.Secondary;
            _scanButton.FitToText(96);
            _scanButton.Invalidate();
            LayoutActions();
            _cleanButton.Enabled = false;
            SetSubtitle("准备扫描…", Theme.Warning);

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                _grid.Rows[i].Cells[3].Value = "…";
                _grid.Rows[i].Cells[4].Value = "扫描中";
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Post(delegate { SetSubtitle("正在并行扫描各分类…", Theme.Warning); });
                try
                {
                    ScanCategoriesParallel();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    _scanButton.Text = "开始扫描";
                    _scanButton.Variant = ButtonVariant.Primary;
                    _scanButton.FitToText(96);
                    _scanButton.Invalidate();
                    LayoutActions();
                    _cleanButton.Enabled = true;
                    UpdateSummary();

                    long total = 0;
                    for (int i = 0; i < _categories.Count; i++)
                    {
                        if (_categories[i].Selected) total += _categories[i].Size;
                    }
                    SetSubtitle("扫描完成，已选中项目可释放约 " + SysInfo.FormatSize(total) + "。", Theme.Success);
                });
            });
        }

        /// <summary>并行扫描所有垃圾类别：各分类的目录遍历相互独立，并发执行可显著缩短总耗时。</summary>
        private void ScanCategoriesParallel()
        {
            _scanner.ScanAll(_categories, delegate(int index)
            {
                Post(delegate
                {
                    if (index < _grid.Rows.Count)
                    {
                        JunkCategory c = _categories[index];
                        _grid.Rows[index].Cells[3].Value = c.FileCount.ToString();
                        _grid.Rows[index].Cells[4].Value = c.SizeText;
                    }
                });
            });
        }

        private void UpdateSummary()
        {
            long total = 0;
            int files = 0;
            int count = 0;
            for (int i = 0; i < _categories.Count; i++)
            {
                JunkCategory c = _categories[i];
                if (!c.Selected) continue;
                count++;
                total += c.Size;
                files += c.FileCount;
            }

            _summary.Clear();
            _summary.Add("已选清理项", count + " 项");
            _summary.Add("可释放空间", SysInfo.FormatSize(total), total > 0 ? Theme.Success : Theme.TextPrimary);
            _summary.Add("涉及文件", files + " 个");
            _summary.Invalidate();

            Relayout();
        }

        // --------------------------------------------------------------
        // 清理
        // --------------------------------------------------------------

        private List<JunkCategory> SelectedTargets()
        {
            List<JunkCategory> targets = new List<JunkCategory>();
            for (int i = 0; i < _categories.Count; i++)
            {
                if (_categories[i].Selected) targets.Add(_categories[i]);
            }
            return targets;
        }

        private void OnCleanClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<JunkCategory> targets = SelectedTargets();
            if (targets.Count == 0)
            {
                Dialog.Info(this, "没有选中项目", "请先勾选需要清理的项目。");
                return;
            }

            ConfirmAndClean(targets);
        }

        /// <summary>一键清理推荐项：选中全部非高级类别，扫描后确认清理。</summary>
        private void OnQuickClean(object sender, EventArgs e)
        {
            if (_busy) return;

            for (int i = 0; i < _categories.Count; i++)
            {
                _categories[i].Selected = !_categories[i].Advanced;
            }
            _suppress = true;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                _grid.Rows[i].Cells[0].Value = _categories[i].Selected;
            }
            _suppress = false;
            UpdateSummary();

            _busy = true;
            _scanner.Reset();
            _scanButton.Enabled = false;
            _cleanButton.Enabled = false;
            SetSubtitle("正在扫描推荐清理项…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ScanCategoriesParallel();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    _scanButton.Enabled = true;
                    _cleanButton.Enabled = true;

                    for (int i = 0; i < _grid.Rows.Count; i++)
                    {
                        JunkCategory c = _grid.Rows[i].Tag as JunkCategory;
                        if (c == null) continue;
                        _grid.Rows[i].Cells[3].Value = c.FileCount.ToString();
                        _grid.Rows[i].Cells[4].Value = c.SizeText;
                    }
                    UpdateSummary();

                    List<JunkCategory> targets = SelectedTargets();
                    if (targets.Count == 0)
                    {
                        Dialog.Info(this, "无需清理", "没有可清理的推荐项。");
                        return;
                    }
                    ConfirmAndClean(targets);
                });
            });
        }

        private void ConfirmAndClean(List<JunkCategory> targets)
        {
            if (_busy) return;

            long total = 0;
            for (int i = 0; i < targets.Count; i++) total += targets[i].Size;

            if (!Dialog.ConfirmDanger(this, "清理垃圾文件",
                "清理 " + targets.Count + " 个类别，预计释放 " + SysInfo.FormatSize(total) + "。",
                "部分可撤销：清空回收站后其中的文件无法恢复；其余为缓存，系统会按需重建。",
                "正在被程序占用的文件会被自动跳过；Windows 更新缓存清理后，已安装的更新将无法回滚。",
                "开始清理", false))
                return;

            RunClean(targets);
        }

        private void RunClean(List<JunkCategory> targets)
        {
            _busy = true;
            _scanner.Reset();
            _cleanButton.Enabled = false;
            _scanButton.Enabled = false;
            SetSubtitle("正在清理…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                JunkScanner.CleanResult result = null;
                try
                {
                    result = _scanner.Clean(targets);
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        _busy = false;
                        _cleanButton.Enabled = true;
                        _scanButton.Enabled = true;
                        Dialog.Error(this, "清理失败", ex.Message);
                    });
                    return;
                }

                Post(delegate
                {
                    _busy = false;
                    _cleanButton.Enabled = true;
                    _scanButton.Enabled = true;

                    for (int i = 0; i < _grid.Rows.Count; i++)
                    {
                        JunkCategory c = _grid.Rows[i].Tag as JunkCategory;
                        if (c == null) continue;
                        if (!targets.Contains(c)) continue;
                        _grid.Rows[i].Cells[3].Value = "0";
                        _grid.Rows[i].Cells[4].Value = "0 B";
                    }

                    UpdateSummary();

                    string text = "共删除 " + result.DeletedFiles + " 个文件，释放 " +
                        SysInfo.FormatSize(result.FreedBytes) + " 磁盘空间。";
                    if (result.SkippedFiles > 0)
                    {
                        text += "\r\n有 " + result.SkippedFiles + " 个文件正在使用中，已被跳过。";
                    }
                    if (result.Errors.Count > 0)
                    {
                        text += "\r\n\r\n部分项目报告了错误：\r\n";
                        for (int i = 0; i < result.Errors.Count && i < 6; i++)
                        {
                            text += "· " + result.Errors[i] + "\r\n";
                        }
                    }

                    SetSubtitle("清理完成，释放 " + SysInfo.FormatSize(result.FreedBytes) + "。", Theme.Success);
                    Dialog.Success(this, "清理完成", text);
                });
            });
        }

        private void OpenCategoryFolder(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            JunkCategory c = _grid.Rows[rowIndex].Tag as JunkCategory;
            if (c == null) return;
            if (c.IsRecycleBin)
            {
                Shell.Cmd("start \"\" shell:RecycleBinFolder", 0);
                return;
            }
            for (int i = 0; i < c.Directories.Count; i++)
            {
                if (System.IO.Directory.Exists(c.Directories[i]))
                {
                    Shell.OpenPath(c.Directories[i]);
                    return;
                }
            }
            Dialog.Info(this, "目录不存在", "该项对应的目录当前不存在。");
        }    }
}
