﻿/* ============================================================
 * 文件说明：网络中心页：状态卡、适配器与 DNS、端口占用
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 网络中心（仪表式单页）：顶部网络状态卡（联网/网关/外网/DNS），
    /// 中部适配器总览与 DNS 快切，下部端口占用与诊断修复——全部功能一页直达。
    /// </summary>
    public sealed class NetworkView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatCard _statusCard = new StatCard();
        private readonly StatCard _gatewayCard = new StatCard();
        private readonly StatCard _internetCard = new StatCard();
        private readonly StatCard _dnsCard = new StatCard();
        private readonly InfoList _dnsInfo = new InfoList();
        private readonly InfoList _mtuInfo = new InfoList();

        private readonly DarkGrid _adapterGrid = new DarkGrid();
        private readonly TextBox _portInput = new TextBox();
        private readonly DarkGrid _portGrid = new DarkGrid();

        private AccentButton _diagButton;
        private AccentButton _portQuery;
        private AccentButton _portKill;

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }


        public NetworkView()
            : base("网络诊断", "联网状态 / 适配器总览 / 端口占用 / 一键修复，一页直达")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "上不了网？按顺序试：开始诊断 → 刷新 DNS 缓存 → 续约 IP → 重置 Winsock / TCP-IP（需重启）。DNS 切换可随时一键恢复自动获取。";

            _statusCard.IconKind = "network";
            _gatewayCard.IconKind = "power";
            _internetCard.IconKind = "gauge";
            _dnsCard.IconKind = "doc";
            ResetCards();

            _dnsInfo.Caption = "DNS 配置";
            _dnsInfo.IconKind = "doc";
            _dnsInfo.CaptionColor = Theme.Cyan;
            _dnsInfo.EmptyText = "正在读取网卡与 DNS 配置…";
            _mtuInfo.Caption = "MTU 与链路";
            _mtuInfo.IconKind = "network";
            _mtuInfo.CaptionColor = Theme.Success;
            _mtuInfo.EmptyText = "正在读取接口 MTU…";

            _diagButton = AddAction("开始诊断", "shield", ButtonVariant.Primary, OnDiagClick, 128);
            AddAction("刷新 DNS 缓存", "clean", ButtonVariant.Secondary, delegate { RunRepair("刷新 DNS 缓存", delegate { return NetTools.FlushDns(); }, false); }, 150);
            AddAction("续约 IP 地址", "power", ButtonVariant.Ghost, delegate { RunRepair("续约 IP 地址", delegate { return NetTools.RenewDhcp(); }, false); }, 140);
            AddAction("清理 ARP 缓存", "refresh", ButtonVariant.Ghost, delegate { RunRepair("清理 ARP 缓存", delegate { return NetTools.ClearArpCache(); }, false); }, 140);
            AddAction("重置 Winsock", "refresh", ButtonVariant.Ghost, delegate { RunRepair("重置 Winsock", delegate { return NetTools.ResetWinsock(); }, true); }, 140);
            AddAction("重置 TCP-IP", "refresh", ButtonVariant.Ghost, delegate { RunRepair("重置 TCP-IP", delegate { return NetTools.ResetTcpIp(); }, true); }, 140);

            BuildLayout();
            LoadAdapters();
        }

        // --------------------------------------------------------------

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            // ① 状态卡行
            FlowLayoutPanel stats = MakeRowFixed(128, 0);
            stats.Controls.Add(_statusCard);
            stats.Controls.Add(_gatewayCard);
            stats.Controls.Add(_internetCard);
            stats.Controls.Add(_dnsCard);
            AddRow(stats);

            // ② 适配器总览
            _adapterGrid.ReadOnly = true;
            _adapterGrid.UseOwnScrollbar = true;
            _adapterGrid.AddTextColumn("网卡", 190, false);
            _adapterGrid.AddTextColumn("类型", 90, false);
            _adapterGrid.AddFillColumn("IPv4 地址", 150);
            _adapterGrid.AddTextColumn("网关", 140, false);
            _adapterGrid.AddTextColumn("DNS", 170, false);
            _adapterGrid.AddTextColumn("速度", 90, false);
            _adapterGrid.AddTextColumn("状态", 80, false);
            AddFull(_adapterGrid, 200, 14);

            // ③ 网络参数分类信息（DNS 配置 / MTU 与链路）——与系统概览同款信息卡
            FlowLayoutPanel infoRow = MakeRow(0, 14);
            infoRow.Controls.Add(_dnsInfo);
            infoRow.Controls.Add(_mtuInfo);
            AddRow(infoRow);

            // ④ 端口占用
            FlowLayoutPanel portRow = MakeRow(0, 12);
            portRow.Controls.Add(MakeLabel("端口占用", 84, true));
            _portInput.BorderStyle = BorderStyle.FixedSingle;
            _portInput.Size = new Size(130, 30);
            Native.SetCue(_portInput, "端口号");
            _portInput.Font = Theme.FontBody;
            _portInput.KeyPress += delegate (object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == '\r') { OnPortQuery(null, null); e.Handled = true; }
                else if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            portRow.Controls.Add(ThemeInput.Wrap(_portInput));
            _portQuery = MakeInlineButton("查询", ButtonVariant.Primary, OnPortQuery, 92);
            portRow.Controls.Add(_portQuery);
            _portKill = MakeInlineButton("结束占用进程", ButtonVariant.Danger, OnPortKill, 140);
            _portKill.Enabled = false;
            portRow.Controls.Add(_portKill);
            Label portTip = MakeLabel("输入端口号回车查询；占用进程可一键结束", 280, false);
            portTip.Margin = new Padding(12, 8, 0, 0);
            portRow.Controls.Add(portTip);
            AddRow(portRow);

            _portGrid.ReadOnly = true;
            _portGrid.UseOwnScrollbar = true;
            _portGrid.AddTextColumn("协议", 70, false);
            _portGrid.AddFillColumn("本地地址", 200);
            _portGrid.AddTextColumn("状态", 130, false);
            _portGrid.AddTextColumn("PID", 70, false);
            _portGrid.AddTextColumn("进程", 200, false);
            // 未查询时不显示（也就不会占位）：空表格只是一大片空白，没有任何信息。
            // 点「查询」出结果后再显示，见 OnPortQuery。
            _portGrid.Visible = false;
            AddFull(_portGrid, 190, 0);

            Body.Resize += delegate
            {
                // 端口表随视口伸缩（适配器表固定高，保证下方内容一屏可见）；
                // DNS 快切与自动调优两行已独立成页签（DNS 切换 / MTU 优化），固定占用高度回调
                _portGrid.Height = Math.Max(160, ViewportHeight - 290);
            };
            RefreshLayout();
        }

        private void ResetCards()
        {
            _statusCard.SetData("联网状态", "--", -1, "点「开始诊断」检测", Theme.Accent);
            _gatewayCard.SetData("网关延迟", "--", -1, "", Theme.Success);
            _internetCard.SetData("外网延迟", "--", -1, "", Theme.Cyan);
            _dnsCard.SetData("DNS 解析", "--", -1, "", Theme.Warning);
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

        private AccentButton MakeInlineButton(string text, ButtonVariant v, EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "power";
            b.Variant = v;
            b.Height = Theme.RowButtonHeight;
            b.FitToText(width);   // width 是「最小宽度」：文字更长时自动放宽，避免被省略号截断
            b.Margin = new Padding(6, 0, 0, 0);
            b.Click += onClick;
            return b;
        }

        private int ViewportWidth
        {
            get { return Body.ClientSize.Width - Body.Padding.Left - Body.Padding.Right; }
        }

        // ---------------- 适配器与当前 DNS ----------------

        private void LoadAdapters()
        {
            _adapterGrid.Rows.Clear();
            List<AdapterInfo> adapters = NetTools.ListAdapters();
            string defaultDns = "-";
            for (int i = 0; i < adapters.Count; i++)
            {
                AdapterInfo a = adapters[i];
                string ip = a.IPv4.Count > 0 ? a.IPv4[0] : "-";
                string gw = a.Gateway.Count > 0 ? a.Gateway[0] : "-";
                string dns = a.Dns.Count > 0 ? string.Join(", ", a.Dns.ToArray()) : "-";
                if (gw != "-" && defaultDns == "-") defaultDns = ip;
                _adapterGrid.Rows.Add(a.Name, a.Type, ip, gw, dns, a.SpeedText, a.Status);
            }

            RefreshNetworkInfo(adapters);
        }

        /// <summary>刷新「DNS 配置 / MTU 与链路」分类信息卡（与系统概览同款的信息展示）。</summary>
        private void RefreshNetworkInfo(List<AdapterInfo> adapters)
        {
            // DNS 配置：每个有 IP 的网卡一行
            _dnsInfo.Clear();
            for (int i = 0; i < adapters.Count; i++)
            {
                AdapterInfo a = adapters[i];
                if (a.IPv4.Count == 0 && a.Status != "已连接" && a.Status != "Up") continue;
                string dns = a.Dns.Count > 0 ? string.Join(" / ", a.Dns.ToArray()) : "自动获取 (DHCP)";
                _dnsInfo.Add(a.Name, dns);
            }
            if (adapters.Count == 0) _dnsInfo.Add("网卡", "未检测到");
            _dnsInfo.Invalidate();

            // MTU 与链路：netsh 的接口 MTU 列表
            _mtuInfo.Clear();
            List<KeyValuePair<string, int>> mtus = NetTools.GetMtuList();
            for (int i = 0; i < mtus.Count; i++)
            {
                int mtu = mtus[i].Value;
                _mtuInfo.Add(mtus[i].Key, mtu + (mtu >= 1500 ? "（标准）" : "（非标准，注意兼容性）"));
            }
            if (mtus.Count == 0) _mtuInfo.Add("MTU", "读取失败或无接口");
            _mtuInfo.Invalidate();

            ResizeInfoCards();
        }

        /// <summary>信息卡高度按内容自适应（与系统概览一致），行高随最高的卡片。</summary>
        private void ResizeInfoCards()
        {
            int h = Math.Max(140, Math.Max(_dnsInfo.PreferredHeight, _mtuInfo.PreferredHeight));
            if (_dnsInfo.Height != h) _dnsInfo.Height = h;
            if (_mtuInfo.Height != h) _mtuInfo.Height = h;
            Control row = _dnsInfo.Parent;
            if (row != null && row.Height != h) row.Height = h;
            RefreshLayout();
        }

        // ---------------- 诊断 ----------------

        private void OnDiagClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _diagButton.Enabled = false;
            SetSubtitle("正在诊断网络…", Theme.Warning);
            _statusCard.SetData("联网状态", "检测中", -1, "", Theme.Warning);
            _statusCard.Invalidate();

            ThreadPool.QueueUserWorkItem(delegate
            {
                // 网关地址取默认路由（第一个有网关的适配器）
                string gateway = null;
                List<AdapterInfo> adapters = NetTools.ListAdapters();
                for (int i = 0; i < adapters.Count; i++)
                {
                    if (adapters[i].Gateway.Count > 0) { gateway = adapters[i].Gateway[0]; break; }
                }

                PingResult gwPing = null;
                if (!string.IsNullOrEmpty(gateway))
                {
                    try { gwPing = NetTools.Ping(gateway, 4, 1500); }
                    catch { }
                }
                PingResult direct = NetTools.Ping("223.5.5.5", 4, 2000);
                PingResult dnsPing = NetTools.Ping("www.baidu.com", 4, 2000);
                // 当前 DNS：取第一个有 IP 的已连接适配器
                string resolvedDns = "";
                for (int i = 0; i < adapters.Count; i++)
                {
                    if (adapters[i].Dns.Count > 0) { resolvedDns = string.Join(" / ", adapters[i].Dns.ToArray()); break; }
                }
                if (string.IsNullOrEmpty(resolvedDns)) resolvedDns = "自动 (DHCP)";

                Post(delegate
                {
                    _busy = false;
                    _diagButton.Enabled = true;

                    // 状态卡
                    bool online = direct.Received > 0;
                    _statusCard.SetData("联网状态", online ? "在线" : "离线",
                        online ? -1 : 100, online ? "外网可达" : "请从下方修复步骤排查",
                        online ? Theme.Success : Theme.Danger);

                    if (gwPing != null)
                    {
                        _gatewayCard.SetData("网关延迟", gwPing.Received > 0 ? gwPing.AvgMs.ToString("0") + " ms" : "超时",
                            gwPing.Received > 0 ? Math.Min(100, gwPing.AvgMs / 5.0) : 100,
                            gwPing.Received > 0 ? gateway + " · 正常" : gateway + " 无响应，检查网线 / Wi-Fi",
                            gwPing.Received > 0 ? Theme.Success : Theme.Danger);
                    }
                    else
                    {
                        _gatewayCard.SetData("网关延迟", "--", -1, "未检测到网关", Theme.TextMuted);
                    }

                    _internetCard.SetData("外网延迟", direct.Received > 0 ? direct.AvgMs.ToString("0") + " ms" : "超时",
                        direct.Received > 0 ? Math.Min(100, direct.AvgMs / 5.0) : 100,
                        direct.Received > 0
                            ? "223.5.5.5 · 丢包 " + direct.LossPercent.ToString("0") + "%"
                            : "223.5.5.5 · 不通，检查路由 / 代理设置",
                        direct.Received > 0 ? Theme.Cyan : Theme.Danger);

                    bool dnsOk = dnsPing.Received > 0;
                    // 诊断结论与修复建议直接写在四张卡的脚注上：原先页面底部另有一张「网络诊断」明细卡，
                    // 内容与状态卡重复（网关 / 外网 / DNS 各说一遍），已删除，避免同一结论两处维护
                    _dnsCard.SetData("DNS 解析", dnsOk ? dnsPing.AvgMs.ToString("0") + " ms" : "失败",
                        dnsOk ? -1 : 100,
                        dnsOk ? resolvedDns : resolvedDns + " · 建议先「刷新 DNS 缓存」",
                        dnsOk ? Theme.Success : Theme.Danger);

                    SetSubtitle(online && dnsOk ? "网络正常。" : "诊断完成：存在问题，按卡片脚注修复。",
                        online && dnsOk ? Theme.Success : Theme.Warning);
                    RefreshLayout();
                });
            });
        }

        // ---------------- 端口 ----------------

        private void OnPortQuery(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(_portInput.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                Dialog.Info(this, "端口无效", "请输入 1-65535 之间的端口号。");
                return;
            }

            _busy = true;
            _portQuery.Enabled = false;
            SetSubtitle("正在查询端口 " + port + " 的占用…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<PortInfo> all = NetTools.GetListeningPorts();
                List<PortInfo> hits = new List<PortInfo>();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].LocalPort == port) hits.Add(all[i]);
                }
                Post(delegate
                {
                    _busy = false;
                    _portQuery.Enabled = true;
                    _portGrid.Rows.Clear();
                    for (int i = 0; i < hits.Count; i++)
                    {
                        PortInfo pi = hits[i];
                        int idx = _portGrid.Rows.Add(pi.Protocol, pi.LocalAddress, pi.State, pi.OwningPid.ToString(), pi.ProcessName);
                        _portGrid.Rows[idx].Tag = pi;
                    }
                    _portKill.Enabled = _portGrid.Rows.Count > 0;
                    // 查询过就展示表格（即使没占用也显示，结论由副标题给出）；未查询前保持隐藏不占位
                    if (!_portGrid.Visible)
                    {
                        _portGrid.Visible = true;
                        RefreshLayout();
                    }
                    SetSubtitle(hits.Count > 0
                        ? "端口 " + port + " 被 " + hits.Count + " 个连接占用，选中后可结束对应进程。"
                        : "端口 " + port + " 当前没有被占用。", hits.Count > 0 ? Theme.Warning : Theme.Success);
                });
            });
        }

        private void OnPortKill(object sender, EventArgs e)
        {
            if (_portGrid.SelectedRows.Count == 0) return;
            PortInfo pi = _portGrid.SelectedRows[0].Tag as PortInfo;
            if (pi == null || pi.OwningPid <= 0) return;

            if (!Dialog.Confirm(this, "结束进程",
                "端口 " + pi.LocalPort + " 的占用进程：" + pi.ProcessName + " (PID " + pi.OwningPid + ")\r\n\r\n" +
                "结束它可能导致该程序未保存的数据丢失，是否继续？")) return;

            _busy = true;
            _portKill.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = "";
                bool ok = false;
                try
                {
                    using (System.Diagnostics.Process proc = System.Diagnostics.Process.GetProcessById(pi.OwningPid))
                    {
                        proc.Kill();
                        ok = proc.WaitForExit(5000);
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                Post(delegate
                {
                    _busy = false;
                    _portKill.Enabled = true;
                    if (ok) { SetSubtitle("已结束进程 " + pi.ProcessName + "，端口 " + pi.LocalPort + " 已释放。", Theme.Success); OnPortQuery(null, null); }
                    else { Dialog.Error(this, "结束失败", error); SetSubtitle("结束进程失败。", Theme.Danger); }
                });
            });
        }

        // ---------------- 修复 ----------------

        private void RunRepair(string title, Func<Shell.Result> action, bool needReboot)
        {
            if (_busy) return;
            if (needReboot && !Dialog.Confirm(this, title,
                title + " 会重置网络组件，执行后需要重启计算机才完全生效。\r\n\r\n是否继续？")) return;

            _busy = true;
            SetSubtitle("正在执行：" + title + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r;
                try { r = action(); }
                catch (Exception ex)
                {
                    r = new Shell.Result();
                    r.ExitCode = -1;
                    r.Error = ex.Message;
                }
                Post(delegate
                {
                    _busy = false;
                    bool ok = r.Ok; // 用进程退出码判定（ExitCode==0），不依赖本地化输出文本
                    Dialog.Output(this, title, r.All.Length > 0 ? r.All : "执行完成。");
                    SetSubtitle(title + " 已执行" + (needReboot ? "，重启后完全生效。" : "。"),
                        ok ? Theme.Success : Theme.Warning);
                });
            });
        }
    }
}
