using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GuyueBox.Core
{
    /// <summary>
    /// 高精度定时器（正确实现）：
    /// 1. NtSetTimerResolution 请求的精度可能被系统在特定事件后悄悄回落，
    ///    因此必须由**专用维持线程**周期性重发请求——社区 TimerResolution 类工具均为此实现；
    /// 2. 查询当前生效精度用 **NtQueryTimerResolution**（纯只读），
    ///    绝不能用 NtSetTimerResolution(…, false, …) 去"查"——那是撤销请求，会把已开启的精度撤销掉；
    /// 3. 停止 = 停维持线程 + 按最后一次请求值撤销。
    /// 效果仅在本进程存活期间有效，进程退出后系统自动恢复。
    /// </summary>
    public static class TimerResolution
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSetTimerResolution(uint desiredMs, bool enable, out uint currentMs);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryTimerResolution(out uint minMs, out uint maxMs, out uint currentMs);

        private static Thread _keepAlive;
        private static volatile bool _keepRunning;
        private static double _targetMs = -1;
        private static readonly object _lock = new object();

        /// <summary>以毫秒为单位请求指定精度（100ns 内部单位换算）。返回是否成功，applied 为系统实际生效值。</summary>
        public static bool Request(double desiredMs, out double appliedMs)
        {
            uint desired = (uint)Math.Round(desiredMs * 10000.0);
            uint cur;
            int status = NtSetTimerResolution(desired, true, out cur);
            appliedMs = status == 0 ? cur / 10000.0 : -1;
            return status == 0;
        }

        /// <summary>
        /// 只读查询当前系统生效精度（毫秒），不影响任何请求状态。失败返回 -1。
        /// </summary>
        public static double Current()
        {
            uint min, max, cur;
            int status = NtQueryTimerResolution(out min, out max, out cur);
            return status == 0 ? cur / 10000.0 : -1;
        }

        /// <summary>
        /// 开启维持线程：以目标精度周期性重发请求（300ms 间隔），
        /// 抵御系统在电源事件/其他程序退出后的精度回落。返回首次请求是否成功。
        /// </summary>
        public static bool Start(double targetMs, out double appliedMs)
        {
            lock (_lock)
            {
                StopKeepAlive();
                bool firstOk = Request(targetMs, out appliedMs);
                if (!firstOk) return false;
                _targetMs = targetMs;
                _keepRunning = true;
                _keepAlive = new Thread(KeepAliveLoop);
                _keepAlive.IsBackground = true;
                _keepAlive.Priority = ThreadPriority.BelowNormal;
                _keepAlive.Start();
                return true;
            }
        }

        /// <summary>停止维持并撤销请求（恢复系统默认调度）。</summary>
        public static void Disable()
        {
            lock (_lock)
            {
                StopKeepAlive();
                _targetMs = -1;
                uint cur;
                try { NtSetTimerResolution(1000, false, out cur); }
                catch { }
            }
        }

        /// <summary>本工具是否正在维持高精度请求。</summary>
        public static bool IsKeeping
        {
            get { return _keepRunning && _targetMs > 0; }
        }

        /// <summary>维持线程的目标精度（未维持时 -1）。</summary>
        public static double TargetMs
        {
            get { return _targetMs; }
        }

        private static void KeepAliveLoop()
        {
            while (_keepRunning)
            {
                double t = _targetMs;
                if (t > 0)
                {
                    uint cur;
                    NtSetTimerResolution((uint)Math.Round(t * 10000.0), true, out cur);
                }
                for (int i = 0; i < 30 && _keepRunning; i++) Thread.Sleep(10); // 300ms，可快速响应停止
            }
        }

        private static void StopKeepAlive()
        {
            _keepRunning = false;
            Thread t = _keepAlive;
            if (t != null)
            {
                try { t.Join(800); } catch { }
                _keepAlive = null;
            }
        }

        /// <summary>
        /// 实测寻优：从 0.5ms 起以 0.001ms 步进逐档请求到 1.0ms，读回实际生效值，
        /// 返回「生效延迟最低」的请求值。bestApplied 带出该档生效精度；本机完全不可用时返回 -1。
        /// </summary>
        public static double FindBestRequest(out double bestApplied)
        {
            double bestReq = -1;
            bestApplied = double.MaxValue;
            for (double req = 0.5; req <= 1.0001; req += 0.001)
            {
                double applied;
                if (Request(req, out applied) && applied > 0 && applied < bestApplied)
                {
                    bestApplied = applied;
                    bestReq = req;
                    if (applied <= 0.50001) break; // 已达理论下限（Win11 可直上 0.5）
                }
            }
            if (bestReq < 0) { bestApplied = -1; return -1; }
            return bestReq;
        }
    }
}
