using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
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
        private readonly AccentButton _refreshButton;
        private bool _busy;

        public DiskHealthView()
            : base("磁盘健康", "物理磁盘 SMART 健康状态与介质类型")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "读取每块物理磁盘的 SMART 健康状态。状态为「警告」的磁盘建议尽快备份数据并更换。";

            _summary.Caption = "磁盘健康";
            _summary.IconKind = "disk";
            _summary.CaptionColor = Theme.Accent;

            _refreshButton = AddAction("重新检测", "refresh", ButtonVariant.Primary,
                delegate { Load(); }, 116);

            _grid.Columns.Add("c1", "磁盘");
            _grid.Columns.Add("c2", "介质类型");
            _grid.Columns.Add("c3", "健康状态");
            _grid.Columns.Add("c4", "容量");
            _grid.Columns[0].Width = 220;
            _grid.Columns[1].Width = 140;
            _grid.Columns[2].Width = 140;
            _grid.Columns[3].Width = 160;
            _grid.Dock = DockStyle.Top;
            _grid.Height = 240;

            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 16);
            row.Controls.Add(_summary);
            AddRow(row);

            Panel gridPanel = new Panel();
            gridPanel.BackColor = Theme.CardBg;
            gridPanel.Height = 260;
            gridPanel.Margin = new Padding(0, 0, 0, 14);
            gridPanel.Tag = "stretch";
            gridPanel.Controls.Add(_grid);
            gridPanel.Resize += delegate { _grid.SetBounds(6, 6, gridPanel.Width - 12, gridPanel.Height - 12); };
            Body.Controls.Add(gridPanel);

            Body.Resize += delegate { RefreshLayout(); };
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
                string error = "";
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                    {
                        foreach (ManagementObject disk in searcher.Get())
                        {
                            string name = Convert.ToString(disk["FriendlyName"]);
                            int media = 0, health = 0;
                            ulong size = 0;
                            try { media = Convert.ToInt32(disk["MediaType"]); } catch { }
                            try { health = Convert.ToInt32(disk["HealthStatus"]); } catch { }
                            try { size = Convert.ToUInt64(disk["Size"]); } catch { }

                            string mediaText = media == 3 ? "SSD" : (media == 4 ? "HDD" : "未知");
                            string healthText = health == 0 ? "健康" : (health == 1 ? "警告" : "未知");
                            rows.Add(new string[] { name, mediaText, healthText, FormatSize(size) });
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _refreshButton.Enabled = true;
                        _grid.Rows.Clear();
                        int warn = 0;
                        for (int i = 0; i < rows.Count; i++)
                        {
                            int idx = _grid.Rows.Add(rows[i]);
                            bool warning = rows[i][2] == "警告";
                            if (warning) warn++;
                            _grid.Rows[idx].Cells[2].Style.ForeColor =
                                rows[i][2] == "健康" ? Theme.Success : (warning ? Theme.Danger : Theme.TextMuted);
                        }
                        _grid.ClearSelection();
                        _grid.Invalidate();

                        _summary.Clear();
                        _summary.Add("磁盘数量", rows.Count.ToString(), rows.Count > 0 ? Theme.Success : Theme.Warning);
                        _summary.Add("健康", rows.Count - warn + " 块", Theme.Success);
                        if (warn > 0) _summary.Add("警告", warn + " 块", Theme.Danger);
                        _summary.Invalidate();

                        if (rows.Count == 0)
                        {
                            SetSubtitle("未能读取磁盘信息（" + error + "）", Theme.Danger);
                        }
                        else if (warn > 0)
                        {
                            SetSubtitle(warn + " 块磁盘健康警告，建议立即备份数据！", Theme.Danger);
                        }
                        else
                        {
                            SetSubtitle("全部磁盘健康状态良好。", Theme.Success);
                        }
                        RefreshLayout();
                    });
                }
                catch
                {
                }
            });
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
