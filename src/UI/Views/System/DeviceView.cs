using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
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
    public sealed class DeviceView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly List<DeviceInfo> _all = new List<DeviceInfo>();

        private bool _busy;
        private bool _loaded;
        private AccentButton _disableButton;
        private AccentButton _enableButton;
        private AccentButton _refreshButton;

        public DeviceView()
            : base("设备管理", "查看与控制即插即用设备（禁用 / 启用）")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "说明：设备出现感叹号/异常时可尝试「禁用后重新启用」（相当于设备管理器里的禁用/启用），常用于修复 USB 外设、声卡、网卡的偶发失灵。禁用网卡/显卡等关键设备会立即失去相应功能，请确认后再操作。列表已把异常与已禁用设备排在最前。";

            _disableButton = AddAction("禁用选中", "close", ButtonVariant.Danger, OnDisableClick, 120);
            _enableButton = AddAction("启用选中", "check", ButtonVariant.Primary, OnEnableClick, 120);
            _refreshButton = AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);

            BuildGrid();
            BuildLayout();
        }

        public override bool IsBusy { get { return _busy; } }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddTextColumn("状态", 96, false);
            _grid.AddTextColumn("设备类", 130, false);
            _grid.AddFillColumn("设备名称", 220);
            _grid.AddFillColumn("实例 ID", 220);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);
            AddFull(_grid, 360, 0);
            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private void Relayout()
        {
            int used = Body.Padding.Top + Body.Padding.Bottom + 56 + 18;
            int avail = ViewportHeight - used;
            if (avail < 200) avail = 200;
            if (_grid.Height != avail) _grid.Height = avail;
            _grid.Invalidate();
            RefreshLayout();
        }

        public override void OnActivated()
        {
            // _loaded 由 Load() 统一设置，此处不再重复赋值
            if (!_loaded) Load();
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

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            _loaded = true;
            UpdateActions();
            SetSubtitle("正在枚举设备…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DeviceInfo> result = EnumerateDevices();
                Post(delegate
                {
                    _busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    Render();
                    SetSubtitle("已枚举 " + result.Count + " 个设备（异常/已禁用排在最前）。", Theme.Success);
                });
            });
        }

        /// <summary>通过 PowerShell Get-PnpDevice 枚举（CSV 输出，绕开 WMI 依赖）。</summary>
        private static List<DeviceInfo> EnumerateDevices()
        {
            List<DeviceInfo> list = new List<DeviceInfo>();
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -Command \"Get-PnpDevice | Sort-Object Status | " +
                "Select-Object Status,Class,FriendlyName,InstanceId | ConvertTo-Csv -NoTypeInformation\"",
                90000);
            if (!r.Ok || string.IsNullOrEmpty(r.Output)) return list;

            string[] lines = r.Output.Replace("\r\n", "\n").Split('\n');
            bool header = true;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line.Length == 0) continue;
                if (header) { header = false; continue; } // 列头行

                string[] f = SplitCsv(line);
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

        /// <summary>解析单行 CSV（处理双引号包裹与 "" 转义）。</summary>
        private static string[] SplitCsv(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else cur.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields.ToArray();
        }

        private static string Unquote(string s)
        {
            if (s == null) return "";
            return s.Trim().Trim('"').Replace("\"\"", "\"");
        }

        private void Render()
        {
            _grid.Rows.Clear();
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
                int idx = _grid.Rows.Add(statusText, d.Class, d.Name, d.InstanceId);
                _grid.Rows[idx].Tag = d;
                _grid.Rows[idx].Cells[0].Style.ForeColor = color;
            }
            _grid.ClearSelection();
            UpdateActions();
        }

        // ---------------- 禁用 / 启用 ----------------

        private DeviceInfo Selected
        {
            get { return SelectedFrom<DeviceInfo>(_grid); }
        }

        private void UpdateActions()
        {
            DeviceInfo d = Selected;
            bool has = d != null && !_busy;
            _disableButton.Enabled = has && !d.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            _enableButton.Enabled = has && d.Status.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            _refreshButton.Enabled = !_busy;
        }

        private void SetDeviceState(bool enable)
        {
            DeviceInfo d = Selected;
            if (_busy) return;
            if (d == null)
            {
                Dialog.Info(this, "未选择设备", "请先在列表中点击选中一个设备（整行高亮），再进行禁用/启用。");
                return;
            }
            if (!Native.IsElevated())
            {
                Dialog.Warn(this, "需要管理员权限", "设备禁用/启用需要管理员权限。\r\n请右键本程序选择「以管理员身份运行」后重试。");
                return;
            }

            string verb = enable ? "启用" : "禁用";
            string detail = enable
                ? "启用后设备将重新上线，可能需要几秒完成初始化。"
                : "禁用后该设备将停止工作（对应功能不可用），可随时重新启用。";
            if (!Dialog.Confirm(this, verb + "设备",
                "确定要" + verb + "设备「" + d.Name + "」吗？\r\n\r\n" + detail)) return;

            _busy = true;
            UpdateActions();
            SetSubtitle("正在" + verb + "设备…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string id = d.InstanceId.Replace("'", "''");
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"" + (enable ? "Enable-PnpDevice" : "Disable-PnpDevice") +
                    " -InstanceId '" + id + "' -Confirm:$false\"", 60000);

                // PnP 状态变更后枚举一次拿到新状态
                Post(delegate
                {
                    SetSubtitle(enable ? "已发送启用指令，正在刷新状态…" : "已发送禁用指令，正在刷新状态…", Theme.Warning);
                });
                List<DeviceInfo> result = EnumerateDevices();
                Post(delegate
                {
                    _busy = false;
                    _all.Clear();
                    _all.AddRange(result);
                    Render();
                    if (r.Ok) SetSubtitle("设备已" + verb + "。", Theme.Success);
                    else SetSubtitle(verb + "指令可能未成功：" + (r.Error.Length > 0 ? r.Error.Trim() : "权限或设备不支持"), Theme.Danger);
                });
            });
        }

        private void OnDisableClick(object sender, EventArgs e) { SetDeviceState(false); }
        private void OnEnableClick(object sender, EventArgs e) { SetDeviceState(true); }    }
}
