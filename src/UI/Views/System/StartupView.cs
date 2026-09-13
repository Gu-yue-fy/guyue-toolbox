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

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(true); }, 92);
            AddAction("全部启用", "check", ButtonVariant.Ghost, OnEnableAll, 110);
            AddAction("全部禁用", "shield", ButtonVariant.Ghost, OnDisableAll, 110);
            AddAction("打开所在位置", "folder", ButtonVariant.Ghost, OnOpenLocation, 130);
            AddAction("删除选中项", "trash", ButtonVariant.Danger, OnDeleteClick, 120);

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

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryHeight + 18;
            int avail = ViewportHeight - used;
            if (avail < 190) avail = 190;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();

            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (_items.Count == 0) Load(false);
        }

        private void Load(bool force)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取启动项…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> loaded = null;
                string error = null;
                try
                {
                    loaded = StartupManager.Load();
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

                if (!StartupManager.CommandExists(it.Command))
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

        private void OnEnableAll(object sender, EventArgs e)
        {
            if (_items.Count == 0) return;
            if (!Dialog.Confirm(this, "全部启用",
                "确定要启用全部可切换的启动项吗？\r\n启用后这些程序会在开机时自动运行。"))
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
            if (!Dialog.Confirm(this, "全部禁用",
                "确定要禁用全部可切换的启动项吗？\r\n\r\n禁用后开机速度会更快，但相关软件将不再随系统自动启动（可随时重新启用）。"))
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
                    Load(true);
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

            string message = "确定要删除启动项「" + it.Name + "」吗？\r\n\r\n" +
                "位置：" + it.Location + "\r\n" +
                "命令：" + it.Command + "\r\n\r\n" +
                "删除后该程序将不再随系统启动，此操作不可撤销。";

            if (!Dialog.Confirm(this, "删除启动项", message)) return;

            if (StartupManager.Delete(it))
            {
                Load(true);
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
                if (!StartupManager.CommandExists(_items[i].Command)) missing++;
            }

            _summary.Clear();
            _summary.Add("启动项总数", _items.Count + " 项");
            _summary.Add("已启用", enabled + " 项", enabled > 0 ? Theme.Success : Theme.TextPrimary);
            _summary.Add("已禁用", disabled + " 项");
            _summary.Add("无效项", missing + " 项", missing > 0 ? Theme.Danger : Theme.TextPrimary);
            _summary.Invalidate();
            Relayout();
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
