using System;
using System.Runtime.InteropServices;

namespace GuyueBox.Core
{
    /// <summary>
    /// 高精度定时器：让系统定时器精度提升到 0.5ms（默认 15.6ms）。
    /// 效果只在本进程存活期间有效，进程退出后系统自动恢复——天然可回退。
    /// </summary>
    public static class TimerResolution
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desiredMs, bool enable, out uint currentMs);

        private const uint HalfMs = 5000; // 单位 100ns，5000 = 0.5ms

        /// <summary>请求高精度定时器。返回是否成功，applied 为系统实际精度。</summary>
        public static bool Enable(out double appliedMs)
        {
            uint cur;
            int status = NtSetTimerResolution(HalfMs, true, out cur);
            appliedMs = status == 0 ? cur / 10000.0 : -1;
            return status == 0;
        }

        /// <summary>释放高精度定时器请求。</summary>
        public static void Disable()
        {
            uint cur;
            try { NtSetTimerResolution(HalfMs, false, out cur); }
            catch { }
        }
    }
}
