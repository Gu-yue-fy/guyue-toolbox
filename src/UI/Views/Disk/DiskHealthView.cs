// 文件说明：磁盘健康页（SMART）

using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>磁盘健康：SMART 状态、介质类型与温度（WMI MSFT_PhysicalDisk）。</summary>
    public sealed class DiskHealthView : ViewBase
    {
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly DarkGrid _grid = new DarkGrid();
        // 表格外框独立成字段：显示空态卡时需要整块隐藏表格，而不是把表清空留一片空白
        private readonly Panel _gridPanel = new Panel();
        private readonly EmptyState _empty = new EmptyState();
        private readonly AccentButton _refreshButton;
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        /// <summary>最近一次读取的表格行（与网格行一一对应，供详情 / 复制 / 导出使用）。</summary>
        private readonly List<string[]> _lastRows = new List<string[]>();

        public DiskHealthView()
            : base("磁盘健康", "物理磁盘 SMART 健康状态与介质类型")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "读取每块物理磁盘的 SMART 健康状态。状态为「警告」或「故障」的磁盘建议尽快备份数据并更换。";

            _summary.Caption = "磁盘健康";
            _summary.IconKind = "disk";
            _summary.CaptionColor = Theme.Accent;

            _refreshButton = AddAction("重新检测", "search", ButtonVariant.Primary,
                delegate { Load(); }, 116);
            AddAction("查看详情", "info", ButtonVariant.Secondary, OnDetailClick, 116);
            AddAction("复制列表", "copy", ButtonVariant.Secondary, OnCopyClick, 116);
            AddAction("导出报告", "doc", ButtonVariant.Secondary, OnExportClick, 124);

            _grid.Columns.Add("c1", "磁盘");
            _grid.Columns.Add("c2", "介质类型");
            _grid.Columns.Add("c3", "健康状态");
            _grid.Columns.Add("c4", "容量");
            _grid.Columns[0].Width = 220;
            _grid.Columns[1].Width = 140;
            _grid.Columns[2].Width = 140;
            _grid.Columns[3].Width = 160;

            BuildLayout();
        }



        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 16);
            row.Controls.Add(_summary);
            AddRow(row);

            _gridPanel.BackColor = Theme.CardBg;
            _gridPanel.Height = 260;
            _gridPanel.Margin = new Padding(0, 0, 0, 14);
            _gridPanel.Tag = "stretch";
            _gridPanel.Controls.Add(_grid);
            // 只保留「Resize 里手工定位」这一种方式：此前 Dock=Top 与本行 SetBounds 互相覆盖，
            // 表格高度/内边距会由事件顺序决定，不稳定
            _gridPanel.Resize += delegate { LayoutGridInner(); };
            Body.Controls.Add(_gridPanel);
            LayoutGridInner();

            // 空态卡与表格互斥显示（默认隐藏，加载完成后决定显示谁）
            _empty.Visible = false;
            _empty.Height = 150;
            _empty.Margin = new Padding(0, 0, 0, 14);
            _empty.Tag = "stretch";
            Body.Controls.Add(_empty);

            Body.Resize += delegate { RefreshLayout(); };
        }

        /// <summary>表格在外框内缩 6px 贴满（本页唯一的表格定位入口）。</summary>
        private void LayoutGridInner()
        {
            int w = _gridPanel.Width - 12;
            int h = _gridPanel.Height - 12;
            if (w < 0) w = 0;
            if (h < 0) h = 0;
            _grid.SetBounds(6, 6, w, h);
        }

        /// <summary>显示空态/失败卡并隐藏表格：读取失败与"没有磁盘"要给出不同说法 + 重试出口。</summary>
        private void ShowEmpty(string title, string sub)
        {
            _empty.SetState(title, sub, "重新检测", "refresh", delegate { Load(); });
            _empty.Visible = true;
            _gridPanel.Visible = false;
            RefreshLayout();
        }

        /// <summary>恢复表格显示（成功取到数据后调用）。</summary>
        private void HideEmpty()
        {
            _empty.Visible = false;
            _gridPanel.Visible = true;
        }

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            _refreshButton.Enabled = false;
            SetSubtitle("正在读取磁盘健康信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string[]> rows = new List<string[]>();
                List<int> healths = new List<int>();   // 与 rows 一一对应：0=健康 1=警告 2=故障
                string error = "";
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                    using (ManagementObjectCollection found = searcher.Get())
                    {
                        foreach (ManagementObject disk in found)
                        {
                            using (disk)
                            {
                                string name = Convert.ToString(disk["FriendlyName"]);
                                int media = 0, health = 0;
                                ulong size = 0;
                                try { media = Convert.ToInt32(disk["MediaType"]); } catch { }
                                try { health = Convert.ToInt32(disk["HealthStatus"]); } catch { }
                                try { size = Convert.ToUInt64(disk["Size"]); } catch { }

                                // MSFT_PhysicalDisk.MediaType：0=未指定 3=HDD 4=SSD 5=SCM
                                // （此前 3/4 写反，SSD 会被显示成 HDD）
                                string mediaText = media == 3 ? "HDD" : (media == 4 ? "SSD" : "未知");
                                // HealthStatus：0=Healthy 1=Warning 2=Unhealthy
                                // 故障必须单列出来并计入告警，不能混进「未知」（否则真故障盘不报红）
                                string healthText = health == 0 ? "健康"
                                    : (health == 1 ? "警告" : (health == 2 ? "故障" : "未知"));
                                rows.Add(new string[] { name, mediaText, healthText, FormatSize(size) });
                                healths.Add(health);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                // 统一走基类 Post：句柄未创建/已销毁时静默丢弃，不会像裸 BeginInvoke 那样
                // 异常被空 catch 吞掉、_busy 与按钮状态永久卡死
                Post(delegate
                {
                    _busy = false;
                    _refreshButton.Enabled = true;
                    _grid.Rows.Clear();
                    _lastRows.Clear();
                    int warn = 0;
                    int bad = 0;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        int idx = _grid.Rows.Add(rows[i]);
                        _grid.Rows[idx].Tag = rows[i];   // 详情 / 复制 / 导出都从这里取原始行
                        _lastRows.Add(rows[i]);
                        int h = i < healths.Count ? healths[i] : 0;
                        if (h == 1) warn++;
                        else if (h >= 2) bad++;
                        _grid.Rows[idx].Cells[2].Style.ForeColor =
                            h == 0 ? Theme.Success : (h >= 2 ? Theme.Danger : (h == 1 ? Theme.Warning : Theme.TextMuted));
                    }
                    _grid.ClearSelection();
                    _grid.Invalidate();

                    _summary.Clear();
                    _summary.Add("磁盘数量", rows.Count.ToString(), rows.Count > 0 ? Theme.Success : Theme.Warning);
                    _summary.Add("健康", (rows.Count - warn - bad) + " 块", Theme.Success);
                    if (warn > 0) _summary.Add("警告", warn + " 块", Theme.Warning);
                    if (bad > 0) _summary.Add("故障", bad + " 块", Theme.Danger);
                    _summary.Invalidate();

                    // 读取失败（error 非空）与"确实没有磁盘"分开表达：前者给原因 + 重试，后者给解释
                    if (rows.Count == 0)
                    {
                        if (error.Length > 0)
                        {
                            ShowEmpty("读取磁盘信息失败",
                                error + "\r\n若提示拒绝访问，请以管理员身份运行本程序后重试。");
                            SetSubtitle("读取磁盘信息失败：" + error, Theme.Danger);
                        }
                        else
                        {
                            ShowEmpty("未检测到物理磁盘",
                                "未枚举到任何物理磁盘；若机型较新，可能不被该 WMI 存储接口支持。");
                            SetSubtitle("未检测到物理磁盘。", Theme.Warning);
                        }
                    }
                    else
                    {
                        HideEmpty();
                        if (bad > 0) SetSubtitle(bad + " 块磁盘报告故障，建议立即备份数据并更换！", Theme.Danger);
                        else if (warn > 0) SetSubtitle(warn + " 块磁盘健康警告，建议尽快备份数据。", Theme.Warning);
                        else SetSubtitle("全部磁盘健康状态良好。", Theme.Success);
                    }
                    RefreshLayout();
                });
            });
        }

        // ---------------- 详情 / 复制 / 导出 ----------------

        private void OnDetailClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择磁盘", "请先在列表里点选一块磁盘（整行高亮），再点「查看详情」。");
                return;
            }
            string[] row = _grid.SelectedRows[0].Tag as string[];
            if (row == null || row.Length == 0) return;
            string name = row[0];

            _busy = true;
            _refreshButton.Enabled = false;
            SetSubtitle("正在读取「" + name + "」的详情…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                DiskDetail d = Disks.ReadDetail(name);
                Post(delegate
                {
                    _busy = false;
                    _refreshButton.Enabled = true;
                    SetSubtitle("详情已读取：" + name, Theme.Success);
                    Dialog.Output(this, "磁盘详情", BuildDetailText(d));
                });
            });
        }

        /// <summary>详情文本：基本属性 / 可靠性计数器 / 分区；读不到的字段统一显示破折号。</summary>
        private static string BuildDetailText(DiskDetail d)
        {
            StringBuilder sb = new StringBuilder();
            if (d.Error.Length > 0)
            {
                sb.AppendLine("读取不完整：" + d.Error);
                sb.AppendLine();
            }

            sb.AppendLine("── 基本属性 ──");
            sb.AppendLine("磁盘：" + Dash(d.FriendlyName));
            sb.AppendLine("序列号：" + Dash(d.SerialNumber));
            sb.AppendLine("固件：" + Dash(d.FirmwareVersion));
            sb.AppendLine("总线类型：" + Dash(d.BusType));
            sb.AppendLine("介质类型：" + Dash(d.MediaType));
            sb.AppendLine("容量：" + Dash(d.SizeText));
            sb.AppendLine("健康状态：" + Dash(d.HealthStatus));
            if (d.SpindleSpeed.Length > 0 && d.SpindleSpeed != "—") sb.AppendLine("转速：" + d.SpindleSpeed);
            sb.AppendLine();

            sb.AppendLine("── 可靠性计数器 ──");
            if (!d.HasReliability)
            {
                sb.AppendLine("该磁盘/驱动未提供可靠性计数器（常见于部分 NVMe 与 USB 硬盘盒）。");
            }
            else
            {
                if (d.Temperature.Length > 0) sb.AppendLine("温度：" + d.Temperature + " ℃");
                if (d.PowerOnHours.Length > 0) sb.AppendLine("通电时间：" + d.PowerOnHours + " 小时");
                if (d.StartStopCycles.Length > 0) sb.AppendLine("启动/停止次数：" + d.StartStopCycles);
                if (d.ReadErrors.Length > 0) sb.AppendLine("读错误总数：" + d.ReadErrors);
                if (d.WriteErrors.Length > 0) sb.AppendLine("写错误总数：" + d.WriteErrors);
                if (d.WearPercent.Length > 0)
                    sb.AppendLine("磨损值：" + d.WearPercent + "（厂商语义不同：多数盘反映已消耗寿命百分比）");
            }
            sb.AppendLine();

            sb.AppendLine("── 分区 ──");
            sb.AppendLine("分区表：" + Dash(d.PartitionStyle));
            sb.AppendLine("分区数：" + Dash(d.PartitionCount));
            sb.AppendLine("系统盘：" + Dash(d.IsSystem));
            sb.AppendLine("启动盘：" + Dash(d.IsBoot));
            return sb.ToString();
        }

        private static string Dash(string v)
        {
            return string.IsNullOrEmpty(v) ? "—" : v;
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            if (_lastRows.Count == 0)
            {
                Dialog.Info(this, "没有数据", "先点「重新检测」读取磁盘信息，再复制。");
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("磁盘\t介质类型\t健康状态\t容量");
            for (int i = 0; i < _lastRows.Count; i++)
            {
                sb.AppendLine(string.Join("\t", _lastRows[i]));
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                SetSubtitle("已复制 " + _lastRows.Count + " 行磁盘信息到剪贴板。", Theme.Success);
                Toast("已复制", "TSV 格式，可直接粘贴进表格软件。", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "复制失败", ex.Message);
            }
        }

        private void OnExportClick(object sender, EventArgs e)
        {
            if (_lastRows.Count == 0)
            {
                Dialog.Info(this, "没有数据", "先点「重新检测」读取磁盘信息，再导出报告。");
                return;
            }
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string path = Path.Combine(dir, "GuyueBox-磁盘健康-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("古月工具箱 · 磁盘健康报告");
                sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("磁盘\t介质类型\t健康状态\t容量");
                for (int i = 0; i < _lastRows.Count; i++)
                {
                    sb.AppendLine(string.Join("\t", _lastRows[i]));
                }
                sb.AppendLine();
                sb.AppendLine("说明：健康状态来自 WMI MSFT_PhysicalDisk.HealthStatus（0 健康 / 1 警告 / 2 故障）；");
                sb.AppendLine("      单盘温度、通电时间与磨损值请在页面上选中磁盘后点「查看详情」。");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // 带 BOM：记事本打开不乱码
                SetSubtitle("报告已导出：" + path, Theme.Success);
                Shell.OpenSelect(path);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "导出失败", ex.Message);
            }
        }

        private static string FormatSize(ulong bytes)
        {
            if (bytes < 1L << 30) return (bytes >> 20) + " MB";
            if (bytes < 1L << 40) return (bytes >> 30) + " GB";
            return ((double)bytes / (1L << 40)).ToString("0.0") + " TB";
        }

        public override void OnActivated()
        {
            if (_grid.Rows.Count == 0) Load();
        }
    }
}
