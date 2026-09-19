/* ============================================================
 * 文件说明：修复中心页。按设计 repair-center 的"诊断 → 修复"闭环组织：
 *           诊断舱（健康分圆环 + 扫描项 / 已检出 / 已修复）+ 分类修复表 +
 *           单项修复 / 修复全部。这是全工具从"只看只扫"走向"能修"的收口页。
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
    public sealed class RepairView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly HeroCard _hero = new HeroCard();

        private readonly SectionTitle _secList = new SectionTitle();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly ProgressStrip _progress = new ProgressStrip();

        private readonly List<RepairItem> _items = new List<RepairItem>();

        private bool _busy;
        private bool _scanned;
        private int _fixedCount;
        /// <summary>只看检出项开关：开启后隐藏「正常」状态行，聚焦需要关注的项。</summary>
        private bool _onlyDetected;
        /// <summary>用户点过取消（由扫描线程读取，故为 volatile）。</summary>
        private volatile bool _cancelRequested;

        public RepairView()
            : base("修复中心", "一键扫描系统潜在故障 · 解释原因 · 安全修复")
        {
            _notice.NoticeIcon = "shield";
            _notice.NoticeAccent = Theme.Success;
            _notice.NoticeText = "修复只做安全动作：清缓存、刷新 DNS、关闭异常代理、应用推荐优化；"
                + "不删个人文件、不改驱动。驱动类问题只报告原因，由你决定是否更新驱动。";

            _hero.TagIcon = "shield";
            _hero.TagText = "一键诊断";
            _hero.Headline = "让电脑回到健康状态";
            _hero.SubText = "尚未扫描——点右侧按钮开始。";
            _hero.Gauge.CenterUnit = "分";
            _hero.Gauge.CenterText = "--";
            _hero.Gauge.StateText = "未扫描";
            _hero.Gauge.Tone = Theme.Accent;
            _hero.Action.Text = "一键诊断";
            _hero.Action.IconKind = "shield";
            _hero.Action.Click += delegate { Scan(); };
            _hero.SetStats(
                new string[] { "扫描项目", "已检出", "已修复" },
                new string[] { "--", "--", "0" });

            _secList.TitleText = "诊断结果";
            _secList.HintText = "检出项可单项修复，也可一键全部修复";
            _secList.Tone = Theme.Warning;

            _grid.Columns.Add("c0", "状态");
            _grid.Columns.Add("c1", "分类");
            _grid.Columns.Add("c2", "项目");
            _grid.Columns.Add("c3", "说明");
            _grid.Columns.Add("c4", "预计耗时");
            _grid.Columns[0].Width = 96;
            _grid.Columns[1].Width = 120;
            _grid.Columns[2].Width = 200;
            _grid.Columns[3].Width = 380;
            _grid.Columns[4].Width = 170;
            // 不设 Dock：表格靠行布局定宽，Dock 会脱离行布局并压住其后的区块
            _grid.Height = 300;
            _grid.ClearSelection();

            _progress.Visible = false;
            _progress.Cancelled += delegate
            {
                _cancelRequested = true;
                SetSubtitle("正在取消，已完成的检查会保留…", Theme.Warning);
            };

            AddFull(_notice, 34, 12);
            AddFull(_hero, 212, Theme.GapSection);
            AddFull(_progress, 46, Theme.GapTight);
            AddFull(_secList, 44, 0);
            AddFull(_grid, 300, 0);

            // 双击结果行 = 查看详情（四段式解释：问题是什么 / 为什么 / 怎么修 / 风险与可逆性）
            _grid.DoubleClick += delegate { ShowDetail(); };

            AddAction("查看详情", "info", ButtonVariant.Secondary, delegate { ShowDetail(); }, 116);
            AddAction("修复全部", "check", ButtonVariant.Primary, delegate { FixAll(); }, 116);
            AddAction("修复选中项", "tune", ButtonVariant.Secondary, delegate { FixSelected(); }, 128);
            AddAction("只看检出项", "list", ButtonVariant.Secondary, delegate { ToggleOnlyDetected(); }, 120);
            AddAction("导出报告", "copy", ButtonVariant.Secondary, delegate { ExportReport(); }, 116);
            AddAction("重新扫描", "refresh", ButtonVariant.Secondary, delegate { Scan(); }, 112);
        }

        public override void OnActivated()
        {
            if (!_scanned) Scan();
        }

        // ==============================================================
        // 扫描
        // ==============================================================

        private void Scan()
        {
            if (_busy) return;
            _busy = true;
            _cancelRequested = false;
            SetSubtitle("正在扫描……", Theme.Warning);
            _progress.Begin("正在扫描", new string[] { "系统组件", "网络与连接", "驱动与设备", "优化项校验" }, true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<RepairItem> result = null;
                string error = null;
                bool wasCancelled = false;
                try
                {
                    result = RepairCenter.Scan(delegate (string phase, int percent)
                    {
                        Post(delegate { _progress.SetPhaseByName(phase, percent); });
                    }, delegate { return _cancelRequested; }, out wasCancelled);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    _busy = false;
                    _progress.Finish();
                    if (error != null)
                    {
                        SetSubtitle("扫描失败：" + error, Theme.Danger);
                        return;
                    }
                    if (wasCancelled)
                    {
                        // 取消不是失败：已完成的部分照常呈现，并明确说明"接下来怎么办"
                        _scanned = true;
                        Render(result);
                        SetSubtitle("已取消扫描，以下是已完成的检查结果。", Theme.TextSecondary);
                        Toast("扫描已取消", "已完成的 " + _items.Count + " 项结果保留，可随时重新扫描。",
                            ToastKind.Info);
                        return;
                    }
                    _scanned = true;
                    _fixedCount = 0;
                    Render(result);
                    int detected = CountDetected();
                    int score = RepairCenter.Score(_items);
                    SetSubtitle(detected == 0
                        ? "扫描完成：未发现需要处理的问题。"
                        : "扫描完成：检出 " + detected + " 项，可单项或一键修复。",
                        detected == 0 ? Theme.Success : Theme.Warning);

                    // 结果用浮层播报（副标题在页头，扫完可能已经被别的提示覆盖）
                    Toast(detected == 0 ? "诊断完成：系统状态良好" : "诊断完成：检出 " + detected + " 项",
                        detected == 0 ? "健康分 " + score + " 分，本次无需修复。"
                                      : "健康分 " + score + " 分，可点「修复全部」一次处理。",
                        detected == 0 ? ToastKind.Success : ToastKind.Warning);
                });
            });
        }

        private int CountDetected()
        {
            int n = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Detected) n++;
            }
            return n;
        }

        private void Render(List<RepairItem> items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);

            int score = RepairCenter.Score(_items);

            _hero.Gauge.Percent = score;
            _hero.Gauge.CenterText = score.ToString();
            _hero.Gauge.Tone = score >= 90 ? Theme.Success : (score >= 70 ? Theme.Warning : Theme.Danger);
            _hero.Gauge.StateText = score >= 90 ? "状态良好" : (score >= 70 ? "需要维护" : "建议修复");
            _hero.SubText = "本次会话已扫描 " + _items.Count + " 个项目，健康分由检出项加权得出。";
            _hero.SetStats(
                new string[] { "扫描项目", "已检出", "已修复" },
                new string[] { _items.Count.ToString(), CountDetected().ToString(), _fixedCount.ToString() });

            FillGrid();

            _secList.RightText = "共 " + _items.Count + " 项";
        }

        private void FillGrid()
        {
            _grid.Rows.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                RepairItem it = _items[i];
                string state;
                if (it.Fixed) state = "已修复";
                else if (it.Detected) state = it.Fixable ? "检出" : "仅提示";
                else state = "正常";

                int idx = _grid.Rows.Add(state, it.Category, it.Title, it.Detail,
                    it.Detected && it.Fixable ? it.TimeText : "");
                _grid.Rows[idx].Tag = it;

                Color tone;
                if (it.Fixed) tone = Theme.Success;
                else if (it.Detected) tone = it.Fixable ? Theme.Warning : Theme.TextSecondary;
                else tone = Theme.TextMuted;

                _grid.Rows[idx].Cells[0].Style.ForeColor = tone;
                if (!it.Detected && !it.Fixed) _grid.Rows[idx].Cells[2].Style.ForeColor = Theme.TextMuted;
                if (_onlyDetected && !it.Detected && !it.Fixed) _grid.Rows[idx].Visible = false;
            }
            _grid.ClearSelection();
        }

        // ==============================================================
        // 修复
        // ==============================================================

        private void FixAll()
        {
            if (_busy) return;

            List<RepairItem> todo = new List<RepairItem>();
            for (int i = 0; i < _items.Count; i++)
            {
                RepairItem it = _items[i];
                if (it.Detected && it.Fixable && !it.Fixed) todo.Add(it);
            }
            if (todo.Count == 0)
            {
                SetSubtitle("当前没有可自动修复的检出项。", Theme.TextSecondary);
                return;
            }

            _busy = true;
            SetSubtitle("正在修复 " + todo.Count + " 项，请稍候…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> messages = new List<string>();
                for (int i = 0; i < todo.Count; i++)
                {
                    RepairItem it = todo[i];
                    string msg = RepairCenter.Fix(it);
                    messages.Add(it.Title + "：" + msg);
                }

                Post(delegate
                {
                    _busy = false;
                    int ok = 0;
                    for (int i = 0; i < todo.Count; i++)
                    {
                        if (todo[i].Fixed) ok++;
                    }
                    _fixedCount += ok;

                    // 本地更新：不重跑一次几秒的完整扫描
                    Render(_items);
                    SetSubtitle("修复完成：" + ok + " / " + todo.Count + " 项已处理。"
                        + (messages.Count > 0 ? "（" + messages[0] + "）" : ""),
                        ok > 0 ? Theme.Success : Theme.Warning);

                    // 部分成功必须与全部成功区分语气：用户要知道"还有没处理的"
                    if (ok == todo.Count)
                    {
                        Toast("修复完成：" + ok + " 项已处理", "健康分已刷新，可点「重新扫描」复核。", ToastKind.Success);
                    }
                    else
                    {
                        Toast("部分修复成功：" + ok + " / " + todo.Count + " 项",
                            "未成功的项多为文件被占用，稍后重试即可。", ToastKind.Warning);
                    }
                });
            });
        }

        private void FixSelected()
        {
            if (_busy) return;
            if (_grid.SelectedRows.Count == 0)
            {
                SetSubtitle("请先在下方表格里选中一项（按行首空白处即可选中）。", Theme.TextSecondary);
                return;
            }

            RepairItem it = _grid.SelectedRows[0].Tag as RepairItem;
            if (it == null) return;
            if (!it.Detected || it.Fixed)
            {
                SetSubtitle("「" + it.Title + "」当前没有需要修复的问题。", Theme.TextSecondary);
                return;
            }
            if (!it.Fixable)
            {
                SetSubtitle("「" + it.Title + "」需要手动处理：" + it.Detail, Theme.Warning);
                return;
            }

            _busy = true;
            SetSubtitle("正在修复「" + it.Title + "」…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string msg = RepairCenter.Fix(it);
                Post(delegate
                {
                    _busy = false;
                    if (it.Fixed) _fixedCount++;
                    Render(_items);
                    SetSubtitle(msg, it.Fixed ? Theme.Success : Theme.Danger);
                    Toast(it.Fixed ? "已修复：" + it.Title : "修复失败：" + it.Title, msg,
                        it.Fixed ? ToastKind.Success : ToastKind.Danger);
                });
            });
        }

        /// <summary>查看选中项的详情：四段式解释（问题 / 原因 / 处理方式 / 风险与可逆性）。</summary>
        private void ShowDetail()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                SetSubtitle("请先在下方表格里选中一项，再查看详情。", Theme.TextSecondary);
                return;
            }

            RepairItem it = _grid.SelectedRows[0].Tag as RepairItem;
            if (it == null) return;

            DetailInfo d = new DetailInfo();
            d.Title = it.Title;
            d.Icon = it.Fixable ? "tune" : "warn";
            d.Accent = it.Detected ? (it.Fixable ? Theme.Warning : Theme.TextSecondary) : Theme.Success;
            d.What = it.Detail;
            d.Why = "该项属于「" + it.Category + "」检查，由本工具读取系统实际状态后判定，不是固定文案。";
            d.How = it.Fixable
                ? "点「" + (string.IsNullOrEmpty(it.FixLabel) ? "修复" : it.FixLabel) + "」由本工具自动处理；预计耗时 " +
                  (string.IsNullOrEmpty(it.TimeText) ? "几秒" : it.TimeText) + "。"
                : "本工具不代改此项（涉及系统或驱动决策），请按上面的说明手动处理。";
            d.Risk = it.Fixable
                ? "修复只做安全动作：清缓存、刷新解析、关闭异常代理、应用推荐优化；不删个人文件、不改驱动、不结束进程。"
                : "只报告、不改动，因此没有额外风险。";
            d.Reversible = "可撤销：清理类删除的是缓存（系统会按需重建），代理与优化项类改动可随时改回。";
            Dialog.Detail(this, d);
        }

        /// <summary>切换「只看检出项」：开启后仅保留检出 / 已修复行，隐藏正常项，减少干扰。</summary>
        private void ToggleOnlyDetected()
        {
            _onlyDetected = !_onlyDetected;
            FillGrid();
            SetSubtitle(_onlyDetected ? "已仅显示检出项。" : "已显示全部项。", Theme.TextSecondary);
        }

        /// <summary>把本次诊断结果整理成纯文本报告并复制到剪贴板，便于粘贴反馈或留存。</summary>
        private void ExportReport()
        {
            if (_items.Count == 0)
            {
                SetSubtitle("暂无报告可导出，请先扫描。", Theme.TextSecondary);
                return;
            }
            int score = RepairCenter.Score(_items);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("古月工具包 · 修复中心诊断报告");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("健康分：" + score + " 分");
            sb.AppendLine("扫描项目：" + _items.Count + "  已检出：" + CountDetected() + "  已修复：" + _fixedCount);
            sb.AppendLine("--------------------------------------------------");
            for (int i = 0; i < _items.Count; i++)
            {
                RepairItem it = _items[i];
                string state = it.Fixed ? "已修复" : (it.Detected ? (it.Fixable ? "检出" : "仅提示") : "正常");
                sb.AppendLine("[" + state + "] " + it.Category + " / " + it.Title);
                if (!string.IsNullOrEmpty(it.Detail)) sb.AppendLine("    说明：" + it.Detail);
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                SetSubtitle("诊断报告已复制到剪贴板。", Theme.Success);
                Toast("报告已复制", "共 " + _items.Count + " 项，可粘贴到记事本或反馈给开发者。", ToastKind.Success);
            }
            catch
            {
                Dialog.Error(this, "导出失败", "无法访问剪贴板。");
            }
        }    }
}
