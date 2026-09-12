using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SysToolbox.Core
{
    /// <summary>一个 Windows 服务。</summary>
    public sealed class ServiceInfo
    {
        public string Name = "";
        public string DisplayName = "";
        public string StartMode = "";
        public string State = "";
        public string Description = "";
        public uint ProcessId;

        public bool IsRunning
        {
            get
            {
                return string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase) ||
                       State.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public string StartText
        {
            get
            {
                switch (StartMode)
                {
                    case "Automatic": return "自动";
                    case "Manual": return "手动";
                    case "Disabled": return "已禁用";
                    case "Boot": return "引导";
                    case "System": return "系统";
                    default: return string.IsNullOrEmpty(StartMode) ? "未知" : StartMode;
                }
            }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case "Running": return "运行中";
                    case "Stopped": return "已停止";
                    case "Start Pending": return "启动中";
                    case "Stop Pending": return "停止中";
                    case "Paused": return "已暂停";
                    default: return string.IsNullOrEmpty(State) ? "未知" : State;
                }
            }
        }
    }

    /// <summary>
    /// 通过原生 Service 控制管理器 (advapi32) 管理 Windows 服务。
    /// 相比 WMI 枚举快一个数量级（冷启动从 1-3 秒降至 0.1 秒左右）。
    /// </summary>
    public static class ServiceManager
    {
        // --------------------------------------------------------------
        // 原生 API
        // --------------------------------------------------------------

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machine, string database, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr sc, string name, uint access);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumServicesStatusEx(
            IntPtr sc, int infoLevel, uint serviceType, uint serviceState,
            IntPtr buffer, uint bufSize, out uint bytesNeeded,
            out uint servicesReturned, out uint resumeHandle, string groupName);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig(
            IntPtr service, IntPtr buffer, uint bufSize, out uint bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig2(
            IntPtr service, uint infoLevel, IntPtr buffer, uint bufSize, out uint bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(
            IntPtr service, uint serviceType, uint startType, uint errorControl,
            string binaryPath, string loadOrderGroup, IntPtr tagId,
            string dependencies, string startName, string password, string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr service, int argc, string[] argv);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS status);

        private const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
        private const uint SERVICE_WIN32 = 0x00000030;
        private const uint SERVICE_STATE_ALL = 0x00000003;
        private const int SC_STATUS_PROCESS_INFO = 0;
        private const uint SERVICE_QUERY_CONFIG = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_STOP = 0x0020;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint SERVICE_CONTROL_STOP = 0x0001;
        private const uint SERVICE_CONFIG_DESCRIPTION = 1;

        private const uint SERVICE_AUTO_START = 2;
        private const uint SERVICE_DEMAND_START = 3;
        private const uint SERVICE_DISABLED = 4;
        private const uint SERVICE_BOOT_START = 0;
        private const uint SERVICE_SYSTEM_START = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct ENUM_SERVICE_STATUS_PROCESS
        {
            public IntPtr ServiceName;
            public IntPtr DisplayName;
            public SERVICE_STATUS_PROCESS Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
            public uint ProcessId;
            public uint ServiceFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QUERY_SERVICE_CONFIG
        {
            public uint ServiceType;
            public uint StartType;
            public uint ErrorControl;
            public IntPtr BinaryPathName;
            public IntPtr LoadOrderGroup;
            public uint TagId;
            public IntPtr Dependencies;
            public IntPtr StartName;
            public IntPtr DisplayName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_DESCRIPTION
        {
            public IntPtr Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
        }

        // --------------------------------------------------------------
        // 枚举
        // --------------------------------------------------------------

        public static List<ServiceInfo> List()
        {
            List<ServiceInfo> list = new List<ServiceInfo>();

            IntPtr sc = OpenSCManager(null, null, SC_MANAGER_ENUMERATE_SERVICE);
            if (sc == IntPtr.Zero) return list;

            try
            {
                uint size = 64 * 1024;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    IntPtr buf = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        uint needed, returned, resume;
                        if (!EnumServicesStatusEx(sc, SC_STATUS_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
                            buf, size, out needed, out returned, out resume, null))
                        {
                            if (Marshal.GetLastWin32Error() == 234 && needed > size)
                            {
                                size = needed;
                                continue;
                            }
                            break;
                        }

                        int itemSize = Marshal.SizeOf(typeof(ENUM_SERVICE_STATUS_PROCESS));
                        for (int i = 0; i < (int)returned; i++)
                        {
                            IntPtr ptr = new IntPtr(buf.ToInt64() + i * itemSize);
                            ENUM_SERVICE_STATUS_PROCESS item =
                                (ENUM_SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(ptr, typeof(ENUM_SERVICE_STATUS_PROCESS));
                            try
                            {
                                ServiceInfo si = new ServiceInfo();
                                si.Name = PtrToString(item.ServiceName);
                                si.DisplayName = PtrToString(item.DisplayName);
                                if (string.IsNullOrEmpty(si.DisplayName)) si.DisplayName = si.Name;
                                si.State = StateName(item.Status.CurrentState);
                                si.ProcessId = item.Status.ProcessId;
                                list.Add(si);
                            }
                            catch
                            {
                            }
                        }
                        break;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
            finally
            {
                try { CloseServiceHandle(sc); } catch { }
            }

            // 批量补齐启动类型与描述
            for (int i = 0; i < list.Count; i++)
            {
                FillDetails(list[i]);
            }

            list.Sort(delegate (ServiceInfo a, ServiceInfo b)
            {
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static void FillDetails(ServiceInfo si)
        {
            IntPtr svc = OpenService(GetSc(), si.Name, SERVICE_QUERY_CONFIG);
            if (svc == IntPtr.Zero) return;
            try
            {
                uint needed;
                // 启动类型
                IntPtr buf = Marshal.AllocHGlobal(2048);
                try
                {
                    if (QueryServiceConfig(svc, buf, 2048, out needed))
                    {
                        QUERY_SERVICE_CONFIG cfg =
                            (QUERY_SERVICE_CONFIG)Marshal.PtrToStructure(buf, typeof(QUERY_SERVICE_CONFIG));
                        si.StartMode = StartName(cfg.StartType);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }

                // 描述
                IntPtr dbuf = Marshal.AllocHGlobal(4096);
                try
                {
                    if (QueryServiceConfig2(svc, SERVICE_CONFIG_DESCRIPTION, dbuf, 4096, out needed))
                    {
                        SERVICE_DESCRIPTION d =
                            (SERVICE_DESCRIPTION)Marshal.PtrToStructure(dbuf, typeof(SERVICE_DESCRIPTION));
                        si.Description = PtrToString(d.Description);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(dbuf);
                }
            }
            catch
            {
            }
            finally
            {
                try { CloseServiceHandle(svc); } catch { }
            }
        }

        private static IntPtr _sc;
        private static IntPtr GetSc()
        {
            if (_sc == IntPtr.Zero)
            {
                _sc = OpenSCManager(null, null, SC_MANAGER_ENUMERATE_SERVICE | SERVICE_QUERY_CONFIG);
            }
            return _sc;
        }

        private static string PtrToString(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(p) ?? "";
        }

        private static string StateName(uint state)
        {
            switch (state)
            {
                case 1: return "Stopped";
                case 2: return "Start Pending";
                case 3: return "Stop Pending";
                case 4: return "Running";
                case 5: return "Continue Pending";
                case 6: return "Pause Pending";
                case 7: return "Paused";
                default: return "未知";
            }
        }

        private static string StartName(uint startType)
        {
            switch (startType)
            {
                case SERVICE_AUTO_START: return "Automatic";
                case SERVICE_DEMAND_START: return "Manual";
                case SERVICE_DISABLED: return "Disabled";
                case SERVICE_BOOT_START: return "Boot";
                case SERVICE_SYSTEM_START: return "System";
                default: return "未知";
            }
        }

        private static uint StartValue(string mode)
        {
            switch (mode)
            {
                case "Automatic": return SERVICE_AUTO_START;
                case "Manual": return SERVICE_DEMAND_START;
                case "Disabled": return SERVICE_DISABLED;
                case "Boot": return SERVICE_BOOT_START;
                case "System": return SERVICE_SYSTEM_START;
                default: return uint.MaxValue;
            }
        }

        // --------------------------------------------------------------
        // 操作
        // --------------------------------------------------------------

        public static bool ChangeStartMode(string name, string mode, out string error)
        {
            error = "";
            uint startType = StartValue(mode);
            if (startType == uint.MaxValue)
            {
                error = "未知的启动类型：" + mode;
                return false;
            }

            IntPtr svc = OpenService(GetSc(), name, SERVICE_CHANGE_CONFIG);
            if (svc == IntPtr.Zero)
            {
                error = "无法打开服务（需要管理员权限）。";
                return false;
            }
            try
            {
                // 仅修改 StartType，其余字段传 null 保持不变
                if (!ChangeServiceConfig(svc, 0xFFFFFFFF, startType, 0xFFFFFFFF,
                    null, null, IntPtr.Zero, null, null, null, null))
                {
                    error = "修改失败（错误码 " + Marshal.GetLastWin32Error() + "），需要管理员权限。";
                    return false;
                }
                return true;
            }
            finally
            {
                try { CloseServiceHandle(svc); } catch { }
            }
        }

        public static bool Start(string name, out string error)
        {
            error = "";
            IntPtr svc = OpenService(GetSc(), name, SERVICE_START | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
            {
                error = "无法打开服务（需要管理员权限）。";
                return false;
            }
            try
            {
                if (!StartService(svc, 0, null))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1056) return true; // 已在运行
                    error = "启动失败（错误码 " + err + "）。";
                    return false;
                }
                return true;
            }
            finally
            {
                try { CloseServiceHandle(svc); } catch { }
            }
        }

        public static bool Stop(string name, out string error)
        {
            error = "";
            IntPtr svc = OpenService(GetSc(), name, SERVICE_STOP | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
            {
                error = "无法打开服务（需要管理员权限）。";
                return false;
            }
            try
            {
                SERVICE_STATUS status = new SERVICE_STATUS();
                if (!ControlService(svc, SERVICE_CONTROL_STOP, ref status))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1062) return true; // 未在运行
                    error = "停止失败（错误码 " + err + "）。有依赖此服务的服务需要先停止。";
                    return false;
                }
                return true;
            }
            finally
            {
                try { CloseServiceHandle(svc); } catch { }
            }
        }
    }
}
