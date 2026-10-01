﻿/* ============================================================
 * 文件说明：修复中心页。按设计 repair-center 的"诊断 → 修复"闭环组织：
 *           诊断舱（健康分圆环 + 扫描项 / 已检出 / 已修复）+ 分类修复表 +
 *           单项修复 / 修复全部。这是全工具从"只看只扫"走向"能修"的收口页。
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
    public sealed class RepairView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly HeroCard _hero = new HeroCard();

        private readonly SectionTitle _secList = new SectionTitle();
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly ProgressStrip _progress = new ProgressStrip();

        private readonly List<RepairItem> _items = new List<RepairItem>();

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
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
            AddAction("导出报告", "doc", ButtonVariant.Secondary, delegate { ExportReport(); }, 116);
            AddAction("重新扫描", "search", ButtonVariant.Secondary, delegate { Scan(); }, 112);
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
            // 扫描期间同步 hero 状态（否则整段扫描中主卡片仍写"尚未扫描"，观感像没开始）
            _hero.SubText = "正在检查系统组件、网络、驱动与优化项，请稍候……";
            _hero.Gauge.StateText = "扫描中";
            _hero.Gauge.Percent = 0;
            _hero.Gauge.Tone = Theme.Warning;
            _hero.Action.Enabled = false;
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
                    _hero.Action.Enabled = true; // 扫描期间禁用的诊断按钮恢复
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
            return CountDetected(_items);
        }

        /// <summary>指定清单里的检出项数（修复后的复检结果也用它）。</summary>
        private static int CountDetected(List<RepairItem> items)
        {
            int n = 0;
            if (items == null) return 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Detected) n++;
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
                // 修复前的检出清单（按 Id）：复检时用它判断"这项原本有问题、现在还检出吗"
                List<string> targets = new List<string>();
                for (int i = 0; i < todo.Count; i++) targets.Add(todo[i].Id);

                List<string> messages = new List<string>();
                for (int i = 0; i < todo.Count; i++)
                {
                    RepairItem it = todo[i];
                    string msg = RepairCenter.Fix(it);
                    messages.Add(it.Title + "：" + msg);
                }

                // 复检：重新跑一遍全部检查。只有"原来检出、复检不再检出"才算真修好——
                // 早期实现是修完就把状态改成已修复，于是分数立刻 100 而问题还在。
                bool cancelled = false;
                List<RepairItem> again = RepairCenter.Scan(null, null, out cancelled);

                Post(delegate
                {
                    _busy = false;
                    int ok = 0;
                    for (int i = 0; i < again.Count; i++)
                    {
                        RepairItem it = again[i];
                        if (targets.Contains(it.Id) && !it.Detected)
                        {
                            it.Fixed = true;
                            ok++;
                        }
                    }
                    _fixedCount += ok;

                    int still = CountDetected(again);
                    Render(again);   // 用复检结果刷新表格与健康分（分数来自真实检出）
                    SetSubtitle("修复完成：" + ok + " 项已恢复正常，复检仍检出 " + still + " 项。"
                        + (messages.Count > 0 ? "（" + messages[0] + "）" : ""),
                        ok > 0 ? Theme.Success : Theme.Warning);

                    if (ok == targets.Count && still == 0)
                    {
                        Toast("修复完成：" + ok + " 项已恢复正常", "复检未见同类问题。", ToastKind.Success);
                    }
                    else if (ok > 0)
                    {
                        Toast("已恢复 " + ok + " / " + targets.Count + " 项，复检仍检出 " + still + " 项",
                            "剩下的是文件被占用或需手动处理的问题，详情见表格。", ToastKind.Warning);
                    }
                    else
                    {
                        Toast("本次没有实际改善", "动作已执行但复检仍检出，多为文件被占用或需手动处理。", ToastKind.Warning);
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
                string id = it.Id;
                string msg = RepairCenter.Fix(it);

                // 复检该项：还检出就如实说"仍未解决"，不冒充已修复
                bool cancelled = false;
                List<RepairItem> again = RepairCenter.Scan(null, null, out cancelled);
                bool ok = false;
                for (int i = 0; i < again.Count; i++)
                {
                    if (!string.Equals(again[i].Id, id, StringComparison.Ordinal)) continue;
                    ok = !again[i].Detected;
                    if (ok) again[i].Fixed = true;
                    break;
                }

                Post(delegate
                {
                    _busy = false;
                    if (ok) _fixedCount++;
                    Render(again);
                    SetSubtitle(ok ? ("已修复：" + msg) : ("仍未解决：" + msg), ok ? Theme.Success : Theme.Danger);
                    Toast(ok ? "已修复：" + it.Title : "仍未解决：" + it.Title, msg,
                        ok ? ToastKind.Success : ToastKind.Danger);
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
            sb.AppendLine("古月工具箱 · 修复中心诊断报告");
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
        }
    }
}
