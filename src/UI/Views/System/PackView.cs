/* ============================================================
 * 文件说明：优化包管理页。外部优化包（packs\*.json）的装载状态、装载问题与投放位置。
 *           装载机制本身早已存在，但此前没有任何界面能触达：
 *           包装载失败只会静默，用户也无从知道该把清单放到哪里。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class PackView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();

        private readonly SectionTitle _secProblems = new SectionTitle();
        private readonly InfoList _problems = new InfoList();

        private readonly SectionTitle _secItems = new SectionTitle();
        private readonly InfoList _items = new InfoList();

        private readonly SectionTitle _secDirs = new SectionTitle();
        private readonly InfoList _dirs = new InfoList();

        private readonly AccentButton _openUser = new AccentButton();
        private readonly AccentButton _openApp = new AccentButton();
        private readonly AccentButton _reload = new AccentButton();

        private bool _busy;

        public PackView()
            : base("优化包管理", "外部优化包（packs）：装载状态、问题与投放位置")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "把优化包清单（JSON）放进下面任一 packs 目录，点「重新装载」即可在此查看结果；"
                + "包内新增的优化项需要重启本程序后才会出现在「优化中心」。";

            _summary.Caption = "装载概览";
            _summary.IconKind = "apps";
            _summary.CaptionColor = Theme.Accent;

            _secProblems.TitleText = "装载问题";
            _secProblems.HintText = "跳过、禁用或格式不合法的记录";
            _secProblems.Tone = Theme.Warning;

            _problems.Caption = "装载问题";
            _problems.IconKind = "warn";
            _problems.CaptionColor = Theme.Warning;
            _problems.EmptyText = "没有装载问题——已发现的清单都装载成功。";
            _problems.EmptyActionText = "重新装载";
            _problems.EmptyAction += delegate { Reload(); };

            _secItems.TitleText = "扩展优化项";
            _secItems.HintText = "由外部包提供、已进入优化中心的项";
            _secItems.Tone = Theme.Success;

            _items.Caption = "扩展优化项";
            _items.IconKind = "tune";
            _items.CaptionColor = Theme.Success;
            _items.EmptyText = "当前没有任何外部包提供优化项。";
            // 空状态即入口：把"该怎么做"直接变成可点的动作
            _items.EmptyActionText = "打开投放目录";
            _items.EmptyAction += delegate { OpenFolder(TweakPackProvider.UserPackFolder()); };

            _secDirs.TitleText = "投放位置";
            _secDirs.HintText = "程序目录优先，其次用户目录";
            _secDirs.Tone = Theme.Cyan;

            _dirs.Caption = "扫描目录";
            _dirs.IconKind = "folder";
            _dirs.CaptionColor = Theme.Cyan;
            _dirs.EmptyText = "无法确定 packs 目录（读取程序路径失败）。";

            BuildButton(_openUser, "打开用户目录", "folder", delegate { OpenFolder(TweakPackProvider.UserPackFolder()); });
            BuildButton(_openApp, "打开程序目录", "folder", delegate { OpenFolder(TweakPackProvider.AppPackFolder()); });
            BuildButton(_reload, "重新装载", "refresh", delegate { Reload(); });

            AddFull(_notice, 34, 12);
            AddFull(_summary, 108, Theme.GapTight);

            FlowLayoutPanel btnRow = MakeRowFixed(34, Theme.GapSection);
            btnRow.Controls.Add(_openUser);
            btnRow.Controls.Add(_openApp);
            btnRow.Controls.Add(_reload);
            AddRow(btnRow);

            // 三张卡的高度按最大行数在挂载前定稿（行布局要求挂载前定稿）；
            // 超出的记录在回填时汇总成一行，不靠卡片长高来容纳
            AddFull(_secProblems, 44, 0);
            AddFull(_problems, InfoListHeight(7), Theme.GapSection);
            AddFull(_secItems, 44, 0);
            AddFull(_items, InfoListHeight(8), Theme.GapSection);
            AddFull(_secDirs, 44, 0);
            AddFull(_dirs, InfoListHeight(2), 0);
        }

        private static void BuildButton(AccentButton b, string text, string icon, EventHandler onClick)
        {
            b.Text = text;
            b.IconKind = icon;
            b.Variant = ButtonVariant.Secondary;
            b.Height = 30;
            b.NaturalWidth = 130;
            b.Click += onClick;
        }

        public override void OnActivated()
        {
            Reload();
        }

        // ==============================================================
        // 装载
        // ==============================================================

        private void Reload()
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在扫描优化包…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ITweak> items = null;
                List<string> problems = null;
                string appDir = "";
                string userDir = "";
                try
                {
                    // 构造即扫描：得到的是一份"当前磁盘状态"的独立快照，
                    // 与启动时注册进优化中心的那个实例无关。
                    TweakPackProvider provider = new TweakPackProvider();
                    items = new List<ITweak>(provider.Provide());
                    problems = new List<string>(TweakPackProvider.Problems);
                    appDir = TweakPackProvider.AppPackFolder();
                    userDir = TweakPackProvider.UserPackFolder();
                }
                catch (Exception ex)
                {
                    problems = new List<string>();
                    problems.Add("扫描失败：" + ex.Message);
                }

                Post(delegate
                {
                    _busy = false;
                    Render(items, problems, appDir, userDir);
                    SetSubtitle("扫描完成。", Theme.Success);

                    int pc = problems == null ? 0 : problems.Count;
                    int ic = items == null ? 0 : items.Count;
                    Toast(pc == 0 ? "优化包扫描完成：未发现问题" : "优化包扫描完成：" + pc + " 条问题",
                        "扩展优化项 " + ic + " 项。",
                        pc == 0 ? ToastKind.Success : ToastKind.Warning);
                });
            });
        }

        private void Render(List<ITweak> items, List<string> problems, string appDir, string userDir)
        {
            int itemCount = items == null ? 0 : items.Count;
            int problemCount = problems == null ? 0 : problems.Count;

            _summary.Clear();
            _summary.Add("扩展优化项", itemCount.ToString());
            _summary.Add("装载问题", problemCount.ToString(),
                problemCount > 0 ? Theme.Warning : Theme.TextPrimary);
            _summary.Add("扫描目录", "2 处");

            // ---- 装载问题（最多 7 行，其余汇总）----
            _problems.Clear();
            if (problems != null)
            {
                int shown = problems.Count < 7 ? problems.Count : 7;
                for (int i = 0; i < shown; i++)
                {
                    _problems.Add("问题 " + (i + 1), problems[i]);
                }
                if (problems.Count > shown)
                {
                    _problems.Add("其他", "另有 " + (problems.Count - shown) + " 条问题未列出");
                }
            }
            _problems.Invalidate();

            // ---- 扩展优化项（最多 8 行，其余汇总）----
            _items.Clear();
            if (items != null)
            {
                int shown = items.Count < 8 ? items.Count : 8;
                for (int i = 0; i < shown; i++)
                {
                    ITweak t = items[i];
                    _items.Add(t.Name, (string.IsNullOrEmpty(t.Group) ? TweakPackProvider.GPack : t.Group) + " · " + t.Id);
                }
                if (items.Count > shown)
                {
                    _items.Add("其他", "另有 " + (items.Count - shown) + " 项未列出");
                }
            }
            _items.Invalidate();

            // ---- 目录 ----
            _dirs.Clear();
            if (appDir.Length > 0) _dirs.Add("程序目录", appDir);
            if (userDir.Length > 0) _dirs.Add("用户目录", userDir);
            _dirs.Invalidate();
        }

        /// <summary>信息卡的定稿高度：标题 46 + 行高 25 × 行数 + 底部留白 12。</summary>
        private static int InfoListHeight(int rows)
        {
            return 46 + Math.Max(1, rows) * InfoList.RowHeight + 12;
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
                SetSubtitle("已在资源管理器中打开：" + path, Theme.Success);
                Toast("已打开目录", path, ToastKind.Success);
            }
            catch (Exception ex)
            {
                SetSubtitle("打开目录失败：" + ex.Message, Theme.Danger);
            }
        }    }
}
