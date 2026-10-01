using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GuyueBox.Core
{
    /// <summary>
    /// 「删不掉 / 移动不了」的文件占用排查。
    /// 用系统自带的 Restart Manager（rstrtmgr.dll）查询占用进程——
    /// 这是 Windows 安装程序判定"文件被谁占用"的官方接口，比遍历句柄更准、无需驱动。
    /// </summary>
    public static class Unlocker
    {
        /// <summary>一个占用项（进程或服务）。</summary>
        public sealed class LockInfo
        {
            public int Pid;
            public string Name;       // 进程名（取不到时用 RM 给的 AppName）
            public string AppName;    // RM 提供的应用显示名
            public string Service;    // 服务短名（服务占用时非空）
            public string AppType;    // 主窗口 / 后台 / 服务 / 资源管理器…
            public string Path;       // 进程可执行文件路径（可能无权限读取）
        }

        /// <summary>目录展开成文件清单时的上限：RM 只接受文件路径，目录必须先展开。</summary>
        private const int MaxExpand = 400;

        /// <summary>把用户给的路径展开成待查文件清单（目录只取第一层，避免几千个文件拖慢查询）。</summary>
        public static List<string> ExpandPaths(string path, out string error)
        {
            error = null;
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(path))
            {
                error = "路径为空。";
                return list;
            }

            try
            {
                if (File.Exists(path))
                {
                    list.Add(path);
                    return list;
                }
                if (Directory.Exists(path))
                {
                    string[] files = Directory.GetFiles(path);
                    for (int i = 0; i < files.Length && list.Count < MaxExpand; i++) list.Add(files[i]);
                    if (list.Count == 0) error = "该文件夹下没有文件（只查第一层）。";
                    return list;
                }
                error = "路径不存在。";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return list;
        }

        /// <summary>查询占用指定文件的进程 / 服务；无占用时返回空列表。</summary>
        public static List<LockInfo> FindLockers(IList<string> files, out string error)
        {
            error = null;
            List<LockInfo> result = new List<LockInfo>();
            if (files == null || files.Count == 0) return result;

            uint handle = 0;
            string key = Guid.NewGuid().ToString();
            int rv = RmStartSession(out handle, 0, key);
            if (rv != 0)
            {
                error = "无法启动占用查询会话（错误码 " + rv + "）。";
                return result;
            }

            try
            {
                string[] names = new string[files.Count];
                for (int i = 0; i < files.Count; i++) names[i] = files[i];

                rv = RmRegisterResources(handle, (uint)names.Length, names, 0, null, 0, null);
                if (rv != 0)
                {
                    error = "登记待查文件失败（错误码 " + rv + "）。";
                    return result;
                }

                uint needed = 0;
                uint count = 0;
                uint reasons = 0;
                rv = RmGetList(handle, out needed, ref count, null, ref reasons);
                if (rv == 0 && needed == 0) return result;   // 无占用
                if (needed == 0)
                {
                    error = "查询占用失败（错误码 " + rv + "）。";
                    return result;
                }

                // 第一次调用用 0 长度拿 needed；这里按所需数量重查
                RM_PROCESS_INFO[] infos = new RM_PROCESS_INFO[needed];
                count = needed;
                rv = RmGetList(handle, out needed, ref count, infos, ref reasons);
                if (rv != 0)
                {
                    error = "读取占用进程失败（错误码 " + rv + "）。";
                    return result;
                }

                for (uint i = 0; i < count; i++)
                {
                    RM_PROCESS_INFO info = infos[i];
                    LockInfo li = new LockInfo();
                    li.Pid = info.Process.dwProcessId;
                    li.AppName = info.strAppName;
                    li.Service = info.strServiceShortName;
                    li.AppType = TypeText(info.ApplicationType);
                    li.Name = li.AppName;

                    try
                    {
                        Process p = Process.GetProcessById(li.Pid);
                        li.Name = p.ProcessName + ".exe";
                        try { li.Path = p.MainModule.FileName; } catch { li.Path = ""; }
                    }
                    catch
                    {
                        // 进程可能已退出或权限不足：保留 RM 给的名字
                    }
                    result.Add(li);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                try { RmEndSession(handle); } catch { }
            }

            return result;
        }

        /// <summary>结束一个进程（先温和请求关闭，失败再强杀，与任务管理器一致的两段式）。</summary>
        public static bool Kill(int pid, out string error)
        {
            error = null;
            try
            {
                Process p = Process.GetProcessById(pid);
                bool closed = false;
                try { closed = p.CloseMainWindow(); } catch { }
                if (closed && p.WaitForExit(1500)) return true;

                p.Kill();
                p.WaitForExit(1500);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string TypeText(RM_APP_TYPE t)
        {
            switch (t)
            {
                case RM_APP_TYPE.RmMainWindow: return "有主窗口";
                case RM_APP_TYPE.RmOtherWindow: return "后台窗口";
                case RM_APP_TYPE.RmService: return "系统服务";
                case RM_APP_TYPE.RmExplorer: return "资源管理器";
                case RM_APP_TYPE.RmConsole: return "控制台程序";
                case RM_APP_TYPE.RmCritical: return "关键进程";
                default: return "后台进程";
            }
        }

        // --------------------------------------------------------------
        // Restart Manager P/Invoke
        // --------------------------------------------------------------

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames,
            uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);
    }
}
