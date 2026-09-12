using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    public sealed class ProgramsView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly Panel _toolbar = new Panel();
        private readonly TextBox _search = new TextBox();
        private readonly Panel _searchWrap = new Panel();
        private readonly ComboBox _sort = new ComboBox();
        private readonly CheckBox _incUpdates = new CheckBox();
        private readonly Label _countLabel = new Label();

        private readonly List<ProgramEntry> _all = new List<ProgramEntry>();
        private bool _busy;
        private bool _loaded;
        private AccentButton _uninstallButton;
        private AccentButton _openButton;

        public ProgramsView()
            : base("已安装程序", "列出本机已安装的软件，可打开安装目录或启动卸载")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "数据来自注册表，软件卸载由系统原生的卸载程序完成，本工具不删除任何文件。部分系统更新与组件默认被隐藏。";

            _summary.Caption = "已安装程序";
            _summary.IconKind = "apps";
            _summary.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, OnRefreshClick, 92);
            _openButton = AddAction("打开位置", "folder", ButtonVariant.Secondary, OnOpenClick, 110);
            _uninstallButton = AddAction("卸载选中", "trash", ButtonVariant.Danger, OnUninstallClick, 118);

            BuildGrid();
            BuildToolbar();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        // --------------------------------------------------------------

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("名称", 200);
            _grid.AddTextColumn("发布者", 180, false);
            _grid.AddTextColumn("版本", 110, false);
            _grid.AddTextColumn("大小", 110, true);
            _grid.AddTextColumn("安装日期", 120, false);

            _grid.CellMouseDoubleClick += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0) return;
                OpenLocation(e.RowIndex);
            };
            _grid.SelectionChanged += delegate { UpdateActions(); };
        }

        private void BuildToolbar()
        {
            _toolbar.BackColor = Theme.WindowBg;
            _toolbar.Height = 34;

            _searchWrap.BackColor = Theme.CardBg;
            _searchWrap.Height = 32;
            _searchWrap.Paint += SearchPaint;
            _searchWrap.Resize += delegate
            {
                _search.SetBounds(30, 0, Math.Max(40, _searchWrap.Width - 38), 32);
            };

            _search.BorderStyle = BorderStyle.None;
            _search.BackColor = Theme.CardBg;
            _search.ForeColor = Theme.TextPrimary;
            _search.Font = Theme.FontBody;
            _search.SetBounds(30, 0, 200, 32);
            _search.TextChanged += delegate { ApplyFilter(); };
            _searchWrap.Controls.Add(_search);

            _sort.DropDownStyle = ComboBoxStyle.DropDownList;
            _sort.FlatStyle = FlatStyle.Flat;
            _sort.BackColor = Theme.CardBg;
            _sort.ForeColor = Theme.TextPrimary;
            _sort.Font = Theme.FontBody;
            _sort.Items.Add("按名称");
            _sort.Items.Add("按大小");
            _sort.Items.Add("按安装时间");
            _sort.SelectedIndex = 0;
            _sort.SelectedIndexChanged += delegate { ApplyFilter(); };

            _incUpdates.AutoSize = true;
            _incUpdates.BackColor = Theme.WindowBg;
            _incUpdates.ForeColor = Theme.TextSecondary;
            _incUpdates.Font = Theme.FontSmall;
            _incUpdates.Text = "包含更新/系统组件";
            _incUpdates.CheckedChanged += delegate { Load(true); };

            _countLabel.AutoSize = true;
            _countLabel.ForeColor = Theme.TextMuted;
            _countLabel.Font = Theme.FontSmall;
            _countLabel.Text = "";
            _countLabel.TextAlign = ContentAlignment.MiddleRight;

            _toolbar.Controls.Add(_searchWrap);
            _toolbar.Controls.Add(_sort);
            _toolbar.Controls.Add(_incUpdates);
            _toolbar.Controls.Add(_countLabel);

            _toolbar.Resize += delegate
            {
                _searchWrap.SetBounds(0, 1, 240, 32);
                _sort.SetBounds(252, 1, 150, 32);
                _incUpdates.SetBounds(252 + 150 + 16, 6, _incUpdates.Width, 24);
                _countLabel.SetBounds(Math.Max(420, _toolbar.Width - _countLabel.Width - 4), 6,
                    _countLabel.Width, 24);
            };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 42, 18);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_toolbar, 34, 18);
            AddFull(_grid, 320, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private void Relayout()
        {
            int summaryH = _summary.PreferredHeight;
            _summary.Height = summaryH;
            Control row = _summary.Parent;
            if (row != null) row.Height = summaryH;

            int used = Body.Padding.Top + Body.Padding.Bottom + 42 + 18 + summaryH + 18 + 34 + 18;
            int avail = ViewportHeight - used;
            if (avail < 220) avail = 220;

            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();
            RefreshLayout();
        }

        public override void OnActivated()
        {
            if (!_loaded) Load(false);
        }

        // --------------------------------------------------------------
        // 数据
        // --------------------------------------------------------------

        private void Load(bool force)
        {
            if (_busy) return;
            _busy = true;
            _loaded = true;
            _uninstallButton.Enabled = false;
            _openButton.Enabled = false;
            SetSubtitle("正在读取已安装的程序…", Theme.Warning);

            bool includeUpdates = _incUpdates.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ProgramEntry> result = null;
                try
                {
                    result = Programs.Scan(includeUpdates);
                }
                catch
                {
                    result = new List<ProgramEntry>();
                }

                Post(delegate
                {
                    _all.Clear();
                    _all.AddRange(result);
                    ApplyFilter();
                    long total = 0;
                    for (int i = 0; i < _all.Count; i++) total += _all[i].SizeBytes;

                    _summary.Clear();
                    _summary.Add("已安装程序", _all.Count + " 项");
                    _summary.Add("估算占用", total > 0 ? SysInfo.FormatSize(total) : "未知",
                        total > 0 ? Theme.Success : Theme.TextPrimary);
                    _summary.Invalidate();

                    _busy = false;
                    SetSubtitle("已读取 " + _all.Count + " 项已安装程序。", Theme.Success);
                    Relayout();
                });
            });
        }

        private void ApplyFilter()
        {
            string q = _search.Text.Trim().ToLower();
            List<ProgramEntry> shown = new List<ProgramEntry>();
            for (int i = 0; i < _all.Count; i++)
            {
                ProgramEntry e = _all[i];
                if (!string.IsNullOrEmpty(q))
                {
                    if (!e.Name.ToLower().Contains(q) && !e.Publisher.ToLower().Contains(q)) continue;
                }
                shown.Add(e);
            }

            int mode = _sort.SelectedIndex;
            if (mode == 1)
            {
                shown.Sort(delegate (ProgramEntry a, ProgramEntry b)
                {
                    return b.SizeBytes.CompareTo(a.SizeBytes);
                });
            }
            else if (mode == 2)
            {
                shown.Sort(delegate (ProgramEntry a, ProgramEntry b)
                {
                    return string.Compare(b.InstallDate, a.InstallDate, StringComparison.Ordinal);
                });
            }

            _grid.Rows.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                ProgramEntry e = shown[i];
                int idx = _grid.Rows.Add(e.Name, e.Publisher, e.Version, e.SizeText, FormatDate(e.InstallDate));
                _grid.Rows[idx].Tag = e;
                if (e.SizeBytes == 0)
                {
                    _grid.Rows[idx].Cells[3].Style.ForeColor = Theme.TextMuted;
                }
            }
            _grid.ClearSelection();

            _countLabel.Text = "显示 " + shown.Count + " / " + _all.Count + " 项";
            _countLabel.SetBounds(Math.Max(420, _toolbar.Width - _countLabel.Width - 4), 6,
                _countLabel.Width, 24);
            _countLabel.Invalidate();
            UpdateActions();
        }

        private void UpdateActions()
        {
            bool has = _grid.SelectedRows.Count > 0;
            _openButton.Enabled = has;
            _uninstallButton.Enabled = has && !_busy;
        }

        // --------------------------------------------------------------
        // 操作
        // --------------------------------------------------------------

        private void OnRefreshClick(object sender, EventArgs e)
        {
            Load(true);
        }

        private void OnOpenClick(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            OpenLocation(_grid.SelectedRows[0].Index);
        }

        private void OpenLocation(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            ProgramEntry e = _grid.Rows[rowIndex].Tag as ProgramEntry;
            if (e == null) return;

            if (e.HasLocation)
            {
                Shell.OpenPath(e.InstallLocation);
            }
            else if (!string.IsNullOrEmpty(e.UninstallString))
            {
                Dialog.Info(this, "无安装目录",
                    "「" + e.Name + "」没有记录安装目录，无法在资源管理器中定位。");
            }
            else
            {
                Dialog.Info(this, "无安装信息",
                    "「" + e.Name + "」没有记录安装目录或卸载命令。");
            }
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0) return;
            ProgramEntry entry = _grid.SelectedRows[0].Tag as ProgramEntry;
            if (entry == null) return;

            string msg = "即将启动「" + entry.Name + "」的卸载程序。\n\n" +
                "· 卸载过程由系统/软件自身的安装程序完成，可能需要管理员权限（UAC 确认）；\n" +
                "· 卸载完成后请点击「刷新」重新读取列表。\n\n是否继续？";
            if (!Dialog.Confirm(this, "卸载程序", msg)) return;

            bool ok = Programs.LaunchUninstall(entry, false);
            if (ok)
            {
                Dialog.Success(this, "已启动卸载",
                    "已启动「" + entry.Name + "」的卸载程序。\n请按提示完成卸载，然后点击「刷新」更新列表。");
            }
            else
            {
                Dialog.Error(this, "无法启动卸载",
                    "无法启动该程序的卸载命令。你可以手动在系统「设置 → 应用」中卸载它。\n\n命令：" +
                    entry.UninstallString);
            }
        }

        private static string FormatDate(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 8) return "—";
            try
            {
                return s.Substring(0, 4) + "-" + s.Substring(4, 2) + "-" + s.Substring(6, 2);
            }
            catch
            {
                return "—";
            }
        }

        private void SearchPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);
            using (Pen p = new Pen(Theme.BorderStrong, 1f))
            {
                Gfx.StrokeRound(g, new Rectangle(0, 0, _searchWrap.Width - 1, _searchWrap.Height - 1), 6,
                    Theme.BorderStrong, 1f);
            }
            IconPainter.Draw(g, "search", new Rectangle(9, 8, 16, 16), Theme.TextMuted);
        }

        private void Post(System.Threading.ThreadStart action)
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
