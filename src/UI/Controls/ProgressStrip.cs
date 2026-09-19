/* ============================================================
 * 文件说明：分阶段进度条（对齐设计规则 3.2 / 3.3 / 3.5）：
 *           - 不止一个百分比，而是逐阶段推进并显示当前阶段在做什么；
 *           - 阶段名写具体动作（"检查系统保护服务…"），让用户理解耗时花在哪；
 *           - 可取消：长任务不该"绑架"用户，取消后回到上一稳定态。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>分阶段进度条：阶段名 + 分段进度 + 可选取消键。</summary>
    internal sealed class ProgressStrip : Control
    {
        private readonly List<string> _phases = new List<string>();
        private int _phaseIndex;
        private int _phasePercent;
        private string _caption = "";
        private Rectangle _cancelRect;
        private bool _cancelHover;
        private bool _cancellable;

        /// <summary>点击取消。</summary>
        public event EventHandler Cancelled;

        public ProgressStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 46;
            Tag = "stretch";
        }

        /// <summary>开始一轮：给出阶段清单。</summary>
        public void Begin(string caption, string[] phases, bool cancellable)
        {
            _caption = caption == null ? "" : caption;
            _phases.Clear();
            if (phases != null) _phases.AddRange(phases);
            _phaseIndex = 0;
            _phasePercent = 0;
            _cancellable = cancellable;
            Visible = true;
            Invalidate();
        }

        /// <summary>更新当前阶段与阶段内进度。percent 为 0~100。</summary>
        public void SetPhase(int index, int percent)
        {
            if (index < 0) index = 0;
            if (index >= _phases.Count) index = Math.Max(0, _phases.Count - 1);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            if (_phaseIndex == index && _phasePercent == percent) return;
            _phaseIndex = index;
            _phasePercent = percent;
            Invalidate();
        }

        /// <summary>按「阶段序号 + 阶段名」更新（供只知道阶段名的调用方使用）。</summary>
        public void SetPhaseByName(string phaseName, int percent)
        {
            for (int i = 0; i < _phases.Count; i++)
            {
                if (string.Equals(_phases[i], phaseName, StringComparison.Ordinal))
                {
                    SetPhase(i, percent);
                    return;
                }
            }
            _phasePercent = percent;
            Invalidate();
        }

        public void Finish()
        {
            Visible = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = _cancellable && _cancelRect.Contains(e.Location);
            if (h != _cancelHover)
            {
                _cancelHover = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate(_cancelRect);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _cancellable && _cancelRect.Contains(e.Location))
            {
                EventHandler h = Cancelled;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 第一行：标题 + 当前阶段名（阶段名写具体动作）
            string phase = _phases.Count == 0 ? "" : _phases[_phaseIndex];
            string line = _caption.Length > 0 ? _caption : "正在处理…";
            if (phase.Length > 0) line += "：" + phase + "…";

            int textW = Math.Max(80, Width - (_cancellable ? 90 : 12));
            Gfx.DrawTextEllipsis(g, line, Theme.FontSmall, Theme.TextPrimary,
                new Rectangle(0, 0, textW, 20));

            // 第二行：分段进度（已完成阶段实心、当前阶段按阶段内进度、未开始为轨道色）
            int barY = 26;
            int barH = 8;
            int barW = Math.Max(40, Width - (_cancellable ? 90 : 0));
            Rectangle track = new Rectangle(0, barY, barW, barH);
            Gfx.FillRound(g, track, 4, Theme.CardBgAlt);

            int n = _phases.Count > 0 ? _phases.Count : 1;
            int segGap = 3;
            int segW = Math.Max(6, (barW - segGap * (n - 1)) / n);
            for (int i = 0; i < n; i++)
            {
                int x = i * (segW + segGap);
                if (x >= barW) break;
                Rectangle seg = new Rectangle(x, barY, Math.Min(segW, barW - x), barH);

                Color tone;
                if (i < _phaseIndex) tone = Theme.Success;                       // 已完成
                else if (i == _phaseIndex) tone = Theme.Accent;                  // 进行中
                else tone = Theme.CardBgAlt;                                    // 未开始

                if (i == _phaseIndex)
                {
                    // 当前阶段：先铺底色，再按阶段内进度压一条填充
                    Gfx.FillRound(g, seg, 4, Gfx.Blend(Theme.CardBgAlt, Theme.Accent, 0.25));
                    int fillW = (int)Math.Round(seg.Width * _phasePercent / 100.0);
                    if (fillW >= 2) Gfx.FillRound(g, new Rectangle(seg.X, seg.Y, fillW, seg.Height), 4, tone);
                }
                else
                {
                    Gfx.FillRound(g, seg, 4, tone);
                }
            }

            if (_cancellable)
            {
                _cancelRect = new Rectangle(Width - 76, 4, 76, 26);
                if (_cancelHover) Gfx.FillRound(g, _cancelRect, Theme.RadiusChip, Theme.CardBgAlt);
                Gfx.DrawTextCenter(g, "取消", Theme.FontSmall,
                    _cancelHover ? Theme.TextPrimary : Theme.TextSecondary, _cancelRect);
            }
        }
    }
}
