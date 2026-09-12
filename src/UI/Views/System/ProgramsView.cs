using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
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
        private readonly ComboBox _wingetBox = new ComboBox();
        private readonly TextBox _wingetId = new TextBox();
        private AccentButton _wingetInstall;

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

            AddFull(_toolbar, 34, 12);

            // Winget 一键安装（系统包管理器，Win10 1809+ 自带）
            FlowLayoutPanel wingetRow = MakeRow(0, 12);
            Label wl = new Label();
            wl.Text = "安装软件：";
            wl.ForeColor = Theme.TextPrimary;
            wl.Font = Theme.FontBodyBold;
            wl.Size = new Size(90, 30);
            wl.Margin = new Padding(0, 0, 8, 0);
            wingetRow.Controls.Add(wl);
            _wingetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _wingetBox.Size = new Size(240, 30);
            for (int i = 0; i < WingetApps.Length; i++) _wingetBox.Items.Add(WingetApps[i].Name);
            if (_wingetBox.Items.Count > 0) _wingetBox.SelectedIndex = 0;
            wingetRow.Controls.Add(_wingetBox);
            _wingetInstall = MakeInlineWingetButton("安装选中", OnWingetInstall, 110);
            wingetRow.Controls.Add(_wingetInstall);
            _wingetId.BorderStyle = BorderStyle.FixedSingle;
            _wingetId.Font = Theme.FontBody;
            _wingetId.Size = new Size(170, 30);
            wingetRow.Controls.Add(_wingetId);
            AccentButton custom = MakeInlineWingetButton("安装自定义", OnWingetCustom, 124);
            wingetRow.Controls.Add(custom);
            Label tip = new Label();
            tip.Text = "静默安装，进度见页头提示";
            tip.ForeColor = Theme.TextMuted;
            tip.Font = Theme.FontSmall;
            tip.Size = new Size(190, 30);
            tip.Margin = new Padding(12, 8, 0, 0);
            wingetRow.Controls.Add(tip);
            AddRow(wingetRow);

            AddFull(_grid, 320, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private static readonly WingetApp[] WingetApps = new WingetApp[]
        {
            new WingetApp("Google Chrome 浏览器", "Google.Chrome"),
            new WingetApp("Mozilla Firefox 浏览器", "Mozilla.Firefox"),
            new WingetApp("7-Zip 压缩工具", "7zip.7zip"),
            new WingetApp("VLC 媒体播放器", "VideoLAN.VLC"),
            new WingetApp("VS Code 编辑器", "Microsoft.VisualStudioCode"),
            new WingetApp("Notepad++ 编辑器", "Notepad++.Notepad++"),
            new WingetApp("PotPlayer 播放器", "PotPlayer.PotPlayer")
        };

        private sealed class WingetApp
        {
            public readonly string Name;
            public readonly string Id;
            public WingetApp(string name, string id) { Name = name; Id = id; }
        }

        private void OnWingetInstall(object sender, EventArgs e)
        {
            if (_wingetBox.SelectedIndex < 0) return;
            WingetInstall(WingetApps[_wingetBox.SelectedIndex].Name, WingetApps[_wingetBox.SelectedIndex].Id);
        }

        private void OnWingetCustom(object sender, EventArgs e)
        {
            string id = _wingetId.Text.Trim();
            if (id.Length == 0)
            {
                Dialog.Info(this, "未输入", "请输入要安装的 winget 包 ID（如 VideoLAN.VLC）。");
                return;
            }
            WingetInstall(id, id);
        }

        private void WingetInstall(string title, string id)
        {
            if (_busy) return;
            if (!Dialog.Confirm(this, "安装软件",
                "将通过 Winget 静默安装：\r\n\r\n  " + title + "（" + id + "）\r\n\r\n" +
                "安装需要联网下载，耗时取决于网速。是否继续？")) return;

            _busy = true;
            _wingetInstall.Enabled = false;
            SetSubtitle("正在通过 Winget 安装 " + title + " …（可能需要数分钟）", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r = Shell.Run("winget",
                    "install --id " + id + " -e --silent --accept-source-agreements --accept-package-agreements",
                    900000);
                Post(delegate
                {
                    _busy = false;
                    _wingetInstall.Enabled = true;
                    bool ok = r.Ok && r.All.IndexOf("0x8", StringComparison.Ordinal) < 0;
                    if (ok)
                    {
                        SetSubtitle(title + " 安装完成。", Theme.Success);
                        Load(false); // 刷新已安装列表
                    }
                    else
                    {
                        Dialog.Output(this, "安装输出", string.IsNullOrEmpty(r.All) ? "winget 执行失败（未安装或网络错误）。" : r.All);
                        SetSubtitle(title + " 安装未成功，详见输出。", Theme.Danger);
                    }
                });
            });
        }

        private AccentButton MakeInlineWingetButton(string text, EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "apps";
            b.Variant = ButtonVariant.Primary;
            b.Size = new Size(width, 30);
            b.Margin = new Padding(6, 0, 8, 0);
            b.Click += onClick;
            return b;
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
