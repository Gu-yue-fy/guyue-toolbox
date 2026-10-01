// 已安装程序页：列出本机软件，支持卸载与 Winget 安装

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class ProgramsView : GridPageView
    {
        private readonly Panel _toolbar = new Panel();
        private readonly TextBox _search = new TextBox();
        private readonly Panel _searchWrap = new Panel();
        private readonly CheckBox _incUpdates = new CheckBox();
        private readonly Label _countLabel = new Label();
        private readonly ComboBox _wingetBox = new ComboBox();
        private readonly TextBox _wingetId = new TextBox();
        private AccentButton _wingetInstall;

        private readonly List<ProgramEntry> _all = new List<ProgramEntry>();
        private AccentButton _uninstallButton;
        private AccentButton _openButton;
        private AccentButton _forceButton;

        public ProgramsView()
            : base("已安装程序", "列出本机已安装的软件，可打开安装目录或启动卸载")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Accent;
            Notice.NoticeText = "「卸载选中」调用软件自带卸载程序；「强制卸载」用于卸载程序损坏/残留清理（会删除安装目录与注册表，不可恢复，执行前会列出明细确认）。点击列头可排序。";

            Summary.Caption = "已安装程序";
            Summary.IconKind = "apps";
            Summary.CaptionColor = Theme.Accent;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, OnRefreshClick, 92);
            _openButton = AddAction("打开位置", "folder", ButtonVariant.Secondary, OnOpenClick, 110);
            _uninstallButton = AddAction("卸载选中", "trash", ButtonVariant.Danger, OnUninstallClick, 118);
            _forceButton = AddAction("强制卸载", "bolt", ButtonVariant.Danger, OnForceUninstallClick, 118);

            BuildToolbar();
            InitializeGridPage();
        }

        // ---- 形态参数：通知条 34+12，统计行间距 18（比通知条更松），工具行 34+12，表格行占位 320 ----
        protected override int NoticeHeight { get { return 34; } }
        protected override int NoticeGap { get { return 12; } }
        protected override int SummaryRowGap { get { return 18; } }
        protected override int ExtraUsedHeight { get { return 34 + 18; } }
        protected override int MinGridHeight { get { return 220; } }

        // --------------------------------------------------------------

        protected override void BuildColumns()
        {
            Grid.AddFillColumn("名称", 200);
            Grid.AddTextColumn("发布者", 180, false);
            Grid.AddTextColumn("版本", 110, false);
            Grid.AddTextColumn("大小", 110, true);
            Grid.AddTextColumn("安装日期", 120, false);

            // 双击打开安装位置（与其他页"双击即操作"的约定一致）
            Grid.CellMouseDoubleClick += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.RowIndex < 0) return;
                OpenLocation(e.RowIndex);
            };

            // 大小列存原始字节数（便于按数值大小排序），显示时格式化为易读文本
            Grid.CellFormatting += delegate (object s, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex != 3 || e.RowIndex < 0 || !(e.Value is long)) return;
                long bytes = (long)e.Value;
                if (bytes <= 0) { e.Value = "未知"; e.CellStyle.ForeColor = Theme.TextMuted; }
                else { e.Value = SysInfo.FormatSize(bytes); }
                e.FormattingApplied = true;
            };
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
            Native.SetCue(_search, "搜索软件…");
            _search.TextChanged += delegate { ApplyFilter(); };
            _searchWrap.Controls.Add(_search);

            _incUpdates.AutoSize = true;
            _incUpdates.BackColor = Theme.WindowBg;
            _incUpdates.ForeColor = Theme.TextSecondary;
            _incUpdates.Font = Theme.FontSmall;
            _incUpdates.Text = "包含更新/系统组件";
            _incUpdates.CheckedChanged += delegate { Load(); };

            _countLabel.AutoSize = true;
            _countLabel.ForeColor = Theme.TextMuted;
            _countLabel.Font = Theme.FontSmall;
            _countLabel.Text = "";
            _countLabel.TextAlign = ContentAlignment.MiddleRight;

            _toolbar.Controls.Add(_searchWrap);
            _toolbar.Controls.Add(_incUpdates);
            _toolbar.Controls.Add(_countLabel);

            _toolbar.Resize += delegate
            {
                _searchWrap.SetBounds(0, 1, 240, 32);
                _incUpdates.SetBounds(252, 6, _incUpdates.Width, 24);
                LayoutRightLabel(_countLabel, _toolbar.Width, 400, 6, 24);
            };
        }

        /// <summary>统计行与表格之间的工具行 + Winget 安装行（本页特有，走布局钩子）。</summary>
        protected override void AddExtraLayoutRows()
        {
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
            wingetRow.Controls.Add(ThemeInput.Wrap(_wingetId));
            Native.SetCue(_wingetId, "如 VideoLAN.VLC");
            AccentButton custom = MakeInlineWingetButton("安装自定义", OnWingetCustom, 124);
            wingetRow.Controls.Add(custom);
            // 本行不放「静默安装，进度见页头提示」一类说明标签：190px + 间距会把本行
            // 撑到 984px，而最小窗口的内容区仅 807px，最右控件会被裁掉。
            // 进度信息由页头提示承载，这里只保留功能控件。
            AddRow(wingetRow);
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
            if (Busy) return;
            if (!Dialog.Confirm(this, "安装软件",
                "将通过 Winget 静默安装：\r\n\r\n  " + title + "（" + id + "）\r\n\r\n" +
                "安装需要联网下载，耗时取决于网速。是否继续？")) return;

            Busy = true;
            _wingetInstall.Enabled = false;
            SetSubtitle("正在通过 Winget 安装 " + title + " …（可能需要数分钟）", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r = Shell.Run("winget",
                    "install --id " + id + " -e --silent --accept-source-agreements --accept-package-agreements",
                    900000, System.Text.Encoding.UTF8, isChange: true); // winget 输出 UTF-8，与控制台工具（GBK）不同
                Post(delegate
                {
                    Busy = false;
                    _wingetInstall.Enabled = true;
                    bool ok = r.Ok; // 以 winget 进程退出码为准，避免对输出文本做脆弱的关键字匹配
                    if (ok)
                    {
                        SetSubtitle(title + " 安装完成。", Theme.Success);
                        Load(); // 刷新已安装列表
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
            b.IconKind = "bolt";
            b.Variant = ButtonVariant.Primary;
            b.Height = Theme.RowButtonHeight;
            b.FitToText(width);
            b.Width = Math.Max(width, b.NaturalWidth);
            b.Margin = new Padding(6, 0, 8, 0);
            b.Click += onClick;
            return b;
        }

        // --------------------------------------------------------------
        // 数据
        // --------------------------------------------------------------

        protected override void Load()
        {
            if (Busy) return;
            Busy = true;
            Loaded = true;
            _uninstallButton.Enabled = false;
            _openButton.Enabled = false;
            SetSubtitle("正在读取已安装的程序…", Theme.Warning);

            bool includeUpdates = _incUpdates.Checked;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ProgramEntry> result = null;
                string error = null;
                try
                {
                    result = Programs.Scan(includeUpdates);
                }
                catch (Exception ex)
                {
                    // 读失败必须与"真的没有装程序"区分开：此前一律吞掉并显示"已读取 0 项"，
                    // 用户会以为系统里没有软件，而不是"这次读取失败了"。
                    result = new List<ProgramEntry>();
                    error = ex.Message;
                }

                Post(delegate
                {
                    _all.Clear();
                    _all.AddRange(result);
                    ApplyFilter();
                    long total = 0;
                    for (int i = 0; i < _all.Count; i++) total += _all[i].SizeBytes;

                    Summary.Clear();
                    Summary.Add("已安装程序", _all.Count + " 项");
                    Summary.Add("估算占用", total > 0 ? SysInfo.FormatSize(total) : "未知",
                        total > 0 ? Theme.Success : Theme.TextPrimary);
                    Summary.Invalidate();

                    Busy = false;
                    if (error != null)
                        SetSubtitle("读取已安装程序失败：" + error, Theme.Danger);
                    else
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

            // 排序交给列头点击（DarkGrid.ColumnClickSort），此处保持数据顺序；
            // 重建后重新应用用户点选的列排序，避免筛选时丢排序。
            shown.Sort(delegate (ProgramEntry a, ProgramEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            Grid.Rows.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                ProgramEntry e = shown[i];
                int idx = Grid.Rows.Add(e.Name, e.Publisher, e.Version, e.SizeBytes, FormatDate(e.InstallDate));
                Grid.Rows[idx].Tag = e;
            }
            Grid.ClearSelection();
            Grid.ReapplySort();

            _countLabel.Text = "显示 " + shown.Count + " / " + _all.Count + " 项";
            LayoutRightLabel(_countLabel, _toolbar.Width, 400, 6, 24);
            _countLabel.Invalidate();
            UpdateActions();
        }

        protected override void UpdateActions()
        {
            bool has = Grid.SelectedRows.Count > 0;
            _openButton.Enabled = has;
            _uninstallButton.Enabled = has && !Busy;
            _forceButton.Enabled = has && !Busy;
        }

        // --------------------------------------------------------------
        // 操作
        // --------------------------------------------------------------

        private void OnRefreshClick(object sender, EventArgs e)
        {
            Load();
        }

        private void OnOpenClick(object sender, EventArgs e)
        {
            if (Grid.SelectedRows.Count == 0) return;
            OpenLocation(Grid.SelectedRows[0].Index);
        }

        private void OpenLocation(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= Grid.Rows.Count) return;
            ProgramEntry e = Grid.Rows[rowIndex].Tag as ProgramEntry;
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
            if (Grid.SelectedRows.Count == 0) return;
            ProgramEntry entry = Grid.SelectedRows[0].Tag as ProgramEntry;
            if (entry == null) return;

            if (!Dialog.ConfirmDanger(this, "卸载程序",
                "启动「" + entry.Name + "」的卸载程序（由软件自身的安装程序执行）。",
                "取决于该软件：多数可在卸载过程中取消，取消后不留改动。",
                "可能需要 UAC 确认；卸载完成后请点「刷新」重新读取列表。",
                "启动卸载", false))
                return;

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

        /// <summary>
        /// 强制卸载：原生卸载程序之外的兜底——强杀进程 + 删安装目录 + 清注册表残留。
        /// 先只读扫描生成明细 → 用户逐项确认 → 执行 → 报告（全部动作记入操作日志，可按组回滚注册表部分）。
        /// </summary>
        private void OnForceUninstallClick(object sender, EventArgs e)
        {
            if (Grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择程序", "请先在列表中点击选中一个程序（整行高亮），再执行强制卸载。");
                return;
            }
            ProgramEntry entry = Grid.SelectedRows[0].Tag as ProgramEntry;
            if (entry == null || Busy) return;

            Busy = true;
            SetSubtitle("正在扫描「" + entry.Name + "」的安装痕迹…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ProgramForcer.Plan plan;
                try { plan = ProgramForcer.BuildPlan(entry); }
                catch (Exception ex)
                {
                    Post(delegate { Busy = false; Dialog.Error(this, "扫描失败", ex.Message); });
                    return;
                }
                Post(delegate
                {
                    Busy = false;
                    ShowForcePlan(entry, plan);
                });
            });
        }

        /// <summary>展示强制卸载明细并二次确认后执行。</summary>
        private void ShowForcePlan(ProgramEntry entry, ProgramForcer.Plan plan)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("将强制清除「").Append(entry.Name).Append("」的全部安装痕迹：\r\n\r\n");
            if (plan.Processes.Count > 0)
                sb.Append("· 结束进程：").Append(string.Join("、", plan.Processes.ToArray())).Append("\r\n");
            for (int i = 0; i < plan.Folders.Count; i++)
                sb.Append("· 删除目录：").Append(plan.Folders[i]).Append("\r\n");
            for (int i = 0; i < plan.Files.Count; i++)
                sb.Append("· 删除快捷方式：").Append(plan.Files[i]).Append("\r\n");
            for (int i = 0; i < plan.RegistryKeys.Count; i++)
                sb.Append("· 删除注册表：").Append(plan.RegistryKeys[i]).Append("\r\n");
            sb.Append("\r\n以上即本次将执行的全部动作，请逐行确认它们都属于该软件。");

            if (plan.Processes.Count == 0 && plan.Folders.Count == 0 && plan.Files.Count == 0 &&
                plan.RegistryKeys.Count == 0)
            {
                Dialog.Info(this, "无可清理项", "未扫描到「" + entry.Name + "」的安装目录、进程或注册表痕迹。");
                return;
            }

            if (!Dialog.ConfirmDanger(this, "强制卸载",
                "对「" + entry.Name + "」执行强制清理（动作清单见下方影响范围）。",
                "不可恢复：目录与注册表删除后无法找回，本工具不做备份。",
                sb.ToString() +
                "\r\n仅当正常卸载不可用或存在卸载残留时使用。",
                "强制卸载", true)) return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在强制卸载「" + entry.Name + "」…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> errors = new List<string>();
                string report;
                try { report = ProgramForcer.Execute(plan, errors); }
                catch (Exception ex) { report = "执行中断：" + ex.Message; }

                Post(delegate
                {
                    Busy = false;
                    UpdateActions();
                    Load(); // 刷新列表
                    if (errors.Count > 0)
                    {
                        string text = report + "\r\n\r\n以下项目未能清理（多为文件被占用或权限不足）：\r\n";
                        for (int i = 0; i < errors.Count && i < 8; i++) text += "· " + errors[i] + "\r\n";
                        text += "\r\n可重启系统后再试一次。";
                        Dialog.Warn(this, "强制卸载完成（有遗留）", text);
                        SetSubtitle("强制卸载完成，" + errors.Count + " 项需重启后重试。", Theme.Warning);
                    }
                    else
                    {
                        Dialog.Success(this, "强制卸载完成", report);
                        SetSubtitle("已强制卸载：" + entry.Name, Theme.Success);
                    }
                });
            });
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
            // Gfx.StrokeRound 内部已走 GdiCache.Pen，此处不要再自行 new Pen
            Gfx.StrokeRound(g, new Rectangle(0, 0, _searchWrap.Width - 1, _searchWrap.Height - 1), 6,
                Theme.BorderStrong, 1f);
            IconPainter.Draw(g, "search", new Rectangle(9, 8, 16, 16), Theme.TextMuted);
        }
    }
}
