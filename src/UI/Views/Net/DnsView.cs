/* ============================================================
 * 文件说明：网络中心 · DNS 切换页：内置 120 条 DNS 库搜索/筛选/测速/应用
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
    /// DNS 切换：从内置库（公共 + 各省电信/联通/移动）按地区/关键词筛选，
    /// 列表测速选优后一键应用到当前网卡，可随时恢复自动获取。
    /// </summary>
    public sealed class DnsView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly ComboBox _adapterBox = new ComboBox();
        private readonly Label _currentDns = new Label();
        private readonly TextBox _searchBox = new TextBox();
        private readonly ComboBox _regionBox = new ComboBox();
        private readonly DarkGrid _grid = new DarkGrid();
        private AccentButton _apply;
        private AccentButton _reset;
        private AccentButton _speedTest;

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }


        /// <summary>切回本页时刷新当前 DNS 显示（页签懒加载，网卡列表仍走「刷新」按钮）。</summary>
        public override void OnActivated()
        {
            base.OnActivated();
            UpdateCurrentDns();
        }

        public DnsView()
            : base("DNS 切换", "内置 120 条公共与运营商 DNS · 搜索 / 测速 / 一键应用")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Cyan;
            _notice.NoticeText = "选择网卡后搜索或点「测速全部」，选中结果点「应用 DNS」立即生效；「恢复自动」随时还原为 DHCP。";

            _apply = AddAction("应用 DNS", "check", ButtonVariant.Primary, OnApplyDNSClick, 116);
            _reset = AddAction("恢复自动", "undo", ButtonVariant.Ghost, OnResetClick, 116);
            _speedTest = AddAction("测速全部", "gauge", ButtonVariant.Secondary, OnSpeedTestClick, 116);
            AddAction("刷新", "refresh", ButtonVariant.Ghost, delegate { ReloadAdapters(); ReloadGrid(); }, 96);

            BuildLayout();
            ReloadAdapters();
            ReloadGrid();
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            // ① 网卡选择
            FlowLayoutPanel row1 = MakeRowFixed(34, 10);
            row1.Controls.Add(MakeLabel("网卡", 64, true));
            _adapterBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _adapterBox.Size = new Size(200, 30);
            _adapterBox.SelectedIndexChanged += delegate { UpdateCurrentDns(); };
            row1.Controls.Add(_adapterBox);
            _currentDns.Text = "";
            _currentDns.ForeColor = Theme.TextMuted;
            _currentDns.Font = Theme.FontSmall;
            _currentDns.AutoSize = true;
            _currentDns.Margin = new Padding(12, 8, 0, 0);
            row1.Controls.Add(_currentDns);
            AddRow(row1);

            // ② 搜索与地区过滤
            FlowLayoutPanel filter = MakeRowFixed(34, 10);
            filter.Controls.Add(MakeLabel("搜索", 64, true));
            _searchBox.BorderStyle = BorderStyle.FixedSingle;
            _searchBox.Size = new Size(180, 30);
            _searchBox.Font = Theme.FontBody;
            Native.SetCue(_searchBox, "地区 / 名称 / IP");
            _searchBox.KeyPress += delegate (object s, KeyPressEventArgs e)
            {
                if (e.KeyChar == '\r') { ReloadGrid(); e.Handled = true; }
            };
            filter.Controls.Add(ThemeInput.WrapSearch(_searchBox));

            _regionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _regionBox.Size = new Size(150, 30);
            filter.Controls.Add(_regionBox);
            _regionBox.SelectedIndexChanged += delegate { ReloadGrid(); };

            Label tip = MakeLabel("回车搜索 · 选地区过滤 · 点行选中", 300, false);
            tip.Margin = new Padding(12, 8, 0, 0);
            filter.Controls.Add(tip);
            AddRow(filter);

            // ③ 结果列表
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddTextColumn("地区", 110, false);
            _grid.AddFillColumn("说明 / 名称", 220);
            _grid.AddTextColumn("主 DNS", 130, false);
            _grid.AddTextColumn("备 DNS", 130, false);
            _grid.AddTextColumn("延迟", 80, false);
            _grid.ColumnClickSort = true;
            AddFull(_grid, 300, 0);
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

        /// <summary>刷新网卡下拉与当前 DNS；地区下拉只在首次填充。</summary>
        public void ReloadAdapters()
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
            UpdateCurrentDns();

            if (_regionBox.Items.Count == 0)
            {
                _regionBox.Items.Add("全部地区");
                List<string> regs = DnsLibrary.Regions();
                for (int i = 0; i < regs.Count; i++) _regionBox.Items.Add(regs[i]);
                _regionBox.SelectedIndex = 0;
            }
        }

        private void ReloadGrid()
        {
            string kw = _searchBox.Text;
            string region = _regionBox.SelectedItem as string;
            List<DnsPreset> list = DnsLibrary.Search(kw);
            List<DnsPreset> filtered = new List<DnsPreset>();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(region) || region == "全部地区" ||
                    list[i].Region == region) filtered.Add(list[i]);
            }

            _grid.Rows.Clear();
            for (int i = 0; i < filtered.Count; i++)
            {
                DnsPreset p = filtered[i];
                int idx = _grid.Rows.Add(p.Region, p.Name, p.Primary,
                    string.IsNullOrEmpty(p.Secondary) ? "-" : p.Secondary, "");
                _grid.Rows[idx].Tag = p;
            }
            SetSubtitle("找到 " + filtered.Count + " 条 DNS 记录，选中后点「应用 DNS」。", Theme.TextSecondary);
        }

        private void UpdateCurrentDns()
        {
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { _currentDns.Text = ""; return; }
            string dns = DnsSwitch.GetDns(adapter);
            _currentDns.Text = "当前：" + (string.IsNullOrEmpty(dns) ? "自动获取 (DHCP)" : dns);
        }

        // ---------------- 应用 / 恢复 ----------------

        private void OnApplyDNSClick(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先在列表中选择一条 DNS。");
                return;
            }
            DnsPreset p = _grid.SelectedRows[0].Tag as DnsPreset;
            if (p == null) return;
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { Dialog.Info(this, "未选择", "请先选择网卡。"); return; }

            if (!Dialog.Confirm(this, "应用 DNS",
                "将网卡「" + adapter + "」的 DNS 切换为：\r\n" +
                p.Region + " · " + p.Name + "\r\n主 " + p.Primary +
                (string.IsNullOrEmpty(p.Secondary) ? "" : " / 备 " + p.Secondary) +
                "\r\n\r\n是否继续？（可随时一键恢复自动获取）")) return;

            _busy = true;
            _apply.Enabled = false;
            SetSubtitle("正在应用 DNS…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = DnsSwitch.Set(adapter, p.Primary, p.Secondary, out error);
                Post(delegate
                {
                    _busy = false;
                    _apply.Enabled = true;
                    if (ok)
                    {
                        SetSubtitle("DNS 已切换为 " + p.Name + "。立即生效，无需重启。", Theme.Success);
                        UpdateCurrentDns();
                    }
                    else { Dialog.Error(this, "应用失败", error); SetSubtitle("DNS 切换失败。", Theme.Danger); }
                });
            });
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            string adapter = _adapterBox.SelectedItem as string;
            if (string.IsNullOrEmpty(adapter)) { Dialog.Info(this, "未选择", "请先选择网卡。"); return; }

            _busy = true;
            _reset.Enabled = false;
            SetSubtitle("正在恢复自动获取…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = DnsSwitch.Reset(adapter, out error);
                Post(delegate
                {
                    _busy = false;
                    _reset.Enabled = true;
                    if (ok)
                    {
                        SetSubtitle("已恢复为自动获取 (DHCP)。", Theme.Success);
                        UpdateCurrentDns();
                    }
                    else { Dialog.Error(this, "恢复失败", error); SetSubtitle("恢复失败。", Theme.Danger); }
                });
            });
        }

        // ---------------- 测速 ----------------

        /// <summary>对当前列表全部候选并行 ping，把平均延迟回填到「延迟」列并按快慢重排。</summary>
        private void OnSpeedTestClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            _speedTest.Enabled = false;
            SetSubtitle("正在测速全部候选 DNS…", Theme.Warning);

            List<DnsPreset> candidates = new List<DnsPreset>();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DnsPreset p = _grid.Rows[i].Tag as DnsPreset;
                if (p != null) candidates.Add(p);
            }
            if (candidates.Count == 0) { _busy = false; _speedTest.Enabled = true; return; }

            Dictionary<DnsPreset, PingResult> results = new Dictionary<DnsPreset, PingResult>();
            int done = 0;
            int total = candidates.Count;
            for (int i = 0; i < total; i++)
            {
                DnsPreset p = candidates[i];
                ThreadPool.QueueUserWorkItem(delegate
                {
                    PingResult r = NetTools.Ping(p.Primary, 3, 900);
                    Post(delegate
                    {
                        lock (results) results[p] = r;
                        done++;
                        SetSubtitle("DNS 测速中…（" + done + "/" + total + "）", Theme.Warning);
                        if (done >= total) FinishSpeedTest(results);
                    });
                });
            }
        }

        private void FinishSpeedTest(Dictionary<DnsPreset, PingResult> results)
        {
            _busy = false;
            _speedTest.Enabled = true;

            // 重排：先可达（延迟升序）后超时
            List<DnsPreset> ordered = new List<DnsPreset>();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DnsPreset p = _grid.Rows[i].Tag as DnsPreset;
                if (p != null) ordered.Add(p);
            }
            ordered.Sort(delegate (DnsPreset a, DnsPreset b)
            {
                long la, lb;
                PingResult ra, rb;
                results.TryGetValue(a, out ra);
                results.TryGetValue(b, out rb);
                la = ra == null || ra.Received == 0 ? long.MaxValue : ra.AvgMs;
                lb = rb == null || rb.Received == 0 ? long.MaxValue : rb.AvgMs;
                return la.CompareTo(lb);
            });

            _grid.Rows.Clear();
            DnsPreset best = null;
            long bestMs = long.MaxValue;
            for (int i = 0; i < ordered.Count; i++)
            {
                DnsPreset p = ordered[i];
                PingResult r;
                results.TryGetValue(p, out r);
                string lat = r == null ? "…" : (r.Received > 0 ? r.AvgMs.ToString("0") + " ms" : "超时");
                int idx = _grid.Rows.Add(p.Region, p.Name, p.Primary,
                    string.IsNullOrEmpty(p.Secondary) ? "-" : p.Secondary, lat);
                _grid.Rows[idx].Tag = p;
                if (r != null && r.Received > 0 && r.AvgMs < bestMs) { bestMs = r.AvgMs; best = p; }
            }

            if (best == null)
            {
                SetSubtitle("测速完成：全部候选均不可达。", Theme.Danger);
                return;
            }

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DnsPreset p = _grid.Rows[i].Tag as DnsPreset;
                if (p == best) { _grid.Rows[i].Selected = true; break; }
            }
            SetSubtitle("测得最快：" + best.Name + "（" + bestMs.ToString("0") + " ms），已选中，可点「应用 DNS」。", Theme.Success);
        }
    }
}