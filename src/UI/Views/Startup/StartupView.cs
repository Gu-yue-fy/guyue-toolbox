// 启动项管理页：查看并控制开机自动运行的程序

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class StartupView : GridPageView
    {
        private readonly List<StartupItem> _items = new List<StartupItem>();
        private readonly TextBox _search = new TextBox();
        /// <summary>指向文件已不存在的启动项命令：后台加载时一次算好（见 CollectMissing），供填充与统计复用。</summary>
        private HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _suppress;
        // 行内操作按钮（v2.2 从页头挪到搜索行）
        private AccentButton _openButton;
        private AccentButton _deleteButton;
        private AccentButton _enableSelectedButton;
        private AccentButton _disableSelectedButton;
        private AccentButton _copyButton;

        public StartupView()
            : base("启动项管理", "查看并控制开机自动运行的程序")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Accent;
            Notice.NoticeText = "取消勾选即可禁用启动项（可随时重新启用）。删除会永久移除该项，请谨慎操作。";

            Summary.Caption = "启动项统计";
            Summary.IconKind = "startup";
            Summary.CaptionColor = Theme.Warning;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            AddAction("全部启用", "check", ButtonVariant.Ghost, OnEnableAll, 110);
            AddAction("全部禁用", "ban", ButtonVariant.Danger, OnDisableAll, 110);
            // 打开位置/删除/启停选中项/复制命令是「选中行内操作」：
            // v2.2 从页头挪到搜索行——页头 8 个按钮在最小窗口下会被压成截断省略号。

            InitializeGridPage();
        }

        // ---- 形态参数：通知条 42+18，搜索行 34+10（含行内操作按钮），表格行占位 340 ----
        protected override int GridRowHeight { get { return 340; } }
        protected override int ExtraUsedHeight { get { return 34 + 10; } }
        protected override int MinGridHeight { get { return 190; } }

        /// <summary>首次进入时按「条目是否为空」判断，而不是靠加载标记。</summary>
        protected override bool NeedsLoad()
        {
            return _items.Count == 0;
        }

        /// <summary>本页需要可编辑的勾选列，故不采用基类的"全表只读"默认值。</summary>
        protected override void ConfigureGrid()
        {
            Grid.UseOwnScrollbar = true;
            Grid.ReadOnly = false; // DarkGrid 默认全表只读——不放开勾选列永远点不动
            Grid.Columns.Add(new DarkCheckColumn());
            Grid.CheckOnRowClick = true;
            Grid.ColumnClickSort = true;

            Grid.CurrentCellDirtyStateChanged += delegate
            {
                if (Grid.IsCurrentCellDirty) Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            Grid.CellValueChanged += OnCellValueChanged;
        }

        protected override void BuildColumns()
        {
            Grid.AddTextColumn("名称", 200, false);
            Grid.AddTextColumn("来源", 170, false);
            Grid.AddTextColumn("发布者 / 程序", 140, false);
            Grid.AddFillColumn("启动命令", 200);
            Grid.AddTextColumn("状态", 90, false);
        }

        /// <summary>统计行与表格之间的搜索行 + 行内操作按钮（本页特有，故走布局钩子）。</summary>
        protected override void AddExtraLayoutRows()
        {
            FlowLayoutPanel searchRow = MakeRow(34, 10);
            Label sl = new Label();
            sl.Text = "搜索：";
            sl.ForeColor = Theme.TextSecondary;
            sl.Font = Theme.FontBody;
            sl.AutoSize = true;
            sl.Margin = new Padding(0, 2, 8, 0);
            searchRow.Controls.Add(sl);
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Theme.FontBody;
            _search.Size = new Size(240, 28);
            Native.SetCue(_search, "搜索启动项…");
            _search.TextChanged += delegate { ApplyFilter(); };
            searchRow.Controls.Add(ThemeInput.WrapSearch(_search));

            // 行内操作（选中行后可用；"打开位置"用短文案：5 个按钮在 1180 最小窗口要一行放下）
            _openButton = MakeRowButton("打开位置", "folder", ButtonVariant.Ghost, OnOpenLocation, 104);
            _deleteButton = MakeRowButton("删除选中项", "trash", ButtonVariant.Danger, OnDeleteClick, 120);
            _enableSelectedButton = MakeRowButton("启用选中项", "check", ButtonVariant.Ghost, OnEnableSelected, 120);
            _disableSelectedButton = MakeRowButton("禁用选中项", "shield", ButtonVariant.Ghost, OnDisableSelected, 120);
            _copyButton = MakeRowButton("复制命令", "copy", ButtonVariant.Ghost, OnCopyCommand, 110);
            searchRow.Controls.Add(_openButton);
            searchRow.Controls.Add(_deleteButton);
            searchRow.Controls.Add(_enableSelectedButton);
            searchRow.Controls.Add(_disableSelectedButton);
            searchRow.Controls.Add(_copyButton);
            AddRow(searchRow);
        }

        /// <summary>搜索行小按钮：高 30，FlowLayoutPanel 自然排布不换行不重叠。</summary>
        private AccentButton MakeRowButton(string text, string icon, ButtonVariant variant,
            EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = icon;
            b.Variant = variant;
            // 宽度按文字自适应：原先把 width 当固定宽，文字长一点就会被内部省略号截断
            b.Height = Theme.RowButtonHeight;
            b.FitToText(width);
            b.Width = Math.Max(width, b.NaturalWidth);
            b.Margin = new Padding(8, 0, 0, 0);
            b.NaturalWidth = b.Width;
            if (onClick != null) b.Click += onClick;
            return b;
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

        protected override void Load()
        {
            if (Busy) return;
            Busy = true;
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
                    Busy = false;
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
            Grid.Rows.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                StartupItem it = _items[i];
                int idx = Grid.Rows.Add(it.Enabled, it.Name, it.SourceText, it.Publisher,
                    it.Command, it.StatusText);
                DataGridViewRow row = Grid.Rows[idx];
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
            Grid.ClearSelection();
            ApplyFilter();
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppress || e.RowIndex < 0 || e.ColumnIndex != 0) return;

            DataGridViewRow row = Grid.Rows[e.RowIndex];
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
            if (Grid.CurrentRow == null) return null;
            return Grid.CurrentRow.Tag as StartupItem;
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
                    Shell.OpenSelect(exe);
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
            for (int i = 0; i < Grid.Rows.Count; i++)
            {
                DataGridViewRow r = Grid.Rows[i];
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
            for (int i = 0; i < Grid.Rows.Count; i++)
            {
                if (!Grid.Rows[i].Visible) continue;
                bool chk = false;
                try { chk = Convert.ToBoolean(Grid.Rows[i].Cells[0].Value); }
                catch { }
                if (!chk) continue;
                StartupItem it = Grid.Rows[i].Tag as StartupItem;
                if (it != null && it.CanToggle && it.Enabled != enable) targets.Add(it);
            }
            if (targets.Count == 0)
            {
                SetSubtitle(enable ? "没有勾选可启用的启动项。" : "没有勾选可禁用的启动项。", Theme.TextSecondary);
                return;
            }

            Busy = true;
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
                    Busy = false;
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
            Busy = true;
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
                    Busy = false;
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

            Summary.Clear();
            Summary.Add("启动项总数", _items.Count + " 项");
            Summary.Add("已启用", enabled + " 项", enabled > 0 ? Theme.Success : Theme.TextPrimary);
            Summary.Add("已禁用", disabled + " 项");
            Summary.Add("无效项", missing + " 项", missing > 0 ? Theme.Danger : Theme.TextPrimary);
            Summary.Invalidate();
            Relayout();
        }
    }
}
