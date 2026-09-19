using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GuyueBox.Core
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

            // All 会被反复访问（日志、对话框、成败判定），此处做记忆化：
            // 用「字段引用」而非值比较来判失效——字符串字段被重新赋值时引用必然变化，
            // 因此缓存自动失效，无需调用方配合清理，O(1) 判定且不产生额外分配。
            private string _allCache;
            private string _allOut;
            private string _allErr;

            public string All
            {
                get
                {
                    if (_allCache != null &&
                        object.ReferenceEquals(_allOut, Output) &&
                        object.ReferenceEquals(_allErr, Error))
                    {
                        return _allCache;
                    }

                    StringBuilder sb = new StringBuilder();
                    if (!string.IsNullOrEmpty(Output)) sb.Append(Output.Trim());
                    if (!string.IsNullOrEmpty(Error))
                    {
                        if (sb.Length > 0) sb.Append(Environment.NewLine);
                        sb.Append(Error.Trim());
                    }

                    _allOut = Output;
                    _allErr = Error;
                    _allCache = sb.ToString();
                    return _allCache;
                }
            }
        }

        /// <summary>
        /// 静默执行一条命令，返回标准输出/错误。会隐藏窗口，不阻塞 UI 之外的事情。
        /// </summary>
        /// <summary>
        /// 系统控制台输出编码 = 系统 ANSI 代码页（中文系统 936/GBK）。
        /// 注意：不能依赖 Console.OutputEncoding——GUI 进程没有控制台时它返回 UTF-8，
        /// 会让按 GBK 输出的工具中文乱码。
        /// </summary>
        /// <summary>控制台编码解析结果缓存：进程生命周期内不变，无需每次解码都查询代码页。</summary>
        private static Encoding _consoleEncoding;

        private static Encoding GetConsoleEncoding()
        {
            Encoding cached = _consoleEncoding;
            if (cached != null) return cached;

            try
            {
                cached = Encoding.GetEncoding(0); // 0 = 系统 ANSI 代码页
                // 系统开了「Beta: 使用 UTF-8 提供全球语言支持」时 ANSI 即 UTF-8，无需特殊处理
            }
            catch
            {
                try { cached = Encoding.Default; }
                catch { cached = Encoding.UTF8; }
            }
            _consoleEncoding = cached;
            return cached;
        }

        /// <summary>
        /// 控制台工具输出解码（自动识别 GBK / UTF-8，根治网络中心乱码）。
        /// 中文系统上 ipconfig / netsh / netstat 等按控制台代码页（默认 GBK/936）输出，
        /// 但若系统开了「Beta UTF-8」或工具自身输出 UTF-8，字节又是 UTF-8。
        /// 两种编码都能"成功解码"成不同文本，单靠合法性校验无法区分，
        /// 因此用「中文 CJK 字符命中数」打分：正确解码的中文文本会包含大量 CJK 码位，
        /// 误解码的乱码几乎不含 CJK，取命中多者即可稳定判定。
        /// </summary>
        private static string DecodeConsoleOutput(byte[] data)
        {
            if (data == null || data.Length == 0) return "";

            bool hasHigh = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= 0x80) { hasHigh = true; break; }
            }
            if (!hasHigh) return Encoding.ASCII.GetString(data); // 纯 ASCII，任何编码结果一致

            string ansi = GetConsoleEncoding().GetString(data);   // 系统 ANSI（中文系统=GBK）
            string utf8 = null;
            try { utf8 = new UTF8Encoding(false, true).GetString(data); } // 严格 UTF-8
            catch (DecoderFallbackException) { utf8 = null; }
            catch (ArgumentException) { utf8 = null; }

            if (utf8 == null) return ansi;          // UTF-8 含非法序列 → 直接按 ANSI
            if (ansi == utf8) return ansi;          // 两种解码一致 → 任取

            int ansiScore = CountCjk(ansi);
            int utf8Score = CountCjk(utf8);
            if (ansiScore != utf8Score)             // 命中中文多者即正确解码
                return ansiScore > utf8Score ? ansi : utf8;

            // 平局：优先不含替换符（U+FFFD）的一方
            bool ansiBad = ansi.IndexOf('\uFFFD') >= 0;
            bool utf8Bad = utf8.IndexOf('\uFFFD') >= 0;
            if (ansiBad && !utf8Bad) return utf8;
            return ansi;
        }

        /// <summary>统计字符串中 CJK 统一表意文字（含扩展 A）的数量，用作解码正确性打分。</summary>
        private static int CountCjk(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF)) n++;
            }
            return n;
        }

        /// <summary>把子进程输出流读进内存（独立线程，双管道并行读避免死锁）。</summary>
        private static void CopyStream(Stream src, MemoryStream dst)
        {
            try
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
            }
            catch
            {
            }
        }

        public static Result Run(string fileName, string arguments, int timeoutMs)
        {
            return Run(fileName, arguments, timeoutMs, null);
        }

        /// <summary>指定输出编码执行（如 winget 输出为 UTF-8），encoding 为 null 时用系统控制台编码。</summary>
        public static Result Run(string fileName, string arguments, int timeoutMs, Encoding encoding)
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
                // 输出按原始字节读取，编码在 DecodeConsoleOutput 中自动识别；
                // 个别特殊工具（如 winget 的 UTF-8）由调用方通过 Run(..., encoding) 显式指定。

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();

                    // 双管道各用一条线程读「原始字节」：解码延后到读完后按内容自动识别
                    // （netsh 输出 UTF-8、部分工具输出 GBK，逐行事件回调在解码阶段就已定死编码，无法兼容两者）
                    MemoryStream outMs = new MemoryStream();
                    MemoryStream errMs = new MemoryStream();
                    Thread tOut = new Thread((ThreadStart)delegate { CopyStream(p.StandardOutput.BaseStream, outMs); });
                    Thread tErr = new Thread((ThreadStart)delegate { CopyStream(p.StandardError.BaseStream, errMs); });
                    tOut.IsBackground = true;
                    tErr.IsBackground = true;
                    tOut.Start();
                    tErr.Start();

                    bool exited = timeoutMs <= 0 || p.WaitForExit(timeoutMs);
                    if (!exited)
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit(2000);
                        tOut.Join(1000);
                        tErr.Join(1000);
                        r.ExitCode = -1;
                        r.Error = "命令执行超时。";
                        return r;
                    }
                    tOut.Join(5000);
                    tErr.Join(5000);

                    // 极端情况：子进程派生的孙进程仍持有管道写端 → 读取线程永远等不到 EOF。
                    // 此时 Join 已超时，但内存流仍在被并发写入（MemoryStream 非线程安全），
                    // 直接 ToArray 会读到截断/错乱的内容。主动关闭管道逼读取线程退出后再取缓冲。
                    if (tOut.IsAlive)
                    {
                        try { p.StandardOutput.BaseStream.Dispose(); } catch { }
                        tOut.Join(1000);
                    }
                    if (tErr.IsAlive)
                    {
                        try { p.StandardError.BaseStream.Dispose(); } catch { }
                        tErr.Join(1000);
                    }

                    byte[] outBytes = outMs.ToArray();
                    byte[] errBytes = errMs.ToArray();
                    if (encoding != null)
                    {
                        r.Output = encoding.GetString(outBytes); // 调用方显式指定（如 winget 的 UTF-8）
                        r.Error = encoding.GetString(errBytes);
                    }
                    else
                    {
                        r.Output = DecodeConsoleOutput(outBytes);
                        r.Error = DecodeConsoleOutput(errBytes);
                    }
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
        public static void OpenSelect(string path)
        {
            try { using (Process.Start("explorer.exe", "/select,\"" + path + "\"")) { } } catch { }
        }

        public static void OpenPath(string path)
        {
            try
            {
                using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
            }
            catch
            {
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
                using (Process.Start(psi)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
