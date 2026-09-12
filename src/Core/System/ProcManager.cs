using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SysToolbox.Core
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
        public string SessionName;
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

        public static Process[] FindByName(string name)
        {
            try { return Process.GetProcessesByName(name); }
            catch { return new Process[0]; }
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
