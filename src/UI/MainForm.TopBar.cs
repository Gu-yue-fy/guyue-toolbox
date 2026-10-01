/* ============================================================
 * 文件说明：顶部命令栏：标题、命令按钮、窗口控制（最小化 / 最大化 / 关闭）与拖动、最大化切换。
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
        // 顶部命令栏
        // ==============================================================

        /// <summary>
        /// 会把自己的背景画给子控件用的容器（顶栏 / 侧栏这类带渐变或光晕的自绘面板）。
        /// 子控件若只铺 Parent.BackColor，会在渐变/光晕上盖出一块边界生硬的平色方块
        /// （修复中心「一键诊断」圆钮外那圈方框、顶栏按钮下的平色块都是它）。
        /// </summary>
        private sealed class SurfacedPanel : Panel, IBackdropSource
        {
            /// <summary>指向顶栏背景画法（底色 + 光晕），由 MainForm 装配。</summary>
            public Action<Graphics> Surface;

            public void PaintBackdrop(Graphics g, Control child)
            {
                if (g == null || child == null || Surface == null) return;
                System.Drawing.Drawing2D.GraphicsState st = g.Save();
                try
                {
                    g.SetClip(new Rectangle(0, 0, child.Width, child.Height));
                    g.TranslateTransform(-child.Left, -child.Top);
                    Surface(g);
                }
                finally { g.Restore(st); }
            }
        }

        /// <summary>顶栏背景：底色 + 上缘冷蓝径向光晕（自绘，同时也是子按钮的铺底来源）。</summary>
        private void PaintTopBarSurface(Graphics g)
        {
            Rectangle rc = _topBar.ClientRectangle;
            g.FillRectangle(GdiCache.Brush(Theme.ShellBg), rc);

            // 环境光晕：以左侧为心的冷蓝径向光，只在上缘铺开（克制）
            using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                gp.AddEllipse(-rc.Width, -rc.Height * 2, rc.Width * 2, rc.Height * 4);
                using (System.Drawing.Drawing2D.PathGradientBrush pg =
                    new System.Drawing.Drawing2D.PathGradientBrush(gp))
                {
                    pg.CenterColor = Gfx.Alpha(Theme.HeroGlow, Theme.IsLight ? 26 : 46);
                    pg.SurroundColors = new Color[] { Color.Transparent };
                    g.FillPath(pg, gp);
                }
            }
        }

        private void BuildTopBar()
        {
            _topBar.BackColor = Theme.ShellBg;
            _topBar.Surface = PaintTopBarSurface;   // 子按钮铺底：重画底色 + 光晕，不留方块
            _topBar.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                Rectangle rc = _topBar.ClientRectangle;

                PaintTopBarSurface(g);

                // 左侧：当前页标题（15px 粗体），避让右侧按钮区。
                // 切页时做一次淡入 + 轻微右移（见 PlaySwitchCue）：顶栏是自绘的，
                // 能精确控制文字的 alpha 与位移，是"换页有过渡感"最划算的一处。
                string title = string.IsNullOrEmpty(_pageTitle) ? AppName : _pageTitle;
                float cue = CueProgress();
                int cueDx = CueOffset(cue, 12);
                System.Drawing.Text.TextRenderingHint oldHint = g.TextRenderingHint;
                if (cue < 1f) g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias; // 灰度抗锯齿才吃 alpha
                Gfx.DrawTextEllipsis(g, title, Theme.FontSubTitle, CueColor(Theme.TextPrimary, cue),
                    new Rectangle(16 + cueDx, 0, Math.Max(40, rc.Width - 470 - cueDx), rc.Height));
                g.TextRenderingHint = oldHint;

                // 底部发丝分隔
                g.DrawLine(GdiCache.Pen(Theme.BorderSoft, 1f), 0, rc.Height - 1, rc.Width, rc.Height - 1);
            };

            // 拖动：非按钮区域按下即拖窗。走 Native.DragWindow（ReleaseCapture + WM_NCLBUTTONDOWN/HTCAPTION），
            // 因此 Aero Snap 与「双击标题栏最大化」都由系统原生提供。
            _topBar.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Native.DragWindow(Handle);
            };

            _topBar.Resize += delegate { LayoutTitleButtons(); };
            BuildTitleButtons();
        }

        /// <summary>标题栏右侧按钮：功能入口 + 最小化/最大化/关闭（设计 WinControlBtn / WinCloseBtn）。</summary>
        private void BuildTitleButtons()
        {
            AddTitleText("反馈", delegate { Shell.OpenUrl(UpdateChecker.ProjectUrl + "/issues"); });
            AddTitleText("关于", delegate { NavigateTo("about"); });

            // Ctrl+K 搜索提示 chip（非输入框，点击打开命令面板）
            AccentButton ctrlk = new AccentButton();
            ctrlk.Text = "Ctrl K";
            ctrlk.IconKind = "search";
            ctrlk.Variant = ButtonVariant.Ghost;
            ctrlk.Font = Theme.FontSmall;
            ctrlk.Height = TopBarHeight - 10;
            ctrlk.Click += delegate { TogglePalette(); };
            _titleButtons.Add(ctrlk);
            _topBar.Controls.Add(ctrlk);
            ctrlk.FitToText(96);

            // 主题切换按钮
            _themeButton = new AccentButton();
            _themeButton.Text = Theme.IsLight ? "深色" : "浅色";
            // 主题切换曾借用 refresh 图标（同一栏里与"刷新"语义冲突），改用明暗主题图标
            _themeButton.IconKind = "theme";
            _themeButton.Variant = ButtonVariant.Ghost;
            _themeButton.Font = Theme.FontSmall;
            _themeButton.Height = TopBarHeight - 10;
            _themeButton.Click += delegate { ToggleTheme(); };
            _titleButtons.Add(_themeButton);
            _topBar.Controls.Add(_themeButton);
            _themeButton.FitToText(86);

            CaptionButton min = new CaptionButton("min");
            min.HoverColor = Theme.CardHover;
            min.Click += delegate { WindowState = FormWindowState.Minimized; };
            AddTitleButton(min, 44);

            _maxButton = new CaptionButton("max");
            _maxButton.HoverColor = Theme.CardHover;
            _maxButton.Click += delegate { ToggleMaximize(); };
            AddTitleButton(_maxButton, 44);

            CaptionButton close = new CaptionButton("close");
            close.HoverColor = Theme.Danger; // 设计：关闭按钮悬停转红
            close.Click += delegate { Close(); };
            AddTitleButton(close, 44);
        }

        private void AddTitleText(string text, EventHandler onClick)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.Variant = ButtonVariant.Ghost;
            b.Font = Theme.FontSmall;
            b.Height = TopBarHeight - 10;
            b.Width = 58;
            b.Click += onClick;
            _titleButtons.Add(b);
            _topBar.Controls.Add(b);
            LayoutTitleButtons();
        }

        private void AddTitleButton(Control c, int width)
        {
            c.Height = TopBarHeight - 4;
            c.Width = width;
            _titleButtons.Add(c);
            _topBar.Controls.Add(c);
            LayoutTitleButtons();
        }

        /// <summary>标题栏按钮靠右排列（最后加入的在最右）。</summary>
        private void LayoutTitleButtons()
        {
            int x = _topBar.Width;
            for (int i = _titleButtons.Count - 1; i >= 0; i--)
            {
                Control c = _titleButtons[i];
                x -= c.Width;
                c.Location = new Point(Math.Max(0, x), (TopBarHeight - c.Height) / 2);
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            if (_maxButton != null)
            {
                _maxButton.IconKind = WindowState == FormWindowState.Maximized ? "restore" : "max";
            }
        }
    }
}
