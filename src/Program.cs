/* ============================================================
 * 文件说明：程序入口：单实例互斥锁、全局异常捕获、DPI/清单初始化，启动 MainForm 消息循环。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
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

            // 全局异常兜底，避免出现 .NET 默认的崩溃对话框
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 多实例提示
            bool createdNew;
            // Local\ 前缀：避免多用户 / 远程桌面会话间的互斥体命名冲突
            using (Mutex mutex = new Mutex(true, @"Local\GuyueBox.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
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
        private static int RunSelfTest()
        {
            string log;
            int code = 0;
            try
            {
                TweakSelfTest.Result r = TweakSelfTest.Run();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("古月工具包 · 优化项目录自检");
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
