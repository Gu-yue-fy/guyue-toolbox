using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    public sealed class DashboardView : ViewBase
    {
        private readonly StatCard _cpuCard = new StatCard();
        private readonly StatCard _memCard = new StatCard();
        private readonly StatCard _diskCard = new StatCard();
        private readonly StatCard _uptimeCard = new StatCard();

        private readonly HealthCard _health = new HealthCard();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly InfoList _sysInfo = new InfoList();
        private readonly DiskList _diskInfo = new DiskList();

        private readonly SysInfo.CpuLoadMeter _cpuMeter = new SysInfo.CpuLoadMeter();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        private AccentButton _checkButton;
        private AccentButton _refreshButton;

        private SystemSnapshot _snapshot;
        private bool _loading;
        private bool _deepLoaded;
        private bool _checking;
        private bool _exporting;

        public DashboardView()
            : base("系统概览", "第一次用？点右下「一键优化」一步到位；所有优化都能在「优化中心」随时还原")
        {
            _cpuCard.IconKind = "cpu";
            _cpuCard.AccentColor = Theme.Accent;
            _memCard.IconKind = "memory";
            _memCard.AccentColor = Theme.Purple;
            _diskCard.IconKind = "disk";
            _diskCard.AccentColor = Theme.Success;
            _uptimeCard.IconKind = "clock";
            _uptimeCard.AccentColor = Theme.Warning;

            _sysInfo.Caption = "系统信息";
            _sysInfo.IconKind = "info";
            _sysInfo.CaptionColor = Theme.Accent;

            _diskInfo.Caption = "磁盘使用情况";
            _diskInfo.IconKind = "disk";
            _diskInfo.CaptionColor = Theme.Success;

            _notice.NoticeIcon = "admin";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "当前以普通权限运行，部分优化与清理功能需要管理员权限。";

            // 按钮只负责触发；操作定义与归口见 DashboardCommands（命令号 dashboard.*）
            DashboardCommands.RegisterAll(this);
            _checkButton = AddCommand(DashboardCommands.HealthCheck, "shield", ButtonVariant.Primary, 128);
            _refreshButton = AddCommand(DashboardCommands.Refresh, "refresh", ButtonVariant.Secondary, 0);
            AddCommand(DashboardCommands.ExportReport, "doc", ButtonVariant.Ghost, 110);
            AddCommand(DashboardCommands.ReleaseMemory, "bolt", ButtonVariant.Ghost, 0);

            BuildHealth();
            BuildLayout();

            _timer.Interval = 3000;
            _timer.Tick += delegate { UpdateFastMetrics(); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        public override void OnActivated()
        {
            if (!_timer.Enabled) _timer.Start();
            if (!_deepLoaded) LoadData(true);
            else UpdateFastMetrics();
        }

        public override void OnDeactivated()
        {
            _timer.Stop();
        }

        public override bool IsBusy
        {
            get { return _loading || _checking; }
        }

        // --------------------------------------------------------------
        // 布局
        // --------------------------------------------------------------

        private void BuildHealth()
        {
            _checkButton2 = new AccentButton();
            _checkButton2.Text = "立即体检";
            _checkButton2.IconKind = "shield";
            _checkButton2.Variant = ButtonVariant.Primary;
            _checkButton2.Size = new Size(150, 44);
            _checkButton2.Click += OnHealthCheck;
            _health.Controls.Add(_checkButton2);

            _health.Resize += delegate
            {
                _checkButton2.Location = new Point(
                    Math.Max(10, _health.Width - 180), (_health.Height - 44) / 2);
            };
        }

        private AccentButton _checkButton2;
        private AccentButton _timerButton;
        private bool _timerOn;

        private void OnTimerToggle(object sender, EventArgs e)
        {
            if (_timerOn)
            {
                TimerResolution.Disable();
                _timerOn = false;
                _timerButton.Text = "开启 0.5ms 高精度计时器";
                _timerButton.Variant = ButtonVariant.Secondary;
                _timerButton.Invalidate();
                SetSubtitle("已恢复默认计时器精度。", Theme.TextSecondary);
                return;
            }

            double ms;
            if (TimerResolution.Enable(out ms))
            {
                _timerOn = true;
                _timerButton.Text = "关闭计时器（当前 " + ms.ToString("0.##") + "ms）";
                _timerButton.Variant = ButtonVariant.Primary;
                _timerButton.Invalidate();
                SetSubtitle("系统定时器精度已提升到 " + ms.ToString("0.##") +
                    "ms（默认 15.6ms）：游戏的帧生成与网络包计时更均匀，帧时间抖动明显降低。效果在本工具运行期间有效，关闭后系统自动还原。",
                    Theme.Success);
            }
            else
            {
                SetSubtitle("设置高精度计时器失败（系统可能不支持）。", Theme.Danger);
            }
        }

        private void BuildLayout()
        {
            // 首页聚焦「概览 + 快速动作」：健康体检 → 实时统计 → 游戏工具，
            // 权限提示条沉底（仅普通权限时可见），详细信息最后。
            // 功能入口统一走侧栏分组与 Ctrl+K 命令面板，不再铺直达磁贴。
            AddFull(_health, 156, 16);

            FlowLayoutPanel statsRow = MakeRowFixed(128, 16);
            statsRow.Controls.Add(_cpuCard);
            statsRow.Controls.Add(_memCard);
            statsRow.Controls.Add(_diskCard);
            statsRow.Controls.Add(_uptimeCard);
            AddRow(statsRow);

            // 游戏工具：0.5ms 高精度计时器
            FlowLayoutPanel timerRow = MakeRow(0, 0);
            _timerButton = new AccentButton();
            _timerButton.Text = "开启 0.5ms 高精度计时器";
            _timerButton.IconKind = "clock";
            _timerButton.Variant = ButtonVariant.Secondary;
            _timerButton.Size = new Size(238, 40);
            _timerButton.Click += OnTimerToggle;
            timerRow.Controls.Add(_timerButton);
            Label timerHint = new Label();
            timerHint.Text = "降低游戏帧时间抖动；关闭本工具自动还原";
            timerHint.ForeColor = Theme.TextMuted;
            timerHint.Font = Theme.FontSmall;
            timerHint.TextAlign = ContentAlignment.MiddleLeft;
            timerHint.AutoSize = true;
            timerHint.Margin = new Padding(10, 0, 0, 0);
            timerRow.Controls.Add(timerHint);
            AddRow(timerRow);

            AddFull(_notice, 42, 16);

            FlowLayoutPanel infoRow = MakeRow(320, 0);
            infoRow.Controls.Add(_sysInfo);
            infoRow.Controls.Add(_diskInfo);
            AddRow(infoRow);

            _sysInfo.Height = 320;
            _diskInfo.Height = 320;

            Body.Resize += delegate { SyncInfoHeights(); };
            SyncInfoHeights();
        }

        private bool _syncingHeights;

        private void SyncInfoHeights()
        {
            if (_syncingHeights) return;
            _syncingHeights = true;
            try
            {
                int infoHeight = Math.Max(200, _sysInfo.PreferredHeight);
                int diskHeight = Math.Max(200, _diskInfo.PreferredHeight);
                if (_sysInfo.Height != infoHeight) _sysInfo.Height = infoHeight;
                if (_diskInfo.Height != diskHeight) _diskInfo.Height = diskHeight;

                LayoutRows();
                RefreshLayout();

                // 内容不满视口时把两张大卡拉高吸收余量——底部不再悬空一块
                int contentBottom = Math.Max(_sysInfo.Bottom, _diskInfo.Bottom) + Body.Padding.Bottom;
                int slack = ViewportHeight - contentBottom;
                if (slack > 8)
                {
                    // 两卡同行（行高取最高卡）：每卡加满 slack 才能正好填满视口
                    int extra = Math.Min(slack, 400);
                    if (extra > 0)
                    {
                        _sysInfo.Height = _sysInfo.Height + extra;
                        _diskInfo.Height = _diskInfo.Height + extra;
                        LayoutRows();
                        RefreshLayout();
                    }
                }
            }
            finally
            {
                _syncingHeights = false;
            }
        }

        // --------------------------------------------------------------
        // 数据
        // --------------------------------------------------------------

        internal void OnRefreshClick(object sender, EventArgs e)
        {
            LoadData(true);
        }

        private void LoadData(bool deep)
        {
            if (_loading) return;
            _loading = true;
            if (_refreshButton != null) _refreshButton.Enabled = false;
            SetSubtitle("正在读取系统信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemSnapshot snap = null;
                Exception error = null;
                try
                {
                    _cpuMeter.Sample();
                    Thread.Sleep(220);
                    snap = SysInfo.Capture(deep, _cpuMeter.Sample());
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                Post(delegate
                {
                    _loading = false;
                    if (_refreshButton != null) _refreshButton.Enabled = true;

                    if (error != null)
                    {
                        SetSubtitle("读取系统信息失败：" + error.Message, Theme.Danger);
                        return;
                    }

                    _snapshot = snap;
                    _deepLoaded = _deepLoaded || deep;
                    Render();
                });
            });
        }

        private void UpdateFastMetrics()
        {
            if (!Visible) return;

            double load = _cpuMeter.Sample();
            if (load >= 0)
            {
                _cpuCard.MetricText = load.ToString("0") + " %";
                _cpuCard.Percent = load;
                _cpuCard.AccentColor = Gfx.LoadColor(load);
                _cpuCard.Invalidate();
            }

            MemoryInfo m = SysInfo.GetMemory();
            if (m.TotalBytes > 0)
            {
                _memCard.MetricText = m.UsedPercent.ToString("0") + " %";
                _memCard.Percent = m.UsedPercent;
                _memCard.AccentColor = Gfx.LoadColor(m.UsedPercent);
                _memCard.FooterText = "已用 " + SysInfo.FormatSize(m.UsedBytes) +
                    " / 共 " + SysInfo.FormatSize(m.TotalBytes);
                _memCard.Invalidate();
            }
        }

        private void Render()
        {
            SystemSnapshot s = _snapshot;
            if (s == null) return;

            SetSubtitle(s.ComputerName + " · " + s.OsName + " · " + s.UserName,
                s.Elevated ? Theme.Success : Theme.Warning);

            _notice.Visible = !s.Elevated;

            _cpuCard.CaptionText = "CPU 使用率";
            _cpuCard.MetricText = s.CpuLoadPercent >= 0 ? s.CpuLoadPercent.ToString("0") + " %" : "--";
            _cpuCard.Percent = s.CpuLoadPercent;
            _cpuCard.AccentColor = s.CpuLoadPercent >= 0 ? Gfx.LoadColor(s.CpuLoadPercent) : Theme.Accent;
            _cpuCard.FooterText = s.CpuCores + " 核 / " + s.CpuThreads + " 线程";
            _cpuCard.Invalidate();

            MemoryInfo m = s.Memory;
            _memCard.CaptionText = "内存占用";
            _memCard.MetricText = m.UsedPercent.ToString("0") + " %";
            _memCard.Percent = m.UsedPercent;
            _memCard.AccentColor = Gfx.LoadColor(m.UsedPercent);
            _memCard.FooterText = "已用 " + SysInfo.FormatSize(m.UsedBytes) +
                " / 共 " + SysInfo.FormatSize(m.TotalBytes);
            _memCard.Invalidate();

            DiskInfo systemDisk = null;
            for (int i = 0; i < s.Disks.Count; i++)
            {
                if (s.Disks[i].Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                {
                    systemDisk = s.Disks[i];
                    break;
                }
            }
            if (systemDisk == null && s.Disks.Count > 0) systemDisk = s.Disks[0];

            if (systemDisk != null)
            {
                _diskCard.CaptionText = "系统盘 " + systemDisk.Name.TrimEnd('\\');
                _diskCard.MetricText = systemDisk.UsedPercent.ToString("0") + " %";
                _diskCard.Percent = systemDisk.UsedPercent;
                _diskCard.AccentColor = Gfx.LoadColor(systemDisk.UsedPercent);
                _diskCard.FooterText = "可用 " + SysInfo.FormatSize(systemDisk.FreeBytes) +
                    " / 共 " + SysInfo.FormatSize(systemDisk.TotalBytes);
            }
            else
            {
                _diskCard.CaptionText = "系统盘";
                _diskCard.MetricText = "--";
                _diskCard.Percent = -1;
                _diskCard.FooterText = "未检测到本地磁盘";
            }
            _diskCard.Invalidate();

            _uptimeCard.CaptionText = "系统运行时间";
            _uptimeCard.MetricText = FormatUptime(s.Uptime);
            _uptimeCard.Percent = -1;
            _uptimeCard.FooterText = s.BootTime == DateTime.MinValue
                ? "" : "上次启动：" + s.BootTime.ToString("MM-dd HH:mm");
            _uptimeCard.Invalidate();

            _sysInfo.Clear();
            _sysInfo.Add("计算机名", s.ComputerName);
            _sysInfo.Add("当前用户", s.UserName);
            _sysInfo.Add("操作系统", s.OsName);
            _sysInfo.Add("系统版本", s.OsVersion + "  " + s.OsArch);
            _sysInfo.Add("处理器", s.CpuName);
            _sysInfo.Add("核心 / 线程", s.CpuCores + " / " + s.CpuThreads);
            _sysInfo.Add("显卡", s.GpuName);
            _sysInfo.Add("主板", string.IsNullOrEmpty(s.BaseBoard) ? "未检测到" : s.BaseBoard);
            _sysInfo.Add("BIOS", string.IsNullOrEmpty(s.BiosVersion) ? "未检测到" : s.BiosVersion);
            _sysInfo.Add("权限", s.Elevated ? "管理员" : "标准用户");
            _sysInfo.Invalidate();

            _diskInfo.Clear();
            for (int i = 0; i < s.Disks.Count; i++)
            {
                DiskInfo d = s.Disks[i];
                DiskList.DiskItem item = new DiskList.DiskItem();
                item.Name = d.Name;
                item.Label = d.Label;
                item.Total = d.TotalBytes;
                item.Free = d.FreeBytes;
                item.Removable = d.DriveType == "可移动";
                _diskInfo.Add(item);
            }
            _diskInfo.Invalidate();

            SyncInfoHeights();
        }

        private static string FormatUptime(TimeSpan t)
        {
            if (t.TotalDays >= 1) return ((int)t.TotalDays) + " 天 " + t.Hours + " 小时";
            if (t.TotalHours >= 1) return ((int)t.TotalHours) + " 小时 " + t.Minutes + " 分";
            return Math.Max(0, (int)t.TotalMinutes) + " 分钟";
        }

        // --------------------------------------------------------------
        // 一键体检
        // --------------------------------------------------------------

        internal void OnHealthCheck(object sender, EventArgs e)
        {
            if (_checking) return;
            _checking = true;
            if (_checkButton != null) _checkButton.Enabled = false;
            if (_checkButton2 != null)
            {
                _checkButton2.Enabled = false;
                _checkButton2.Text = "正在体检…";
            }
            _health.SetScanning(true);
            SetSubtitle("正在体检，请稍候…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder report = new StringBuilder();
                int score = 100;
                int issues = 0;

                try
                {
                    // 1. 垃圾体积
                    long junk = 0;
                    try
                    {
                        List<JunkCategory> cats = JunkScanner.BuildDefaultCategories();
                        JunkScanner scanner = new JunkScanner();
                        for (int i = 0; i < cats.Count; i++)
                        {
                            scanner.Scan(cats[i]);
                            junk += cats[i].Size;
                        }
                    }
                    catch
                    {
                    }

                    report.AppendLine("【垃圾文件】" + SysInfo.FormatSize(junk));
                    if (junk > 2L * 1024 * 1024 * 1024) { score -= 12; issues++; report.AppendLine("  可清理空间较多，建议清理。"); }
                    else if (junk > 500L * 1024 * 1024) { score -= 6; issues++; report.AppendLine("  建议清理。"); }
                    else report.AppendLine("  正常。");
                    report.AppendLine();

                    // 2. 启动项
                    int startup = 0;
                    try { startup = StartupManager.Load().Count; }
                    catch { }
                    report.AppendLine("【启动项】共 " + startup + " 项");
                    if (startup > 20) { score -= 8; issues++; report.AppendLine("  启动项偏多，建议禁用不必要的项目。"); }
                    else if (startup > 12) { score -= 4; issues++; report.AppendLine("  可以适当精简。"); }
                    else report.AppendLine("  正常。");
                    report.AppendLine();

                    // 3. 优化项
                    int pending = 0;
                    try
                    {
                        List<ITweak> tweaks = TweakLibrary.All();
                        for (int i = 0; i < tweaks.Count; i++)
                        {
                            try { if (!tweaks[i].IsApplied()) pending++; }
                            catch { }
                        }
                    }
                    catch
                    {
                    }
                    report.AppendLine("【系统优化】待优化 " + pending + " 项");
                    if (pending >= 12) { score -= 15; issues++; report.AppendLine("  建议执行一键推荐优化。"); }
                    else if (pending >= 1) { score -= 8; issues++; report.AppendLine("  有几项可以优化。"); }
                    else report.AppendLine("  已全部优化。");
                    report.AppendLine();

                    // 4. 内存
                    MemoryInfo mem = SysInfo.GetMemory();
                    report.AppendLine("【内存占用】" + mem.UsedPercent.ToString("0") + "%");
                    if (mem.UsedPercent > 85) { score -= 12; issues++; report.AppendLine("  内存紧张，建议整理。"); }
                    else if (mem.UsedPercent > 70) { score -= 6; issues++; report.AppendLine("  偏高。"); }
                    else report.AppendLine("  正常。");
                    report.AppendLine();

                    // 5. 系统盘
                    List<DiskInfo> disks = SysInfo.GetDisks(true);
                    DiskInfo sys = null;
                    for (int i = 0; i < disks.Count; i++)
                    {
                        if (disks[i].Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) { sys = disks[i]; break; }
                    }
                    if (sys == null && disks.Count > 0) sys = disks[0];

                    if (sys != null)
                    {
                        report.AppendLine("【系统盘 " + sys.Name.TrimEnd('\\') + " 已用】" +
                            sys.UsedPercent.ToString("0") + "%");
                        if (sys.UsedPercent > 90) { score -= 18; issues++; report.AppendLine("  空间严重不足，建议尽快清理。"); }
                        else if (sys.UsedPercent > 80) { score -= 9; issues++; report.AppendLine("  空间偏紧。"); }
                        else report.AppendLine("  正常。");
                    }

                    if (score < 0) score = 0;
                    if (score > 100) score = 100;

                    int finalScore = score;
                    int issueCount = issues;

                    Post(delegate
                    {
                        _checking = false;
                        if (_checkButton != null) _checkButton.Enabled = true;
                        if (_checkButton2 != null)
                        {
                            _checkButton2.Enabled = true;
                            _checkButton2.Text = "重新体检";
                        }

                        // 得分环形滚动动画
                        _health.SetScanning(false);
                        _health.SetScoreAnimated(finalScore);
                        ApplyScore(finalScore, issueCount);

                        string title = "体检完成，得分 " + finalScore + " 分";
                        SetSubtitle(title + "，共 " + issueCount + " 项建议处理。",
                            issueCount > 0 ? Theme.Warning : Theme.Success);

                        if (issueCount > 0)
                        {
                            // 体检结果已在卡片与副标题完整呈现，不再弹窗打断；
                            // 建议明细通过「导出报告」查看。
                        }
                        else
                        {
                            Dialog.Success(this, title, "系统状态良好，无需处理。");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Post(delegate
                    {
                        _checking = false;
                        if (_checkButton != null) _checkButton.Enabled = true;
                        if (_checkButton2 != null)
                        {
                            _checkButton2.Enabled = true;
                            _checkButton2.Text = "立即体检";
                        }
                        SetSubtitle("体检失败：" + ex.Message, Theme.Danger);
                        Dialog.Error(this, "体检失败", ex.Message);
                    });
                }
            });
        }

        private void ApplyScore(int score, int issues)
        {
            // 分数已由 SetScoreAnimated 以滚动动画呈现，这里只补颜色与文案
            _health.ScoreColor = score >= 85 ? Theme.Success : (score >= 60 ? Theme.Warning : Theme.Danger);
            _health.StatusText = issues == 0
                ? "系统状态良好，无需处理"
                : "发现 " + issues + " 项建议处理的问题";
            _health.HintText = issues == 0
                ? "保持当前状态即可，建议每周体检一次。"
                : "点击右侧「重新体检」复查，或前往对应页面处理。";
        }

        // --------------------------------------------------------------

        internal void OnReleaseMemory(object sender, EventArgs e)
        {
            AccentButton btn = sender as AccentButton;
            if (btn != null) btn.Enabled = false;
            SetSubtitle("正在整理内存…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                long before = 0, after = 0;
                int touched = 0;
                try
                {
                    touched = ProcManager.ReleaseMemory(out before, out after);
                }
                catch
                {
                }

                Post(delegate
                {
                    if (btn != null) btn.Enabled = true;
                    long delta = after - before;
                    string msg = delta > 0
                        ? "已释放约 " + SysInfo.FormatSize(delta) + " 物理内存。"
                        : "内存已经比较充裕，本次没有明显释放空间。";

                    SetSubtitle("内存整理完成，" + msg, Theme.Success);
                    UpdateFastMetrics();
                    Dialog.Success(this, "整理完成",
                        msg + "\r\n\r\n共整理了 " + touched + " 个进程的工作集。");
                });
            });
        }

        internal void OnExportClick(object sender, EventArgs e)
        {
            if (_exporting) return;
            _exporting = true;
            SetSubtitle("正在生成系统报告…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string path;
                string content;
                bool ok = SystemReport.Save(out path, out content);
                Post(delegate
                {
                    _exporting = false;
                    if (ok)
                    {
                        SetSubtitle("报告已保存：" + path, Theme.Success);
                        Dialog.Output(this, "系统报告已生成", content);
                    }
                    else
                    {
                        SetSubtitle("报告生成失败。", Theme.Danger);
                        Dialog.Error(this, "生成失败", "无法写入报告文件，请检查桌面目录权限。");
                    }
                });
            });
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
            {            }
        }
    }
}
