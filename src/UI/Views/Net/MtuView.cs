/* ============================================================
 * 文件说明：网络中心 · MTU 优化页：接口 MTU 一览 / 寻优 1400-1500 / 应用与还原
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
    /// MTU 优化：查看各接口当前 MTU（带数值来源解释），从 1400 到 1500 逐级探测最优值，
    /// 或**自己填**一个 MTU 值应用 —— 不再提供"推荐档位"：链路环境千差万别，
    /// 摆在界面上当默认值反而容易被误当成"最优值"照点。
    /// 寻优结果按网卡留存（本机记忆），换网卡后仍能看到上次测得的最优值。
    /// </summary>
    public sealed class MtuView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly ComboBox _adapterBox = new ComboBox();
        private readonly Label _currentMtu = new Label();
        private readonly TextBox _mtuInput = new TextBox();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly InfoList _result = new InfoList();
        private AccentButton _probe;
        private AccentButton _apply;
        private AccentButton _reset;

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        /// <summary>最近一次寻优得到的最优 MTU（-1 = 尚未寻优）。</summary>
        private int _bestMtu = -1;
        /// <summary>「网卡 → 上次寻优最优 MTU」本机记忆（跨会话保留，由 NetTools 收口读写）。</summary>
        private readonly Dictionary<string, int> _bestByAdapter = NetTools.LoadBestMtu();



        /// <summary>切回本页时刷新当前 MTU 显示（页签懒加载，完整列表仍走「刷新」按钮）。</summary>
        public override void OnActivated()
        {
            base.OnActivated();
            UpdateCurrentMtu();
        }

        public MtuView()
            : base("MTU 优化", "接口 MTU 一览 · 手动指定 · 1400-1500 寻优 · 一键应用")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Success;
            _notice.NoticeText = "MTU 过大遇到分片会被丢弃导致卡顿；用「寻优」探测路径能容纳的最大值，或自己填一个值应用。恢复标准 1500 随时可还原。";

            _probe = AddAction("MTU 寻优 1400-1500", "gauge", ButtonVariant.Primary, OnProbeClick, 180);
            _apply = AddAction("应用最优 MTU", "check", ButtonVariant.Secondary, OnApplyClick, 140);
            _reset = AddAction("恢复标准 1500", "undo", ButtonVariant.Ghost, OnResetClick, 140);
            AddAction("刷新", "refresh", ButtonVariant.Ghost, delegate { Reload(); }, 96);

            BuildLayout();
            Reload();
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            // ① 网卡选择
            FlowLayoutPanel row1 = MakeRowFixed(34, 10);
            row1.Controls.Add(MakeLabel("网卡", 64, true));
            _adapterBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _adapterBox.Size = new Size(200, 30);
            _adapterBox.SelectedIndexChanged += delegate { UpdateCurrentMtu(); };
            row1.Controls.Add(_adapterBox);
            _currentMtu.Text = "";
            _currentMtu.ForeColor = Theme.TextMuted;
            _currentMtu.Font = Theme.FontSmall;
            _currentMtu.AutoSize = true;
            _currentMtu.Margin = new Padding(12, 8, 0, 0);
            row1.Controls.Add(_currentMtu);
            AddRow(row1);

            // ② 自定义 MTU（自己填；原来的"常用档位"按钮已去掉 —— 预设值容易被当成最优值照点）
            FlowLayoutPanel row2 = MakeRowFixed(34, 10);
            row2.Controls.Add(MakeLabel("自定义", 64, true));
            _mtuInput.BorderStyle = BorderStyle.FixedSingle;
            _mtuInput.Font = Theme.FontBody;
            _mtuInput.Size = new Size(120, 30);
            _mtuInput.Text = "1500";
            _mtuInput.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == '\r') { OnCustomApply(null, null); e.Handled = true; }
                else if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            row2.Controls.Add(ThemeInput.Wrap(_mtuInput));
            Label range = MakeLabel("范围 576 – 9000（1500 为标准值）", 260, false);
            range.Margin = new Padding(8, 8, 0, 0);
            row2.Controls.Add(range);
            AccentButton applyCustom = new AccentButton();
            applyCustom.Text = "应用此 MTU";
            applyCustom.IconKind = "check";
            applyCustom.Variant = ButtonVariant.Primary;
            applyCustom.Size = new Size(130, 30);
            applyCustom.Margin = new Padding(8, 0, 0, 0);
            applyCustom.Click += OnCustomApply;
            row2.Controls.Add(applyCustom);
            AddRow(row2);

            // ③ 接口 MTU 一览
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("接口", 260);
            _grid.AddTextColumn("当前 MTU", 110, false);
            _grid.AddTextColumn("说明 / 状态", 300, false);
            AddFull(_grid, 160, 10);

            // ④ 寻优结果（含档位解释与上次寻优记忆）
            _result.Caption = "寻优结果与说明";
            _result.IconKind = "gauge";
            _result.CaptionColor = Theme.Success;
            _result.EmptyText = "点页头「MTU 寻优」：从 1400 到 1500 逐级探测（禁止分片），结果与说明会展示在这里。";
            AddFull(_result, InfoList.HeightFor(6), 0);
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
            l.Margin = new Padding(0, 0, 8, 0);
            return l;
        }

        public void Reload()
        {
            string keep = _adapterBox.SelectedItem as string;
            _adapterBox.Items.Clear();
            List<string> names = DnsSwitch.ListAdapters();
            for (int i = 0; i < names.Count; i++) _adapterBox.Items.Add(names[i]);
            if (names.Count > 0)
            {
                if (keep != null && names.Contains(keep)) _adapterBox.SelectedItem = keep;
                else _adapterBox.SelectedIndex = 0;
            }
            UpdateCurrentMtu();
            RefreshMtuGrid();
        }

        private void RefreshMtuGrid()
        {
            _grid.Rows.Clear();
            List<KeyValuePair<string, int>> mtus = NetTools.GetMtuList();
            for (int i = 0; i < mtus.Count; i++)
            {
                _grid.Rows.Add(mtus[i].Key, mtus[i].Value.ToString(), ExplainMtu(mtus[i].Value));
            }
        }

        private void UpdateCurrentMtu()
        {
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { _currentMtu.Text = ""; return; }
            List<KeyValuePair<string, int>> mtus = NetTools.GetMtuList();
            for (int i = 0; i < mtus.Count; i++)
            {
                if (string.Equals(mtus[i].Key, adapter, StringComparison.OrdinalIgnoreCase))
                {
                    int remembered;
                    string memory = _bestByAdapter.TryGetValue(adapter, out remembered)
                        ? " · 上次寻优：" + remembered
                        : "";
                    _currentMtu.Text = "当前 MTU：" + mtus[i].Value + "（" + ExplainMtu(mtus[i].Value) + "）" + memory;
                    return;
                }
            }
            _currentMtu.Text = "当前 MTU：未读取到（该接口可能未启用 IPv4）";
        }

        /// <summary>把 MTU 数值翻译成链路来源（用户最常问"这个值正常吗、为什么是这个值"）。</summary>
        private static string ExplainMtu(int mtu)
        {
            if (mtu >= 1500) return "标准以太网";
            if (mtu >= 1492) return "PPPoE 拨号链路典型值";
            if (mtu >= 1480) return "VPN / 隧道链路常见值";
            if (mtu >= 1472) return "PPPoE 下 IPv4 常见上限（1500−28）";
            if (mtu >= 1452) return "部分运营商 / 隧道链路";
            if (mtu >= 1400) return "移动网络 / 保守值";
            if (mtu > 0) return "偏低：可能影响兼容性";
            return "未读取到";
        }

        // ---------------- 寻优 ----------------

        private void OnProbeClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _probe.Enabled = false;
            SetSubtitle("正在从 1400 到 1500 逐级探测最优 MTU…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string target = "223.5.5.5";
                int best = 0;
                long bestMs = -1;
                int failedAt = 0;
                for (int mtu = 1400; mtu <= 1500; mtu++)
                {
                    long ms;
                    bool ok = NetTools.PingDontFragment(target, mtu - 28, 500, out ms);
                    if (!ok) ok = NetTools.PingDontFragment(target, mtu - 28, 500, out ms);
                    if (!ok) { failedAt = mtu; break; }
                    best = mtu;
                    bestMs = ms;
                }
                int rBest = best;
                long rMs = bestMs;
                int rFail = failedAt;
                Post(delegate { FinishProbe(rBest, rMs, rFail, target); });
            });
        }

        private void FinishProbe(int best, long bestMs, int failedAt, string target)
        {
            _busy = false;
            _probe.Enabled = true;
            _result.Clear();

            if (best < 1400)
            {
                _result.Add("MTU 寻优", "1400 也探测失败——路径 MTU 低于 1400 或外网不可达。");
                _result.Height = _result.PreferredHeight;
                SetSubtitle("MTU 寻优失败：请先确认外网连通。", Theme.Danger);
                return;
            }

            _result.Add("最优 MTU", best + "（" + ExplainMtu(best) + "）"
                + (best >= 1500 ? "，已到标准以太网上限" : ""));
            _result.Add("探测方式", target + " · 禁止分片 (DF) · 载荷 = MTU-28 逐级验证");
            _result.Add("验证结果", "1400 → " + best + " 全部通过" +
                (failedAt > 0 ? "；" + failedAt + " 起被路径拒绝（需要分片）" : ""));
            if (bestMs >= 0) _result.Add("该尺寸往返延迟", bestMs + " ms");

            // 按网卡留存本次结果：换网卡回来时仍能看到上次测得值
            string adapter = _adapterBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(adapter))
            {
                _bestByAdapter[adapter] = best;
                NetTools.SaveBestMtu(adapter, best);
                _result.Add("本页记忆", "网卡「" + adapter + "」上次寻优：" + best + "（已留存，可随时重测）");
            }

            _result.Height = _result.PreferredHeight;
            _bestMtu = best;
            UpdateCurrentMtu();
            RefreshLayout();
            SetSubtitle("最优 MTU：" + best + "。选中网卡后点「应用最优 MTU」，或在「自定义」里填值应用。", Theme.Success);
        }

        // ---------------- 应用 / 恢复 ----------------

        /// <summary>应用用户自己填写的 MTU：只做范围校验，不再有"推荐档位"。</summary>
        private void OnCustomApply(object sender, EventArgs e)
        {
            if (_busy) return;
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { Dialog.Info(this, "未选择", "请先选择网卡。"); return; }

            int mtu;
            if (!int.TryParse(_mtuInput.Text.Trim(), out mtu) || mtu < 576 || mtu > 9000)
            {
                Dialog.Info(this, "数值无效",
                    "请输入 576 – 9000 之间的 MTU。\r\n\r\n常见取值：1500 标准以太网 / 1492 PPPoE / "
                    + "1480 VPN / 1472 PPPoE+IPv4 / 1452 隧道。");
                return;
            }

            if (mtu == 1500)
            {
                if (!Dialog.Confirm(this, "恢复标准 MTU",
                    "1500 就是标准值：将把网卡「" + adapter + "」恢复为标准 1500，是否继续？")) return;
                ApplyMtu(1500, "标准以太网");
                return;
            }

            if (!Dialog.Confirm(this, "应用 MTU " + mtu,
                "把网卡「" + adapter + "」的 MTU 设为 " + mtu + "（" + ExplainMtu(mtu) + "）。\r\n"
                + "写入持久配置，随时可改回。\r\n\r\n是否继续？"))
                return;
            ApplyMtu(mtu, ExplainMtu(mtu));
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            if (_busy) return;
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { Dialog.Info(this, "未选择", "请先选择网卡。"); return; }

            int best = _bestMtu >= 1400 ? _bestMtu : 1500;
            if (!Dialog.Confirm(this, "应用 MTU",
                "将把网卡「" + adapter + "」的 MTU 设为 " + best +
                (best >= 1500 ? "（标准以太网上限）" : "（寻优测得）") +
                "。\r\n写入持久配置，随时可改回。\r\n\r\n是否继续？")) return;
            ApplyMtu(best, best >= 1500 ? "标准以太网上限" : "寻优测得");
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            if (_busy) return;
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { Dialog.Info(this, "未选择", "请先选择网卡。"); return; }
            if (!Dialog.Confirm(this, "恢复标准 MTU", "将网卡「" + adapter + "」的 MTU 恢复为标准 1500，是否继续？")) return;
            ApplyMtu(1500, "标准以太网");
        }

        /// <summary>统一的 MTU 应用入口：档位按钮 / 应用最优 / 恢复标准都走这里，保证门禁与反馈一致。</summary>
        private void ApplyMtu(int mtu, string note)
        {
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) return;

            _busy = true;
            _apply.Enabled = false;
            _reset.Enabled = false;
            SetSubtitle("正在设置 MTU…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Shell.Result r = NetTools.SetMtu(adapter, mtu);
                Post(delegate
                {
                    _busy = false;
                    _apply.Enabled = true;
                    _reset.Enabled = true;
                    Dialog.Output(this, "设置 MTU", r.All.Length > 0 ? r.All : "已把 MTU 设为 " + mtu + "。");
                    SetSubtitle(r.Ok ? "MTU 已设为 " + mtu + "（" + note + "）。" : "MTU 设置失败。",
                        r.Ok ? Theme.Success : Theme.Danger);
                    Reload();
                });
            });
        }
    }
}
