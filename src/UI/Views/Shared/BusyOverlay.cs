/* UI/Views/Optimize/BusyOverlay.cs — 加载指示遮罩（轨道双星）。 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 加载指示遮罩：「轨道双星」——两颗光点沿椭圆轨道互相环绕，中心呼吸微光，
    /// 比传统转圈更有辨识度；淡入淡出。
    /// </summary>
    internal sealed class BusyOverlay : Panel
    {
        private float _angle;
        private float _alpha;
        private float _alphaTarget;
        private float _breath;

        // 60fps 动画：画刷/画笔常驻并就地改色。
        // 透明度每帧都在变，走 GdiCache 会因颜色不同而无限膨胀，必须用实例对象。
        private readonly SolidBrush _scrim = new SolidBrush(Color.Transparent);
        private readonly Pen _ringPen = new Pen(Color.Transparent, 4f);
        private readonly Pen _sweepPen = new Pen(Color.Transparent, 4f);
        private readonly Pen _tailPen = new Pen(Color.Transparent, 4f);
        private readonly SolidBrush _textBrush = new SolidBrush(Color.Transparent);

        public BusyOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            // 圆头线帽只需设置一次，不必每帧重设
            _ringPen.StartCap = LineCap.Round;
            _ringPen.EndCap = LineCap.Round;
            _sweepPen.StartCap = LineCap.Round;
            _sweepPen.EndCap = LineCap.Round;
            _tailPen.StartCap = LineCap.Round;
            _tailPen.EndCap = LineCap.Round;

            // 自旋与呼吸由全局 AnimationClock 驱动（16ms 粒度）；淡出收敛后由 SpinTick 自行退订并隐藏
        }

        public void StartSpin()
        {
            _alphaTarget = 1f;
            if (_alpha < 0.05f) _alpha = 0f;
            AnimationClock.Instance.Subscribe(SpinTick);
        }

        public void StopSpin()
        {
            _alphaTarget = 0f;
            // 不再创建延迟 Timer：淡出收敛到 0 后由 SpinTick 自行退订并隐藏，
            // 中途被 StartSpin 打断（_alphaTarget 重设 1）则不会误隐藏。
            AnimationClock.Instance.Subscribe(SpinTick);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            _scrim.Color = Gfx.Alpha(Theme.WindowBg, (int)(170 * _alpha));
            g.FillRectangle(_scrim, ClientRectangle);
            if (_alpha < 0.05f) return;

            // 流畅的弧线扫掠（macOS 风格）：弧长与转速呼吸联动，圆帽端点
            int r = 26;
            int cx = Width / 2, cy = Height / 2 - 12;

            // 底环：极淡的完整圆，给出稳定轮廓
            _ringPen.Color = Gfx.Alpha(Theme.Border, (int)(90 * _alpha));
            g.DrawArc(_ringPen, cx - r, cy - r, r * 2, r * 2, 0, 360);

            // 主弧：长度 60°~240° 呼吸，位置旋转，主色渐变尾迹
            float sweep = 60f + 60f * (0.5f + 0.5f * (float)Math.Sin(_breath * 1.7f));
            _sweepPen.Color = Gfx.Alpha(Theme.Accent, (int)(255 * _alpha));
            g.DrawArc(_sweepPen, cx - r, cy - r, r * 2, r * 2, _angle, sweep);

            // 尾迹弧：主弧后方 10°，紫色低透明度，形成层次
            _tailPen.Color = Gfx.Alpha(Theme.Purple, (int)(110 * _alpha));
            g.DrawArc(_tailPen, cx - r, cy - r, r * 2, r * 2, _angle + sweep + 10, 22f);

            _textBrush.Color = Gfx.Alpha(Theme.TextSecondary, (int)(255 * _alpha));
            g.DrawString("正在处理…", Theme.FontBody, _textBrush,
                new Rectangle(0, cy + r + 16, Width, 22), GdiCache.Center);
        }

        private void SpinTick()
        {
            bool alphaChanged = Math.Abs(_alphaTarget - _alpha) >= 0.02f;
            // 每帧步进 = 360° / 周期：统一为 Theme.Motion.LoopSpin（1000ms 一圈）
            _angle = (_angle + 360f * 16f / Theme.Motion.LoopSpin) % 360f;
            _breath += Theme.Motion.BreathStep;
            _alpha += (_alphaTarget - _alpha) * Theme.Motion.FadeLerp;
            if (Math.Abs(_alphaTarget - _alpha) < 0.02f) _alpha = _alphaTarget;
            // 只重绘动画区域（中心圆环附近），避免 60fps 全区域重绘引发闪烁
            Rectangle animRect = new Rectangle(
                Width / 2 - 44, Height / 2 - 44, 88, 88);
            Invalidate(animRect);
            if (alphaChanged) Invalidate(); // 淡入淡出期间才需要全区域重绘
            // 淡出收敛完成且未被重新唤起时，自动停转并隐藏（取代原 300ms stopper 延迟）
            if (_alphaTarget == 0f && Math.Abs(_alpha) < 0.02f)
            {
                _alpha = 0f;
                AnimationClock.Instance.Unsubscribe(SpinTick);
                Hide();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AnimationClock.Instance.Unsubscribe(SpinTick);

                _scrim.Dispose();
                _ringPen.Dispose();
                _sweepPen.Dispose();
                _tailPen.Dispose();
                _textBrush.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
