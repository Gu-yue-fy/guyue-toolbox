using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>
    /// Win32 API 封装。仅包含工具箱实际使用到的少量调用。
    /// </summary>
    internal static class Native
    {
        // ---------- 内存 ----------

        /// <summary>
        /// GlobalMemoryStatusEx 的输出结构。字段的顺序与个数由 Win32 布局决定：
        /// 即使某些字段本程序未读取，也不得删除或重排，否则其后字段会整体错位。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        // ---------- 回收站 ----------

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public const uint SHERB_NOCONFIRMATION = 0x00000001;
        public const uint SHERB_NOPROGRESSUI = 0x00000002;
        public const uint SHERB_NOSOUND = 0x00000004;

        /// <summary>
        /// 查询回收站信息。返回 true 表示查询成功（info 已填充）；
        /// 返回 false 表示失败（如回收站被禁用、拒绝访问）——此时 info 清零，调用方应如实报错，
        /// 而非把"查不到"误当成"回收站为空"。SHQueryRecycleBin 返回 HRESULT，0=成功，失败不抛异常。
        /// </summary>
        public static bool QueryRecycleBin(out SHQUERYRBINFO info)
        {
            info = new SHQUERYRBINFO();
            info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            try
            {
                return SHQueryRecycleBin(null, ref info) == 0; // S_OK
            }
            catch
            {
                info.i64Size = 0;
                info.i64NumItems = 0;
                return false;
            }
        }

        // ---------- 无边框窗体拖动 ----------

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 0x2;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        /// <summary>为单行输入框设置占位提示（失焦或空值时显示的灰色水印文字）。
        /// 用 EM_SETCUEBANNER，比自行用 Got/LostFocus 切换文本更稳，也不会污染 Text 取值。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

        public static void SetCue(System.Windows.Forms.TextBox tb, string cue)
        {
            SendMessage(tb.Handle, 0x1501 /*EM_SETCUEBANNER*/, 1, cue);
        }

        public static void DragWindow(IntPtr handle)
        {
            ReleaseCapture();
            SendMessage(handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        // ---------- 内存整理（空工作集） ----------

        [DllImport("psapi.dll")]
        public static extern int EmptyWorkingSet(IntPtr hProcess);

        // ---------- 权限 ----------

        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

    }
}
