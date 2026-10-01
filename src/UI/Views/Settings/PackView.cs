/* UI/Views/System/PackView.cs — 优化包管理页：外部优化包（packs\*.json）的装载状态、问题与投放位置。 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using Microsoft.Win32;

namespace GuyueBox.UI.Views
{
    public sealed class PackView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatStrip _summary = new StatStrip();

        private readonly SectionTitle _secPacks = new SectionTitle();
        private readonly InfoList _packs = new InfoList();

        private readonly SectionTitle _secProblems = new SectionTitle();
        private readonly InfoList _problems = new InfoList();

        private readonly SectionTitle _secItems = new SectionTitle();
        private readonly InfoList _items = new InfoList();

        private readonly SectionTitle _secDirs = new SectionTitle();
        private readonly InfoList _dirs = new InfoList();

        private readonly AccentButton _openUser = new AccentButton();
        private readonly AccentButton _openApp = new AccentButton();
        private readonly AccentButton _reload = new AccentButton();
        private readonly AccentButton _import = new AccentButton();
        private readonly AccentButton _export = new AccentButton();
        private readonly AccentButton _sample = new AccentButton();
        private readonly AccentButton _online = new AccentButton();

        /// <summary>最近一次装载到的扩展项（导出清单时据此生成 JSON）。</summary>
        private readonly List<ITweak> _lastItems = new List<ITweak>();

        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }

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

            _secPacks.TitleText = "已装载的包";
            _secPacks.HintText = "每个清单文件的装载结果";
            _secPacks.Tone = Theme.Accent;
            _packs.Caption = "已装载的包";
            _packs.IconKind = "apps";
            _packs.CaptionColor = Theme.Accent;
            _packs.EmptyText = "没有装载任何外部包。点「导入清单…」选择 JSON，或「生成示例包」了解格式。";

            BuildButton(_import, "导入清单…", "plus", delegate { ImportPack(); });
            BuildButton(_export, "导出清单…", "doc", delegate { ExportPack(); });
            BuildButton(_sample, "生成示例包", "feature", delegate { MakeSample(); });
            BuildButton(_online, "获取更多包", "network", delegate { OpenOnline(); });

            BuildButton(_openUser, "打开用户目录", "folder", delegate { OpenFolder(TweakPackProvider.UserPackFolder()); });
            BuildButton(_openApp, "打开程序目录", "folder", delegate { OpenFolder(TweakPackProvider.AppPackFolder()); });
            BuildButton(_reload, "重新装载", "refresh", delegate { Reload(); });

            AddFull(_notice, 34, 12);
            AddFull(_summary, 108, Theme.GapTight);

            FlowLayoutPanel packRow = MakeRowFixed(34, 10);
            packRow.Controls.Add(_import);
            packRow.Controls.Add(_export);
            packRow.Controls.Add(_sample);
            packRow.Controls.Add(_online);
            AddRow(packRow);

            FlowLayoutPanel dirRow = MakeRowFixed(34, Theme.GapSection);
            dirRow.Controls.Add(_openUser);
            dirRow.Controls.Add(_openApp);
            dirRow.Controls.Add(_reload);
            AddRow(dirRow);

            // 卡片高度按最大行数在挂载前定稿（行布局要求挂载前定稿）；
            // 超出的记录在回填时汇总成一行，不靠卡片长高来容纳
            AddFull(_secPacks, 44, 0);
            AddFull(_packs, InfoList.HeightFor(4), Theme.GapSection);
            AddFull(_secProblems, 44, 0);
            AddFull(_problems, InfoList.HeightFor(7), Theme.GapSection);
            AddFull(_secItems, 44, 0);
            AddFull(_items, InfoList.HeightFor(8), Theme.GapSection);
            AddFull(_secDirs, 44, 0);
            AddFull(_dirs, InfoList.HeightFor(2), 0);
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

                    int pc = problems == null ? 0 : problems.Count;
                    int ic = items == null ? 0 : items.Count;

                    // 扫描自身失败（items 为 null）必须如实报错：旧代码一律写"扫描完成"并给成功色，会误导读用户
                    if (items == null)
                    {
                        string reason = pc > 0 ? problems[0] : "未知原因";
                        SetSubtitle("扫描优化包失败：" + reason, Theme.Danger);
                        Toast("优化包扫描失败", reason, ToastKind.Danger);
                        return;
                    }

                    SetSubtitle(pc == 0 ? "扫描完成，未发现问题。" : "扫描完成：" + pc + " 条问题需要处理。",
                        pc == 0 ? Theme.Success : Theme.Warning);
                    Toast(pc == 0 ? "优化包扫描完成：未发现问题" : "优化包扫描完成：" + pc + " 条问题",
                        "扩展优化项 " + ic + " 项。",
                        pc == 0 ? ToastKind.Success : ToastKind.Warning);
                });
            });
        }

        private void Render(List<ITweak> items, List<string> problems, string appDir, string userDir)
        {
            // 目录探测失败时 TweakPackProvider 返回 null（程序目录不可写等）：先归一化，
            // 否则下面 appDir.Length 会在 UI 线程抛空引用，中断整页渲染
            if (appDir == null) appDir = "";
            if (userDir == null) userDir = "";

            // 供「导出清单…」使用（导出的是当前这次扫描到的项，与界面一致）
            _lastItems.Clear();
            if (items != null) _lastItems.AddRange(items);

            int itemCount = items == null ? 0 : items.Count;
            int problemCount = problems == null ? 0 : problems.Count;

            _summary.Clear();
            _summary.Add("扩展优化项", itemCount.ToString());
            _summary.Add("装载问题", problemCount.ToString(),
                problemCount > 0 ? Theme.Warning : Theme.TextPrimary);

            // ---- 已装载的包（最多 4 行，其余汇总）----
            _packs.Clear();
            List<string[]> packs = new List<string[]>(TweakPackProvider.LoadedPacks);
            int pshown = packs.Count < 4 ? packs.Count : 4;
            for (int i = 0; i < pshown; i++)
            {
                _packs.Add(packs[i][0], packs[i][1] + " · " + packs[i][2] + " 项");
            }
            if (packs.Count > pshown) _packs.Add("其他", "另有 " + (packs.Count - pshown) + " 个包未列出");
            _packs.Invalidate();

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

            // 与实际可用目录数一致（原先硬编码「2 处」，目录不可用时会对不上）
            _summary.Add("扫描目录", ((appDir.Length > 0 ? 1 : 0) + (userDir.Length > 0 ? 1 : 0)) + " 处");
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Shell.OpenPath(path);
                SetSubtitle("已在资源管理器中打开：" + path, Theme.Success);
                Toast("已打开目录", path, ToastKind.Success);
            }
            catch (Exception ex)
            {
                SetSubtitle("打开目录失败：" + ex.Message, Theme.Danger);
            }
        }

        // ==============================================================
        // 导入 / 导出 / 示例包 / 在线清单
        // ==============================================================

        /// <summary>把外部 JSON 清单导入用户 packs 目录，然后重新装载让问题在这里统一暴露。</summary>
        private void ImportPack()
        {
            string dir = TweakPackProvider.UserPackFolder();
            if (string.IsNullOrEmpty(dir))
            {
                Dialog.Error(this, "无法导入",
                    "用户 packs 目录不可用（%LOCALAPPDATA% 读取失败或被安全软件拦截）。");
                return;
            }

            string file = null;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择优化包清单（JSON）";
                dlg.Filter = "优化包清单 (*.json)|*.json|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK) file = dlg.FileName;
            }
            if (string.IsNullOrEmpty(file)) return;

            // 只做"显然是别的文件"的轻量拦截；格式合法性交给装载器统一判定（避免两处规则漂移）
            try
            {
                string head = File.ReadAllText(file, Encoding.UTF8).TrimStart();
                if (head.Length == 0 || head[0] != '{')
                {
                    Dialog.Info(this, "不像是清单文件",
                        "选中的文件不是 JSON 对象（应以 { 开头）。请选择优化包清单文件，例如用「生成示例包」产出的文件。");
                    return;
                }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "读取失败", ex.Message);
                return;
            }

            string name = Path.GetFileName(file);
            string target = Path.Combine(dir, name);
            if (File.Exists(target) &&
                !Dialog.Confirm(this, "已存在同名清单",
                    "目标目录已有「" + name + "」，是否覆盖？\r\n\r\n" + target))
                return;

            try
            {
                File.Copy(file, target, true);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "导入失败", ex.Message);
                return;
            }

            SetSubtitle("已导入：" + target, Theme.Success);
            Toast("清单已导入", "重启程序后新增优化项会出现在「优化中心」。", ToastKind.Success);
            Reload();
        }

        /// <summary>把当前装载到的扩展项导出为一份 JSON 清单（可直接投放或分享）。</summary>
        private void ExportPack()
        {
            if (_lastItems.Count == 0)
            {
                Dialog.Info(this, "没有可导出的内容",
                    "当前没有被外部包装载的优化项。\r\n\r\n可以先点「生成示例包」了解清单格式。");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"pack\": \"古月工具箱导出\",");
            sb.AppendLine("  \"version\": \"" + MainForm.AppVersion + "\",");
            sb.AppendLine("  \"enabled\": true,");
            sb.AppendLine("  \"items\": [");

            int written = 0;
            int skipped = 0;
            StringBuilder items = new StringBuilder();
            for (int i = 0; i < _lastItems.Count; i++)
            {
                RegTweak rt = _lastItems[i] as RegTweak;
                string itemJson = ItemJson(rt, ref skipped);
                if (itemJson == null) continue;
                if (written > 0) items.AppendLine(",");
                items.Append(itemJson);
                written++;
            }
            sb.Append(items);
            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            if (written == 0)
            {
                Dialog.Info(this, "无法导出",
                    "当前项没有可导出的注册表写入（二进制写入暂不支持导出）。");
                return;
            }

            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "GuyueBox-packs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                SetSubtitle("已导出 " + written + " 项：" + path, Theme.Success);
                Toast("清单已导出",
                    skipped > 0 ? ("另有 " + skipped + " 项/条不支持导出，已跳过。") : "可直接投放进 packs 目录或分享。",
                    ToastKind.Success);
                Shell.OpenSelect(path);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "导出失败", ex.Message);
            }
        }

        /// <summary>把一个优化项序列化为 JSON（无有效写入时返回 null，并累加跳过计数）。</summary>
        private static string ItemJson(RegTweak rt, ref int skipped)
        {
            if (rt == null || rt.Enable == null || rt.Enable.Count == 0)
            {
                if (rt != null) skipped++;
                return null;
            }

            StringBuilder writes = new StringBuilder();
            int n = 0;
            for (int k = 0; k < rt.Enable.Count; k++)
            {
                string w = WriteJson(rt.Enable[k]);
                if (w == null) { skipped++; continue; }
                if (n > 0) writes.AppendLine(",");
                writes.Append(w);
                n++;
            }
            if (n == 0) return null;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("    {");
            sb.AppendLine("      \"id\": \"" + Escape(rt.Id) + "\",");
            sb.AppendLine("      \"group\": \"" + Escape(rt.Group) + "\",");
            sb.AppendLine("      \"name\": \"" + Escape(rt.Name) + "\",");
            sb.AppendLine("      \"desc\": \"" + Escape(rt.Description) + "\",");
            sb.AppendLine("      \"admin\": " + (rt.AdminOnly ? "true" : "false") + ",");
            sb.AppendLine("      \"risky\": " + (rt.Risky ? "true" : "false") + ",");
            sb.AppendLine("      \"recommended\": " + (rt.Recommended ? "true" : "false") + ",");
            sb.AppendLine("      \"writes\": [");
            sb.AppendLine(writes.ToString());
            sb.AppendLine("      ]");
            sb.Append("    }");
            return sb.ToString();
        }

        /// <summary>把一条写入序列化为 JSON（不支持的返回 null，由调用方计数跳过）。</summary>
        private static string WriteJson(RegWrite w)
        {
            if (w == null || string.IsNullOrEmpty(w.Path) || string.IsNullOrEmpty(w.Name)) return null;
            string hive = w.Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
            string head = "        { \"hive\": \"" + hive + "\", \"path\": \"" + Escape(w.Path) +
                "\", \"name\": \"" + Escape(w.Name) + "\", ";

            if (w.Delete)
            {
                return head + "\"kind\": \"delete\" }";
            }
            if (w.Kind == RegistryValueKind.DWord)
            {
                int v;
                try { v = Convert.ToInt32(w.Value); }
                catch { return null; }
                return head + "\"kind\": \"dword\", \"value\": " + v + " }";
            }
            if (w.Kind == RegistryValueKind.QWord)
            {
                long v;
                try { v = Convert.ToInt64(w.Value); }
                catch { return null; }
                return head + "\"kind\": \"qword\", \"value\": " + v + " }";
            }
            if (w.Kind == RegistryValueKind.MultiString)
            {
                string[] arr = w.Value as string[];
                if (arr == null) return null;
                StringBuilder sb = new StringBuilder();
                sb.Append(head).Append("\"kind\": \"multistring\", \"value\": [");
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("\"").Append(Escape(arr[i])).Append("\"");
                }
                sb.Append("] }");
                return sb.ToString();
            }
            if (w.Kind == RegistryValueKind.ExpandString)
            {
                return head + "\"kind\": \"expand\", \"value\": \"" + Escape(Convert.ToString(w.Value)) + "\" }";
            }
            if (w.Kind == RegistryValueKind.String)
            {
                return head + "\"kind\": \"string\", \"value\": \"" + Escape(Convert.ToString(w.Value)) + "\" }";
            }
            return null;   // Binary 等：暂不支持导出（会在提示里计数）
        }

        /// <summary>在用户 packs 目录生成示例清单（enabled=false，装载时会被标注为"包已禁用"）。</summary>
        private void MakeSample()
        {
            string dir = TweakPackProvider.UserPackFolder();
            if (string.IsNullOrEmpty(dir))
            {
                Dialog.Error(this, "无法生成", "用户 packs 目录不可用（%LOCALAPPDATA% 读取失败）。");
                return;
            }

            string path = Path.Combine(dir, "example.json");
            if (File.Exists(path) &&
                !Dialog.Confirm(this, "示例包已存在", "覆盖 " + path + " ？")) return;

            try
            {
                File.WriteAllText(path, SampleJson(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "写入失败", ex.Message);
                return;
            }

            SetSubtitle("示例包已生成：" + path, Theme.Success);
            Dialog.Output(this, "示例包已生成",
                "文件：" + path + "\r\n\r\n"
                + "· 默认 enabled=false：装载时会提示「包已禁用」，不会真的生效；\r\n"
                + "· 把 \"enabled\" 改成 true 并重启程序，包内优化项即出现在「优化中心 · 扩展优化包」；\r\n"
                + "· 需要管理员权限的写入，在条目里加 \"admin\": true；\r\n"
                + "· kind 支持 dword / qword / string / expand / multistring / delete。");
            Reload();
        }

        private static string SampleJson()
        {
            return
                "{\r\n"
                + "  \"pack\": \"示例包（默认禁用）\",\r\n"
                + "  \"version\": \"1.0\",\r\n"
                + "  \"author\": \"你的名字\",\r\n"
                + "  \"enabled\": false,\r\n"
                + "  \"items\": [\r\n"
                + "    {\r\n"
                + "      \"id\": \"pack_demo_search_suggest\",\r\n"
                + "      \"group\": \"扩展优化包\",\r\n"
                + "      \"name\": \"示例项：关闭搜索框网页建议\",\r\n"
                + "      \"desc\": \"示例条目：把 Windows 搜索框的网页建议写入用户策略键。把 enabled 改成 true 并重启程序后，可在优化中心看到它。\",\r\n"
                + "      \"admin\": false,\r\n"
                + "      \"risky\": false,\r\n"
                + "      \"recommended\": false,\r\n"
                + "      \"writes\": [\r\n"
                + "        { \"hive\": \"HKCU\", \"path\": \"Software\\\\Policies\\\\Microsoft\\\\Windows\\\\Explorer\", \"name\": \"DisableSearchBoxSuggestions\", \"kind\": \"dword\", \"value\": 1 }\r\n"
                + "      ]\r\n"
                + "    }\r\n"
                + "  ]\r\n"
                + "}\r\n";
        }

        /// <summary>打开项目主页（社区清单/模板见仓库 packs 目录）。</summary>
        private void OpenOnline()
        {
            if (!UpdateChecker.ProjectConfigured)
            {
                Dialog.Info(this, "未配置项目主页",
                    "项目主页地址尚未配置，无法打开在线清单入口。可先用「生成示例包」了解清单格式。");
                return;
            }
            try
            {
                Shell.OpenUrl(UpdateChecker.ProjectUrl);
                SetSubtitle("已在浏览器打开项目主页（社区清单见仓库 packs 目录）。", Theme.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "无法打开", ex.Message);
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
