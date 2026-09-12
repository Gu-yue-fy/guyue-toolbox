using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 系统维护：Defender 扫描、系统完整性修复（SFC / DISM / 时间同步）、
    /// Windows 管理控制台快捷入口——修复类工具参考通用系统工具的常见能力集。
    /// </summary>
    public sealed class MaintenanceView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly InfoList _defender = new InfoList();
        private readonly InfoList _repair = new InfoList();

        private bool _busy;
        public override bool IsBusy { get { return _busy; } }

        public MaintenanceView()
            : base("系统维护", "Defender 扫描 / 系统文件修复 / 时间同步 / 管理控制台快捷入口")
        {
            _notice.NoticeIcon = "shield";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "SFC 与 DISM 会校验并修复系统文件，耗时可能达数分钟到数十分钟，会在独立的 PowerShell 窗口中执行（可实时查看进度，完成后窗口自动保留结果）。";

            _defender.Caption = "Defender 扫描";
            _defender.IconKind = "shield";
            _defender.CaptionColor = Theme.Success;

            _repair.Caption = "系统修复";
            _repair.IconKind = "refresh";
            _repair.CaptionColor = Theme.Accent;

            AddAction("快速扫描", "shield", ButtonVariant.Primary, delegate { DefenderScan("QuickScan", "快速扫描"); }, 118);
            AddAction("完全扫描", "shield", ButtonVariant.Secondary, delegate { DefenderScan("FullScan", "完全扫描"); }, 118);

            BuildLayout();
        }

        // --------------------------------------------------------------

        private void BuildLayout()
        {
            AddFull(_notice, 56, 14);

            // ① Defender
            FlowLayoutPanel defRow = MakeRow(0, 14);
            defRow.Controls.Add(_defender);
            AddRow(defRow);

            // ② 系统修复
            FlowLayoutPanel repRow = MakeRow(0, 8);
            repRow.Controls.Add(_repair);
            AddRow(repRow);

            FlowLayoutPanel repBtns = MakeRow(0, 14);
            repBtns.Controls.Add(MakeLabel("", 84, false));
            AccentButton sfc = MakeInlineButton("系统文件校验", OnSfc);
            repBtns.Controls.Add(sfc);
            AccentButton dism = MakeInlineButton("DISM 镜像修复", OnDism);
            repBtns.Controls.Add(dism);
            AccentButton ntp = MakeInlineButton("时间同步", OnTimeSync);
            repBtns.Controls.Add(ntp);
            AddRow(repBtns);

            // ③ 快捷工具
            FlowLayoutPanel tools1 = MakeRow(0, 8);
            tools1.Controls.Add(MakeLabel("快捷工具", 84, true));
            tools1.Controls.Add(ToolButton("计算机管理", "compmgmt.msc"));
            tools1.Controls.Add(ToolButton("磁盘管理", "diskmgmt.msc"));
            tools1.Controls.Add(ToolButton("设备管理器", "devmgmt.msc"));
            tools1.Controls.Add(ToolButton("服务", "services.msc"));
            AddRow(tools1);

            FlowLayoutPanel tools2 = MakeRow(0, 14);
            tools2.Controls.Add(MakeLabel("", 84, false));
            tools2.Controls.Add(ToolButton("系统信息", "msinfo32"));
            tools2.Controls.Add(ToolButton("注册表编辑器", "regedit"));
            tools2.Controls.Add(ToolButton("任务管理器", "taskmgr"));
            tools2.Controls.Add(ToolButton("系统配置", "msconfig"));
            AddRow(tools2);

            Body.Resize += delegate { RefreshLayout(); };
            RefreshLayout();
        }

        private Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = bold ? Theme.TextPrimary : Theme.TextMuted;
            l.Font = bold ? Theme.FontBodyBold : Theme.FontBody;
            l.TextAlign = ContentAlignment.MiddleLeft;
            if (width > 0) l.Size = new Size(width, 30);
            else l.AutoSize = true;
            l.Margin = new Padding(0, 0, 10, 0);
            return l;
        }

        private AccentButton MakeInlineButton(string text, EventHandler onClick)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "refresh";
            b.Variant = ButtonVariant.Secondary;
            b.Size = new Size(132, 32);
            b.Margin = new Padding(0, 0, 10, 0);
            b.Click += onClick;
            return b;
        }

        private void OnSfc(object sender, EventArgs e)
        {
            RunRepairWindow("系统文件校验 (SFC)", "sfc /scannow");
        }

        private void OnDism(object sender, EventArgs e)
        {
            RunRepairWindow("DISM 组件修复",
                "DISM /Online /Cleanup-Image /RestoreHealth");
        }

        private void OnTimeSync(object sender, EventArgs e)
        {
            RunRepairWindow("时间同步",
                "w32tm /resync; Write-Host '完成。若报错请先检查 Windows Time 服务是否运行。'");
        }

        private AccentButton ToolButton(string text, string target)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "feature";
            b.Variant = ButtonVariant.Secondary;
            b.Size = new Size(122, 32);
            b.Margin = new Padding(0, 0, 10, 0);
            b.Click += delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = target;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    SetSubtitle("已打开：" + text, Theme.Success);
                }
                catch (Exception ex)
                {
                    Dialog.Error(this, "无法打开", ex.Message);
                }
            };
            return b;
        }

        // ---------------- Defender ----------------

        private void DefenderScan(string scanType, string title)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在启动 Defender " + title + "…", Theme.Warning);

            // 发射式：独立隐藏进程执行，不受本工具生命周期影响（完全扫描可达数小时）
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = "";
                bool ok;
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-NoProfile -WindowStyle Hidden -Command \"Start-MpScan -ScanType " + scanType + "\"";
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    ok = true;
                }
                catch (Exception ex) { ok = false; err = ex.Message; }

                Post(delegate
                {
                    _busy = false;
                    _defender.Clear();
                    if (ok)
                    {
                        SetSubtitle(title + " 已在后台启动（独立进程）。完成后可在 Windows 安全中心查看结果。",
                            Theme.Success);
                        _defender.Add(title, "已提交后台执行 ✓");
                    }
                    else
                    {
                        SetSubtitle(title + " 未能启动：" + (err.Length > 0 ? err : "Defender 服务不可用"), Theme.Danger);
                        _defender.Add(title, "启动失败 ✗");
                    }
                    _defender.Invalidate();
                });
            });
        }

        // ---------------- 系统修复（可见 PowerShell 窗口执行，用户可实时看进度） ----------------

        private void RunRepairWindow(string title, string command)
        {
            if (_busy) return;
            if (!Dialog.Confirm(this, title,
                "将在独立的 PowerShell 窗口中执行：\r\n\r\n  " + command +
                "\r\n\r\n耗时可能数分钟到数十分钟，请勿中途关闭窗口。是否继续？")) return;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -NoExit -Command \"" +
                    command.Replace("\"", "`\"") + "\"";
                psi.UseShellExecute = true;
                Process.Start(psi);
                SetSubtitle(title + " 已在独立窗口启动，完成后该窗口会保留执行结果。", Theme.Success);
                _repair.Add(title, "已启动（独立窗口执行）");
                _repair.Invalidate();
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "启动失败", ex.Message);
            }
        }

        private void Post(ThreadStart action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch
            {
            }
        }
    }
}
