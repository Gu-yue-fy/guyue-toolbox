// 服务管理页：查看与控制系统服务，一键精简后台释放资源

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class ServicesView : GridPageView
    {
        private readonly Panel _toolbar = new Panel();
        private readonly TextBox _search = new TextBox();
        private readonly Panel _searchWrap = new Panel();
        private readonly Label _countLabel = new Label();

        private readonly List<ServiceInfo> _all = new List<ServiceInfo>();
        private AccentButton _startButton;
        private AccentButton _stopButton;
        private AccentButton _disableButton;
        private AccentButton _autoButton;
        private AccentButton _optimizeButton;
        private AccentButton _restoreButton;

        /// <summary>提速预设：将若干非关键后台服务调整为手动/禁用，以加快开机、释放资源。</summary>
        private sealed class PresetRule
        {
            public string Name;
            public string Mode;
        }

        private static readonly PresetRule[] _presetRules = new PresetRule[]
        {
            new PresetRule { Name = "SysMain", Mode = "Manual" },
            new PresetRule { Name = "DiagTrack", Mode = "Disabled" },
            new PresetRule { Name = "dmwappushsvc", Mode = "Disabled" },
            new PresetRule { Name = "RetailDemo", Mode = "Disabled" },
            new PresetRule { Name = "MapsBroker", Mode = "Manual" },
            new PresetRule { Name = "Fax", Mode = "Disabled" },
            new PresetRule { Name = "PrintNotify", Mode = "Manual" },
        };

        private string _lastBackup;

        public ServicesView()
            : base("服务管理", "查看与控制系统服务，精简后台、释放资源")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Warning;
            Notice.NoticeText = "禁用关键系统服务可能导致系统不稳定甚至无法启动，请只停用你确认不需要的服务。修改启动类型与启停需要管理员权限。";

            Summary.Caption = "Windows 服务";
            Summary.IconKind = "services";
            Summary.CaptionColor = Theme.Warning;

            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);
            _optimizeButton = AddAction("一键提速", "bolt", ButtonVariant.Primary, OnOptimizeClick, 110);
            _restoreButton = AddAction("恢复预设", "undo", ButtonVariant.Ghost, OnRestoreClick, 110);
            // 启动/停止/设为禁用/设为自动/排序是「选中服务后的行内操作」：
            // v2.2 从页头挪到内容区工具行——页头 8 个按钮在 1180 最小窗口下会挤成"刷.../停..."截断。

            BuildToolbar();
            InitializeGridPage();
        }

        // ---- 形态参数：通知条 42+18，工具行 34+18，表格行占位 320 ----
        protected override int ExtraUsedHeight { get { return 34 + 18; } }
        protected override int MinGridHeight { get { return 220; } }

        protected override void BuildColumns()
        {
            Grid.AddTextColumn("显示名称", 180, false);
            Grid.AddTextColumn("服务名", 140, false);
            Grid.AddTextColumn("启动类型", 88, false);
            Grid.AddTextColumn("状态", 76, false);
            Grid.AddTextColumn("推荐", 88, false);
            Grid.AddFillColumn("说明", 160);
        }

        /// <summary>统计行与表格之间的工具行（搜索框 + 计数）：本页特有，走布局钩子。</summary>
        protected override void AddExtraLayoutRows()
        {
            AddFull(_toolbar, 34, 18);
        }

        private void BuildToolbar()
        {
            _toolbar.BackColor = Theme.WindowBg;
            _toolbar.Height = 34;

            _searchWrap.BackColor = Theme.CardBg;
            _searchWrap.Height = Theme.RowButtonHeight;   // 与同排按钮同高（原来是 32，和 30 的按钮错 2px）
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
            Native.SetCue(_search, "搜索服务…");
            _search.TextChanged += delegate { ApplyFilter(); };
            _searchWrap.Controls.Add(_search);

            _countLabel.AutoSize = true;
            _countLabel.ForeColor = Theme.TextMuted;
            _countLabel.Font = Theme.FontSmall;
            _countLabel.TextAlign = ContentAlignment.MiddleRight;

            _toolbar.Controls.Add(_searchWrap);
            _toolbar.Controls.Add(_countLabel);

            // 行内操作按钮（原页头按钮挪下来）：启停与设值按钮靠右。
            // 排序不走绑死的「排序」按钮循环切换——表格列头（显示名称/服务名/启动类型/状态/推荐）
            // 点击即排序（GridPageView 默认 ColumnClickSort），与其它表格页行为一致。
            _startButton = MakeInlineToolButton("启动", "play", ButtonVariant.Primary, OnStartClick, 92);
            _stopButton = MakeInlineToolButton("停止", "stop", ButtonVariant.Secondary, OnStopClick, 92);
            _disableButton = MakeInlineToolButton("设为禁用", "shield", ButtonVariant.Danger, OnDisableClick, 110);
            _autoButton = MakeInlineToolButton("设为自动", "check", ButtonVariant.Secondary, OnAutoClick, 110);
            _toolbar.Controls.Add(_startButton);
            _toolbar.Controls.Add(_stopButton);
            _toolbar.Controls.Add(_disableButton);
            _toolbar.Controls.Add(_autoButton);

            _toolbar.Resize += delegate { LayoutToolbar(); };
        }

        /// <summary>工具行小按钮：高度走 Theme.RowButtonHeight，宽度按文字自适应（不再写死，避免长文案被裁）。</summary>
        private AccentButton MakeInlineToolButton(string text, string icon, ButtonVariant variant,
            EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = icon;
            b.Variant = variant;
            b.Height = Theme.RowButtonHeight;
            b.FitToText(width);
            int w = Math.Max(width, b.NaturalWidth);
            b.SetBounds(0, 2, w, Theme.RowButtonHeight);
            b.NaturalWidth = w;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>工具行内部排布：搜索(左) + 排序(紧随) + 计数(中) + 启停与设值(右)。</summary>
        private void LayoutToolbar()
        {
            int w = _toolbar.Width;
            _searchWrap.SetBounds(0, 2, 240, Theme.RowButtonHeight);

            int rightX = w;
            const int gap = 8;
            // 从右往左：设为自动 → 设为禁用 → 停止 → 启动
            rightX -= _autoButton.Width + gap;
            _autoButton.Location = new Point(rightX, 1);
            rightX -= _disableButton.Width + gap;
            _disableButton.Location = new Point(rightX, 1);
            rightX -= _stopButton.Width + gap;
            _stopButton.Location = new Point(rightX, 1);
            rightX -= _startButton.Width + gap;
            _startButton.Location = new Point(rightX, 1);

            // 计数标签落在搜索框与行内操作按钮之间（都避开）
            LayoutRightLabel(_countLabel, Math.Max(300, rightX), 380, 6, 24);
        }

        // --------------------------------------------------------------

        protected override void Load()
        {
            if (Busy) return;
            Busy = true;
            Loaded = true;
            SetSubtitle("正在读取 Windows 服务…", Theme.Warning);
            UpdateActions();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ServiceInfo> result = null;
                string error = null;
                try { result = ServiceManager.List(); }
                catch (Exception ex)
                {
                    // 与"程序列表"同一条纪律：读失败不能显示成"0 个服务 + 绿色成功"，
                    // 否则用户会以为系统里真的没有服务，而不是这次查询失败（多为权限问题）。
                    result = new List<ServiceInfo>();
                    error = ex.Message;
                }

                Post(delegate
                {
                    Busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    ApplyFilter();

                    int running = 0;
                    for (int i = 0; i < _all.Count; i++) if (_all[i].IsRunning) running++;

                    Summary.Clear();
                    Summary.Add("服务总数", _all.Count + " 项");
                    Summary.Add("运行中", running + " 项", running > 0 ? Theme.Success : Theme.TextPrimary);
                    Summary.Add("已停止", (_all.Count - running) + " 项");
                    Summary.Invalidate();

                    DiscoverBackup();
                    UpdateActions();

                    // 读失败时不要报"已读取 0 个服务"的成功样式（用户会以为系统里没有服务）
                    SetSubtitle(error != null
                        ? "读取服务列表失败：" + error
                        : "已读取 " + _all.Count + " 个服务，其中 " + running + " 个正在运行。",
                        error != null ? Theme.Danger : Theme.Success);
                    Relayout();
                });
            });
        }

        /// <summary>是否属于「建议禁用」的推荐清单（与一键提速预设一致）。</summary>
        private bool IsRecommendedDisable(string serviceName)
        {
            for (int i = 0; i < _presetRules.Length; i++)
            {
                if (_presetRules[i].Mode == "Disabled" &&
                    string.Equals(_presetRules[i].Name, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyFilter()
        {
            string q = _search.Text.Trim().ToLower();
            List<ServiceInfo> shown = new List<ServiceInfo>();
            for (int i = 0; i < _all.Count; i++)
            {
                ServiceInfo s = _all[i];
                if (!string.IsNullOrEmpty(q))
                {
                    if (!s.DisplayName.ToLower().Contains(q) && !s.Name.ToLower().Contains(q)) continue;
                }
                shown.Add(s);
            }

            // 默认按显示名称排序；点列头（显示名称/服务名/启动类型/状态/推荐）随时改排序
            List<ServiceInfo> ordered = new List<ServiceInfo>(shown);
            ordered.Sort(delegate (ServiceInfo a, ServiceInfo b)
            {
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            Grid.Rows.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                ServiceInfo s = ordered[i];
                string rec = IsRecommendedDisable(s.Name) ? "建议禁用" : "";
                int idx = Grid.Rows.Add(s.DisplayName, s.Name, s.StartText, s.StateText, rec,
                    string.IsNullOrEmpty(s.Description) ? "—" : s.Description);
                Grid.Rows[idx].Tag = s;
                if (rec.Length > 0) Grid.Rows[idx].Cells[4].Style.ForeColor = Theme.Warning;
                if (s.IsRunning)
                {
                    Grid.Rows[idx].Cells[3].Style.ForeColor = Theme.Success;
                }
                else if (s.StartMode == "Disabled")
                {
                    Grid.Rows[idx].Cells[2].Style.ForeColor = Theme.TextMuted;
                }
            }
            Grid.ClearSelection();

            _countLabel.Text = "显示 " + shown.Count + " / " + _all.Count + " 项";
            LayoutRightLabel(_countLabel, _toolbar.Width, 248, 6, 24);
            _countLabel.Invalidate();
            UpdateActions();
        }

        private ServiceInfo Selected
        {
            get { return SelectedFrom<ServiceInfo>(Grid); }
        }

        protected override void UpdateActions()
        {
            ServiceInfo s = Selected;
            bool has = s != null && !Busy;
            _startButton.Enabled = has && !s.IsRunning && s.StartMode != "Disabled";
            _stopButton.Enabled = has && s.IsRunning;
            _disableButton.Enabled = has && s.StartMode != "Disabled";
            _autoButton.Enabled = has && s.StartMode != "Automatic";
            _restoreButton.Enabled = !Busy && !string.IsNullOrEmpty(_lastBackup);
        }

        private ServiceInfo FindService(string name)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _all[i];
            }
            return null;
        }

        private static string ModeText(string m)
        {
            switch (m)
            {
                case "Automatic": return "自动";
                case "Manual": return "手动";
                case "Disabled": return "禁用";
                default: return m;
            }
        }

        // --------------------------------------------------------------
        // 一键提速预设：调整非关键服务启动方式，并备份原始状态
        // --------------------------------------------------------------

        private void OnOptimizeClick(object sender, EventArgs e)
        {
            if (_all.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _presetRules.Length; i++)
            {
                ServiceInfo s = FindService(_presetRules[i].Name);
                if (s == null || s.StartMode == _presetRules[i].Mode) continue;
                sb.Append("· ").Append(s.DisplayName).Append(" → ").Append(ModeText(_presetRules[i].Mode)).Append("\n");
            }
            if (sb.Length == 0)
            {
                Dialog.Info(this, "无需优化", "预设中的服务当前已是推荐状态。");
                return;
            }

            if (!Dialog.ConfirmDanger(this, "一键提速预设",
                "调整下列服务的启动方式（需管理员权限）：\r\n" + sb,
                "可撤销：执行前会自动备份当前启动类型，可随时点「恢复预设」还原。",
                "禁用系统服务可能影响依赖它的功能，建议逐项确认后再执行。",
                "应用预设", false))
                return;

            EnsureElevated();
            if (!Native.IsElevated()) return;

            Dictionary<string, string> backup = new Dictionary<string, string>();
            for (int i = 0; i < _presetRules.Length; i++)
            {
                ServiceInfo s = FindService(_presetRules[i].Name);
                if (s != null) backup[s.Name] = s.StartMode;
            }
            string backupPath = WriteBackup(backup);

            Busy = true;
            UpdateActions();
            SetSubtitle("正在应用提速预设…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                int fail = 0;
                string firstError = "";
                for (int i = 0; i < _presetRules.Length; i++)
                {
                    ServiceInfo s = FindService(_presetRules[i].Name);
                    if (s == null) continue;
                    string err;
                    if (ServiceManager.ChangeStartMode(s.Name, _presetRules[i].Mode, out err))
                    {
                        if (_presetRules[i].Mode == "Disabled" && s.IsRunning)
                        {
                            string e2;
                            ServiceManager.Stop(s.Name, out e2);
                        }
                        ok++;
                    }
                    else
                    {
                        fail++;
                        if (string.IsNullOrEmpty(firstError)) firstError = err;
                    }
                }

                Post(delegate
                {
                    Busy = false;
                    if (backupPath != null) _lastBackup = backupPath;
                    UpdateActions();
                    Load();
                    SetSubtitle("已优化 " + ok + " 个服务" +
                        (fail > 0 ? "，" + fail + " 个失败。" : "。") +
                        (backupPath != null ? "（已备份，可一键恢复）" : ""),
                        fail > 0 ? Theme.Warning : Theme.Success);
                    if (fail > 0)
                    {
                        Dialog.Warn(this, "部分失败",
                            "成功 " + ok + " 个，失败 " + fail + " 个。" + (firstError != "" ? "\r\n" + firstError : ""));
                    }
                });
            });
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastBackup) || !File.Exists(_lastBackup))
            {
                Dialog.Info(this, "无备份", "尚未执行过提速预设，或备份文件已不存在。");
                return;
            }
            if (!Dialog.ConfirmDanger(this, "恢复预设前状态",
                "把提速预设改过的服务恢复到执行前的启动类型。",
                "可撤销：恢复后仍可再次应用预设。",
                "只影响预设改过的那些服务；期间你手动改动的服务也会被一并恢复。",
                "恢复", false))
                return;

            EnsureElevated();
            if (!Native.IsElevated()) return;

            Dictionary<string, string> backup = ReadBackup(_lastBackup);
            if (backup.Count == 0)
            {
                Dialog.Warn(this, "备份为空", "未从备份文件中读取到任何服务记录。");
                return;
            }

            Busy = true;
            UpdateActions();
            SetSubtitle("正在恢复服务…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                int fail = 0;
                foreach (KeyValuePair<string, string> kv in backup)
                {
                    string err;
                    if (ServiceManager.ChangeStartMode(kv.Key, kv.Value, out err)) ok++;
                    else fail++;
                }

                Post(delegate
                {
                    Busy = false;
                    Load();
                    SetSubtitle("已恢复 " + ok + " 个服务" + (fail > 0 ? "，" + fail + " 个失败。" : "。"),
                        fail > 0 ? Theme.Warning : Theme.Success);
                });
            });
        }

        private static string WriteBackup(Dictionary<string, string> data)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GuyueBox", "backups");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "services_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in data)
                {
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\n");
                }
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, string> ReadBackup(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch
            {
            }
            return d;
        }

        /// <summary>
        /// 扫描最近一次的服务备份文件。
        /// 虽然是单个小目录，但目录枚举 + 排序仍放到后台，不占用 UI 线程；
        /// 完成后刷新动作按钮（「恢复预设」的可用状态依赖 _lastBackup）。
        /// </summary>
        private void DiscoverBackup()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string found = null;
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "GuyueBox", "backups");
                    if (Directory.Exists(dir))
                    {
                        string[] files = Directory.GetFiles(dir, "services_*.txt");
                        if (files.Length > 0)
                        {
                            Array.Sort(files);
                            found = files[files.Length - 1];
                        }
                    }
                }
                catch
                {
                    found = null;
                }

                Post(delegate
                {
                    _lastBackup = found;
                    UpdateActions();
                });
            });
        }

        private void EnsureElevated()
        {
            if (Native.IsElevated()) return;
            bool go = Dialog.Confirm(this, "需要管理员权限",
                "修改服务的启动类型或启停需要管理员权限。\r\n\r\n是否以管理员身份重新启动本程序？");
            if (go && Shell.RestartElevated(""))
            {
                Application.Exit();
            }
            else if (go)
            {
                Dialog.Error(this, "提权失败", "未能以管理员身份启动，请右键程序选择「以管理员身份运行」。");
            }
        }

        // --------------------------------------------------------------

        private void OnStartClick(object sender, EventArgs e)
        {
            ServiceInfo s = Selected;
            if (s == null) return;
            EnsureElevated();
            if (!Native.IsElevated()) return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在启动 " + s.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ServiceManager.Start(s.Name, out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok) { SetSubtitle("已启动：" + s.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, "启动失败", "无法启动 " + s.Name + "：\r\n" + error); SetSubtitle("启动失败：" + error, Theme.Danger); }
                });
            });
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            ServiceInfo s = Selected;
            if (s == null) return;
            if (!Dialog.ConfirmDanger(this, "停止服务",
                "立即停止服务「" + s.DisplayName + "」。",
                "可撤销：可随时重新启动该服务。",
                "停止后依赖它的功能会立即不可用；系统关键服务可能导致界面异常，请确认后再操作。",
                "停止", false))
                return;
            EnsureElevated();
            if (!Native.IsElevated()) return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在停止 " + s.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ServiceManager.Stop(s.Name, out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok) { SetSubtitle("已停止：" + s.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, "停止失败", "无法停止 " + s.Name + "：\r\n" + error); SetSubtitle("停止失败：" + error, Theme.Danger); }
                });
            });
        }

        private void OnDisableClick(object sender, EventArgs e)
        {
            ServiceInfo s = Selected;
            if (s == null) return;
            if (!Dialog.ConfirmDanger(this, "禁用服务",
                "把服务「" + s.DisplayName + "」的启动类型改为「禁用」。",
                "可撤销：之后可随时改回「自动」或「手动」；服务文件与其数据不会被删除。",
                "禁用后该服务不随系统启动，依赖它的功能可能不可用。",
                "禁用", false))
                return;
            EnsureElevated();
            if (!Native.IsElevated()) return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在禁用 " + s.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ServiceManager.ChangeStartMode(s.Name, "Disabled", out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok) { SetSubtitle("已禁用：" + s.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, "禁用失败", "无法禁用 " + s.Name + "：\r\n" + error); SetSubtitle("禁用失败：" + error, Theme.Danger); }
                });
            });
        }

        private void OnAutoClick(object sender, EventArgs e)
        {
            ServiceInfo s = Selected;
            if (s == null) return;
            EnsureElevated();
            if (!Native.IsElevated()) return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在设为自动 " + s.Name + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = ServiceManager.ChangeStartMode(s.Name, "Automatic", out error);
                Post(delegate
                {
                    Busy = false;
                    if (ok) { SetSubtitle("已设为自动：" + s.Name, Theme.Success); Load(); }
                    else { Dialog.Error(this, "设置失败", "无法将 " + s.Name + " 设为自动：\r\n" + error); SetSubtitle("设置失败：" + error, Theme.Danger); }
                });
            });
        }

        private void SearchPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);
            Gfx.StrokeRound(g, new Rectangle(0, 0, _searchWrap.Width - 1, _searchWrap.Height - 1), 6,
                Theme.BorderStrong, 1f);
            IconPainter.Draw(g, "search", new Rectangle(9, 8, 16, 16), Theme.TextMuted);
        }
    }
}
