﻿/* ============================================================
 * 文件说明：系统概览页：体检评分、资源统计与功能矩阵
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class DashboardView : ViewBase
    {
        /// <summary>数据带（对齐设计概览页的主视觉承载面）：CPU / 内存 / 系统盘 / 运行时间 四格连成一体。</summary>
        private readonly MetricBand _band = new MetricBand();
        private readonly SectionTitle _secDevice = new SectionTitle();
        private readonly SectionTitle _secMatrix = new SectionTitle();

        private readonly HealthCard _health = new HealthCard();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly InfoList _sysInfo = new InfoList();
        private readonly DiskList _diskInfo = new DiskList();

        private readonly SysInfo.CpuLoadMeter _cpuMeter = new SysInfo.CpuLoadMeter();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        private AccentButton _checkButton;
        private AccentButton _refreshButton;

        /// <summary>电竞模式卡片：标题 + 状态 chip + 进入/退出按钮。</summary>
        private readonly EsportsCard _esportsCard = new EsportsCard();
        private bool _esportsBusy;

        private static MetricBand.Cell NewCell(string label, Color tone)
        {
            MetricBand.Cell c = new MetricBand.Cell();
            c.Label = label;
            c.Tone = tone;
            return c;
        }

        private SystemSnapshot _snapshot;
        private bool _loading;
        private bool _deepLoaded;
        private bool _checking;
        private bool _exporting;

        public DashboardView()
            : base("系统概览", AppSettings.FirstRun
                ? "第一次用？点右上「一键体检」查看系统健康；每项优化都能在「优化中心」随时还原"
                : "系统健康总览：体检得分 · 资源占用 · 优化建议")
        {
            _band.SetCells(new MetricBand.Cell[]
            {
                NewCell("CPU", Theme.Accent),
                NewCell("内存", Theme.Purple),
                NewCell("系统盘", Theme.Success),
                NewCell("运行时间", Theme.Warning)
            });

            _secDevice.TitleText = "设备与系统信息";
            _secDevice.HintText = "计算机名 · 操作系统 · 处理器 · 显卡";
            _secDevice.Tone = Theme.Accent;

            _secMatrix.TitleText = "功能矩阵";
            _secMatrix.HintText = "常用功能一屏直达";
            _secMatrix.Tone = Theme.Cyan;

            _sysInfo.Caption = "系统信息";
            _sysInfo.IconKind = "info";
            _sysInfo.CaptionColor = Theme.Accent;

            _diskInfo.Caption = "磁盘使用情况";
            _diskInfo.IconKind = "disk";
            _diskInfo.CaptionColor = Theme.Accent;

            // 权限提示按实际情况显示——管理员下不再误报"普通权限"
            if (Native.IsElevated())
            {
                _notice.NoticeIcon = "shield";
                _notice.NoticeAccent = Theme.Success;
                _notice.NoticeText = "已以管理员身份运行，全部功能可用。所有优化在「优化中心」随时可还原。";
            }
            else
            {
                _notice.NoticeIcon = "admin";
                _notice.NoticeAccent = Theme.Warning;
                _notice.NoticeText = "当前以普通权限运行，部分优化与清理功能需要管理员权限。";
            }

            // 顶栏操作按钮（原 DashboardCommands 命令框架已删除，直接绑定处理器）
            // 体检入口不在这里：它和「一键修复」一起放在体检卡右侧（主操作贴着它作用的对象）
            _refreshButton = AddAction("刷新", "refresh", ButtonVariant.Secondary, OnRefreshClick, 0);
            AddAction("导出报告", "doc", ButtonVariant.Secondary, OnExportClick, 110);
            AddAction("释放内存", "memory", ButtonVariant.Ghost, OnReleaseMemory, 0);

            BuildHealth();
            BuildLayout();

            _esportsCard.EnterClicked += OnEnterEsports;
            _esportsCard.ExitClicked += OnExitEsports;
            // 注意：这里刻意**不**同步调用 UpdateEsportsState()——
            // 它内部要 TweakLibrary.All()（近 400 项 + 硬件适用性探测，首次约 2 秒），
            // 放在构造函数里会把首屏前的时间全吃掉（实测 2.1 秒）。
            // 状态改由 OnActivated → RefreshEsportsStateAsync() 后台刷新。

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
            RefreshEsportsStateAsync();
        }

        /// <summary>
        /// 电竞模式状态异步刷新：先确保优化项库已构建（后台，命中缓存时是毫秒级），
        /// 再回 UI 线程更新 chip。避免任何一次首屏显示被库构建阻塞。
        /// </summary>
        private void RefreshEsportsStateAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { TweakLibrary.All(); }
                catch { }
                Post(delegate { UpdateEsportsState(); });
            });
        }

        public override void OnDeactivated()
        {
            _timer.Stop();
            if (AppSettings.FirstRun) AppSettings.MarkFirstRunDone(); // 用户已离开首页：引导使命完成
        }

        public override bool IsBusy
        {
            get { return _loading || _checking || _esportsBusy; }
        }

        // --------------------------------------------------------------
        // 布局
        // --------------------------------------------------------------

        private void BuildHealth()
        {
            // 体检入口与「一键修复」并列放在卡片右侧，页头只留通用动作：
            // 主操作贴着它作用的对象（这张体检卡），比挂在页头动作条更好找。
            _checkButton = new AccentButton();
            _checkButton.Text = "一键体检";
            _checkButton.IconKind = "shield";
            _checkButton.Variant = ButtonVariant.Primary;
            _checkButton.Size = new Size(150, 44);
            _checkButton.Click += OnHealthCheck;
            _health.Controls.Add(_checkButton);

            // 体检闭环的另一半：查出问题后直接给出"能自动处理的那部分"的入口。
            // 之前体检只给分数并把用户推到别的页，等于"发现问题和解决问题之间断了"。
            _fixButton = new AccentButton();
            _fixButton.Text = "一键修复";
            _fixButton.IconKind = "check";
            _fixButton.Variant = ButtonVariant.Warning;
            _fixButton.Size = new Size(150, 44);
            _fixButton.Visible = false;
            _fixButton.Click += OnFixAll;
            _health.Controls.Add(_fixButton);

            _health.Resize += delegate { LayoutHealthActions(); };
            LayoutHealthActions();
        }

        /// <summary>
        /// 卡片右侧两个动作的排布：只有体检时它垂直居中；
        /// 「一键修复」出现后体检上移、修复在下方，形成「先体检 → 再修复」的阅读顺序。
        /// </summary>
        private void LayoutHealthActions()
        {
            int x = Math.Max(10, _health.Width - 180);
            int cy = (_health.Height - 44) / 2;
            bool two = _fixButton != null && _fixButton.Visible;
            if (_checkButton != null) _checkButton.Location = new Point(x, two ? cy - 26 : cy);
            if (_fixButton != null) _fixButton.Location = new Point(x, cy + 26);
        }

        private AccentButton _fixButton;

        private void BuildLayout()
        {
            // 版式对齐设计 OverviewPage：体检卡 → 电竞模式卡 → 数据带 → 提示 → 设备信息 → 功能矩阵。
            AddFull(_health, 156, 14);

            // 电竞模式（英雄区第二个入口）：一键进/出，chip 实时显示已启用项数
            AddFull(_esportsCard, EsportsCard.HeightHint, 14);

            // 数据带：四格连成一体（格间 1px 竖线），概览页唯一的强主视觉
            AddFull(_band, MetricBand.CellMinHeight, 14);

            AddFull(_notice, 34, 14);

            AddFull(_secDevice, 44, 8);
            _infoRow = MakeRow(320, 14);
            _infoRow.Controls.Add(_sysInfo);
            _infoRow.Controls.Add(_diskInfo);
            AddRow(_infoRow);

            // 功能矩阵：3 列磁贴（设计 OverviewModuleTile），常用入口一屏直达
            AddFull(_secMatrix, 44, 8);
            // "optimize|分组名" = 进入优化中心并选中该分类：
            // 10 个「按分组过滤」的独立页面已收敛为页内分类 chip，避免侧栏出现 11 个同类入口
            AddRow(BuildTileRow("optimize", "全部优化项", "逐项开关，随时还原", "tune", Theme.Accent,
                "optimize|性能优化", "性能优化", "CPU · 内存 · 文件系统", "bolt", Theme.Cyan,
                "optimize|游戏优化", "游戏优化", "降延迟 · 全屏独占", "play", Theme.Purple));
            AddRow(BuildTileRow("optimize|网络优化", "网络优化", "TCP · DNS · 网卡", "globe", Theme.Success,
                "optimize|系统服务", "系统服务", "后台服务启停", "services", Theme.Warning,
                "cleaner", "垃圾清理", "临时文件 · 缓存 · 回收站", "clean", Theme.Danger));

            Body.Resize += delegate { SyncInfoHeights(); };
            SyncInfoHeights();
        }

        /// <summary>一行 3 个功能磁贴。</summary>
        private FlowLayoutPanel BuildTileRow(string k1, string t1, string d1, string i1, Color a1,
            string k2, string t2, string d2, string i2, Color a2,
            string k3, string t3, string d3, string i3, Color a3)
        {
            FlowLayoutPanel row = MakeRowFixed(92, 10);
            row.Controls.Add(MakeTile(k1, t1, d1, i1, a1));
            row.Controls.Add(MakeTile(k2, t2, d2, i2, a2));
            row.Controls.Add(MakeTile(k3, t3, d3, i3, a3));
            return row;
        }

        private static FeatureTile MakeTile(string key, string title, string desc, string icon, Color accent)
        {
            FeatureTile t = new FeatureTile(title, desc, icon, accent);
            t.Height = 92;
            t.Margin = new Padding(0, 0, 10, 0);
            t.Click += delegate
            {
                MainForm mf = MainForm.Current;
                if (mf == null) return;

                // 支持 "页面键|目标名"：优化中心用它选中具体分类（OptimizeView.PendingGroup）。
                string page = key;
                int bar = key.IndexOf('|');
                if (bar > 0)
                {
                    string tail = key.Substring(bar + 1);
                    page = key.Substring(0, bar);
                    if (string.Equals(page, "optimize", StringComparison.OrdinalIgnoreCase)) OptimizeView.PendingGroup = tail;
                }
                mf.NavigateTo(page);
            };
            return t;
        }

        private FlowLayoutPanel _infoRow;
        private bool _syncingHeights;

        private void SyncInfoHeights()
        {
            if (_syncingHeights) return;
            _syncingHeights = true;
            try
            {
                // 信息行高度自适应视口：
                // - 大屏（视口 > 内容）：拉高信息/磁盘两卡吸收底部余量——页面恰好铺满，不露空、不需要滚
                // - 小屏（视口 < 内容）：保持内容准高，超出部分靠滚轮/滚动条浏览
                // 卡片自绘内容顶部对齐，拉高部分是卡片底色，视觉上是"更大的卡"而不是空缺。
                int infoHeight = Math.Max(200, _sysInfo.PreferredHeight);
                int diskHeight = Math.Max(200, _diskInfo.PreferredHeight);
                int cardH = Math.Max(infoHeight, diskHeight);

                // 上方固定内容：体检卡 156+14 / 电竞模式卡 +14 / 数据带 166+14 / 提示条 34+14 / 区块标题 44+8
                int usedTop = Body.Padding.Top + 156 + 14 + EsportsCard.HeightHint + 14
                    + MetricBand.CellMinHeight + 14 + 34 + 14 + 44 + 8;
                int remain = ViewportHeight - usedTop - Body.Padding.Bottom;
                // 上限 420：下方还有「功能矩阵」区块，信息行不能再无限吸收余量，
                // 否则矩阵会被推到折叠线以下（设计的设备信息区本就是紧凑三列）。
                int rowH = Math.Max(300, Math.Min(remain, 420));
                if (_infoRow.Height != rowH) _infoRow.Height = rowH;

                cardH = Math.Max(cardH, rowH);
                if (_sysInfo.Height != cardH) _sysInfo.Height = cardH;
                if (_diskInfo.Height != cardH) _diskInfo.Height = cardH;

                LayoutRows();
                RefreshLayout();
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

            // 本方法由 3 秒 Timer.Tick 在 UI 线程直接调用：一旦采样抛异常（性能计数器
            // 不可用 / WMI 访问被拒等）会直接崩进程，因此整体包一层保护。
            try
            {
                double load = _cpuMeter.Sample();
                if (load >= 0)
                {
                    _band.UpdateCell(0, load.ToString("0") + " %", load, LoadStatus(load), null);
                    _band.SetTone(0, Gfx.LoadColor(load));
                }

                MemoryInfo m = SysInfo.GetMemory();
                if (m.TotalBytes > 0)
                {
                    _band.UpdateCell(1, m.UsedPercent.ToString("0") + " %", m.UsedPercent,
                        LoadStatus(m.UsedPercent),
                        "已用 " + SysInfo.FormatSize(m.UsedBytes) + " / 共 " + SysInfo.FormatSize(m.TotalBytes));
                    _band.SetTone(1, Gfx.LoadColor(m.UsedPercent));
                }
            }
            catch
            {
                // 采样失败时不更新卡片，静默跳过（下一次 Tick 再试）。
            }

            // 电竞模式状态轻量刷新：3 秒一刷可接受，内部再套一层保护（IsApplied 可能访问注册表/服务）
            UpdateEsportsState();
        }

        private void Render()
        {
            SystemSnapshot s = _snapshot;
            if (s == null) return;

            // 首次运行时保留新手指引文案；权限提示条已按实际权限条件化，始终显示
            if (!AppSettings.FirstRun)
            {
                SetSubtitle(s.ComputerName + " · " + s.OsName + " · " + s.UserName,
                    s.Elevated ? Theme.Success : Theme.Warning);
            }
            _notice.Visible = true;

            _band.UpdateCell(0,
                s.CpuLoadPercent >= 0 ? s.CpuLoadPercent.ToString("0") + " %" : "--",
                s.CpuLoadPercent, LoadStatus(s.CpuLoadPercent),
                s.CpuCores + " 核 / " + s.CpuThreads + " 线程");
            _band.SetTone(0, s.CpuLoadPercent >= 0 ? Gfx.LoadColor(s.CpuLoadPercent) : Theme.Accent);

            MemoryInfo m = s.Memory;
            _band.UpdateCell(1, m.UsedPercent.ToString("0") + " %", m.UsedPercent,
                LoadStatus(m.UsedPercent),
                "已用 " + SysInfo.FormatSize(m.UsedBytes) + " / 共 " + SysInfo.FormatSize(m.TotalBytes));
            _band.SetTone(1, Gfx.LoadColor(m.UsedPercent));

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
                _band.UpdateCell(2, systemDisk.UsedPercent.ToString("0") + " %", systemDisk.UsedPercent,
                    LoadStatus(systemDisk.UsedPercent) + " · " + systemDisk.Name.TrimEnd('\\'),
                    "可用 " + SysInfo.FormatSize(systemDisk.FreeBytes) +
                    " / 共 " + SysInfo.FormatSize(systemDisk.TotalBytes));
                _band.SetTone(2, Gfx.LoadColor(systemDisk.UsedPercent));
            }
            else
            {
                _band.UpdateCell(2, "--", -1, "未检测到本地磁盘", "");
            }

            _band.UpdateCell(3, FormatUptime(s.Uptime), -1, "自上次启动",
                s.BootTime == DateTime.MinValue ? "" : "上次启动 " + s.BootTime.ToString("MM-dd HH:mm"));

            _sysInfo.Clear();
            // 核心 6 行：概览首屏一屏放下（主板/BIOS/系统版本等完整信息在「导出报告」里）
            _sysInfo.Add("计算机名", s.ComputerName);
            _sysInfo.Add("当前用户", s.UserName);
            _sysInfo.Add("操作系统", s.OsName + " (" + s.OsArch + ")");
            _sysInfo.Add("处理器", s.CpuName);
            _sysInfo.Add("核心 / 线程", s.CpuCores + " / " + s.CpuThreads);
            _sysInfo.Add("显卡", s.GpuName);
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

        /// <summary>把占用率转成一句状态文案（设计数据带的状态行）。</summary>
        private static string LoadStatus(double percent)
        {
            if (percent < 0) return "";
            if (percent >= 90) return "占用很高";
            if (percent >= 70) return "占用偏高";
            return "占用正常";
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
            if (_checkButton != null)
            {
                _checkButton.Enabled = false;
                _checkButton.Text = "正在体检…";
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
                        scanner.ScanAll(cats, null); // 与清理页共用并行扫描
                        for (int i = 0; i < cats.Count; i++) junk += cats[i].Size;
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
                        if (_checkButton != null)
                        {
                            _checkButton.Enabled = true;
                            _checkButton.Text = "重新体检";
                        }

                        // 得分环形滚动动画
                        _health.SetScanning(false);
                        _health.SetScoreAnimated(finalScore);
                        ApplyScore(finalScore, issueCount);

                        // 有建议项时才出现「一键修复」：没有问题时不留一个点不动的按钮
                        if (_fixButton != null)
                        {
                            _fixButton.Visible = issueCount > 0;
                            _fixButton.Text = issueCount > 0 ? "一键修复" : "";
                            LayoutHealthActions();   // 修复出现后体检上移，两个按钮不重叠
                        }

                        string title = "体检完成，得分 " + finalScore + " 分";
                        SetSubtitle(issueCount > 0
                            ? title + "，共 " + issueCount + " 项建议——清理与启动项可到「清理与磁盘」处理。"
                            : title + "，系统状态良好。",
                            issueCount > 0 ? Theme.Warning : Theme.Success);
                        AppSettings.MarkFirstRunDone();

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
                        if (_checkButton != null)
                        {
                            _checkButton.Enabled = true;
                            _checkButton.Text = "一键体检";
                        }
                        SetSubtitle("体检失败：" + ex.Message, Theme.Danger);
                        Dialog.Error(this, "体检失败", ex.Message);
                    });
                }
            });
        }

        /// <summary>
        /// 一键修复：只处理"本工具确实能自动修"的那部分（清缓存、刷新解析、关闭异常代理、
        /// 应用推荐优化项），复用 RepairCenter 的检测与修复动作，不另造一套。
        /// 修不了的一律明说去哪手动处理——不给假承诺。
        /// </summary>
        internal void OnFixAll(object sender, EventArgs e)
        {
            if (_checking) return;
            if (!Dialog.ConfirmDanger(this, "处理可自动修复项",
                "扫描并修复本工具能自动处理的问题：清理缓存、刷新 DNS、关闭异常代理、应用推荐优化项。",
                "可撤销：清理的是缓存（系统会按需重建）；优化项与代理改动都有备份，可逐项改回。",
                "不会删除个人文件、不改驱动、不结束进程；需要手动处理的问题（如驱动异常）只做提示。",
                "开始处理", false))
                return;

            _checking = true;
            if (_fixButton != null) _fixButton.Enabled = false;
            SetSubtitle("正在处理可自动修复项…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int fixedCount = 0;
                int fixable = 0;
                string firstError = null;
                try
                {
                    bool cancelled;
                    List<RepairItem> items = RepairCenter.Scan(null, null, out cancelled);

                    // 先修复，再复检：只有"原来检出、复检不再检出"才算真修好
                    // （Fix 不再伪造状态，否则这里的计数会永远是 0 或与事实不符）
                    List<string> targets = new List<string>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        RepairItem it = items[i];
                        if (!it.Detected || !it.Fixable) continue;
                        fixable++;
                        targets.Add(it.Id);
                        string msg = RepairCenter.Fix(it);
                        if (firstError == null && (msg ?? "").IndexOf("失败", StringComparison.Ordinal) >= 0) firstError = msg;
                    }

                    if (targets.Count > 0)
                    {
                        List<RepairItem> again = RepairCenter.Scan(null, null, out cancelled);
                        for (int i = 0; i < again.Count; i++)
                        {
                            RepairItem it = again[i];
                            if (targets.Contains(it.Id) && !it.Detected) fixedCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    firstError = ex.Message;
                }

                Post(delegate
                {
                    _checking = false;
                    if (_fixButton != null) _fixButton.Enabled = true;

                    if (fixable == 0)
                    {
                        SetSubtitle("没有可自动修复的问题；其余建议需手动处理（详见「修复中心」）。",
                            Theme.TextSecondary);
                        if (_fixButton != null) { _fixButton.Visible = false; LayoutHealthActions(); }
                        return;
                    }

                    SetSubtitle("已处理 " + fixedCount + " / " + fixable + " 项可自动修复问题"
                        + (fixedCount < fixable ? "，其余多为文件被占用，稍后重试即可。" : "。"),
                        fixedCount > 0 ? Theme.Success : Theme.Warning);
                    Toast("已处理 " + fixedCount + " 项", "正在重新体检以刷新评分…", ToastKind.Success);
                    OnHealthCheck(this, EventArgs.Empty);
                });
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

        // --------------------------------------------------------------
        // 电竞模式
        // --------------------------------------------------------------

        /// <summary>进入电竞模式：确认谨慎项 → 应用预设 → 抬升计时器分辨率并 keepalive。</summary>
        private void OnEnterEsports(object sender, EventArgs e)
        {
            if (_esportsBusy) return;

            // 谨慎项先说明风险：确认后才应用（全部可还原）
            List<string> risky = TweakLibrary.RiskyInPreset(TweakLibrary.EsportsIds);
            if (risky.Count > 0)
            {
                if (!Dialog.ConfirmDanger(this, "进入电竞模式",
                    "将应用电竞预设共 " + TweakLibrary.EsportsIds.Length + " 项，其中以下谨慎项会一并生效：" + RiskyNames(risky) + "。",
                    "全部可还原：点「退出电竞模式」即整组还原，也可到「优化中心」逐项关闭。",
                    "主要调整游戏模式 / 网络与多媒体调度 / 电源计划 / 后台 Xbox 服务；不删除文件、不改驱动。",
                    "进入电竞模式", false))
                    return;
            }

            _esportsBusy = true;
            _esportsCard.Busy = true;
            SetSubtitle("正在进入电竞模式…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> report = new List<string>();
                int[] result = TweakLibrary.ApplyPreset(TweakLibrary.EsportsIds, report);
                // 抬高系统计时器分辨率到 0.5ms 并启动 keepalive（TimerResolution 自带维持线程）
                double appliedMs;
                bool timerOk = TimerResolution.Start(0.5, out appliedMs);

                Post(delegate
                {
                    _esportsBusy = false;
                    _esportsCard.Busy = false;
                    UpdateEsportsState();

                    int ok = result[0], skip = result[1], fail = result[2];
                    string msg = "电竞模式已启用：成功 " + ok + " / 跳过 " + skip + " / 失败 " + fail;
                    SetSubtitle(msg, fail > 0 ? Theme.Warning : Theme.Success);
                    Toast(msg,
                        timerOk ? "计时器分辨率已抬升至 0.5ms，退出电竞模式自动释放。"
                                : "计时器分辨率提升未生效（本机可能不支持）。",
                        fail > 0 ? ToastKind.Warning : ToastKind.Success);

                    if (fail > 0) Dialog.Output(this, "电竞模式启用详情", BuildReportText(report));
                });
            });
        }

        /// <summary>退出电竞模式：还原日常预设并释放计时器分辨率。</summary>
        private void OnExitEsports(object sender, EventArgs e)
        {
            if (_esportsBusy) return;

            _esportsBusy = true;
            _esportsCard.Busy = true;
            SetSubtitle("正在退出电竞模式…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> report = new List<string>();
                int[] result = TweakLibrary.RevertPreset(TweakLibrary.DailyIds, report);
                TimerResolution.Disable(); // 释放 0.5ms 请求，恢复系统默认调度

                Post(delegate
                {
                    _esportsBusy = false;
                    _esportsCard.Busy = false;
                    UpdateEsportsState();

                    int ok = result[0], fail = result[1];
                    SetSubtitle("已退出电竞模式，计时器分辨率已释放。", Theme.Success);
                    Toast("电竞模式已退出",
                        "已还原 " + ok + " / " + (ok + fail) + " 项，其余保持现状。",
                        fail > 0 ? ToastKind.Warning : ToastKind.Success);
                });
            });
        }

        /// <summary>统计预设中 IsApplied 的项数并刷新 chip 状态（内部 try/catch 保护）。</summary>
        private void UpdateEsportsState()
        {
            try
            {
                List<ITweak> all = TweakLibrary.All();
                string[] ids = TweakLibrary.EsportsIds;
                int applied = 0;
                for (int j = 0; j < ids.Length; j++)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (!string.Equals(all[i].Id, ids[j], StringComparison.OrdinalIgnoreCase)) continue;
                        try { if (all[i].IsApplied()) applied++; }
                        catch { }
                        break;
                    }
                }
                _esportsCard.SetState(applied, ids.Length);
            }
            catch
            {
                // 采样失败：保留上一次 chip 显示，下一次 Tick 再试
            }
        }

        /// <summary>把谨慎项 Id 翻译成可读的中文名称（找不到时回退到 Id）。</summary>
        private static string RiskyNames(List<string> ids)
        {
            List<ITweak> all = TweakLibrary.All();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                string name = ids[i];
                for (int j = 0; j < all.Count; j++)
                {
                    if (string.Equals(all[j].Id, ids[i], StringComparison.OrdinalIgnoreCase))
                    {
                        name = all[j].Name;
                        break;
                    }
                }
                if (i > 0) sb.Append("、");
                sb.Append(name);
            }
            return sb.ToString();
        }

        /// <summary>把预设报告整理成可弹窗查看的纯文本明细。</summary>
        private static string BuildReportText(List<string> report)
        {
            if (report == null || report.Count == 0) return "没有可显示的执行明细。";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < report.Count; i++)
            {
                sb.Append("· ").Append(report[i]).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 电竞模式卡片：标题 + 状态 chip + 进入/退出按钮。自绘圆角卡，chip 状态由外部刷新。
        /// </summary>
        private sealed class EsportsCard : Card
        {
            /// <summary>卡片固定高度（供外部布局使用）。</summary>
            public const int HeightHint = 96;

            private readonly BadgeLabel _chip = new BadgeLabel();
            private readonly AccentButton _enter = new AccentButton();
            private readonly AccentButton _exit = new AccentButton();

            public event EventHandler EnterClicked;
            public event EventHandler ExitClicked;

            public EsportsCard()
            {
                Height = HeightHint;
                HeaderText = "电竞模式";
                HeaderIcon = "bolt";
                HeaderIconColor = Theme.Accent;

                _chip.Size = new Size(150, 22);
                Controls.Add(_chip);

                _enter.Text = "进入电竞模式";
                _enter.IconKind = "play";
                _enter.Variant = ButtonVariant.Primary;
                _enter.Click += delegate { if (EnterClicked != null) EnterClicked(this, EventArgs.Empty); };
                Controls.Add(_enter);

                _exit.Text = "退出电竞模式";
                _exit.IconKind = "undo";
                _exit.Variant = ButtonVariant.Secondary;
                _exit.Click += delegate { if (ExitClicked != null) ExitClicked(this, EventArgs.Empty); };
                Controls.Add(_exit);

                Resize += delegate { Reposition(); };
            }

            /// <summary>是否忙碌：执行期间禁用两个按钮，防止重复触发。</summary>
            public bool Busy
            {
                set
                {
                    _busy = value;
                    ApplyButtonState();
                }
            }

            /// <summary>刷新状态 chip：>0 项已启用视为启用，显示已启用项数；按钮文案跟随三态。</summary>
            public void SetState(int applied, int total)
            {
                _appliedState = applied;
                _totalState = total;
                if (applied >= total && total > 0)
                {
                    _chip.Filled = true;
                    _chip.BadgeColor = Theme.Success;
                    _chip.Text = "已全部启用 " + total + "/" + total;
                }
                else if (applied > 0)
                {
                    _chip.Filled = true;
                    _chip.BadgeColor = Theme.Success;
                    _chip.Text = "已启用 " + applied + " / " + total + " 项";
                }
                else
                {
                    _chip.Filled = false;
                    _chip.BadgeColor = Theme.TextMuted;
                    _chip.Text = "未启用";
                }
                ApplyButtonState();
            }

            private int _appliedState;
            private int _totalState;
            private bool _busy;

            private void ApplyButtonState()
            {
                bool full = _appliedState >= _totalState && _totalState > 0;
                _enter.Text = full
                    ? "已全部启用"
                    : (_appliedState > 0 ? "补全电竞模式" : "进入电竞模式");
                _enter.Enabled = !_busy && !full;
                _exit.Enabled = !_busy && _appliedState > 0;
            }

            private void Reposition()
            {
                int w = Width;
                // chip 落在页头区域右侧；按钮落在分隔线下的内容区
                _chip.Location = new Point(Math.Max(16, w - _chip.Width - 20), 13);
                int by = Card.HeaderSize + 9;
                _enter.SetBounds(w - 150 - 16, by, 150, 32);
                _exit.SetBounds(w - 150 - 16 - 156, by, 150, 32);
            }
        }
    }
}
