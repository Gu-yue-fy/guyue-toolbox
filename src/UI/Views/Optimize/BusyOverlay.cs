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
        private readonly Timer _spin;
        private float _angle;
        private float _alpha;
        private float _alphaTarget;
        private float _breath;

        public BusyOverlay()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _spin = new Timer();
            _spin.Interval = 16;
            _spin.Tick += delegate
            {
                bool alphaChanged = Math.Abs(_alphaTarget - _alpha) >= 0.02f;
                _angle = (_angle + 4.2f) % 360f;
                _breath += 0.09f;
                _alpha += (_alphaTarget - _alpha) * 0.3f;
                if (Math.Abs(_alphaTarget - _alpha) < 0.02f) _alpha = _alphaTarget;
                // 只重绘动画区域（中心圆环附近），避免 60fps 全区域重绘引发闪烁
                Rectangle animRect = new Rectangle(
                    Width / 2 - 44, Height / 2 - 44, 88, 88);
                Invalidate(animRect);
                if (alphaChanged) Invalidate(); // 淡入淡出期间才需要全区域重绘
            };
        }

        public void StartSpin()
        {
            _alphaTarget = 1f;
            if (_alpha < 0.05f) _alpha = 0f;
            _spin.Start();
        }

        public void StopSpin()
        {
            _alphaTarget = 0f;
            Timer stopper = new Timer();
            stopper.Interval = 300;
            stopper.Tick += delegate
            {
                stopper.Stop();
                stopper.Dispose();
                if (_alphaTarget == 0f)
                {
                    _spin.Stop();
                    Hide();
                }
            };
            stopper.Start();
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

            using (SolidBrush b = new SolidBrush(Gfx.Alpha(Theme.WindowBg, (int)(170 * _alpha))))
            {
                g.FillRectangle(b, ClientRectangle);
            }
            if (_alpha < 0.05f) return;

            // 流畅的弧线扫掠（macOS 风格）：弧长与转速呼吸联动，圆帽端点
            int r = 26;
            int cx = Width / 2, cy = Height / 2 - 12;

            // 底环：极淡的完整圆，给出稳定轮廓
            using (Pen pen = new Pen(Gfx.Alpha(Theme.Border, (int)(90 * _alpha)), 4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 0, 360);
            }

            // 主弧：长度 60°~240° 呼吸，位置旋转，主色渐变尾迹
            float sweep = 60f + 60f * (0.5f + 0.5f * (float)Math.Sin(_breath * 1.7f));
            using (Pen pen = new Pen(Gfx.Alpha(Theme.Accent, (int)(255 * _alpha)), 4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, _angle, sweep);
            }

            // 尾迹弧：主弧后方 12°，紫色低透明度，形成层次
            using (Pen pen = new Pen(Gfx.Alpha(Theme.Purple, (int)(110 * _alpha)), 4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, _angle + sweep + 10, 22f);
            }

            using (SolidBrush b = new SolidBrush(Gfx.Alpha(Theme.TextSecondary, (int)(255 * _alpha))))
            {
                g.DrawString("正在处理…", Theme.FontBody, b,
                    new Rectangle(0, cy + r + 16, Width, 22), GdiCache.Center);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 忙碌中被销毁时停表，防止定时器继续触发并持有已释放控件
                if (_spin != null) _spin.Stop();
                _spin.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
