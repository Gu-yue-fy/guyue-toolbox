using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GuyueBox.Core
{
    public sealed class ProcInfo
    {
        public int Pid;
        public string Name;
        public string Path;
        public string Description;
        public long WorkingSet;
        public long PrivateBytes;
        public double CpuPercent;
        public TimeSpan CpuTime;
        public bool Responding = true;

        public string MemoryText
        {
            get { return SysInfo.FormatSize(WorkingSet); }
        }

        public string CpuPercentText
        {
            get { return CpuPercent.ToString("0.0") + " %"; }
        }
    }

    /// <summary>
    /// 进程枚举与终止。CPU 占用通过两次采样差值计算，因此需保持同一个实例。
    /// </summary>
    public sealed class ProcManager
    {
        private sealed class Sample
        {
            public DateTime At;
            public TimeSpan Cpu;
        }

        private readonly Dictionary<int, Sample> _samples = new Dictionary<int, Sample>();

        /// <summary>
        /// 采集一次进程快照。首次调用时 CPU 百分比为 0（缺少基准点）。
        /// </summary>
        public List<ProcInfo> Snapshot()
        {
            List<ProcInfo> list = new List<ProcInfo>();
            Process[] processes;

            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return list;
            }

            DateTime now = DateTime.UtcNow;
            Dictionary<int, Sample> fresh = new Dictionary<int, Sample>();

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    ProcInfo info = new ProcInfo();
                    info.Pid = p.Id;
                    info.Name = p.ProcessName;

                    try { info.WorkingSet = p.WorkingSet64; }
                    catch { info.WorkingSet = 0; }

                    try { info.PrivateBytes = p.PrivateMemorySize64; }
                    catch { info.PrivateBytes = 0; }

                    TimeSpan cpu = TimeSpan.Zero;
                    try { cpu = p.TotalProcessorTime; }
                    catch { }
                    info.CpuTime = cpu;

                    try { info.Responding = p.Responding; }
                    catch { info.Responding = true; }

                    Sample prev;
                    if (_samples.TryGetValue(info.Pid, out prev))
                    {
                        double seconds = (now - prev.At).TotalSeconds;
                        if (seconds > 0.05)
                        {
                            double delta = (cpu - prev.Cpu).TotalMilliseconds;
                            if (delta < 0) delta = 0;
                            double pct = delta / (seconds * 1000.0 * Environment.ProcessorCount) * 100.0;
                            if (pct > 100) pct = 100;
                            info.CpuPercent = Math.Round(pct, 1);
                        }
                    }

                    Sample s = new Sample();
                    s.At = now;
                    s.Cpu = cpu;
                    fresh[info.Pid] = s;

                    try { info.Path = p.MainModule.FileName; }
                    catch { info.Path = ""; }

                    if (!string.IsNullOrEmpty(info.Path))
                    {
                        try { info.Description = FileVersionInfo.GetVersionInfo(info.Path).FileDescription; }
                        catch { }
                    }
                    if (string.IsNullOrEmpty(info.Description)) info.Description = "";

                    list.Add(info);
                }
                catch
                {
                }
                finally
                {
                    try { p.Dispose(); }
                    catch { }
                }
            }

            _samples.Clear();
            foreach (KeyValuePair<int, Sample> kv in fresh) _samples[kv.Key] = kv.Value;

            return list;
        }

        public static bool Kill(int pid, bool wholeTree)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                    return true;
                }
                catch
                {
                    if (!wholeTree) throw;
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }

                if (wholeTree)
                    return Shell.Run("taskkill.exe", "/PID " + pid + " /T /F", 15000).Ok;
                return false;
            }
            catch
            {
                return Shell.Run("taskkill.exe", "/PID " + pid + " /F", 15000).Ok;
            }
        }

        // ==============================================================
        // 进程亲和性与优先级
        //
        // .NET 的 Process 没有亲和性 API；优先级虽有 PriorityClass 属性，
        // 但在受保护进程上会抛异常。这里统一走 Win32 调用，并把失败原因回传，
        // 由界面提示"为什么设不上"（如权限不足 / 进程已退出）。
        // ==============================================================

        // 注意：OpenProcess / CloseHandle / PROCESS_QUERY_INFORMATION 已在本文件末尾
        // 为"工作集回收"声明过，此处只补亲和性设置额外需要的权限与调用。
        private const int PROCESS_SET_INFORMATION = 0x0200;

        private const int IDLE_PRIORITY_CLASS = 0x00000040;
        private const int BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
        private const int NORMAL_PRIORITY_CLASS = 0x00000020;
        private const int ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        private const int HIGH_PRIORITY_CLASS = 0x00000080;
        private const int REALTIME_PRIORITY_CLASS = 0x00000100;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr handle, out ulong processMask, out ulong systemMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr handle, ulong mask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr handle, int priorityClass);

        /// <summary>优先级档位文案（索引与 <see cref="SetPriority"/> 的 level 一一对应）。</summary>
        public static readonly string[] PriorityNames =
            new string[] { "空闲", "低于正常", "正常", "高于正常", "高", "实时" };

        private static int PriorityFlag(int level)
        {
            switch (level)
            {
                case 0: return IDLE_PRIORITY_CLASS;
                case 1: return BELOW_NORMAL_PRIORITY_CLASS;
                case 2: return NORMAL_PRIORITY_CLASS;
                case 3: return ABOVE_NORMAL_PRIORITY_CLASS;
                case 4: return HIGH_PRIORITY_CLASS;
                default: return REALTIME_PRIORITY_CLASS;
            }
        }

        /// <summary>本机可用的逻辑核掩码（系统亲和性）；失败返回 0。</summary>
        public static ulong SystemAffinityMask()
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                int self = Process.GetCurrentProcess().Id;
                h = OpenProcess(PROCESS_QUERY_INFORMATION, false, self);
                if (h == IntPtr.Zero) return 0;
                ulong own, system;
                if (!GetProcessAffinityMask(h, out own, out system)) return 0;
                return system;
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        /// <summary>读取进程当前亲和性掩码。失败返回 0 并给出原因。</summary>
        public static ulong GetAffinity(int pid, out string error)
        {
            error = "";
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    error = "无法打开该进程（可能已退出或权限不足）。";
                    return 0;
                }
                ulong own, system;
                if (!GetProcessAffinityMask(h, out own, out system))
                {
                    error = "读取亲和性失败（错误码 " + Marshal.GetLastWin32Error() + "）。";
                    return 0;
                }
                return own;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 0;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        /// <summary>
        /// 设置进程亲和性。<paramref name="mask"/> 必须是当前系统掩码的子集，
        /// 且不能为 0——0 或越界的掩码会让进程无法被调度，系统会直接拒绝。
        /// </summary>
        public static bool SetAffinity(int pid, ulong mask, out string error)
        {
            error = "";
            if (mask == 0)
            {
                error = "至少要保留一个核心。";
                return false;
            }

            ulong system = SystemAffinityMask();
            if (system != 0 && (mask & ~system) != 0)
            {
                error = "选中的核心超出了本机可用范围。";
                return false;
            }

            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    error = "无法打开该进程（可能已退出或权限不足）。";
                    return false;
                }
                if (!SetProcessAffinityMask(h, mask))
                {
                    error = "设置失败（错误码 " + Marshal.GetLastWin32Error() + "），该进程可能不允许修改。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        /// <summary>设置进程优先级。<paramref name="level"/> 见 <see cref="PriorityNames"/>。</summary>
        public static bool SetPriority(int pid, int level, out string error)
        {
            error = "";
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    error = "无法打开该进程（权限不足，需以管理员身份运行）。";
                    return false;
                }
                if (!SetPriorityClass(h, PriorityFlag(level)))
                {
                    error = "设置失败（错误码 " + Marshal.GetLastWin32Error() + "），该进程可能不允许修改。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        /// <summary>读取进程优先级档位序号（0~5）；失败返回 -1。</summary>
        public static int GetPriority(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                try
                {
                    switch (p.PriorityClass)
                    {
                        case ProcessPriorityClass.Idle: return 0;
                        case ProcessPriorityClass.BelowNormal: return 1;
                        case ProcessPriorityClass.Normal: return 2;
                        case ProcessPriorityClass.AboveNormal: return 3;
                        case ProcessPriorityClass.High: return 4;
                        case ProcessPriorityClass.RealTime: return 5;
                        default: return -1;
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 请求系统释放尽可能多的物理内存：清空各进程工作集 + 清空系统文件缓存。
        /// </summary>
        public static int ReleaseMemory(out long before, out long after)
        {
            MemoryInfo m0 = SysInfo.GetMemory();
            before = (long)m0.AvailBytes;

            int touched = 0;
            Process[] processes;
            try { processes = Process.GetProcesses(); }
            catch { processes = new Process[0]; }

            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                try
                {
                    using (p)
                    {
                        // 仅整理工作集较小的进程，避免影响大型程序
                        if (p.WorkingSet64 > 512L * 1024 * 1024) continue;
                        int id = p.Id;
                        IntPtr h = OpenProcessForTrim(id);
                        if (h == IntPtr.Zero) continue;
                        try
                        {
                            if (Native.EmptyWorkingSet(h) != 0) touched++;
                        }
                        finally
                        {
                            CloseHandleSafe(h);
                        }
                    }
                }
                catch
                {
                }
            }

            MemoryInfo m1 = SysInfo.GetMemory();
            after = (long)m1.AvailBytes;
            return touched;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_SET_QUOTA = 0x0100;

        private static IntPtr OpenProcessForTrim(int pid)
        {
            try
            {
                return OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static void CloseHandleSafe(IntPtr h)
        {
            try { CloseHandle(h); }
            catch { }
        }
    }
}
