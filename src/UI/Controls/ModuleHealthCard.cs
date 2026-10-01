﻿/* ============================================================
 * 文件说明：模块健康卡：环形启用率 + 风险图例
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 模块页健康卡，对齐设计 Web 概念稿的 <c>mod-health-card</c>：
    /// 左侧圆环呈现「已启用 / 总数」与启用率（圆环进度即启用率），
    /// 右侧给出「已启用 N%」与各风险档的项数图例（语义色点 + 文案）。
    ///
    /// 与旧的扁平统计条（StatStrip 四个「标签/数值」）相比，这一版把
    /// 「整体进度」做成可一眼读出的环形，风险档以图例收拢，符合设计
    /// 「状态色只承担语义、信息优先」的页面语言。
    /// </summary>
    public class ModuleHealthCard : RoundPanel
    {
        private int _total;
        private int _applied;
        private int _safe;
        private int _careful;

        // 卡高 76：页头每省一档竖高，首屏就多露出一整行优化项。
        // 环缩到 58 仍能读出比例，但把横向空间让给右侧的状态筛选与计数。
        private const int RingSize = 58;
        private const int RingThick = 7;

        public ModuleHealthCard()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;
            // 高度固定：行高在挂载前即定型，避免挂载后再改行高导致其后行错位
            Height = 76;
            Tag = "stretch";
        }

        /// <summary>刷新数据。total=优化项总数，applied=已启用，safe/careful=风险档项数。</summary>
        public void SetData(int total, int applied, int safe, int careful)
        {
            _total = total < 0 ? 0 : total;
            _applied = applied < 0 ? 0 : applied;
            _safe = safe < 0 ? 0 : safe;
            _careful = careful < 0 ? 0 : careful;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // ---- 内容整体居中 ----
            // 卡片是被拉伸到满行的（Tag=stretch）：内容若一律贴左缘，右侧会留一大片空白。
            // 这里把「圆环 + 计数 + 图例」当作一个整体在卡内居中；窄卡（内容比卡还宽）退回贴左缘。
            const int CountW = 200;
            const int LegendW = 260;
            const int ContentW = RingSize + 16 + CountW + 24 + LegendW;
            bool wide = Width >= ContentW + 44;
            int x0 = wide ? (Width - ContentW) / 2 : 20;

            // 画法与内存页环形仪表共用 Gfx.DrawRingArc：带"环境光"光晕、不带末端落点，每段约 15°
            Rectangle ring = new Rectangle(x0, (Height - RingSize) / 2, RingSize, RingSize);
            double pct = _total > 0 ? (double)_applied / _total : 0.0;

            Gfx.DrawRingArc(g, ring, RingThick, pct * 360.0,
                Theme.Accent, Gfx.Shade(Theme.Accent, 1.35), true, false, 15.0);

            // 环内只放百分比：58px 的环放不下两行文字，之前 79 与 /234 会挤在一起。
            Gfx.DrawTextCenter(g, (pct * 100.0).ToString("0") + "%", Theme.FontSmall, Theme.TextPrimary, ring);

            // ---- 启用计数（大字）+ 风险图例 ----
            int x = ring.Right + 16;
            int textW = wide ? CountW : Math.Max(60, Width - x - 14);

            g.DrawString(_applied + " / " + _total, Theme.FontSubTitle,
                GdiCache.Brush(Theme.TextPrimary), new Rectangle(x, 10, textW, 26));
            Gfx.DrawTextEllipsis(g, "已启用 / 共 " + _total + " 项", Theme.FontMicro, Theme.TextMuted,
                new Rectangle(x, 36, textW, 16));

            // 图例只在卡片够宽时画：窄卡上固定偏移会把图例画到卡外，
            // 宁可只显示计数，也不要出现「已启用 / 共 2…」截断。
            if (wide)
            {
                DrawLegend(g, x + textW + 24, 14, Theme.Success, "安全", _safe);
                DrawLegend(g, x + textW + 24, 36, Theme.Warning, "谨慎", _careful);
            }
        }

        private static void DrawLegend(Graphics g, int x, int y, Color dot, string label, int count)
        {
            g.FillEllipse(GdiCache.Brush(dot), x, y + 6, 8, 8);
            Gfx.DrawTextEllipsis(g, label + " " + count, Theme.FontSmall, Theme.TextSecondary,
                new Rectangle(x + 14, y, 110, 20));
        }
    }
}
