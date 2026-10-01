﻿/* ============================================================
 * 文件说明：概览条目控件：体检得分卡等
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{


    /// <summary>健康评分大卡片：左侧环形分数，右侧状态说明与操作按钮。</summary>
    public class HealthCard : RoundPanel
    {
        private int _score = -1;
        private string _status = "尚未体检";
        private string _hint = "点击右侧「一键体检」开始检查系统状态";   // 按钮就在卡片右侧
        private Color _scoreColor = Theme.TextMuted;

        // 动画状态：得分滚动 + 体检扫描环
        private float _displayScore;      // 0→目标分数的插值
        private float _scanAngle;         // 体检中旋转角
        private Func<bool> _tick;         // 当前 tick 逻辑（每次 StartAnim 替换，修复"闭包只捕获首次"问题）

        // 分数字号较大，字体缓存起来，避免每次绘制创建 GDI 对象
        private static Font _scoreFont;

        private static Font ScoreFont
        {
            get
            {
                if (_scoreFont == null) _scoreFont = new Font(Theme.FontFamilyName, 30F, FontStyle.Bold);
                return _scoreFont;
            }
        }

        public HealthCard()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;
            Height = 156;
        }

        /// <summary>得分滚动动画：圆环与数字从 0 缓动到目标值。</summary>
        public void SetScoreAnimated(int score)
        {
            if (!AppSettings.Animations)
            {
                _displayScore = score;
                Invalidate();
                return;
            }
            _score = score;
            StartAnim(() =>
            {
                _displayScore += (score - _displayScore) * Theme.Motion.ScoreLerp;
                if (Math.Abs(score - _displayScore) < 0.6f)
                {
                    _displayScore = score;
                    return true;
                }
                return false;
            });
        }

        /// <summary>体检进行中：圆环进入旋转扫描状态。</summary>
        public void SetScanning(bool scanning)
        {
            if (scanning)
            {
                _score = -2; // 扫描态
                StartAnim(() =>
                {
                    _scanAngle = (_scanAngle + Theme.Motion.ScanDegPerFrame) % 360f;
                    return false; // 持续旋转直到外部停止
                });
            }
            else
            {
                _score = -1;
                AnimationClock.Instance.Unsubscribe(HealthTick); // 停止扫描旋转，否则定时器永续重绘且得分恒 0
            }
            Invalidate();
        }

        private void StartAnim(Func<bool> tick)
        {
            _tick = tick; // 每次替换当前 tick：修复"闭包只捕获首次委托"导致后续动画被忽略
            AnimationClock.Instance.Subscribe(HealthTick);
        }

        private void HealthTick()
        {
            if (_tick == null || _tick())
            {
                AnimationClock.Instance.Unsubscribe(HealthTick);
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AnimationClock.Instance.Unsubscribe(HealthTick);
                // _scoreFont 是进程级 static 单例，不随实例 Dispose 释放：
                // 多实例场景下一个实例释放后，其它实例（含 ScoreFont 惰性重建路径）
                // 会用到已释放的 Font 抛 ObjectDisposedException。与 Theme.Font*
                // 同策略——进程退出由 OS 回收（诊断报告 #7）。
            }
            base.Dispose(disposing);
        }

        public int Score
        {
            get { return _score; }
            set { _score = value; _displayScore = value; Invalidate(); }
        }

        public string StatusText
        {
            get { return _status; }
            set { _status = value == null ? "" : value; Invalidate(); }
        }

        public string HintText
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; Invalidate(); }
        }

        public Color ScoreColor
        {
            get { return _scoreColor; }
            set { _scoreColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            int ring = 104;
            int left = 30;
            int top = (Height - ring) / 2;
            Rectangle r = new Rectangle(left, top, ring, ring);

            Color color = _score < 0 ? Theme.Border : _scoreColor;

            // 底环（画笔走全局缓存：体检扫描态下这张卡每 16ms 重绘一次，会连续跑好几秒）
            g.DrawEllipse(GdiCache.Pen(Theme.CardBgAlt, 9f), r);

            if (_score == -2)
            {
                // 体检扫描态：底环上旋转高亮弧
                g.DrawArc(GdiCache.RoundPen(Theme.Accent, 9f), r, _scanAngle, 95);
                Gfx.DrawTextCenter(g, "···", ScoreFont, Theme.Accent, r);
                Gfx.DrawTextCenter(g, "体检中",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }
            else if (_score >= 0)
            {
                // 得分滚动动画：圆弧与数字随 _displayScore 缓动
                int sweep = (int)(360.0 * Math.Max(0, Math.Min(100, _displayScore)) / 100.0);
                g.DrawArc(GdiCache.RoundPen(color, 9f), r, -90, sweep);

                int shown = (int)Math.Round(_displayScore);
                Gfx.DrawTextCenter(g, shown.ToString(), ScoreFont, Theme.TextPrimary, r);
                Gfx.DrawTextCenter(g, "分",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }
            else
            {
                string scoreText = "--";
                Gfx.DrawTextCenter(g, scoreText, ScoreFont, Theme.TextMuted, r);
                Gfx.DrawTextCenter(g, "未体检",
                    Theme.FontSmall, Theme.TextMuted,
                    new Rectangle(r.X, r.Bottom - 34, r.Width, 20));
            }

            int textLeft = left + ring + 34;
            int textWidth = Math.Max(10, Width - textLeft - 210);

            Gfx.DrawTextEllipsis(g, "系统健康评分", Theme.FontSmall, Theme.TextMuted,
                new Rectangle(textLeft, top + 14, textWidth, 20));

            Gfx.DrawTextEllipsis(g, _status, Theme.FontSubTitle, Theme.TextPrimary,
                new Rectangle(textLeft, top + 38, textWidth, 26));

            Gfx.DrawTextEllipsis(g, _hint, Theme.FontSmall, Theme.TextSecondary,
                new Rectangle(textLeft, top + 70, textWidth, 22));
        }
    }
}
