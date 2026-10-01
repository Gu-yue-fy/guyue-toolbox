// 系统信息页：处理器/显卡、主板与固件、系统版本、磁盘明细

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class HardwareView : ViewBase
    {
        private readonly MetricBand _band = new MetricBand();

        private readonly SectionTitle _secChip = new SectionTitle();
        private readonly InfoList _chip = new InfoList();

        private readonly SectionTitle _secBoard = new SectionTitle();
        private readonly InfoList _board = new InfoList();

        private readonly SectionTitle _secMemNet = new SectionTitle();
        private readonly InfoList _memnet = new InfoList();

        private readonly SectionTitle _secDisk = new SectionTitle();
        private readonly InfoList _disk = new InfoList();

        private readonly SysInfo.CpuLoadMeter _meter = new SysInfo.CpuLoadMeter();
        private readonly System.Windows.Forms.Timer _autoTimer;
        private AccentButton _autoButton;

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _loaded;
        private bool _autoOn;

        /// <summary>最近一次渲染出来的信息行（[分组, 标签, 值]），供「复制」使用。</summary>
        private readonly List<string[]> _rows = new List<string[]>();

        // 指标带里"非本次刷新项"的补充文案（自动刷新时保留 CPU 名 / 启动时间）
        private string _cpuExtra = "";
        private string _bootExtra = "";

        public HardwareView()
            : base("系统信息", "硬件与系统详情：处理器 / 显卡 / 主板与固件 / 内存与网络 / 磁盘")
        {
            _secChip.TitleText = "处理器与显卡";
            _secChip.HintText = "含大小核（异构）判定";
            _secChip.Tone = Theme.Accent;

            _chip.Caption = "处理器与显卡";
            _chip.IconKind = "cpu";
            _chip.CaptionColor = Theme.Accent;
            _chip.EmptyText = "正在读取硬件信息…";

            _secBoard.TitleText = "系统与固件";
            _secBoard.HintText = "主板 / BIOS / 系统版本";
            _secBoard.Tone = Theme.Purple;

            _board.Caption = "系统与固件";
            _board.IconKind = "info";
            _board.CaptionColor = Theme.Purple;
            _board.EmptyText = "正在读取系统信息…";

            _secMemNet.TitleText = "内存与网络";
            _secMemNet.HintText = "内存条 / 活动网卡与地址";
            _secMemNet.Tone = Theme.Cyan;

            _memnet.Caption = "内存与网络";
            _memnet.IconKind = "memory";
            _memnet.CaptionColor = Theme.Cyan;
            _memnet.EmptyText = "正在读取内存与网卡信息…";

            _secDisk.TitleText = "磁盘";
            _secDisk.HintText = "容量与占用";
            _secDisk.Tone = Theme.Success;

            _disk.Caption = "磁盘明细";
            _disk.IconKind = "disk";
            _disk.CaptionColor = Theme.Success;
            _disk.EmptyText = "未检测到本地磁盘。";

            // 卡片高度按各自的最大行数在挂载前定稿：
            // 行布局要求"行高在挂载前定稿"，数据回填后再改高会与其后区块错位。
            AddFull(_band, MetricBand.CellMinHeight, Theme.GapSection);
            AddFull(_secChip, 44, 0);
            AddFull(_chip, InfoList.HeightFor(7), Theme.GapSection);
            AddFull(_secBoard, 44, 0);
            AddFull(_board, InfoList.HeightFor(7), Theme.GapSection);
            AddFull(_secMemNet, 44, 0);
            AddFull(_memnet, InfoList.HeightFor(6), Theme.GapSection);
            AddFull(_secDisk, 44, 0);
            AddFull(_disk, InfoList.HeightFor(6), 0);

            AddAction("导出报告", "doc", ButtonVariant.Secondary, OnExport, 124);
            AddAction("复制…", "copy", ButtonVariant.Secondary, OnCopyClick, 100);
            _autoButton = AddAction("自动刷新：关", "clock", ButtonVariant.Ghost, OnToggleAuto, 140);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);

            _band.SetCells(new MetricBand.Cell[] {
                NewCell("CPU 负载", "--", "正在采样", "", Theme.Accent, true),
                NewCell("内存占用", "--", "物理内存", "", Theme.Purple, true),
                NewCell("系统盘", "--", "首个固定磁盘", "", Theme.Warning, true),
                NewCell("运行时间", "--", "自上次启动", "", Theme.Success, false)
            });

            _autoTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _autoTimer.Tick += OnAutoTick;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _autoTimer != null)
            {
                _autoTimer.Stop();
                _autoTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private static MetricBand.Cell NewCell(string label, string value, string status, string extra, Color tone, bool withBar)
        {
            MetricBand.Cell c = new MetricBand.Cell();
            c.Label = label;
            c.Value = value;
            c.Status = status;
            c.Extra = extra;
            c.Tone = tone;
            c.Percent = withBar ? 0 : -1;
            return c;
        }

        public override void OnActivated()
        {
            if (!_loaded) Load();
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取硬件信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemSnapshot snap = null;
                HardwareExtra extra = null;
                try
                {
                    double load = _meter.Sample();
                    // deep=true：含主板 / BIOS / 显卡等需要 WMI 的字段，故放在后台线程
                    snap = SysInfo.Capture(true, load);
                    extra = SysInfo.GetHardwareExtra();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    _loaded = true;
                    if (snap == null)
                    {
                        SetSubtitle("读取硬件信息失败。", Theme.Danger);
                        return;
                    }
                    Render(snap, extra);
                    SetSubtitle("硬件信息已更新。", Theme.Success);
                });
            });
        }

        private void Render(SystemSnapshot s, HardwareExtra extra)
        {
            int logical = s.CpuThreads > 0 ? s.CpuThreads : Environment.ProcessorCount;
            int physical = s.CpuCores > 0 ? s.CpuCores : logical;
            double load = s.CpuLoadPercent;

            _rows.Clear();

            _cpuExtra = s.CpuName;
            _bootExtra = s.BootTime == DateTime.MinValue ? "" : "于 " + s.BootTime.ToString("MM-dd HH:mm");
            _band.UpdateCell(0, load < 0 ? "--" : load.ToString("0") + "%", load, "当前占用", _cpuExtra);
            _band.SetTone(0, Gfx.LoadColor(load));

            double memPct = s.Memory.UsedPercent;
            _band.UpdateCell(1, memPct.ToString("0") + "%", memPct, "物理内存",
                SysInfo.FormatSize(s.Memory.UsedBytes) + " / " + SysInfo.FormatSize(s.Memory.TotalBytes));
            _band.SetTone(1, Gfx.LoadColor(memPct));

            double diskPct = -1;
            string diskText = "--";
            string diskExtra = "";
            if (s.Disks != null && s.Disks.Count > 0)
            {
                DiskInfo d = s.Disks[0];
                diskPct = d.UsedPercent;
                diskText = diskPct.ToString("0") + "%";
                diskExtra = d.Name + " · " + SysInfo.FormatSize(d.FreeBytes) + " 可用";
            }
            _band.UpdateCell(2, diskText, diskPct, "首个固定磁盘", diskExtra);
            _band.SetTone(2, Gfx.LoadColor(diskPct));

            _band.UpdateCell(3, UptimeText(s.Uptime), -1, "自上次启动", _bootExtra);

            // ---- 处理器与显卡 ----
            _chip.Clear();
            AddRow(_chip, "处理器与显卡", "处理器", OrDash(s.CpuName));
            AddRow(_chip, "处理器与显卡", "物理核心", physical.ToString() + " 核");
            AddRow(_chip, "处理器与显卡", "逻辑核心", logical.ToString() + " 线程");
            AddRow(_chip, "处理器与显卡", "处理器架构", CpuTopology.IsHybrid ? "大小核（异构，P 核 + E 核）" : "同构（所有核心一致）");
            AddRow(_chip, "处理器与显卡", "显卡", OrDash(s.GpuName));
            AddRow(_chip, "处理器与显卡", "显存", extra != null && extra.GpuVram.Length > 0 ? extra.GpuVram : "未读取到");
            AddRow(_chip, "处理器与显卡", "系统体系结构", OrDash(s.OsArch));

            // ---- 系统与固件 ----
            _board.Clear();
            AddRow(_board, "系统与固件", "计算机名", OrDash(s.ComputerName));
            AddRow(_board, "系统与固件", "当前用户", OrDash(s.UserName));
            AddRow(_board, "系统与固件", "操作系统", OrDash(s.OsName) + (s.OsArch.Length > 0 ? "（" + s.OsArch + "）" : ""));
            AddRow(_board, "系统与固件", "系统版本", OrDash(s.OsVersion));
            AddRow(_board, "系统与固件", "主板", OrDash(s.BaseBoard));
            AddRow(_board, "系统与固件", "BIOS", OrDash(s.BiosVersion));
            AddRow(_board, "系统与固件", "运行权限", s.Elevated ? "管理员（全部功能可用）" : "标准用户（部分功能不可用）");

            // ---- 内存与网络 ----
            _memnet.Clear();
            if (extra != null && extra.MemoryModuleCount > 0)
            {
                AddRow(_memnet, "内存与网络", "内存条",
                    extra.MemoryModuleCount + " 条 · 共 " + SysInfo.FormatSize(extra.MemoryModuleTotal));
            }
            else
            {
                AddRow(_memnet, "内存与网络", "内存条", "未读取到（WMI 查询失败）");
            }
            AddRow(_memnet, "内存与网络", "内存频率", extra != null && extra.MemorySpeed.Length > 0
                ? extra.MemorySpeed + " MHz" : "未读取到");
            AddRow(_memnet, "内存与网络", "活动网卡", extra != null && extra.ActiveAdapter.Length > 0
                ? extra.ActiveAdapter : "没有处于已连接状态的网卡");
            AddRow(_memnet, "内存与网络", "本机 IPv4", extra != null && extra.IPv4.Length > 0 ? extra.IPv4 : "—");
            AddRow(_memnet, "内存与网络", "默认网关", extra != null && extra.Gateway.Length > 0 ? extra.Gateway : "—");
            AddRow(_memnet, "内存与网络", "DNS", extra != null && extra.Dns.Length > 0 ? extra.Dns : "—");

            // ---- 磁盘（最多 5 行，其余汇总——卡片高度已定稿，不能靠长高来容纳）----
            _disk.Clear();
            if (s.Disks != null)
            {
                int shown = s.Disks.Count < 5 ? s.Disks.Count : 5;
                for (int i = 0; i < shown; i++)
                {
                    DiskInfo d = s.Disks[i];
                    AddRow(_disk, "磁盘", d.Name, d.UsedPercent.ToString("0") + "% 已用 · " +
                        SysInfo.FormatSize(d.FreeBytes) + " 可用 / " + SysInfo.FormatSize(d.TotalBytes));
                }
                if (s.Disks.Count > shown)
                {
                    AddRow(_disk, "磁盘", "其他", "另有 " + (s.Disks.Count - shown) + " 块磁盘，详见「导出报告」");
                }
            }
        }

        /// <summary>同时写入卡片与复制用行表（保证「复制」与界面完全一致）。</summary>
        private void AddRow(InfoList list, string group, string label, string value)
        {
            list.Add(label, value);
            _rows.Add(new string[] { group, label, value == null ? "" : value });
        }

        /// <summary>空值统一显示为破折号（不可命名为 Text——那会隐藏 Control.Text）。</summary>
        private static string OrDash(string v)
        {
            return string.IsNullOrEmpty(v) ? "—" : v;
        }

        private static string UptimeText(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return "--";
            if (t.TotalDays >= 1) return (int)t.TotalDays + " 天 " + t.Hours + " 小时";
            if (t.TotalHours >= 1) return (int)t.TotalHours + " 小时 " + t.Minutes + " 分";
            return Math.Max(1, (int)t.TotalMinutes) + " 分钟";
        }

        // ---------------- 自动刷新 ----------------

        private void OnToggleAuto(object sender, EventArgs e)
        {
            _autoOn = !_autoOn;
            _autoButton.Text = _autoOn ? "自动刷新：开" : "自动刷新：关";
            _autoButton.Variant = _autoOn ? ButtonVariant.Secondary : ButtonVariant.Ghost;
            _autoButton.Invalidate();
            if (_autoOn) _autoTimer.Start();
            else _autoTimer.Stop();
            SetSubtitle(_autoOn
                ? "已开启自动刷新：每 5 秒更新 CPU / 内存 / 磁盘指标（其余信息仍靠「刷新」）。"
                : "已关闭自动刷新。", Theme.TextSecondary);
        }

        /// <summary>轻量刷新：只更新指标带，不重扫 WMI 详情（避免每 5 秒一次的昂贵查询）。</summary>
        private void OnAutoTick(object sender, EventArgs e)
        {
            if (_busy || !Visible) return;
            _busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double load = -1;
                MemoryInfo mem = null;
                List<DiskInfo> disks = null;
                try
                {
                    load = _meter.Sample();
                    mem = SysInfo.GetMemory();
                    disks = SysInfo.GetDisks(true);
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    if (mem == null) return;

                    _band.UpdateCell(0, load < 0 ? "--" : load.ToString("0") + "%", load, "当前占用", _cpuExtra);
                    _band.SetTone(0, Gfx.LoadColor(load));

                    double memPct = mem.UsedPercent;
                    _band.UpdateCell(1, memPct.ToString("0") + "%", memPct, "物理内存",
                        SysInfo.FormatSize(mem.UsedBytes) + " / " + SysInfo.FormatSize(mem.TotalBytes));
                    _band.SetTone(1, Gfx.LoadColor(memPct));

                    if (disks != null && disks.Count > 0)
                    {
                        DiskInfo d = disks[0];
                        _band.UpdateCell(2, d.UsedPercent.ToString("0") + "%", d.UsedPercent, "首个固定磁盘",
                            d.Name + " · " + SysInfo.FormatSize(d.FreeBytes) + " 可用");
                        _band.SetTone(2, Gfx.LoadColor(d.UsedPercent));
                    }

                    TimeSpan up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
                    _band.UpdateCell(3, UptimeText(up), -1, "自上次启动", _bootExtra);
                    SetSubtitle("自动刷新 " + DateTime.Now.ToString("HH:mm:ss") + "（每 5 秒）", Theme.TextSecondary);
                });
            });
        }

        // ---------------- 复制 ----------------

        private void OnCopyClick(object sender, EventArgs e)
        {
            if (_rows.Count == 0)
            {
                Dialog.Info(this, "没有数据", "先点「刷新」读取硬件信息，再复制。");
                return;
            }

            string what = Chooser.ChooseOne(this, "复制什么？",
                new List<string> { "全部信息（文本）", "选择单项…" });
            if (what == null) return;

            if (what == "全部信息（文本）")
            {
                CopyText(BuildAllText());
                return;
            }

            List<string> options = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
            {
                options.Add(_rows[i][1] + "：" + _rows[i][2]);
            }
            string picked = Chooser.ChooseOne(this, "复制哪一项？", options);
            if (picked == null) return;

            int colon = picked.IndexOf('：');
            CopyText(colon >= 0 ? picked.Substring(colon + 1) : picked);
        }

        private void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                SetSubtitle("已复制到剪贴板。", Theme.Success);
                Toast("已复制", "内容已放入剪贴板。", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "复制失败", ex.Message);
            }
        }

        /// <summary>按分组拼出可读文本（与界面一致，附生成时间）。</summary>
        private string BuildAllText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("古月工具箱 · 系统信息");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            string group = "";
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!string.Equals(group, _rows[i][0], StringComparison.Ordinal))
                {
                    group = _rows[i][0];
                    sb.AppendLine("【" + group + "】");
                }
                sb.AppendLine(_rows[i][1] + "：" + _rows[i][2]);
            }
            return sb.ToString();
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在生成系统报告…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string path;
                string content;
                bool ok = SystemReport.Save(out path, out content);
                Post(delegate
                {
                    _busy = false;
                    if (!ok)
                    {
                        SetSubtitle("报告生成失败。", Theme.Danger);
                        return;
                    }
                    SetSubtitle("报告已生成：" + path, Theme.Success);
                    Shell.OpenSelect(path);
                });
            });
        }
    }
}
