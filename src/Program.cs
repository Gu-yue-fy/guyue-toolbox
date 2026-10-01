﻿/* ============================================================
 * 文件说明：程序入口：单实例互斥锁、全局异常捕获、DPI/清单初始化，启动 MainForm 消息循环。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI;

namespace GuyueBox
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // --selftest：优化项目录自检（只读，不启动界面），供构建脚本与人工核查。
            // 必须放在单实例互斥锁之前，否则已有一个实例在跑时自检会被挡住
            if (args != null && Array.IndexOf(args, "--selftest") >= 0)
            {
                Environment.Exit(RunSelfTest());
                return;
            }

            // --apply <方案.json> [--yes]：无人值守应用优化方案（不开界面，同样放在互斥锁之前）。
            // 不带 --yes = 试运行：只打印"将做什么"的计划，不写任何注册表键；
            // 谨慎项执行前强制过还原点闸门，创建失败即整体取消。
            if (args != null && Array.IndexOf(args, "--apply") >= 0)
            {
                int ai = Array.IndexOf(args, "--apply");
                string applyFile = (ai + 1 < args.Length && !args[ai + 1].StartsWith("--")) ? args[ai + 1] : null;
                bool applyYes = Array.IndexOf(args, "--yes") >= 0;
                Environment.Exit(RunApply(applyFile, applyYes));
                return;
            }

            // --shots [dir]：自动截图模式。启动后逐页离屏渲染保存 PNG，完成后自动退出。
            // 纯程序驱动（DrawToBitmap），不模拟任何鼠标/键盘输入；独立互斥体名，与已运行的
            // 正常实例互不干扰。
            string shotDir = null;
            string pageKey = null;
            string perfTourFile = null;
            if (args != null)
            {
                int si = Array.IndexOf(args, "--shots");
                if (si >= 0)
                {
                    shotDir = (si + 1 < args.Length && !args[si + 1].StartsWith("--"))
                        ? args[si + 1] : "shots";
                }

                // --page <key>：启动即打开指定页面（自动化验证用：如 --page optimize 后向窗口
                // 发滚轮消息，用来实测滚动路由，不必模拟鼠标点击导航）。
                int pi = Array.IndexOf(args, "--page");
                if (pi >= 0 && pi + 1 < args.Length && !args[pi + 1].StartsWith("--"))
                {
                    pageKey = args[pi + 1];
                }

                // --perf-tour [file]：遍历全部页面并记录每页构建/激活耗时（性能回归用，不截图）。
                // 缺省落盘到程序目录 perf.log。
                int fi = Array.IndexOf(args, "--perf-tour");
                if (fi >= 0)
                {
                    perfTourFile = (fi + 1 < args.Length && !args[fi + 1].StartsWith("--"))
                        ? args[fi + 1]
                        : Path.Combine(Application.StartupPath, "perf.log");
                }
            }

            // 巡检模式：一进 Main 就打开打点，冷启动各阶段才会被记进日志
            if (!string.IsNullOrEmpty(perfTourFile))
            {
                Perf.Enabled = true;
                Perf.Mark("startup.main");
            }

            // 测试模式：独立互斥体（可与在跑的正常实例共存），且不写回界面状态
            bool testMode = shotDir != null || pageKey != null || perfTourFile != null;

            // 后台任务里有很多"阻塞等外部进程"的活（PowerShell / powercfg / schtasks / WMI）：
            // 默认线程池线程数 = CPU 数，几个页面的检测同时排队时，后提交的任务会被饿住
            // （表现：页面长时间停在"正在处理…"）。这里抬高最小线程数，让新任务随到随开。
            try
            {
                int min = Math.Max(16, Environment.ProcessorCount * 2);
                ThreadPool.SetMinThreads(min, min);
            }
            catch
            {
            }

            // 全局异常兜底，避免出现 .NET 默认的崩溃对话框
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 多实例提示
            bool createdNew;
            // Local\ 前缀：避免多用户 / 远程桌面会话间的互斥体命名冲突
            using (Mutex mutex = new Mutex(true,
                testMode ? @"Local\GuyueBox.AutoShots" : @"Local\GuyueBox.SingleInstance",
                out createdNew))
            {
                if (!createdNew)
                {
                    if (testMode) Environment.Exit(0); // 测试模式：有并发实例在跑，直接放弃
                    MessageBox.Show(MainForm.AppName + " 已经在运行中。", MainForm.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // 装载外部优化包（<程序目录>\packs\*.json 与 %LOCALAPPDATA%\GuyueBox\packs\*.json），
                // 机制：清单声明（JSON）→ 运行时物化。
                // 必须在首次访问 TweakLibrary.All() 之前注册；装载失败不得影响主程序启动。
                try
                {
                    TweakLibrary.RegisterProvider(new TweakPackProvider());
                }
                catch
                {
                }

                // 优化项库首次构建约 2 秒（近 400 项 + 硬件适用性探测）：启动即后台预热，
                // 打开概览/优化中心时通常已就绪（双检锁保证与页面自己的预热不会重复构建）。
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { TweakLibrary.All(); }
                    catch { }
                });

                MainForm.AutoShotDir = shotDir;        // 非空时 OnShown 触发逐页截图并自动退出
                MainForm.PerfTourFile = perfTourFile;  // 非空时 OnShown 触发性能巡检并自动退出
                MainForm.StartPageKey = pageKey;       // 非空时启动即打开该页（自动化验证用）
                Application.Run(new MainForm());
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportException(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportException(e.ExceptionObject as Exception);
        }

        private static readonly System.Collections.Generic.List<string> Reported =
            new System.Collections.Generic.List<string>();

        private static int _reportCount;

        private static void ReportException(Exception ex)
        {
            if (ex == null) return;

            // 同一个错误只提示一次，避免绘制异常反复弹窗刷屏
            string key = ex.GetType().FullName + "|" + ex.Message + "|" + FirstFrame(ex);
            lock (Reported)
            {
                if (Reported.Contains(key)) return;
                Reported.Add(key);
                _reportCount++;
                if (_reportCount > 5) return;
            }

            string text = ex.Message + "\r\n\r\n" + ex.GetType().FullName + "\r\n" + ex.StackTrace;

            // 崩溃日志：写入 RegLog（可在「操作日志」查看），同时落盘 crashes.log 便于事后定位
            try { RegLog.Add("CRASH", "崩溃", ex.GetType().FullName + " | " + ex.Message); } catch { }
            try { WriteCrashFile(text); } catch { }

            try
            {
                Dialog.Error(null, "程序遇到问题", text);
            }
            catch
            {
                try
                {
                    MessageBox.Show(text, MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                }
            }
        }

        /// <summary>把崩溃信息追加到程序目录下的 crashes.log，便于进程退出后仍能事后定位。</summary>
        private static void WriteCrashFile(string text)
        {
            try
            {
                string file = Path.Combine(Application.StartupPath, "crashes.log");
                using (StreamWriter w = new StreamWriter(file, true, Encoding.UTF8))
                {
                    w.WriteLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                    w.WriteLine(text);
                    w.WriteLine();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 优化项目录自检：只读校验，结果写入程序目录 selftest.log，
        /// 退出码 0=通过 / 1=存在违规。不启动界面、不写注册表、无副作用。
        /// </summary>
        /// <summary>
        /// 无人值守应用优化方案（--apply）。
        /// 退出码：0=成功（或试运行完成）/ 1=存在失败或闸门取消 / 2=用法或文件错误。
        /// 安全设计：只启用方案里列出的项，**不碰**方案外已启用的项——界面"导入方案"是差分同步
        /// （方案外已启用的会被还原），无人值守场景下少动比多动安全，故语义收窄并在文档里写明；
        /// 谨慎项必须先过还原点闸门（RestorePoints.EnsureRecent），失败即整体取消、一个键都不写。
        /// </summary>
        private static int RunApply(string file, bool yes)
        {
            StringBuilder log = new StringBuilder();
            log.AppendLine("古月工具箱 · 无人值守应用方案");
            log.AppendLine("时间： " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            log.AppendLine("文件： " + (file ?? "(未指定)"));

            if (string.IsNullOrEmpty(file))
            {
                log.AppendLine("用法：GuyueBox.exe --apply <方案.stprofile> [--yes]");
                log.AppendLine("不带 --yes 时只打印计划，不修改系统。");
                FinishApplyLog(log);
                return 2;
            }

            string parseError;
            ProfileDocument doc = ProfileFile.Read(file, out parseError);
            if (doc == null)
            {
                log.AppendLine("文件无效： " + parseError);
                FinishApplyLog(log);
                return 2;
            }
            for (int i = 0; i < doc.Warnings.Count; i++) log.AppendLine("警告： " + doc.Warnings[i]);
            log.AppendLine("方案条目： " + doc.Ids.Count + " 项");

            // 外部优化包与有界面的实例走同一条装载路径（必须在首次 TweakLibrary.All() 之前注册）
            try { TweakLibrary.RegisterProvider(new TweakPackProvider()); }
            catch
            {
            }

            List<ITweak> library = TweakLibrary.All();
            Dictionary<string, ITweak> byId = new Dictionary<string, ITweak>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < library.Count; i++) byId[library[i].Id] = library[i];

            List<ITweak> toApply = new List<ITweak>();
            int unknown = 0, already = 0, blocked = 0;
            foreach (string id in doc.Ids)
            {
                ITweak t;
                if (!byId.TryGetValue(id, out t))
                {
                    unknown++;
                    log.AppendLine("  跳过（本库不存在）： " + id);
                    continue;
                }
                if (t.AdminOnly && !Native.IsElevated())
                {
                    blocked++;
                    log.AppendLine("  跳过（需要管理员权限）： " + id);
                    continue;
                }
                if (t.IsApplied())
                {
                    already++;
                    log.AppendLine("  已是目标状态： " + id);
                    continue;
                }
                toApply.Add(t);
            }

            int riskyCount = 0;
            for (int i = 0; i < toApply.Count; i++)
            {
                if (toApply[i].Risky) riskyCount++;
            }

            log.AppendLine("计划：将启用 " + toApply.Count + " 项（谨慎项 " + riskyCount + " 项）；" +
                "已是目标状态 " + already + " 项；本库不存在 " + unknown + " 项；权限不足跳过 " + blocked + " 项。");

            if (!yes)
            {
                log.AppendLine("（试运行：未做任何修改。确认无误后加 --yes 执行。）");
                FinishApplyLog(log);
                return 0;
            }

            if (toApply.Count == 0)
            {
                log.AppendLine("没有需要变更的项。");
                FinishApplyLog(log);
                return 0;
            }

            if (riskyCount > 0)
            {
                bool rpOk;
                string rpNote = RestorePoints.EnsureRecent("GuyueBox - 无人值守方案应用", out rpOk);
                log.AppendLine("还原点： " + (rpOk ? (rpNote.Length == 0 ? "24 小时内已有可用还原点，跳过创建" : rpNote) : "创建失败"));
                if (!rpOk)
                {
                    log.AppendLine("已取消：谨慎项应用前必须确保还原点可用，本次一个键都不会写。");
                    FinishApplyLog(log);
                    return 1;
                }
            }

            int okCount = 0, failCount = 0;
            for (int i = 0; i < toApply.Count; i++)
            {
                ITweak t = toApply[i];
                TweakExecutor.Result r = TweakExecutor.Apply(t);
                if (r != null && r.IsOk)
                {
                    okCount++;
                    log.AppendLine("  已启用： " + t.Id);
                }
                else
                {
                    failCount++;
                    log.AppendLine("  失败： " + t.Id);
                }
            }

            log.AppendLine("完成：成功 " + okCount + " 项，失败 " + failCount + " 项。");
            FinishApplyLog(log);
            return failCount > 0 ? 1 : 0;
        }

        /// <summary>apply.log 落盘并镜像到控制台（winexe 无控制台，文件才是可靠输出；脚本重定向时两者都可用）。</summary>
        private static void FinishApplyLog(StringBuilder log)
        {
            try
            {
                File.WriteAllText(Path.Combine(Application.StartupPath, "apply.log"),
                    log.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
            try { Console.Out.Write(log.ToString()); }
            catch
            {
            }
        }

        private static int RunSelfTest()
        {
            string log;
            int code = 0;
            try
            {
                TweakSelfTest.Result r = TweakSelfTest.Run();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("古月工具箱 · 优化项目录自检");
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("检查项数：" + r.Checked);
                sb.AppendLine();
                if (r.Failures.Count == 0)
                {
                    sb.AppendLine("[PASS] 未发现违规。");
                }
                else
                {
                    code = 1;
                    sb.AppendLine("[FAIL] 违规 " + r.Failures.Count + " 条：");
                    for (int i = 0; i < r.Failures.Count; i++) sb.AppendLine("  · " + r.Failures[i]);
                }
                if (r.Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("提示 " + r.Warnings.Count + " 条：");
                    for (int i = 0; i < r.Warnings.Count; i++) sb.AppendLine("  · " + r.Warnings[i]);
                }
                log = sb.ToString();
            }
            catch (Exception ex)
            {
                code = 1;
                log = "自检异常：" + ex.Message + "\r\n" + ex.StackTrace;
            }

            try
            {
                string file = Path.Combine(Application.StartupPath, "selftest.log");
                File.WriteAllText(file, log, new UTF8Encoding(false));
            }
            catch
            {
            }
            try { Console.Out.Write(log); }
            catch
            {
            }
            return code;
        }

        private static string FirstFrame(Exception ex)
        {
            try
            {
                string trace = ex.StackTrace;
                if (string.IsNullOrEmpty(trace)) return "";
                int nl = trace.IndexOf('\n');
                return nl > 0 ? trace.Substring(0, nl).Trim() : trace.Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
