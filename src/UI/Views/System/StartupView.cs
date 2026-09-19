using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class StartupView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly List<StartupItem> _items = new List<StartupItem>();
        private readonly TextBox _search = new TextBox();
        /// <summary>指向文件已不存在的启动项命令：后台加载时一次算好（见 CollectMissing），供填充与统计复用。</summary>
        private HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppress;
        private bool _busy;

        public StartupView()
            : base("启动项管理", "查看并控制开机自动运行的程序")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "取消勾选即可禁用启动项（可随时重新启用）。删除会永久移除该项，请谨慎操作。";

            _summary.Caption = "启动项统计";
            _summary.IconKind = "startup";
            _summary.CaptionColor = Theme.Warning;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            AddAction("全部启用", "check", ButtonVariant.Ghost, OnEnableAll, 110);
            AddAction("全部禁用", "shield", ButtonVariant.Ghost, OnDisableAll, 110);
            AddAction("打开所在位置", "folder", ButtonVariant.Ghost, OnOpenLocation, 130);
            AddAction("删除选中项", "trash", ButtonVariant.Danger, OnDeleteClick, 120);
            AddAction("启用选中项", "check", ButtonVariant.Ghost, OnEnableSelected, 120);
            AddAction("禁用选中项", "shield", ButtonVariant.Ghost, OnDisableSelected, 120);
            AddAction("复制命令", "copy", ButtonVariant.Ghost, OnCopyCommand, 110);

            BuildGrid();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrid()
        {
            _grid.UseOwnScrollbar = true;
            _grid.ReadOnly = false; // DarkGrid 默认全表只读——不放开勾选列永远点不动
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.CheckOnRowClick = true;
            _grid.ColumnClickSort = true;
            _grid.AddTextColumn("名称", 200, false);
            _grid.AddTextColumn("来源", 170, false);
            _grid.AddTextColumn("发布者 / 程序", 140, false);
            _grid.AddFillColumn("启动命令", 200);
            _grid.AddTextColumn("状态", 90, false);

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

            FlowLayoutPanel searchRow = MakeRow(30, 10);
            Label sl = new Label();
            sl.Text = "搜索：";
            sl.ForeColor = Theme.TextSecondary;
            sl.Font = Theme.FontBody;
            sl.AutoSize = true;
            sl.Margin = new Padding(0, 0, 8, 0);
            searchRow.Controls.Add(sl);
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Theme.FontBody;
            _search.Size = new Size(240, 28);
            Native.SetCue(_search, "搜索启动项…");
            _search.TextChanged += delegate { ApplyFilter(); };
            searchRow.Controls.Add(_search);
            AddRow(searchRow);

            AddFull(_grid, 340, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            int summaryHeight = _summary.PreferredHeight;
            _summary.Height = summaryHeight;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryHeight;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryHeight + 18 + 30 + 10;
            int avail = ViewportHeight - used;
            if (avail < 190) avail = 190;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (_items.Count == 0) Load();
        }

        /// <summary>
        /// 在后台线程一次性判定哪些启动项指向的文件已不存在（每个项一次 File.Exists），
        /// 避免填充表格、以及之后每次勾选变化都在 UI 线程重复发 IO。
        /// </summary>
        private static HashSet<string> CollectMissing(List<StartupItem> items)
        {
            HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items == null) return missing;
            for (int i = 0; i < items.Count; i++)
            {
                StartupItem it = items[i];
                if (it == null || string.IsNullOrEmpty(it.Command)) continue;
                try
                {
                    if (!StartupManager.CommandExists(it.Command)) missing.Add(it.Command);
                }
                catch
                {
                }
            }
            return missing;
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取启动项…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> loaded = null;
                HashSet<string> missing = null;
                string error = null;
                try
                {
                    loaded = StartupManager.Load();
                    missing = CollectMissing(loaded);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    _busy = false;
                    if (error != null)
                    {
                        SetSubtitle("读取启动项失败：" + error, Theme.Danger);
                        return;
                    }

                    _missing = missing != null
                        ? missing
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    _items.Clear();
                    _items.AddRange(loaded);
                    Populate();
                    Relayout();
                    UpdateSummary();
                    SetSubtitle("共发现 " + _items.Count + " 个启动项。", Theme.Success);
                });
            });
        }

        private void Populate()
        {
            _suppress = true;
            _grid.Rows.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                StartupItem it = _items[i];
                int idx = _grid.Rows.Add(it.Enabled, it.Name, it.SourceText, it.Publisher,
                    it.Command, it.StatusText);
                DataGridViewRow row = _grid.Rows[idx];
                row.Tag = it;

                if (_missing.Contains(it.Command))
                {
                    row.Cells[4].Style.ForeColor = Theme.Danger;
                    row.Cells[4].Value = it.Command + "   [文件不存在]";
                }

                if (!it.Enabled)
                {
                    row.Cells[1].Style.ForeColor = Theme.TextMuted;
                    row.Cells[5].Style.ForeColor = Theme.TextMuted;
                }
                else
                {
                    row.Cells[5].Style.ForeColor = Theme.Success;
                }
            }

            _suppress = false;
            _grid.ClearSelection();
            ApplyFilter();
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppress || e.RowIndex < 0 || e.ColumnIndex != 0) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            StartupItem it = row.Tag as StartupItem;
            if (it == null) return;

            bool desired = false;
            try { desired = Convert.ToBoolean(row.Cells[0].Value); }
            catch { }

            if (desired == it.Enabled) return;

            bool ok = StartupManager.SetEnabled(it, desired);
            if (!ok)
            {
                _suppress = true;
                row.Cells[0].Value = it.Enabled;
                _suppress = false;
                Dialog.Error(this, "操作失败",
                    "无法修改该启动项。请确认程序以管理员身份运行，或该启动项尚未被系统锁定。");
                return;
            }

            _suppress = true;
            row.Cells[5].Value = it.StatusText;
            row.Cells[1].Style.ForeColor = it.Enabled ? Theme.TextPrimary : Theme.TextMuted;
            row.Cells[5].Style.ForeColor = it.Enabled ? Theme.Success : Theme.TextMuted;
            _suppress = false;

            UpdateSummary();
            SetSubtitle("已" + (desired ? "启用" : "禁用") + "：" + it.Name, Theme.Success);
        }

        private StartupItem SelectedItem()
        {
            if (_grid.CurrentRow == null) return null;
            return _grid.CurrentRow.Tag as StartupItem;
        }

        private void OnOpenLocation(object sender, EventArgs e)
        {
            StartupItem it = SelectedItem();
            if (it == null)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择一个启动项。");
                return;
            }

            if (it.Source == StartupSource.StartupFolder)
            {
                Shell.OpenPath(Path.GetDirectoryName(it.FilePath));
                return;
            }

            string exe = StartupManager.ExtractExecutable(it.Command);
            exe = Environment.ExpandEnvironmentVariables(exe);
            try
            {
                if (File.Exists(exe))
                {
                    Shell.Run("explorer.exe", "/select,\"" + exe + "\"", 0);
                    return;
                }
            }
            catch
            {
            }
            Dialog.Warn(this, "无法定位文件", "该启动项指向的文件不存在：\r\n" + exe);
        }

        /// <summary>按搜索框过滤列表：名称 / 来源 / 发布者 / 启动命令 任一包含关键词即保留（不区分大小写）。</summary>
        private void ApplyFilter()
        {
            string q = _search.Text.Trim();
            StringComparison cmp = StringComparison.OrdinalIgnoreCase;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DataGridViewRow r = _grid.Rows[i];
                StartupItem it = r.Tag as StartupItem;
                bool show = true;
                if (q.Length > 0 && it != null)
                {
                    show = (it.Name != null && it.Name.IndexOf(q, cmp) >= 0)
                        || (it.SourceText != null && it.SourceText.IndexOf(q, cmp) >= 0)
                        || (it.Publisher != null && it.Publisher.IndexOf(q, cmp) >= 0)
                        || (it.Command != null && it.Command.IndexOf(q, cmp) >= 0);
                }
                r.Visible = show;
            }
        }

        private void OnEnableSelected(object sender, EventArgs e)
        {
            ApplyChecked(true);
        }

        private void OnDisableSelected(object sender, EventArgs e)
        {
            ApplyChecked(false);
        }

        /// <summary>对勾选的启动项批量启用 / 禁用（只处理可切换且状态不符的项）。</summary>
        private void ApplyChecked(bool enable)
        {
            if (_items.Count == 0) return;
            List<StartupItem> targets = new List<StartupItem>();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                if (!_grid.Rows[i].Visible) continue;
                bool chk = false;
                try { chk = Convert.ToBoolean(_grid.Rows[i].Cells[0].Value); }
                catch { }
                if (!chk) continue;
                StartupItem it = _grid.Rows[i].Tag as StartupItem;
                if (it != null && it.CanToggle && it.Enabled != enable) targets.Add(it);
            }
            if (targets.Count == 0)
            {
                SetSubtitle(enable ? "没有勾选可启用的启动项。" : "没有勾选可禁用的启动项。", Theme.TextSecondary);
                return;
            }

            _busy = true;
            SetSubtitle(enable ? "正在启用选中项…" : "正在禁用选中项…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0, fail = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (StartupManager.SetEnabled(targets[i], enable)) ok++; else fail++;
                }
                Post(delegate
                {
                    _busy = false;
                    Load();
                    SetSubtitle((enable ? "已启用 " : "已禁用 ") + ok + " 个选中项" +
                        (fail > 0 ? "，" + fail + " 个失败。" : "。"), fail > 0 ? Theme.Warning : Theme.Success);
                });
            });
        }

        private void OnCopyCommand(object sender, EventArgs e)
        {
            StartupItem it = SelectedItem();
            if (it == null)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择一个启动项。");
                return;
            }
            try
            {
                Clipboard.SetText(it.Command ?? "");
                SetSubtitle("已复制启动命令：" + it.Name, Theme.Success);
            }
            catch
            {
                Dialog.Error(this, "复制失败", "无法访问剪贴板。");
            }
        }

        private void OnEnableAll(object sender, EventArgs e)
        {
            if (_items.Count == 0) return;
            if (!Dialog.ConfirmDanger(this, "全部启用启动项",
                "把全部可切换的启动项设为启用，这些程序将在开机时自动运行。",
                "可撤销：可随时再点「全部禁用」，或逐项关闭。",
                "会明显延长开机时间，具体取决于启用的程序数量。",
                "全部启用", false))
                return;
            ApplyBatch(true);
        }

        private void OnDisableAll(object sender, EventArgs e)
        {
            if (_items.Count == 0) return;
            int enabled = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Enabled && _items[i].CanToggle) enabled++;
            }
            if (enabled == 0)
            {
                Dialog.Info(this, "无启用项", "当前没有已启用的启动项可禁用。");
                return;
            }
            if (!Dialog.ConfirmDanger(this, "全部禁用启动项",
                "把全部可切换的启动项设为禁用，相关软件不再随系统自动启动。",
                "可撤销：状态随启动项记录保存，可随时重新启用。",
                "开机更快；但安全软件、输入法、驱动管理类程序的自启动也会被一并关闭，请自行判断。",
                "全部禁用", false))
                return;
            ApplyBatch(false);
        }

        private void ApplyBatch(bool enable)
        {
            _busy = true;
            SetSubtitle(enable ? "正在启用全部启动项…" : "正在禁用全部启动项…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                int fail = 0;
                for (int i = 0; i < _items.Count; i++)
                {
                    StartupItem it = _items[i];
                    if (!it.CanToggle) continue;
                    if (it.Enabled == enable) { ok++; continue; }
                    if (StartupManager.SetEnabled(it, enable)) ok++;
                    else fail++;
                }

                Post(delegate
                {
                    _busy = false;
                    Load();
                    SetSubtitle((enable ? "已启用 " : "已禁用 ") + ok + " 个启动项" +
                        (fail > 0 ? "，" + fail + " 个失败。" : "。"),
                        fail > 0 ? Theme.Warning : Theme.Success);
                });
            });
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            StartupItem it = SelectedItem();
            if (it == null)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择一个启动项。");
                return;
            }

            if (!Dialog.ConfirmDanger(this, "删除启动项",
                "删除「" + it.Name + "」的启动记录，该程序不再随系统自动启动。",
                "不可撤销：本工具不为启动项删除建备份，需要时只能重新手动添加。",
                "位置：" + it.Location + "\r\n命令：" + it.Command,
                "删除", true))
                return;

            if (StartupManager.Delete(it))
            {
                Load();
                SetSubtitle("已删除启动项：" + it.Name, Theme.Success);
            }
            else
            {
                Dialog.Error(this, "删除失败", "无法删除该启动项，请确认是否具有相应权限。");
            }
        }

        private void UpdateSummary()
        {
            int enabled = 0;
            int disabled = 0;
            int missing = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Enabled) enabled++; else disabled++;
                if (_missing.Contains(_items[i].Command)) missing++;
            }

            _summary.Clear();
            _summary.Add("启动项总数", _items.Count + " 项");
            _summary.Add("已启用", enabled + " 项", enabled > 0 ? Theme.Success : Theme.TextPrimary);
            _summary.Add("已禁用", disabled + " 项");
            _summary.Add("无效项", missing + " 项", missing > 0 ? Theme.Danger : Theme.TextPrimary);
            _summary.Invalidate();
            Relayout();
        }    }
}
