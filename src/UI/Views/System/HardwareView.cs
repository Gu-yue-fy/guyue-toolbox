/* ============================================================
 * 文件说明：系统信息页。把此前只存在于「导出报告」里的内容搬上界面：
 *           处理器（含大小核判定）、显卡、主板与固件、系统版本、磁盘明细。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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

        private readonly SectionTitle _secDisk = new SectionTitle();
        private readonly InfoList _disk = new InfoList();

        private readonly SysInfo.CpuLoadMeter _meter = new SysInfo.CpuLoadMeter();

        private bool _busy;
        private bool _loaded;

        public HardwareView()
            : base("系统信息", "硬件与系统详情：处理器 / 显卡 / 主板与固件 / 磁盘")
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

            _secDisk.TitleText = "磁盘";
            _secDisk.HintText = "容量与占用";
            _secDisk.Tone = Theme.Success;

            _disk.Caption = "磁盘明细";
            _disk.IconKind = "disk";
            _disk.CaptionColor = Theme.Success;
            _disk.EmptyText = "未检测到本地磁盘。";

            // 三张信息卡的高度按各自的最大行数在挂载前定稿：
            // 行布局要求"行高在挂载前定稿"，数据回填后再改高会与其后区块错位。
            AddFull(_band, MetricBand.CellMinHeight, Theme.GapSection);
            AddFull(_secChip, 44, 0);
            AddFull(_chip, InfoListHeight(6), Theme.GapSection);
            AddFull(_secBoard, 44, 0);
            AddFull(_board, InfoListHeight(7), Theme.GapSection);
            AddFull(_secDisk, 44, 0);
            AddFull(_disk, InfoListHeight(6), 0);

            AddAction("导出报告", "doc", ButtonVariant.Primary, OnExport, 124);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);

            _band.SetCells(new MetricBand.Cell[] {
                NewCell("CPU 负载", "--", "正在采样", "", Theme.Accent, true),
                NewCell("内存占用", "--", "物理内存", "", Theme.Purple, true),
                NewCell("系统盘", "--", "首个固定磁盘", "", Theme.Warning, true),
                NewCell("运行时间", "--", "自上次启动", "", Theme.Success, false)
            });
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
                try
                {
                    double load = _meter.Sample();
                    // deep=true：含主板 / BIOS / 显卡等需要 WMI 的字段，故放在后台线程
                    snap = SysInfo.Capture(true, load);
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
                    Render(snap);
                    SetSubtitle("硬件信息已更新。", Theme.Success);
                });
            });
        }

        private void Render(SystemSnapshot s)
        {
            int logical = s.CpuThreads > 0 ? s.CpuThreads : Environment.ProcessorCount;
            int physical = s.CpuCores > 0 ? s.CpuCores : logical;
            double load = s.CpuLoadPercent;

            _band.UpdateCell(0, load < 0 ? "--" : load.ToString("0") + "%", load, "当前占用", s.CpuName);
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

            _band.UpdateCell(3, UptimeText(s.Uptime), -1, "自上次启动",
                s.BootTime == DateTime.MinValue ? "" : "于 " + s.BootTime.ToString("MM-dd HH:mm"));

            // ---- 处理器与显卡 ----
            _chip.Clear();
            _chip.Add("处理器", OrDash(s.CpuName));
            _chip.Add("物理核心", physical.ToString() + " 核");
            _chip.Add("逻辑核心", logical.ToString() + " 线程");
            _chip.Add("处理器架构", CpuTopology.IsHybrid ? "大小核（异构，P 核 + E 核）" : "同构（所有核心一致）");
            _chip.Add("显卡", OrDash(s.GpuName));
            _chip.Add("系统体系结构", OrDash(s.OsArch));

            // ---- 系统与固件 ----
            _board.Clear();
            _board.Add("计算机名", OrDash(s.ComputerName));
            _board.Add("当前用户", OrDash(s.UserName));
            _board.Add("操作系统", OrDash(s.OsName) + (s.OsArch.Length > 0 ? "（" + s.OsArch + "）" : ""));
            _board.Add("系统版本", OrDash(s.OsVersion));
            _board.Add("主板", OrDash(s.BaseBoard));
            _board.Add("BIOS", OrDash(s.BiosVersion));
            _board.Add("运行权限", s.Elevated ? "管理员（全部功能可用）" : "标准用户（部分功能不可用）");

            // ---- 磁盘（最多 5 行，其余汇总——卡片高度已定稿，不能靠长高来容纳）----
            _disk.Clear();
            if (s.Disks != null)
            {
                int shown = s.Disks.Count < 5 ? s.Disks.Count : 5;
                for (int i = 0; i < shown; i++)
                {
                    DiskInfo d = s.Disks[i];
                    _disk.Add(d.Name, d.UsedPercent.ToString("0") + "% 已用 · " +
                        SysInfo.FormatSize(d.FreeBytes) + " 可用 / " + SysInfo.FormatSize(d.TotalBytes));
                }
                if (s.Disks.Count > shown)
                {
                    _disk.Add("其他", "另有 " + (s.Disks.Count - shown) + " 块磁盘，详见「导出报告」");
                }
            }
        }

        /// <summary>信息卡的定稿高度：标题 46 + 行高 25 × 行数 + 底部留白 12。</summary>
        private static int InfoListHeight(int rows)
        {
            return 46 + Math.Max(1, rows) * InfoList.RowHeight + 12;
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
                    try { using (Process.Start("explorer.exe", "/select,\"" + path + "\"")) { } }
                    catch { }
                });
            });
        }    }
}
