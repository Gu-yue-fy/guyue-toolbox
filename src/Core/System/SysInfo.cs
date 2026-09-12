using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace SysToolbox.Core
{
    public sealed class MemoryInfo
    {
        public ulong TotalBytes;
        public ulong AvailBytes;
        public ulong PageFileTotal;
        public ulong PageFileAvail;

        public ulong UsedBytes
        {
            get { return TotalBytes > AvailBytes ? TotalBytes - AvailBytes : 0; }
        }

        public double UsedPercent
        {
            get { return TotalBytes == 0 ? 0 : (double)UsedBytes * 100.0 / TotalBytes; }
        }
    }

    public sealed class DiskInfo
    {
        public string Name;
        public string Label;
        public string Format;
        public string DriveType;
        public long TotalBytes;
        public long FreeBytes;

        public long UsedBytes
        {
            get { return TotalBytes - FreeBytes; }
        }

        public double UsedPercent
        {
            get { return TotalBytes <= 0 ? 0 : (double)UsedBytes * 100.0 / TotalBytes; }
        }
    }

    public sealed class SystemSnapshot
    {
        public string ComputerName = "";
        public string UserName = "";
        public string OsName = "";
        public string OsVersion = "";
        public string OsArch = "";
        public string CpuName = "";
        public int CpuCores;
        public int CpuThreads;
        public string GpuName = "";
        public string BiosVersion = "";
        public string BaseBoard = "";
        public MemoryInfo Memory = new MemoryInfo();
        public List<DiskInfo> Disks = new List<DiskInfo>();
        public TimeSpan Uptime = TimeSpan.Zero;
        public DateTime BootTime = DateTime.MinValue;
        public bool Elevated;
        public double CpuLoadPercent = -1;
    }

    /// <summary>
    /// 采集本机硬件 / 操作系统信息。所有方法均可安全失败并返回占位文本。
    /// </summary>
    public static class SysInfo
    {
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "-";
            double v = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            while (v >= 1024.0 && i < units.Length - 1)
            {
                v /= 1024.0;
                i++;
            }
            if (i == 0) return ((long)v).ToString() + " " + units[i];
            return v.ToString(v >= 100 ? "0" : "0.0") + " " + units[i];
        }

        public static string FormatSize(ulong bytes)
        {
            if (bytes > long.MaxValue) return FormatSize(long.MaxValue);
            return FormatSize((long)bytes);
        }

        public static MemoryInfo GetMemory()
        {
            MemoryInfo m = new MemoryInfo();
            try
            {
                Native.MEMORYSTATUSEX st = new Native.MEMORYSTATUSEX();
                if (Native.GlobalMemoryStatusEx(st))
                {
                    m.TotalBytes = st.ullTotalPhys;
                    m.AvailBytes = st.ullAvailPhys;
                    m.PageFileTotal = st.ullTotalPageFile;
                    m.PageFileAvail = st.ullAvailPageFile;
                }
            }
            catch
            {
            }
            return m;
        }

        public static List<DiskInfo> GetDisks(bool fixedOnly)
        {
            List<DiskInfo> list = new List<DiskInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (fixedOnly && d.DriveType != DriveType.Fixed) continue;

                        DiskInfo di = new DiskInfo();
                        di.Name = d.Name;
                        di.Label = string.IsNullOrEmpty(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel;
                        di.Format = d.DriveFormat;
                        di.TotalBytes = d.TotalSize;
                        di.FreeBytes = d.TotalFreeSpace;
                        switch (d.DriveType)
                        {
                            case DriveType.Fixed: di.DriveType = "本地磁盘"; break;
                            case DriveType.Removable: di.DriveType = "可移动"; break;
                            case DriveType.Network: di.DriveType = "网络"; break;
                            case DriveType.CDRom: di.DriveType = "光驱"; break;
                            default: di.DriveType = "其他"; break;
                        }
                        list.Add(di);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public static string WmiString(string query, string property)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                using (ManagementObjectCollection col = searcher.Get())
                {
                    foreach (ManagementBaseObject obj in col)
                    {
                        try
                        {
                            object v = obj[property];
                            if (v != null) return v.ToString().Trim();
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        public static SystemSnapshot Capture(bool deep, double cpuLoad)
        {
            SystemSnapshot s = new SystemSnapshot();
            s.ComputerName = SafeComputerName();
            s.UserName = SafeUserName();
            s.Elevated = Native.IsElevated();
            s.Memory = GetMemory();
            s.Disks = GetDisks(true);
            s.CpuLoadPercent = cpuLoad;

            try
            {
                s.CpuThreads = Environment.ProcessorCount;
            }
            catch
            {
            }

            try
            {
                TimeSpan up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
                s.Uptime = up;
                s.BootTime = DateTime.Now - up;
            }
            catch
            {
            }

            OSVersionInfo os = GetOsVersion();
            s.OsName = os.Name;
            s.OsVersion = os.Version;
            s.OsArch = os.Arch;

            if (deep)
            {
                // 全部走注册表（毫秒级），不再逐项查询 WMI
                s.CpuName = RegStr(Microsoft.Win32.Registry.LocalMachine,
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
                if (s.CpuCores <= 0) s.CpuCores = QueryPhysicalCores();

                s.GpuName = QueryGpuName();
                s.BiosVersion = RegStr(Microsoft.Win32.Registry.LocalMachine,
                    @"HARDWARE\DESCRIPTION\System\BIOS", "BIOSVersion");
                s.BaseBoard = RegStr(Microsoft.Win32.Registry.LocalMachine,
                    @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct");
            }

            if (s.CpuCores <= 0) s.CpuCores = s.CpuThreads;
            if (string.IsNullOrEmpty(s.CpuName)) s.CpuName = "未知处理器";
            if (string.IsNullOrEmpty(s.GpuName)) s.GpuName = "未检测到";
            return s;
        }

        /// <summary>物理核心数：每个进程仅查询一次 WMI 并缓存（无注册表直读来源）。</summary>
        private static int _cachedCores = -1;
        private static int QueryPhysicalCores()
        {
            if (_cachedCores >= 0) return _cachedCores;
            string cores = WmiString("SELECT NumberOfCores FROM Win32_Processor", "NumberOfCores");
            int c;
            _cachedCores = int.TryParse(cores, out c) ? c : 0;
            return _cachedCores;
        }

        /// <summary>显卡名称：从显示适配器类驱动的注册表项读取。</summary>
        private static string QueryGpuName()
        {
            const string gpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 10; i++)
            {
                string sub = gpuClass + @"\000" + i.ToString();
                string desc = RegStr(Microsoft.Win32.Registry.LocalMachine, sub, "DriverDesc");
                if (!string.IsNullOrEmpty(desc)) return desc;
            }
            return "";
        }

        private static string SafeComputerName()
        {
            try { return Environment.MachineName; }
            catch { return "未知"; }
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName; }
            catch { return "未知"; }
        }

        // ---------- 操作系统版本 ----------

        public sealed class OSVersionInfo
        {
            public string Name = "Windows";
            public string Version = "";
            public string Arch = "";
        }

        public static OSVersionInfo GetOsVersion()
        {
            OSVersionInfo info = new OSVersionInfo();
            try
            {
                info.Arch = Environment.Is64BitOperatingSystem ? "64 位" : "32 位";

                // 注册表直读（毫秒级），替代 WMI 查询
                string product = RegStr(Microsoft.Win32.Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
                string build = RegStr(Microsoft.Win32.Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");

                if (!string.IsNullOrEmpty(product))
                {
                    // Windows 11（Build >= 22000）的 ProductName 仍写作 Windows 10，需修正
                    int buildNum;
                    if (int.TryParse(build, out buildNum) && buildNum >= 22000)
                    {
                        product = product.Replace("Windows 10", "Windows 11");
                    }
                    info.Name = product;
                }

                if (!string.IsNullOrEmpty(build))
                {
                    string display = RegStr(Microsoft.Win32.Registry.LocalMachine,
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
                    info.Version = "10.0." + build + (string.IsNullOrEmpty(display) ? "" : " (" + display + ")");
                }
                else
                {
                    info.Version = Environment.OSVersion.Version.ToString();
                }
            }
            catch
            {
            }
            return info;
        }

        private static string RegStr(Microsoft.Win32.RegistryKey hive, string path, string name)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = hive.OpenSubKey(path, false))
                {
                    if (k == null) return "";
                    object v = k.GetValue(name);
                    return v == null ? "" : v.ToString().Trim();
                }
            }
            catch
            {
                return "";
            }
        }

        // ---------- 实时 CPU 负载 ----------

        /// <summary>
        /// 通过两次采样 Process.GetCurrentProcess 之外的全局 CPU 时间来计算负载。
        /// 需要保留上一次采样结果，因此封装成对象。
        /// </summary>
        public sealed class CpuLoadMeter
        {
            private ulong _lastIdle;
            private ulong _lastKernel;
            private ulong _lastUser;
            private bool _hasPrev;

            [System.Runtime.InteropServices.DllImport("kernel32.dll")]
            private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

            public double Sample()
            {
                try
                {
                    long idle, kernel, user;
                    if (!GetSystemTimes(out idle, out kernel, out user)) return -1;

                    ulong i = (ulong)idle;
                    ulong k = (ulong)kernel;
                    ulong u = (ulong)user;

                    if (!_hasPrev)
                    {
                        _lastIdle = i; _lastKernel = k; _lastUser = u;
                        _hasPrev = true;
                        return 0;
                    }

                    ulong dIdle = i - _lastIdle;
                    ulong dKernel = k - _lastKernel;
                    ulong dUser = u - _lastUser;

                    _lastIdle = i; _lastKernel = k; _lastUser = u;

                    // kernel 时间已包含 idle 时间
                    ulong total = dKernel + dUser;
                    if (total == 0) return 0;
                    double busy = total - dIdle;
                    if (busy < 0) busy = 0;
                    double pct = busy * 100.0 / total;
                    if (pct < 0) pct = 0;
                    if (pct > 100) pct = 100;
                    return pct;
                }
                catch
                {
                    return -1;
                }
            }
        }
    }
}
