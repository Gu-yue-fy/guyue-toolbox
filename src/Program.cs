using System;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;
using SysToolbox.UI;

namespace SysToolbox
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 全局异常兜底，避免出现 .NET 默认的崩溃对话框
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 多实例提示
            bool createdNew;
            // Local\ 前缀：避免多用户 / 远程桌面会话间的互斥体命名冲突
            using (Mutex mutex = new Mutex(true, @"Local\SysToolbox.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(MainForm.AppName + " 已经在运行中。", MainForm.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
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
