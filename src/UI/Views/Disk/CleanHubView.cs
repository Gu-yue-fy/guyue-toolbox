using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class CleanHubView : ViewBase
    {
        /// <summary>统一的行模型：把垃圾类别与使用痕迹项收敛成同一种可选 FDentry。</summary>
        private sealed class Row
        {
            public bool IsJunk;
            public JunkCategory Junk;
            public PrivacyItem Privacy;

            public bool Selected
            {
                get { return IsJunk ? Junk.Selected : Privacy.Selected; }
                set { if (IsJunk) Junk.Selected = value; else Privacy.Selected = value; }
            }

            public bool Scanned
            {
                get { return IsJunk ? Junk.Scanned : Privacy.Scanned; }
                set { if (IsJunk) Junk.Scanned = value; else Privacy.Scanned = value; }
            }

            public int Count { get { return IsJunk ? Junk.FileCount : Privacy.Count; } }
            public long Size { get { return IsJunk ? Junk.Size : 0; } }
            public string Name { get { return IsJunk ? Junk.Name : Privacy.Name; } }
            public string Description { get { return IsJunk ? Junk.Description : Privacy.Description; } }
            public bool Advanced { get { return IsJunk && Junk.Advanced; } }

            public static Row OfJunk(JunkCategory c)
            {
                Row r = new Row();
                r.IsJunk = true;
                r.Junk = c;
                return r;
            }

            public static Row OfPrivacy(PrivacyItem it)
            {
                Row r = new Row();
                r.IsJunk = false;
                r.Privacy = it;
                return r;
            }
        }

        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly JunkScanner _scanner = new JunkScanner();

        private readonly List<JunkCategory> _junk = JunkScanner.BuildDefaultCategories();
        private readonly List<PrivacyItem> _privacy = PrivacyCleaner.BuildItems();
        private readonly List<Row> _rows = new List<Row>();

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _scanning;
        private bool _scanned;
        private AccentButton _scanButton;
        private AccentButton _cleanButton;
        private AccentButton _toggleButton;

        public CleanHubView()
            : base("清理", "系统垃圾与使用痕迹，一次扫描两类、一次清理干净")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "「类型」列区分垃圾文件与使用痕迹。勾选后点「清理选中项」即可同时处理两类；"
                + "正在被占用的文件会自动跳过，不影响系统与程序功能。";

            _summary.Caption = "扫描结果";
            _summary.IconKind = "clean";
            _summary.CaptionColor = Theme.Accent;

            for (int i = 0; i < _junk.Count; i++) _rows.Add(Row.OfJunk(_junk[i]));
            for (int i = 0; i < _privacy.Count; i++) _rows.Add(Row.OfPrivacy(_privacy[i]));

            // 一条动作条只留一个主按钮：主操作是「一键清理（推荐项）」，
            // 扫描与选中项清理分别是次要动作与破坏性动作（破坏性动作另有二次确认）。
            _scanButton = AddAction("开始扫描", "search", ButtonVariant.Secondary, OnScanClick, 118);
            _cleanButton = AddAction("清理选中项", "trash", ButtonVariant.Danger, OnCleanClick, 150);
            AddAction("一键清理（推荐项）", "bolt", ButtonVariant.Primary, OnQuickClean, 150);
            _toggleButton = AddAction("全选", "check", ButtonVariant.Secondary, OnToggleAllClick, 92);

            BuildGrid();
            BuildLayout();
        }

        private void BuildGrid()
        {
            // 虚拟模式：行数据来自 _rows + 原对象，绘制时按需填充，避免逐行创建对象
            _grid.VirtualMode = true;
            _grid.ReadOnly = false;
            _grid.UseOwnScrollbar = true;
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.CheckOnRowClick = true;
            _grid.AddTextColumn("类型", 64, false);
            _grid.AddTextColumn("项目", 170, false);
            _grid.AddFillColumn("说明", 230);
            _grid.AddTextColumn("数量", 90, true);
            _grid.AddTextColumn("占用空间", 110, true);

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellValuePushed += OnCellValuePushed;
            _grid.CellFormatting += OnCellFormatting;
            _grid.CellMouseDoubleClick += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0) return;
                OpenRowFolder(e.RowIndex);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            // 自建表格必须开自带滚动条：DarkGrid 默认 ScrollBars.None，
            // 行数超过可视高度时底下的分类直接"被挡住"且无法滚动到（用户反馈的原话）
            _grid.UseOwnScrollbar = true;
            AddFull(_grid, 300, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            Populate();
        }

        /// <summary>统计条 + 撑满剩余高度的表格（高度账统一由 ViewBase 计算）。</summary>
        private void Relayout()
        {
            LayoutGrid(_grid, _summary, 0, 200);
        }

        private void Populate()
        {
            _grid.RowCount = _rows.Count;
            _grid.ClearSelection();
            _grid.Invalidate();
        }

        // --------------------------------------------------------------
        // 网格取值
        // --------------------------------------------------------------

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            Row r = _rows[e.RowIndex];

            string countText;
            if (r.Scanned) countText = r.Count.ToString();
            else if (_scanning) countText = "…";
            else if (_scanned) countText = "—";
            else countText = "未扫描";

            switch (e.ColumnIndex)
            {
                case 0: e.Value = r.Selected; break;
                case 1: e.Value = r.IsJunk ? "垃圾" : "痕迹"; break;
                case 2: e.Value = r.Name; break;
                case 3: e.Value = r.Description; break;
                case 4: e.Value = countText; break;
                case 5:
                    if (!r.IsJunk) e.Value = "—";
                    else if (r.Scanned) e.Value = SysInfo.FormatSize(r.Size);
                    else e.Value = (_scanning ? "扫描中" : "未扫描");
                    break;
            }
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            Row r = _rows[e.RowIndex];

            if (e.ColumnIndex == 1)
            {
                e.CellStyle.ForeColor = r.IsJunk ? Theme.Warning : Theme.Cyan;
                e.FormattingApplied = true;
                return;
            }
            if (e.ColumnIndex == 3 && r.Advanced)
            {
                e.CellStyle.ForeColor = Theme.TextMuted;
                e.FormattingApplied = true;
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
            UpdateSummary();
        }

        private void OnToggleAllClick(object sender, EventArgs e)
        {
            bool anyUnselected = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].Selected) { anyUnselected = true; break; }
            }

            for (int i = 0; i < _rows.Count; i++) _rows[i].Selected = anyUnselected;
            _grid.Invalidate();

            _toggleButton.Text = anyUnselected ? "取消全选" : "全选";
            _toggleButton.FitToText(84);
            LayoutActions();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int junkCount = 0, privCount = 0, files = 0, traces = 0;
            long total = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.Selected) continue;
                if (r.IsJunk)
                {
                    junkCount++;
                    total += r.Size;
                    files += r.Junk.FileCount;
                }
                else
                {
                    privCount++;
                    if (r.Scanned) traces += r.Privacy.Count;
                }
            }

            _summary.Clear();
            _summary.Add("已选项", (junkCount + privCount) + " 项");
            _summary.Add("可释放", SysInfo.FormatSize(total), total > 0 ? Theme.Success : Theme.TextPrimary);
            _summary.Add("垃圾文件", files + " 个");
            _summary.Add("使用痕迹", traces > 0 ? traces + " 条" : "—",
                traces > 0 ? Theme.Warning : Theme.TextPrimary);
            _summary.Invalidate();

            Relayout();
        }

        // --------------------------------------------------------------
        // 扫描（两类一次完成）
        // --------------------------------------------------------------

        private void OnScanClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                _scanner.Cancel();
                SetSubtitle("正在取消扫描…", Theme.Warning);
                return;
            }
            BeginScan();
        }

        private void BeginScan()
        {
            _busy = true;
            _scanning = true;
            _scanner.Reset();
            _scanButton.Text = "取消扫描";
            _scanButton.Variant = ButtonVariant.Secondary;
            _scanButton.FitToText(96);
            _scanButton.Invalidate();
            LayoutActions();
            _cleanButton.Enabled = false;
            _toggleButton.Enabled = false;
            SetSubtitle("准备扫描…", Theme.Warning);
            _grid.Invalidate();

            PreparePrivacyScan();   // 剪贴板必须在 UI 线程扫描

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    Post(delegate { SetSubtitle("正在并行扫描垃圾分类…", Theme.Warning); });
                    ScanJunkParallel();

                    Post(delegate { SetSubtitle("正在扫描使用痕迹…", Theme.Warning); });
                    ScanPrivacyItems();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    ScanFinishedUi(error);
                });
            });
        }

        /// <summary>垃圾类别在列表前段，故类别下标即行下标。</summary>
        private void ScanJunkParallel()
        {
            _scanner.ScanAll(_junk, delegate (int index)
            {
                int rowIndex = index;
                Post(delegate
                {
                    if (rowIndex < _grid.RowCount) _grid.InvalidateRow(rowIndex);
                });
            });
        }

        /// <summary>使用痕迹逐个扫描（剪贴板项已在 UI 线程处理，这里跳过）。</summary>
        private void ScanPrivacyItems()
        {
            for (int i = 0; i < _privacy.Count; i++)
            {
                PrivacyItem it = _privacy[i];
                if (it.Id == "clipboard") continue;

                PrivacyCleaner.Scan(it);

                int rowIndex = _junk.Count + i;   // 局部变量：闭包捕获用，避免共享循环变量
                Post(delegate
                {
                    if (rowIndex < _grid.RowCount) _grid.InvalidateRow(rowIndex);
                });
            }
        }

        /// <summary>扫描前在 UI 线程处理剪贴板（STA 限制），并重置其余痕迹项状态。</summary>
        private void PreparePrivacyScan()
        {
            for (int i = 0; i < _privacy.Count; i++)
            {
                PrivacyItem it = _privacy[i];
                if (it.Id == "clipboard")
                {
                    it.Count = PrivacyCleaner.ScanClipboard();
                    it.Scanned = true;
                }
                else
                {
                    it.Scanned = false;
                }
            }
        }

        private void ScanFinishedUi(string error)
        {
            _busy = false;
            _scanning = false;
            _scanned = true;
            _scanButton.Text = "开始扫描";
            _scanButton.Variant = ButtonVariant.Primary;
            _scanButton.FitToText(96);
            _scanButton.Invalidate();
            LayoutActions();
            _cleanButton.Enabled = true;
            _toggleButton.Enabled = true;
            _grid.Invalidate();
            UpdateSummary();

            long canFree = 0;
            int traces = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.Selected) continue;
                if (r.IsJunk) canFree += r.Size;
                else if (r.Scanned) traces += r.Privacy.Count;
            }

            SetSubtitle("扫描完成：可释放 " + SysInfo.FormatSize(canFree) + "，使用痕迹 " + traces + " 条。",
                (canFree > 0 || traces > 0) ? Theme.Warning : Theme.Success);

            if (error != null) Dialog.Error(this, "扫描出错", error);
        }

        // --------------------------------------------------------------
        // 清理（按类型分派到各自的清理器）
        // --------------------------------------------------------------

        private void OnCleanClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<JunkCategory> junkTargets = new List<JunkCategory>();
            List<PrivacyItem> privacyTargets = new List<PrivacyItem>();
            CollectTargets(junkTargets, privacyTargets);

            if (junkTargets.Count == 0 && privacyTargets.Count == 0)
            {
                Dialog.Info(this, "没有选中项目", "请先勾选需要清理的项目。");
                return;
            }

            ConfirmAndClean(junkTargets, privacyTargets);
        }

        /// <summary>一键清理推荐项：非高级垃圾 + 全部使用痕迹，扫描后就清理。</summary>
        private void OnQuickClean(object sender, EventArgs e)
        {
            if (_busy) return;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                r.Selected = r.IsJunk ? !r.Advanced : true;
            }
            _grid.Invalidate();
            UpdateSummary();

            _busy = true;
            _scanning = true;
            _scanner.Reset();
            _scanButton.Enabled = false;
            _cleanButton.Enabled = false;
            _toggleButton.Enabled = false;
            SetSubtitle("正在扫描推荐清理项…", Theme.Warning);

            PreparePrivacyScan();

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ScanJunkParallel();
                    ScanPrivacyItems();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    _scanning = false;
                    _scanned = true;
                    _scanButton.Enabled = true;
                    _cleanButton.Enabled = true;
                    _toggleButton.Enabled = true;
                    _grid.Invalidate();
                    UpdateSummary();

                    List<JunkCategory> jt = new List<JunkCategory>();
                    List<PrivacyItem> pt = new List<PrivacyItem>();
                    CollectTargets(jt, pt);
                    if (jt.Count == 0 && pt.Count == 0)
                    {
                        Dialog.Info(this, "无需清理", "没有可清理的推荐项。");
                        return;
                    }
                    ConfirmAndClean(jt, pt);
                });
            });
        }

        private void CollectTargets(List<JunkCategory> junkTargets, List<PrivacyItem> privacyTargets)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                if (!r.Selected) continue;
                if (r.IsJunk) junkTargets.Add(r.Junk);
                else privacyTargets.Add(r.Privacy);
            }
        }

        private void ConfirmAndClean(List<JunkCategory> junkTargets, List<PrivacyItem> privacyTargets)
        {
            if (_busy) return;

            long total = 0;
            for (int i = 0; i < junkTargets.Count; i++) total += junkTargets[i].Size;

            int traces = 0;
            for (int i = 0; i < privacyTargets.Count; i++)
            {
                if (privacyTargets[i].Scanned) traces += privacyTargets[i].Count;
            }

            string line1 = "清理 " + junkTargets.Count + " 个垃圾类别（预计释放 " + SysInfo.FormatSize(total) +
                "）与 " + privacyTargets.Count + " 个隐私项（" + traces + " 条痕迹）。";

            if (!Dialog.ConfirmDanger(this, "清理", line1,
                "部分可撤销：垃圾多为缓存会自动重建，但回收站清空后无法恢复，使用痕迹删除后也不可找回。",
                "正在被占用的文件会自动跳过；不影响系统与程序功能，也不涉及个人文件。",
                "开始清理", false))
                return;

            RunClean(junkTargets, privacyTargets);
        }

        private void RunClean(List<JunkCategory> junkTargets, List<PrivacyItem> privacyTargets)
        {
            _busy = true;
            _scanner.Reset();
            _cleanButton.Enabled = false;
            _scanButton.Enabled = false;
            _toggleButton.Enabled = false;
            SetSubtitle("正在清理…", Theme.Warning);

            List<string> errors = new List<string>();

            // 剪贴板必须在 UI 线程清理
            for (int i = 0; i < privacyTargets.Count; i++)
            {
                if (privacyTargets[i].Id != "clipboard") continue;
                string err;
                if (!PrivacyCleaner.CleanClipboard(out err)) errors.Add("剪贴板：" + err);
                else privacyTargets[i].Count = 0;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                JunkScanner.CleanResult jr = null;
                string fatal = null;
                try
                {
                    if (junkTargets.Count > 0) jr = _scanner.Clean(junkTargets);
                }
                catch (Exception ex)
                {
                    fatal = ex.Message;
                }

                int cleanedPrivacy = 0;
                for (int i = 0; i < privacyTargets.Count; i++)
                {
                    PrivacyItem it = privacyTargets[i];
                    if (it.Id == "clipboard") continue;
                    string err;
                    if (!PrivacyCleaner.Clean(it, out err)) errors.Add(it.Name + "：" + err);
                    else cleanedPrivacy++;
                }

                Post(delegate
                {
                    CleanFinishedUi(junkTargets, privacyTargets, jr, cleanedPrivacy, errors, fatal);
                });
            });
        }

        private void CleanFinishedUi(List<JunkCategory> junkTargets, List<PrivacyItem> privacyTargets,
            JunkScanner.CleanResult jr, int cleanedPrivacy, List<string> errors, string fatal)
        {
            _busy = false;
            _cleanButton.Enabled = true;
            _scanButton.Enabled = true;
            _toggleButton.Enabled = true;

            // 清理后 Size / Count 已归零，标记已扫描让其显示 0
            for (int i = 0; i < junkTargets.Count; i++) junkTargets[i].Scanned = true;
            for (int i = 0; i < privacyTargets.Count; i++) privacyTargets[i].Scanned = true;
            _grid.Invalidate();
            UpdateSummary();

            long freed = jr == null ? 0 : jr.FreedBytes;
            string text = "";

            if (junkTargets.Count > 0)
            {
                text += "垃圾：删除 " + (jr == null ? 0 : jr.DeletedFiles) + " 个文件，释放 " +
                    SysInfo.FormatSize(freed) + "。";
                if (jr != null && jr.SkippedFiles > 0)
                {
                    text += "\r\n有 " + jr.SkippedFiles + " 个文件正在使用中，已被跳过。";
                }
            }
            if (privacyTargets.Count > 0)
            {
                if (text.Length > 0) text += "\r\n\r\n";
                text += "痕迹：清理 " + cleanedPrivacy + " 个隐私项。";
            }
            if (fatal != null) text += "\r\n\r\n垃圾清理失败：" + fatal;
            if (errors.Count > 0)
            {
                text += "\r\n\r\n部分项目报告错误：\r\n";
                for (int i = 0; i < errors.Count && i < 6; i++) text += "· " + errors[i] + "\r\n";
            }

            SetSubtitle("清理完成，释放 " + SysInfo.FormatSize(freed) + "。", Theme.Success);
            Dialog.Success(this, "清理完成", text);
        }

        /// <summary>双击垃圾行打开对应目录（痕迹项无目录可开）。</summary>
        private void OpenRowFolder(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rows.Count) return;
            Row r = _rows[rowIndex];
            if (!r.IsJunk) return;

            JunkCategory c = r.Junk;
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
        }
    }
}
