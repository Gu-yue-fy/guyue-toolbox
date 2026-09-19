/* ============================================================
 * 文件说明：内存优化页。按设计「工具型」页面组织：
 *           Hero 卡（圆环占用率 + 一键释放）+ 占用排行 TOP N + 内存明细。
 *           与「性能加速」页的区别：本页是即时工具，改动的是内存当前状态；
 *           性能加速页改的是注册表策略。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class MemoryView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly HeroCard _hero = new HeroCard();
        private readonly RankStrip _rank = new RankStrip();
        private readonly SectionTitle _secDetail = new SectionTitle();
        private readonly InfoList _detail = new InfoList();

        private readonly ProcManager _procs = new ProcManager();

        private const int RankTop = 8;

        private bool _busy;
        private bool _releasing;
        private bool _loaded;

        public MemoryView()
            : base("内存优化", "实时占用 / 一键释放 / 占用排行")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "释放只回收待机缓存与各进程的空闲工作集，不结束任何进程、不改系统设置；"
                + "正在被使用的内存不受影响，随后会自动回填。";

            _hero.TagIcon = "bolt";
            _hero.TagText = "即时清理工具";
            _hero.Headline = "一键释放被占用的内存";
            _hero.SubText = "回收待机列表与空闲工作集，实测释放量在完成后显示。";
            _hero.Gauge.CenterUnit = "%";
            _hero.Gauge.CenterText = "--";
            _hero.Gauge.StateText = "读取中";
            _hero.Action.Text = "立即清理";
            _hero.Action.IconKind = "bolt";
            _hero.Action.Click += OnReleaseClick;

            // 排行条自带标题与刷新按钮（设计 mem-top-strip 的写法），
            // 页面不再另加区块标题，避免出现两个「占用排行」
            _rank.TitleText = "占用排行";
            _rank.TitleHint = "TOP " + RankTop;
            _rank.RefreshClick += delegate { Load(); };

            _secDetail.TitleText = "内存明细";
            _secDetail.HintText = "物理内存 / 页面文件 / 提交量";
            _secDetail.Tone = Theme.Success;

            _detail.Caption = "内存明细";
            _detail.IconKind = "memory";
            _detail.CaptionColor = Theme.Success;
            _detail.EmptyText = "正在读取内存信息…";

            AddFull(_notice, 34, 12);
            AddFull(_hero, 212, Theme.GapSection);
            // 高度在这里一次定稿（排行条按最大行数、明细卡按固定 5 行）：
            // 行布局要求"行高在挂载前定稿"，取到数据后再改高会与其后区块错位。
            AddFull(_rank, RankStrip.HeightFor(RankTop), Theme.GapSection);
            AddFull(_secDetail, 44, 0);
            AddFull(_detail, 46 + 5 * InfoList.RowHeight + 12, 0);

            AddAction("一键释放", "bolt", ButtonVariant.Primary, OnReleaseClick, 124);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 96);
        }

        public override void OnActivated()
        {
            // 首次进入才读数据；每次切回都重扫进程代价太大
            if (!_loaded) Load();
        }

        // ==============================================================
        // 数据加载
        // ==============================================================

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在读取内存信息…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                MemoryInfo m = null;
                List<ProcInfo> procs = null;
                try
                {
                    m = SysInfo.GetMemory();
                    procs = _procs.Snapshot();
                }
                catch
                {
                }

                Post(delegate
                {
                    _busy = false;
                    if (m == null)
                    {
                        SetSubtitle("读取内存信息失败。", Theme.Danger);
                        return;
                    }
                    _loaded = true;
                    Render(m, procs);
                    SetSubtitle("内存信息已更新。", Theme.Success);
                });
            });
        }

        private void Render(MemoryInfo m, List<ProcInfo> procs)
        {
            double pct = m.UsedPercent;

            _hero.Gauge.Percent = pct;
            _hero.Gauge.Tone = Gfx.LoadColor(pct);
            _hero.Gauge.ToneTo = pct >= 70 ? Gfx.Shade(Gfx.LoadColor(pct), 1.25) : Theme.Cyan;
            _hero.Gauge.CenterText = pct.ToString("0");
            _hero.Gauge.SubText = SysInfo.FormatSize(m.UsedBytes) + " / " + SysInfo.FormatSize(m.TotalBytes);
            _hero.Gauge.StateText = pct >= 90 ? "内存紧张" : (pct >= 75 ? "占用偏高" : "运行良好");

            _hero.SetStats(
                new string[] { "可用", "页面文件", "提交上限" },
                new string[] {
                    SysInfo.FormatSize(m.AvailBytes),
                    m.PageFileAvail > 0 ? SysInfo.FormatSize(m.PageFileTotal - m.PageFileAvail) : "--",
                    SysInfo.FormatSize(m.PageFileTotal)
                });

            // ---- 占用排行 ----
            List<ProcInfo> top = null;
            if (procs != null && procs.Count > 0)
            {
                List<ProcInfo> sorted = new List<ProcInfo>(procs);
                sorted.Sort(delegate (ProcInfo a, ProcInfo b) { return b.WorkingSet.CompareTo(a.WorkingSet); });

                long max = sorted[0].WorkingSet;
                if (max <= 0) max = 1;

                top = new List<ProcInfo>();
                int n = sorted.Count < RankTop ? sorted.Count : RankTop;
                for (int i = 0; i < n; i++) top.Add(sorted[i]);

                List<RankStrip.Row> rows = new List<RankStrip.Row>();
                for (int i = 0; i < top.Count; i++)
                {
                    ProcInfo p = top[i];
                    RankStrip.Row r = new RankStrip.Row();
                    r.Rank = i + 1;
                    r.Icon = IconOf(p.Name);
                    r.Name = p.Name + "（" + p.Pid + "）";
                    r.Value = SysInfo.FormatSize(p.WorkingSet);
                    r.Percent = (double)p.WorkingSet * 100.0 / max;
                    r.Tone = i == 0 ? Theme.Danger : (i < 3 ? Theme.Warning : Theme.Accent);
                    rows.Add(r);
                }
                _rank.SetRows(rows);
            }
            else
            {
                _rank.SetRows(null);
            }

            // ---- 明细 ----
            _detail.Clear();
            _detail.Add("物理内存总量", SysInfo.FormatSize(m.TotalBytes));
            _detail.Add("已用", SysInfo.FormatSize(m.UsedBytes) + "（" + pct.ToString("0.0") + "%）");
            _detail.Add("可用", SysInfo.FormatSize(m.AvailBytes));
            _detail.Add("页面文件", m.PageFileTotal > 0
                ? SysInfo.FormatSize(m.PageFileTotal - m.PageFileAvail) + " 已用 / " + SysInfo.FormatSize(m.PageFileTotal) + " 总量"
                : "未启用或读取失败");
            _detail.Add("占用最高的进程", top != null && top.Count > 0
                ? top[0].Name + "（" + SysInfo.FormatSize(top[0].WorkingSet) + "）"
                : "--");
            _detail.Invalidate();
        }

        /// <summary>按进程名的用途给出图标名（用于排行行的图标块）。</summary>
        private static string IconOf(string name)
        {
            string n = (name == null ? "" : name).ToLowerInvariant();
            if (n.IndexOf("chrome") >= 0 || n.IndexOf("edge") >= 0 || n.IndexOf("firefox") >= 0) return "globe";
            if (n.IndexOf("code") >= 0 || n.IndexOf("devenv") >= 0) return "tune";
            if (n.IndexOf("explorer") >= 0 || n.IndexOf("dwm") >= 0) return "apps";
            if (n.IndexOf("svchost") >= 0 || n.IndexOf("service") >= 0) return "services";
            if (n.IndexOf("game") >= 0 || n.IndexOf("steam") >= 0) return "play";
            return "process";
        }

        // ==============================================================
        // 一键释放
        // ==============================================================

        private void OnReleaseClick(object sender, EventArgs e)
        {
            if (_releasing) return;
            _releasing = true;
            _hero.Action.Enabled = false;
            SetSubtitle("正在释放内存，请稍候…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                long before = 0;
                long after = 0;
                int touched = 0;
                string error = null;
                try
                {
                    touched = ProcManager.ReleaseMemory(out before, out after);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    _releasing = false;
                    _hero.Action.Enabled = true;

                    if (error != null)
                    {
                        SetSubtitle("释放失败：" + error, Theme.Danger);
                        return;
                    }

                    double freedMb = (after - before) / 1048576.0;
                    SetSubtitle(freedMb > 0
                        ? "已释放 " + freedMb.ToString("0.0") + " MB（处理 " + touched + " 个进程）。"
                        : "已整理 " + touched + " 个进程的工作集，当前没有可回收的额外内存。",
                        Theme.Success);

                    // 释放是"看得见结果"的操作：把释放量与下一步一起播报
                    Toast(freedMb > 0 ? "已释放 " + freedMb.ToString("0.0") + " MB" : "内存已整理",
                        "处理了 " + touched + " 个进程的工作集，占用量会被系统按需重新填充。",
                        freedMb > 0 ? ToastKind.Success : ToastKind.Info);
                    Load();
                });
            });
        }    }
}
