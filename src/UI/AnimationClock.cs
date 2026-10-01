/* ============================================================
 * 文件说明：全局动画时钟：用单个 Timer 驱动全站过渡动画，取代各控件分散持有的 Timer。
 * 项目：古月工具箱（GuyueBox）
 *
 * 设计：订阅者模式。控件调用 Subscribe(回调) 登记每帧推进逻辑，动画完成时调用
 * Unsubscribe 退出驱动；订阅表为空时钟自动停止，无空转。Tick 遍历期间允许订阅者
 * 安全地增删自身（经延迟队列），避免枚举中修改集合导致的异常。
 *
 * 订阅者清单（阶段1 收敛，原分散 Timer 已全部迁入）：
 *   - NavSideItem / TweakRow / ToggleSwitch / Toast / FeatureTile / Buttons / Cards：状态过渡
 *   - Chips.Spinner / BusyOverlay：旋转与加载遮罩（BusyOverlay 灭掉了原 300ms 延迟 stopper）
 *   - Entries.HealthCard：得分滚动 / 体检扫描
 *   - ViewBase：页面元素级联入场
 *   - OptimizeView：优化项详情栏滑入滑出
 *   - DarkGrid / ScrollHost：平滑滚动（指数插值，经 SmoothScroll 统一封装）
 *   - MainForm.Nav：切页 8px 缓入
 * 保留的数据轮询（非视觉动画，不迁入）：MainForm._statusTimer / ViewBase._busyPoll /
 *   ProcessView / TimerView / DashboardView。
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 全站唯一的动画时钟。所有 16ms 级过渡动画（悬停 / 选中 / 开关 / 淡入…）共用这一个 Timer，
    /// 取代原先「每控件一个 Timer」的分散模式，降低计时器实例数与潜在空转 / 重叠。
    /// </summary>
    public sealed class AnimationClock
    {
        private static readonly AnimationClock _instance = new AnimationClock();

        /// <summary>全局唯一实例。</summary>
        public static AnimationClock Instance { get { return _instance; } }

        private readonly Timer _timer;
        private readonly List<Action> _subs = new List<Action>();
        private readonly List<Action> _toAdd = new List<Action>();
        private readonly List<Action> _toRemove = new List<Action>();
        private bool _iterating;

        private AnimationClock()
        {
            _timer = new Timer { Interval = 16 };
            _timer.Tick += delegate
            {
                _iterating = true;
                try
                {
                    for (int i = 0; i < _subs.Count; i++)
                    {
                        Action a = _subs[i];
                        if (a != null)
                        {
                            try { a(); }
                            catch { }
                        }
                    }
                }
                finally { _iterating = false; }

                // 本帧遍历期间挂起的订阅变更（控件在回调里对自己 Unsubscribe/Subscribe）
                // 延迟到遍历结束再应用，避免「枚举中修改集合」异常。
                if (_toRemove.Count > 0)
                {
                    foreach (Action r in _toRemove) _subs.Remove(r);
                    _toRemove.Clear();
                }
                if (_toAdd.Count > 0)
                {
                    foreach (Action a in _toAdd) if (!_subs.Contains(a)) _subs.Add(a);
                    _toAdd.Clear();
                }
                if (_subs.Count == 0) _timer.Stop();
            };
        }

        /// <summary>登记每帧回调。重复登记同一回调无效；登记后时钟自动启动。</summary>
        public void Subscribe(Action tick)
        {
            if (tick == null) return;
            if (_iterating)
            {
                if (!_toAdd.Contains(tick)) _toAdd.Add(tick);
            }
            else if (!_subs.Contains(tick))
            {
                _subs.Add(tick);
            }
            if (!_timer.Enabled) _timer.Start();
        }

        /// <summary>注销每帧回调。遍历中途调用会延迟到本帧结束生效。</summary>
        public void Unsubscribe(Action tick)
        {
            if (tick == null) return;
            if (_iterating)
            {
                if (!_toRemove.Contains(tick)) _toRemove.Add(tick);
            }
            else _subs.Remove(tick);
        }
    }
}
