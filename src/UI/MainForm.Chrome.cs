﻿/* ============================================================
 * 文件说明：无边框窗框与窗口过程：自绘描边与内侧高光、窗口消息处理（命中测试 / 缩放 / DWM 标题栏）。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{
    public sealed partial class MainForm
    {
        // ==============================================================
        // 无边框窗框：渐变形描边 + 内侧高光（设计 Shell.Frame）
        // ==============================================================

        /// <summary>
        /// 自绘窗框。窗体外圈 1px 由 Padding 让出，绘制竖向三停渐变描边
        /// （上 #52647D → 侧 #334155 → 下 #1E293B），并叠一道内侧高光。
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Rectangle rc = ClientRectangle;

            SolidBrush b = GdiCache.Brush(Theme.ShellBg);
            {
                g.FillRectangle(b, rc);
            }

            // 窗框渐变画笔走缓存：窗口拉伸时本方法每帧都会被调用，
            // 原先每帧新建 LinearGradientBrush + ColorBlend + Pen。
            g.DrawRectangle(FramePen(rc.Height), 0, 0, rc.Width - 1, rc.Height - 1);

            // 内高光：上缘通亮，左右两侧渐隐（设计 Shell.Frame.Brush.InnerHighlight）
            g.DrawLine(GdiCache.Pen(Theme.FrameInnerHighlight, 1f), 1, 1, rc.Width - 2, 1);
            g.DrawLine(GdiCache.Pen(Theme.LightBeam, 1f), 1, 1, 1, rc.Height - 2);
            g.DrawLine(GdiCache.Pen(Theme.LightBeam, 1f), rc.Width - 2, 1, rc.Width - 2, rc.Height - 2);
        }

        // ------------------------------------------------------------------
        // 切页过渡：标题与状态文字淡入 + 轻微位移
        // ------------------------------------------------------------------
        private const int CueSteps = 11;      // 11 帧 × 16ms ≈ 180ms
        private int _cueStep;                 // 0 = 已就位（不做动画）
        private bool _cueRunning;

        /// <summary>
        /// 切页时给顶栏标题与状态栏文字跑一次淡入过渡。
        /// 只动文字（顶栏/状态栏都是自绘，可精确控制 alpha 与位移）——
        /// 整页做透明度在 WinForms 里要动分层窗口，代价远大于收益。
        /// </summary>
        private void PlaySwitchCue()
        {
            // 关掉动画，或处于截图/巡检测试模式时不动画：
            // 回归基线必须确定（否则每张图都拍在过渡的不同帧上，像素比对全是噪声）。
            if (!AppSettings.Animations || TestMode)
            {
                _cueStep = 0;
                _topBar.Invalidate();
                _statusBar.Invalidate();
                return;
            }

            _cueStep = CueSteps;
            if (_cueRunning) return;
            _cueRunning = true;
            AnimationClock.Instance.Subscribe(CueTick);
        }

        private void CueTick()
        {
            if (_cueStep <= 0)
            {
                _cueRunning = false;
                AnimationClock.Instance.Unsubscribe(CueTick);
                return;
            }
            _cueStep--;
            _topBar.Invalidate();
            _statusBar.Invalidate();
        }

        /// <summary>过渡进度 0..1（二次缓出：先快后慢，落位不生硬）。</summary>
        private float CueProgress()
        {
            if (_cueStep <= 0) return 1f;
            float t = 1f - (float)_cueStep / CueSteps;
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>
        /// 过渡期的文字颜色：进度 1 时直接用原色（不做无谓的 alpha 合成），
        /// 过渡中按进度给 alpha。
        /// </summary>
        private static Color CueColor(Color target, float t)
        {
            if (t >= 1f) return target;
            int a = (int)Math.Round(target.A * t);
            if (a < 0) a = 0;
            return Color.FromArgb(a, target);
        }

        /// <summary>过渡期的文字位移（像素，从右往左落位）。</summary>
        private static int CueOffset(float t, int distance)
        {
            if (t >= 1f) return 0;
            int d = (int)Math.Round(distance * (1f - t));
            return d < 0 ? 0 : d;
        }

        // 窗框渐变画笔缓存（键 = 高度 + 三色）：尺寸或配色变了才重建。
        private System.Drawing.Drawing2D.LinearGradientBrush _frameBrush;
        private Pen _framePen;
        private int _frameKey;

        private Pen FramePen(int height)
        {
            int k = (Math.Max(1, height) * 397) ^ Theme.FrameTop.ToArgb()
                ^ (Theme.FrameSide.ToArgb() * 31) ^ (Theme.FrameBottom.ToArgb() * 131);
            if (_framePen != null && k == _frameKey) return _framePen;

            if (_framePen != null) { try { _framePen.Dispose(); } catch { } }
            if (_frameBrush != null) { try { _frameBrush.Dispose(); } catch { } }

            _frameBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, 1, Math.Max(1, height)), Theme.FrameTop, Theme.FrameBottom, 90f);
            System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(3);
            cb.Colors = new Color[] { Theme.FrameTop, Theme.FrameSide, Theme.FrameBottom };
            cb.Positions = new float[] { 0f, 0.42f, 1f };
            _frameBrush.InterpolationColors = cb;
            _framePen = new Pen(_frameBrush, 1f);
            _frameKey = k;
            return _framePen;
        }

        // ==============================================================
        // 无边框窗口的缩放与最大化钳制
        // ==============================================================

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                          HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXPOINT { public int X, Y; }

        /// <summary>
        /// WM_GETMINMAXINFO 的参数结构。本程序只用 ptMinTrackSize，
        /// 但前三个字段必须保留：缺少它们会让 ptMinTrackSize 落到错误的偏移上。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public MINMAXPOINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        /// <summary>边缘缩放热区宽度（设计 WindowChrome ResizeBorderThickness = 8）。</summary>
        private const int ResizeBorder = 8;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);

                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point(lp & 0xFFFF, (lp >> 16) & 0xFFFF));
                bool left = p.X <= ResizeBorder;
                bool right = p.X >= ClientSize.Width - ResizeBorder;
                bool top = p.Y <= ResizeBorder;
                bool bottom = p.Y >= ClientSize.Height - ResizeBorder;

                if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                try
                {
                    // 无边框窗口最大化时默认会盖住任务栏，这里按屏幕工作区钳制
                    Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    mmi.ptMaxPosition.X = wa.X;
                    mmi.ptMaxPosition.Y = wa.Y;
                    mmi.ptMaxSize.X = wa.Width;
                    mmi.ptMaxSize.Y = wa.Height;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch
                {
                }
                return;
            }

            base.WndProc(ref m);
        }
    }
}
