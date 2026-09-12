using System;
using System.Diagnostics;
using System.Text;

namespace SysToolbox.Core
{
    /// <summary>
    /// 外部命令执行辅助。
    /// </summary>
    public static class Shell
    {
        public sealed class Result
        {
            public int ExitCode;
            public string Output;
            public string Error;

            public bool Ok
            {
                get { return ExitCode == 0; }
            }

            public string All
            {
                get
                {
                    StringBuilder sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(Output)) sb.Append(Output.Trim());
                    if (!string.IsNullOrEmpty(Error))
                    {
                        if (sb.Length > 0) sb.Append(Environment.NewLine);
                        sb.Append(Error.Trim());
                    }
                    return sb.ToString();
                }
            }
        }

        /// <summary>
        /// 静默执行一条命令，返回标准输出/错误。会隐藏窗口，不阻塞 UI 之外的事情。
        /// </summary>
        /// <summary>系统控制台输出编码（中文系统 GBK/936；开启全局 UTF-8 时为 UTF-8）。</summary>
        private static Encoding GetConsoleEncoding()
        {
            try { return Console.OutputEncoding; }
            catch { try { return Encoding.GetEncoding(0); } catch { return Encoding.UTF8; } }
        }

        public static Result Run(string fileName, string arguments, int timeoutMs)
        {
            Result r = new Result();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                // 控制台工具（netsh/powercfg/dism/cmd…）输出的是系统控制台编码
                // （中文系统为 GBK/936）——之前强制 UTF-8 解码导致中文乱码。
                // 跟随控制台实际编码（系统开全局 UTF-8 时自动变为 UTF-8）。
                Encoding consoleEnc = GetConsoleEncoding();
                psi.StandardOutputEncoding = consoleEnc;
                psi.StandardErrorEncoding = consoleEnc;

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();

                    // 异步读双管道：同步先读 stdout 会让 stderr 缓冲塞满时子进程卡死（经典死锁）
                    StringBuilder stdout = new StringBuilder();
                    StringBuilder stderr = new StringBuilder();
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stdout.AppendLine(e.Data);
                    };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) stderr.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    bool exited = timeoutMs <= 0 || p.WaitForExit(timeoutMs);
                    if (!exited)
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit(2000);
                        r.ExitCode = -1;
                        r.Error = "命令执行超时。";
                        return r;
                    }
                    p.WaitForExit(); // 无参重载：等待异步输出回调全部排空
                    r.Output = stdout.ToString();
                    r.Error = stderr.ToString();
                    try { r.ExitCode = p.ExitCode; }
                    catch { r.ExitCode = -1; r.Error = "无法获取退出码。"; }
                }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Error = ex.Message;
            }
            return r;
        }

        public static Result Run(string fileName, string arguments)
        {
            return Run(fileName, arguments, 120000);
        }

        /// <summary>执行 cmd 内建命令。</summary>
        public static Result Cmd(string command, int timeoutMs)
        {
            return Run("cmd.exe", "/c " + command, timeoutMs);
        }

        /// <summary>执行 netsh 命令。</summary>
        public static Result Netsh(string arguments)
        {
            return Run("netsh.exe", arguments, 60000);
        }

        /// <summary>用资源管理器打开路径。</summary>
        public static void OpenPath(string path)
        {
            try
            {
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch
            {
            }
        }

        /// <summary>用系统外壳启动程序 / 文档 / .msc 控制台（自动提权由 Verb 决定）。</summary>
        public static bool Launch(string file)
        {
            return Launch(file, "");
        }

        public static bool Launch(string file, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = file;
                psi.Arguments = arguments == null ? "" : arguments;
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>以管理员身份重新启动当前程序。</summary>
        public static bool RestartElevated(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = System.Windows.Forms.Application.ExecutablePath;
                psi.Arguments = arguments == null ? "" : arguments;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
