// 设备管理页：枚举即插即用设备，支持禁用/启用选中设备

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 设备管理：枚举即插即用设备（状态/类/名称），支持禁用与启用选中设备。
    /// 通过 PowerShell Get-PnpDevice / Disable-PnpDevice / Enable-PnpDevice 实现，
    /// 需要管理员权限。禁用错误状态的设备（如冲突/异常的 USB 设备）是官方推荐的排障手段。
    /// </summary>
    public sealed class DeviceView : GridPageView
    {
        private readonly List<DeviceInfo> _all = new List<DeviceInfo>();
        private AccentButton _disableButton;
        private AccentButton _enableButton;
        private AccentButton _refreshButton;

        public DeviceView()
            : base("设备管理", "查看与控制即插即用设备（禁用 / 启用）")
        {
            Notice.NoticeIcon = "info";
            Notice.NoticeAccent = Theme.Warning;
            Notice.NoticeText = "说明：设备出现感叹号/异常时可尝试「禁用后重新启用」（相当于设备管理器里的禁用/启用），常用于修复 USB 外设、声卡、网卡的偶发失灵。禁用网卡/显卡等关键设备会立即失去相应功能，请确认后再操作。列表已把异常与已禁用设备排在最前。";

            _disableButton = AddAction("禁用选中", "ban", ButtonVariant.Danger, OnDisableClick, 120);
            _enableButton = AddAction("启用选中", "check", ButtonVariant.Primary, OnEnableClick, 120);
            _refreshButton = AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);

            InitializeGridPage();
        }

        // ---- 形态参数：本页无统计条（通知条 34+12，表格行占位 360）----
        protected override bool HasSummary { get { return false; } }
        protected override int NoticeHeight { get { return 34; } }
        protected override int NoticeGap { get { return 12; } }
        protected override int GridRowHeight { get { return 360; } }
        // 本页无统计条：按 GridPageView 契约，ExtraUsedHeight = 页头以下全部固定占位（含通知条本身）
        protected override int ExtraUsedHeight { get { return NoticeHeight + NoticeGap; } }

        /// <summary>本页不支持点列头排序（状态列随时间变化，排序意义不大）。</summary>
        protected override void ConfigureGrid()
        {
            Grid.ReadOnly = true;
            Grid.UseOwnScrollbar = true;
        }

        protected override void BuildColumns()
        {
            Grid.AddTextColumn("状态", 96, false);
            Grid.AddTextColumn("设备类", 130, false);
            Grid.AddFillColumn("设备名称", 220);
            Grid.AddFillColumn("实例 ID", 220);
        }

        // ---------------- 数据加载 ----------------

        private sealed class DeviceInfo
        {
            public string Status = "";
            public string Class = "";
            public string Name = "";
            public string InstanceId = "";

            public int SortRank
            {
                get
                {
                    if (string.Equals(Status, "Error", StringComparison.OrdinalIgnoreCase)) return 0;
                    if (string.Equals(Status, "Disabled", StringComparison.OrdinalIgnoreCase)) return 1;
                    if (string.Equals(Status, "Unknown", StringComparison.OrdinalIgnoreCase)) return 2;
                    return 3;
                }
            }
        }

        protected override void Load()
        {
            if (Busy) return;
            Busy = true;
            Loaded = true;
            UpdateActions();
            SetSubtitle("正在枚举设备…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                List<DeviceInfo> result = EnumerateDevices(out error);
                Post(delegate
                {
                    Busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    Render();

                    // 枚举失败与"零设备"分开表达：失败给原因 + 重试提示，零设备说明可能被系统/权限限制
                    if (error.Length > 0)
                    {
                        SetSubtitle("枚举设备失败：" + error + "（可点「刷新」重试）", Theme.Danger);
                        Toast("枚举设备失败", error, ToastKind.Danger);
                        return;
                    }
                    if (result.Count == 0)
                    {
                        SetSubtitle("未枚举到任何设备；若为权限或 WMI 限制，请以管理员身份运行后重试。", Theme.Warning);
                        return;
                    }
                    SetSubtitle("已枚举 " + result.Count + " 个设备（异常/已禁用排在最前）。", Theme.Success);
                });
            });
        }

        /// <summary>
        /// 通过 PowerShell Get-PnpDevice 枚举（CSV 输出，绕开 WMI 依赖）。
        /// 失败原因写入 error：页面据此区分"读取失败"与"确实没有设备"。
        /// </summary>
        private static List<DeviceInfo> EnumerateDevices(out string error)
        {
            error = "";
            List<DeviceInfo> list = new List<DeviceInfo>();
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -Command \"Get-PnpDevice | Sort-Object Status | " +
                "Select-Object Status,Class,FriendlyName,InstanceId | ConvertTo-Csv -NoTypeInformation\"",
                90000);
            if (!r.Ok)
            {
                error = r.Error != null && r.Error.Length > 0 ? r.Error.Trim() : "PowerShell 退出码 " + r.ExitCode;
                return list;
            }
            if (string.IsNullOrEmpty(r.Output))
            {
                error = "PowerShell 未返回任何输出。";
                return list;
            }

            string[] lines = r.Output.Replace("\r\n", "\n").Split('\n');
            bool header = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                if (header) { header = false; continue; } // 列头行

                string[] f = Csv.SplitLine(line);
                if (f.Length < 4) continue;
                DeviceInfo d = new DeviceInfo();
                d.Status = Unquote(f[0]);
                d.Class = Unquote(f[1]);
                d.Name = Unquote(f[2]);
                d.InstanceId = Unquote(f[3]);
                if (string.IsNullOrEmpty(d.Name) && string.IsNullOrEmpty(d.InstanceId)) continue;
                list.Add(d);
            }
            list.Sort(delegate (DeviceInfo a, DeviceInfo b)
            {
                int c = a.SortRank.CompareTo(b.SortRank);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static string Unquote(string s)
        {
            if (s == null) return "";
            return s.Trim().Trim('"').Replace("\"\"", "\"");
        }

        private void Render()
        {
            Grid.Rows.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                DeviceInfo d = _all[i];
                string statusText;
                Color color = Theme.TextPrimary;
                switch (d.Status.ToLowerInvariant())
                {
                    case "ok": statusText = "正常"; color = Theme.Success; break;
                    case "error": statusText = "异常"; color = Theme.Danger; break;
                    case "disabled": statusText = "已禁用"; color = Theme.TextMuted; break;
                    case "degraded": statusText = "降级运行"; color = Theme.Warning; break;
                    default: statusText = d.Status.Length == 0 ? "—" : d.Status; color = Theme.TextSecondary; break;
                }
                int idx = Grid.Rows.Add(statusText, d.Class, d.Name, d.InstanceId);
                Grid.Rows[idx].Tag = d;
                Grid.Rows[idx].Cells[0].Style.ForeColor = color;
            }
            Grid.ClearSelection();
            UpdateActions();
        }

        // ---------------- 禁用 / 启用 ----------------

        protected override void UpdateActions()
        {
            DeviceInfo d = SelectedRow<DeviceInfo>();
            bool has = d != null && !Busy;
            _disableButton.Enabled = has && !d.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            _enableButton.Enabled = has && d.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            _refreshButton.Enabled = !Busy;
        }

        private void SetDeviceState(bool enable)
        {
            DeviceInfo d = SelectedRow<DeviceInfo>();
            if (Busy) return;
            if (d == null)
            {
                Dialog.Info(this, "未选择设备", "请先在列表中点击选中一个设备（整行高亮），再进行禁用/启用。");
                return;
            }

            // 统一管理员门禁：可直接一键以管理员身份重启，而不是只提示一句"请以管理员身份运行"
            if (!EnsureElevated(enable ? "启用设备需要管理员权限。" : "禁用设备需要管理员权限。")) return;

            string verb = enable ? "启用" : "禁用";
            string detail = enable
                ? "启用后设备将重新上线，可能需要几秒完成初始化。"
                : "禁用后该设备将停止工作（对应功能不可用），可随时重新启用。";
            if (!Dialog.ConfirmDanger(this, verb + "设备",
                "将" + verb + "设备「" + d.Name + "」（" + d.Class + "）。" + detail,
                "可撤销：随时在本页重新" + (enable ? "禁用" : "启用") + "该设备。",
                "只影响这一个设备；其他设备与个人文件不受影响。",
                verb, false))
                return;

            Busy = true;
            UpdateActions();
            SetSubtitle("正在" + verb + "设备…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string id = d.InstanceId.Replace("'", "''");
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"" + (enable ? "Enable-PnpDevice" : "Disable-PnpDevice") +
                    " -InstanceId '" + id + "' -Confirm:$false\"", 60000, isChange: true);

                // PnP 状态变更后枚举一次拿到新状态
                Post(delegate
                {
                    SetSubtitle(enable ? "已发送启用指令，正在刷新状态…" : "已发送禁用指令，正在刷新状态…", Theme.Warning);
                });
                string enumError;
                List<DeviceInfo> result = EnumerateDevices(out enumError);
                Post(delegate
                {
                    Busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    Render();

                    if (!r.Ok)
                    {
                        // 失败给出可操作的原因（权限 / 设备不支持 / 被占用），而不是含糊的"可能未成功"
                        string reason = r.Error != null && r.Error.Length > 0 ? r.Error.Trim() : "退出码 " + r.ExitCode;
                        Dialog.Error(this, verb + "失败",
                            "设备「" + d.Name + "」" + verb + "未成功：\r\n" + reason +
                            "\r\n\r\n若该设备正被使用，请先停止相关程序；部分设备也不支持热插拔式禁用。");
                        SetSubtitle(verb + "失败：" + reason, Theme.Danger);
                        return;
                    }

                    if (enumError.Length > 0)
                        SetSubtitle("设备已" + verb + "，但状态刷新失败：" + enumError + "（可点「刷新」）", Theme.Warning);
                    else
                        SetSubtitle("设备已" + verb + "。", Theme.Success);
                });
            });
        }

        private void OnDisableClick(object sender, EventArgs e) { SetDeviceState(false); }
        private void OnEnableClick(object sender, EventArgs e) { SetDeviceState(true); }
    }
}
