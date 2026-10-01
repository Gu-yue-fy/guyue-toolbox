/* ============================================================
 * 文件说明：平滑滚动器：把「订阅 AnimationClock 每帧向目标值指数插值」的通用逻辑
 * 从 ScrollHost / DarkGrid 各自复制的实现收口到一处，消除重复。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;

namespace GuyueBox.UI
{
    /// <summary>
    /// 平滑滚动器：把「目标值 + 每帧指数插值逼近」的动画逻辑与具体控件解耦。
    /// 调用方只需提供当前值读取、新值应用两个回调，以及收敛参数
    /// （lerp 系数、吸附阈值）。差值小于阈值时直接吸附到目标并自动退订时钟，无空转。
    /// 读取/应用回调抛异常时自动停止，避免控件已释放（切页销毁控件树）后仍空转。
    /// </summary>
    public sealed class SmoothScroll
    {
        private readonly AnimationClock _clock = AnimationClock.Instance;
        private readonly Func<double> _read;     // 读当前位置（像素或行索引）
        private readonly Action<double> _apply;  // 应用新位置（含必要的夹紧/重绘）
        private readonly double _lerp;           // 指数插值系数：越小越柔、越大越快收敛
        private readonly double _snap;           // 差值吸附阈值（低于即直接落位并停止）
        private readonly Action _done;           // 收敛完成回调（可选）

        private double _target;
        private bool _running;

        public SmoothScroll(Func<double> read, Action<double> apply, double lerp, double snap, Action done = null)
        {
            _read = read;
            _apply = apply;
            _lerp = lerp;
            _snap = snap;
            _done = done;
        }

        /// <summary>当前目标值（滚轮连续累加时作为下一格的基准）。</summary>
        public double Target { get { return _target; } }

        /// <summary>是否正在缓动中。</summary>
        public bool IsRunning { get { return _running; } }

        /// <summary>设定新目标并启动缓动；若已运行则仅更新目标（动画自然平滑改向）。</summary>
        public void AnimateTo(double target)
        {
            _target = target;
            if (!_running)
            {
                _running = true;
                _clock.Subscribe(Tick);
            }
        }

        /// <summary>立即到位并停止动画（拖动 / 程序化跳转时用，避免残留动画与第一帧冲突）。</summary>
        public void JumpTo(double value)
        {
            Stop();
            _apply(value);
        }

        /// <summary>停止缓动（保留当前位置，不修改目标）。</summary>
        public void Stop()
        {
            if (_running)
            {
                _running = false;
                _clock.Unsubscribe(Tick);
            }
        }

        private void Tick()
        {
            double cur;
            try { cur = _read(); }
            catch { Stop(); return; }

            double diff = _target - cur;
            if (Math.Abs(diff) < _snap)
            {
                _apply(_target);
                Stop();
                if (_done != null) _done();
                return;
            }

            try { _apply(cur + diff * _lerp); }
            catch { Stop(); }
        }
    }
}
