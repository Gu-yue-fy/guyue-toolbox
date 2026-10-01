﻿/* ============================================================
 * 文件说明：外部命令执行：管道读取、GBK/UTF-8 自动识别、提权启动
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;

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

        // —— 受控执行边界（借鉴 YuqiEngine 的 ProcessRunner：可执行文件白名单 + 危险模式拦截）——
        // C# 经 Shell 协调 PowerShell 与系统内置工具完成提权变更；所有外部进程统一收口到 Shell.Run，
        // 并在此加两道护栏。每次调用记入 RegLog（类别 "Shell"）以便审计与排错。
        private static readonly HashSet<string> AllowedExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "powershell.exe", "cmd.exe", "reg.exe", "winget.exe", "taskkill.exe",
            "powercfg.exe", "netsh.exe", "dism.exe", "bcdedit.exe", "schtasks.exe",
            "wevtutil.exe", "sc.exe", "explorer.exe"
        };

        // 危险模式：下载执行 / 远程拉取 / 磁盘销毁 / 反射加载等，命中即硬拦截（不依赖白名单）。
        private static readonly Regex DangerousPowerShell = new Regex(
            @"(Invoke-Expression|\biex\b|Invoke-WebRequest|\biwr\b|Start-Process|" +
            @"DownloadString|DownloadFile|Net\.WebClient|Reflection\.Assembly|" +
            @"Remove-Item\s+-Recurse.*-Force|rm\s+-rf|del\s+/s|Format-Volume|" +
            @"Clear-Disk|Initialize-Disk|rmdir\s+/s|rd\s+/s|format\s|\$env:)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 白名单完整回归确认后可置 true，变为拒绝式（未知 exe 直接拒绝而非放行告警）。
        private static readonly bool DenyByDefault = false;

        private static string NormalizeExe(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string name = Path.GetFileName(fileName).ToLowerInvariant();
            if (name.IndexOf('.') < 0) name += ".exe"; // 无扩展名按 .exe 处理（如 winget）
            return name;
        }

        private static bool IsExeAllowed(string fileName)
        {
            return AllowedExes.Contains(NormalizeExe(fileName));
        }

        private static bool HasDangerousPattern(string args)
        {
            return args != null && DangerousPowerShell.IsMatch(args);
        }

        private static void Audit(string level, string msg)
        {
            try { RegLog.Add("Shell", level, msg); } catch { }
        }

        /// <summary>把变更类外部命令归并为操作日志里的可读类别标签。</summary>
        private static string ChangeLabel(string exeKey)
        {
            switch (exeKey)
            {
                case "taskkill.exe": return "结束进程 (taskkill)";
                case "winget.exe": return "软件包管理 (winget)";
                case "powercfg.exe": return "电源计划 (powercfg)";
                case "reg.exe": return "注册表 (reg)";
                case "bcdedit.exe": return "启动配置 (bcdedit)";
                case "dism.exe": return "系统功能 (dism)";
                case "netsh.exe": return "网络 (netsh)";
                case "schtasks.exe": return "计划任务 (schtasks)";
                case "sc.exe": return "系统服务 (sc)";
                case "wevtutil.exe": return "事件日志 (wevtutil)";
                case "powershell.exe": return "PowerShell 脚本";
                default: return exeKey;
            }
        }

        /// <summary>仅对"变更类"外部命令留痕进 TweakExecutor.Recent()，避免只读探测刷屏操作日志。</summary>
        private static void RecordIfChange(bool isChange, string exeKey, string arguments, Result r)
        {
            // 优化项内部的 shell 调用不重复入日志：该改动已由 TweakExecutor 的 tweak 结果代表。
            if (!isChange || TweakExecutor.InTweakExecution) return;
            try
            {
                TweakExecutor.RecordShell(ChangeLabel(exeKey), Truncate(arguments, 200),
                    r.ExitCode == 0, r.ExitCode == 0 ? "执行成功" : (r.Error ?? "命令失败"));
            }
            catch { }
        }

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

            string utf8 = null;
            try { utf8 = new UTF8Encoding(false, true).GetString(data); } // 严格 UTF-8
            catch (DecoderFallbackException) { utf8 = null; }
            catch (ArgumentException) { utf8 = null; }

            // 严格 UTF-8 解码成功且含高位字节 → 直接采用。
            // 不能只靠 CJK 计分：UTF-8 的中文按 GBK 误读会产生"更多"的 CJK 字符
            // （3 字节/字 → 1.5 字/字），得分反而压过正确解码（实测
            // 「本地连接* 2」被误判成「键 D 道码块* 2」）。
            // 而随机 GBK 字节序列恰好构成合法 UTF-8 的概率极低，可放心优先。
            if (utf8 != null) return utf8;

            // 候选集：系统 ANSI + GBK 兜底。
            // 关键坑：系统开「Beta UTF-8」时 ANSI 即 UTF-8，GBK 输出会被解出 U+FFFD 乱码，
            // 而 utf8 严格解码又失败 → 旧逻辑直接返回这份乱码。故显式补一个 GBK 候选。
            string gbk = null;
            try { gbk = Encoding.GetEncoding(936).GetString(data); } catch { }
            string ansi = GetConsoleEncoding().GetString(data);

            string[] cands = new string[] { ansi, gbk };
            string best = null;
            int bestScore = -1;
            for (int i = 0; i < cands.Length; i++)
            {
                string s = cands[i];
                if (s == null) continue;
                // 同分时优先不含替换符的一方，其次先到先得
                int score = CountCjk(s) * 2 + (s.IndexOf('\uFFFD') >= 0 ? -1 : 0);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best ?? ansi;
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

        /// <summary>把子进程输出流读进内存（独立线程，双管道并行读避免死锁）。
        /// 返回 true = 正常读到 EOF；false = 管道中途异常（输出被截断）。
        /// 诊断报告 #4：此前空 catch 静默吞掉管道断开异常，截断的输出仍按"成功"返回，
        /// 会误判网络 / 电源等命令的成败。</summary>
        private static bool CopyStream(Stream src, MemoryStream dst)
        {
            try
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Result Run(string fileName, string arguments, int timeoutMs)
        {
            return Run(fileName, arguments, timeoutMs, null);
        }

        /// <summary>执行并标记"变更类"（进入操作日志），使用系统控制台编码。</summary>
        public static Result Run(string fileName, string arguments, int timeoutMs, bool isChange)
        {
            return Run(fileName, arguments, timeoutMs, null, isChange);
        }

        /// <summary>受信任的内部命令通道：跳过危险模式拦截（用于本工具自身需要的清理 / 重置命令，
        /// 如清除 Windows 更新缓存）。仅用于代码内部、明确安全的命令，绝不可传入用户提供的字符串。</summary>
        public static Result RunTrusted(string fileName, string arguments, int timeoutMs, bool isChange = false)
        {
            return Run(fileName, arguments, timeoutMs, null, isChange, true);
        }

        /// <summary>指定输出编码执行（如 winget 输出为 UTF-8），encoding 为 null 时用系统控制台编码。</summary>
        /// <param name="isChange">true 表示本次为"变更类"操作（写注册表/装包/杀进程等），
        /// 将留痕进 TweakExecutor.Recent() 与注册表优化项同视图；只读探测应传 false 以免刷屏。</param>
        public static Result Run(string fileName, string arguments, int timeoutMs, Encoding encoding, bool isChange = false, bool trusted = false)
        {
            Result r = new Result();
            // —— 受控执行边界（借鉴 YuqiEngine 的 ProcessRunner）——
            string exeKey = NormalizeExe(fileName);
            if (!trusted && (exeKey == "powershell.exe" || exeKey == "cmd.exe") && HasDangerousPattern(arguments))
            {
                r.ExitCode = -1;
                r.Error = "命令包含被禁止的危险模式（下载执行 / 磁盘销毁 / 远程拉取等），已拒绝执行。";
                Audit("拒绝", exeKey + " → 危险模式拦截");
                RecordIfChange(isChange, exeKey, arguments, r);
                return r;
            }
            if (!IsExeAllowed(fileName))
            {
                if (DenyByDefault)
                {
                    r.ExitCode = -1;
                    r.Error = "可执行文件不在白名单，已拒绝执行：" + exeKey;
                    Audit("拒绝", exeKey + " → 不在白名单");
                    RecordIfChange(isChange, exeKey, arguments, r);
                    return r;
                }
                Audit("告警", "可执行文件不在白名单，已放行：" + exeKey);
            }
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
                    bool outOk = true, errOk = true;
                    Thread tOut = new Thread((ThreadStart)delegate { outOk = CopyStream(p.StandardOutput.BaseStream, outMs); });
                    Thread tErr = new Thread((ThreadStart)delegate { errOk = CopyStream(p.StandardError.BaseStream, errMs); });
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
                        Audit("超时", exeKey + " " + Truncate(arguments, 120) + " → exit " + r.ExitCode);
                        RecordIfChange(isChange, exeKey, arguments, r);
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

                    // 管道中途断开：输出被截断却拿到了退出码，仍会按 Ok 判成功——
                    // 在 Error 尾部如实标注，让调用方（网络 / 电源命令成败判定）能感知
                    if (!outOk || !errOk)
                    {
                        string note = "输出管道中途断开，内容可能不完整。";
                        r.Error = string.IsNullOrEmpty(r.Error) ? note : r.Error + Environment.NewLine + note;
                    }
                }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Error = ex.Message;
            }
            Audit(r.ExitCode == 0 ? "执行" : "失败",
                exeKey + " " + Truncate(arguments, 120) + " → exit " + r.ExitCode);
            RecordIfChange(isChange, exeKey, arguments, r);
            return r;
        }

        public static Result Run(string fileName, string arguments)
        {
            return Run(fileName, arguments, 120000);
        }

        /// <summary>执行 cmd 内建命令。isChange 为 true 时记入操作日志。</summary>
        public static Result Cmd(string command, int timeoutMs, bool isChange = false)
        {
            return Run("cmd.exe", "/c " + command, timeoutMs, null, isChange);
        }

        /// <summary>执行 netsh 命令。isChange 为 true 时记入操作日志。</summary>
        public static Result Netsh(string arguments, bool isChange = false)
        {
            return Run("netsh.exe", arguments, 60000, null, isChange);
        }

        /// <summary>用资源管理器打开路径并选中文件。路径不存在时尝试打开父目录。</summary>
        public static void OpenSelect(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    using (Process.Start("explorer.exe", "/select,\"" + path + "\"")) { }
                }
                else if (Directory.Exists(path))
                {
                    using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
                }
                else
                {
                    // 路径不存在时尝试打开父目录
                    string parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        using (Process.Start("explorer.exe", "\"" + parent + "\"")) { }
                }
            }
            catch { }
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

        /// <summary>用默认程序打开 URL（统一收口，避免散落的裸 Process.Start(url)）。</summary>
        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { using (Process.Start(url)) { } }
            catch { }
        }

        /// <summary>打开一个空白资源管理器窗口（修复中心重启资源管理器后用于恢复桌面）。</summary>
        public static void OpenExplorer()
        {
            try { using (Process.Start("explorer.exe")) { } }
            catch { }
        }

        /// <summary>启动外部进程但不等待其结束（自更新等场景），仍经过白名单与危险模式护栏。</summary>
        public static Result RunDetached(string fileName, string arguments)
        {
            Result r = new Result();
            string exeKey = NormalizeExe(fileName);
            if ((exeKey == "powershell.exe" || exeKey == "cmd.exe") && HasDangerousPattern(arguments))
            {
                r.ExitCode = -1;
                r.Error = "命令包含被禁止的危险模式，已拒绝执行。";
                Audit("拒绝", exeKey + " → 危险模式拦截");
                return r;
            }
            if (!IsExeAllowed(fileName))
            {
                if (DenyByDefault)
                {
                    r.ExitCode = -1;
                    r.Error = "可执行文件不在白名单，已拒绝执行：" + exeKey;
                    Audit("拒绝", exeKey + " → 不在白名单");
                    return r;
                }
                Audit("告警", "可执行文件不在白名单，已放行：" + exeKey);
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = new Process()) { p.StartInfo = psi; p.Start(); }
            }
            catch (Exception ex)
            {
                r.ExitCode = -1;
                r.Error = ex.Message;
            }
            Audit(r.ExitCode == 0 ? "执行" : "失败",
                exeKey + " " + Truncate(arguments, 120) + " → exit " + r.ExitCode);
            return r;
        }

        /// <summary>把审计摘要截断到 max 长度（去换行，避免日志被长命令撑爆）。</summary>
        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        /// <summary>以管理员身份重新启动当前程序。</summary>
        public static bool RestartElevated(string arguments)
        {
            return StartElevated(System.Windows.Forms.Application.ExecutablePath, arguments);
        }

        /// <summary>
        /// 启动外部程序：优先请求管理员权限（runas）；用户取消 UAC 或目标程序不支持提权时回退普通启动。
        /// 「以管理员身份重启自身」与「启动软件卸载程序」共用这一份实现——
        /// 此前两处各写了一份 runas + 回退逻辑，修改时容易只改一处。
        /// </summary>
        public static bool StartElevated(string fileName, string arguments)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            if (TryStart(fileName, arguments, true)) return true;
            return TryStart(fileName, arguments, false);
        }

        private static bool TryStart(string fileName, string arguments, bool elevated)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments == null ? "" : arguments;
                psi.UseShellExecute = true;
                if (elevated) psi.Verb = "runas";
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
