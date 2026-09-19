using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
/* =============================================================
 * 文件说明：高精度计时器页（独立功能）。
 * 核心实现（用户经验校准）：
 *   · NtSetTimerResolution 的请求会被系统悄悄回落，必须由**专用维持线程
 *     周期性重发请求**才能持续生效（社区 TimerResolution 类工具均为此实现）；
 *   · 查询用 NtQueryTimerResolution（纯只读），绝不能用 Set(false) 去"查"——那是撤销；
 *   · 寻优：从 0.5ms 起逐档实测到 1.0ms，取生效延迟最低的档
 *     （Win10 锁 0.5017ms，Win11 达 0.5ms，0.5033ms 为多数机器实测最优）；
 *   · 支持手动输入任意精度值（0.25–15.6ms）。
 * 效果仅在本工具运行期间有效（维持线程存活即生效），退出自动还原。
 * ============================================================= */
    public sealed class TimerView : ViewBase
    {
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly StatCard _cardCurrent = new StatCard();
        private readonly StatCard _cardBest = new StatCard();
        private readonly StatCard _cardDefault = new StatCard();
        private readonly TextBox _customInput = new TextBox();
        private readonly Label _statusLabel = new Label();
        private readonly System.Windows.Forms.Timer _poll;
        private AccentButton _toggleButton;
        private AccentButton _applyCustom;
        private double _bestApplied = -1;   // 最近一次寻优/开启的实际生效精度
        private bool _busy;

        private AccentButton MakeInlineButton(string text, ButtonVariant v, EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "power";
            b.Variant = v;
            b.Size = new Size(width, 32);
            b.Margin = new Padding(6, 0, 0, 0);
            b.Click += onClick;
            return b;
        }

        public TimerView()
            : base("高精度计时器", "实测寻优 + 手动指定系统定时器精度，降低游戏帧时间抖动")
        {
            _notice.NoticeIcon = "clock";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "开启后本工具会用维持线程持续请求所选精度（系统会悄悄回落，所以必须持续维持）。效果仅在本工具运行期间有效，关闭或退出即自动还原。";

            _cardCurrent.IconKind = "clock";
            _cardCurrent.AccentColor = Theme.Accent;
            _cardBest.IconKind = "bolt";
            _cardBest.AccentColor = Theme.Cyan;
            _cardDefault.IconKind = "info";
            _cardDefault.AccentColor = Theme.TextMuted;
            _cardCurrent.SetData("当前生效精度", "--", -1, "", Theme.Accent);
            _cardBest.SetData("实测最佳档", "未探测", -1, "点击「自动寻优」", Theme.Cyan);
            _cardDefault.SetData("系统默认", "15.6ms", -1, "未开启时的标准精度", Theme.TextMuted);

            _statusLabel.ForeColor = Theme.TextSecondary;
            _statusLabel.Font = Theme.FontBody;
            _statusLabel.AutoSize = true;

            AddAction("自动寻优", "bolt", ButtonVariant.Primary, OnFindBestClick, 120);
            _toggleButton = AddAction("停止维持", "history", ButtonVariant.Danger, OnToggleClick, 130);
            AddAction("恢复默认", "undo", ButtonVariant.Ghost, OnRestoreClick, 110);

            // 维持状态轮询（500ms）：当前精度变化实时反映到卡片
            _poll = new System.Windows.Forms.Timer { Interval = 500 };
            _poll.Tick += delegate { RefreshCurrent(); };

            BuildLayout();
            _poll.Start();
            RefreshCurrent();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 14);

            FlowLayoutPanel row = MakeRowFixed(128, 14);
            row.Controls.Add(_cardCurrent);
            row.Controls.Add(_cardBest);
            row.Controls.Add(_cardDefault);
            AddRow(row);

            // 自定义精度输入行
            FlowLayoutPanel customRow = MakeRow(0, 12);
            customRow.Controls.Add(MakeLabel("自定义精度", 100, true));
            _customInput.BorderStyle = BorderStyle.FixedSingle;
            _customInput.Font = Theme.FontBody;
            _customInput.Size = new Size(120, 30);
            _customInput.Text = "0.5033";
            customRow.Controls.Add(_customInput);
            Label unit = MakeLabel("ms（范围 0.25 – 15.6）", 200, false);
            unit.Margin = new Padding(8, 8, 0, 0);
            customRow.Controls.Add(unit);
            _applyCustom = MakeInlineButton("按此值开启", ButtonVariant.Primary, OnApplyCustom, 130);
            _applyCustom.Margin = new Padding(12, 0, 0, 0);
            customRow.Controls.Add(_applyCustom);
            AddRow(customRow);

            FlowLayoutPanel statusRow = MakeRow(0, 0);
            statusRow.Controls.Add(_statusLabel);
            AddRow(statusRow);

            FlowLayoutPanel infoRow = MakeRow(0, 0);
            Label info = new Label();
            info.Text = "原理：Windows 默认以 15.6ms 节拍唤醒调度，高精度请求会缩短节拍。\r\n" +
                "· 必须持续维持：系统会悄悄回落精度，本工具用后台线程每 300ms 重发一次请求；\r\n" +
                "· 不是越低越好：值越低唤醒越频繁，功耗/续航代价越大——游戏用 0.5～1ms，日常建议恢复默认；\r\n" +
                "· 寻优：从 0.5ms 逐档实测到 1.0ms 取生效延迟最低的档（Win10 锁 0.5017，Win11 达 0.5，多数机器 0.5033 最优）；\r\n" +
                "· 也可以在上方手动填任意值（例如 0.5033 / 1 / 2），按回车外的「按此值开启」生效；\r\n" +
                "· 关闭或退出本工具后系统自动恢复默认，无需手动清理。";
            info.ForeColor = Theme.TextMuted;
            info.Font = Theme.FontSmall;
            info.AutoSize = true;
            info.Margin = new Padding(0, 8, 0, 0);
            infoRow.Controls.Add(info);
            AddRow(infoRow);

            RefreshCurrent();
        }

        private Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = bold ? Theme.TextPrimary : Theme.TextSecondary;
            l.Font = bold ? Theme.FontBodyBold : Theme.FontBody;
            l.TextAlign = ContentAlignment.MiddleLeft;
            if (width > 0) l.Size = new Size(width, 30);
            else l.AutoSize = true;
            l.Margin = new Padding(0, 0, 10, 0);
            return l;
        }

        /// <summary>轮询刷新状态卡（不产生任何副作用）。</summary>
        private void RefreshCurrent()
        {
            double cur = -1;
            try { cur = TimerResolution.Current(); }
            catch { }
            bool active = TimerResolution.IsKeeping;
            _cardCurrent.SetData("当前生效精度", cur > 0 ? cur.ToString("0.####") + "ms" : "--",
                active ? 100 : -1, active ? "维持线程运行中（目标 " + TimerResolution.TargetMs.ToString("0.####") + "ms）" : "系统默认调度",
                active ? Theme.Success : Theme.Accent);
            _statusLabel.Text = active
                ? "高精度维持中：目标 " + TimerResolution.TargetMs.ToString("0.####") + "ms，系统实际 " + cur.ToString("0.####") + "ms。关闭或退出本工具即还原。"
                : "当前为系统默认调度（15.6ms 节拍）。自动寻优或手动填值后开启。";
        }

        /// <summary>自动寻优：0.5 → 1.0ms 逐档实测，锁定延迟最低的请求值并开启维持。</summary>
        private void OnFindBestClick(object sender, EventArgs e)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在从 0.5ms 逐档实测到 1.0ms，寻找本机延迟最低的档位…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                double req = -1, applied = -1;
                try { req = TimerResolution.FindBestRequest(out applied); }
                catch { }

                bool started = false;
                if (req > 0 && applied > 0)
                {
                    // 锁定最佳档并启动维持线程
                    started = TimerResolution.Start(req, out applied);
                }
                else
                {
                    TimerResolution.Disable();
                }

                Post(delegate
                {
                    _busy = false;
                    if (started && applied > 0)
                    {
                        _bestApplied = applied;
                        _cardBest.SetData("实测最佳档", applied.ToString("0.####") + "ms", 100,
                            "请求值 " + req.ToString("0.####") + "ms", Theme.Success);
                        _toggleButton.Text = "停止维持";
                        _toggleButton.Variant = ButtonVariant.Danger;
                        _toggleButton.Invalidate();
                        SetSubtitle("寻优完成并已开启维持：最佳档 " + applied.ToString("0.####") +
                            "ms（请求 " + req.ToString("0.####") + "ms）。", Theme.Success);
                    }
                    else
                    {
                        _cardBest.SetData("实测最佳档", "探测失败", -1, "系统可能限制了定时器分辨率", Theme.Warning);
                        SetSubtitle("探测失败（系统可能限制了定时器分辨率）。", Theme.Danger);
                    }
                });
            });
        }

        /// <summary>按自定义输入值开启维持。</summary>
        private void OnApplyCustom(object sender, EventArgs e)
        {
            if (_busy) return;
            double ms;
            if (!double.TryParse(_customInput.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out ms) || ms < 0.25 || ms > 15.6)
            {
                Dialog.Info(this, "数值无效", "请输入 0.25 – 15.6 之间的毫秒值（例如 0.5033 或 1）。");
                return;
            }

            _busy = true;
            SetSubtitle("正在按自定义值 " + ms.ToString("0.####") + "ms 开启维持…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                double applied;
                bool ok = TimerResolution.Start(ms, out applied);
                Post(delegate
                {
                    _busy = false;
                    if (ok && applied > 0)
                    {
                        _toggleButton.Text = "停止维持";
                        _toggleButton.Variant = ButtonVariant.Danger;
                        _toggleButton.Invalidate();
                        SetSubtitle("已按自定义值生效：请求 " + ms.ToString("0.####") + "ms，实际 " +
                            applied.ToString("0.####") + "ms（系统已取整到可达值）。", Theme.Success);
                    }
                    else
                    {
                        SetSubtitle("开启失败（系统可能限制了定时器分辨率）。", Theme.Danger);
                    }
                });
            });
        }

        private void OnToggleClick(object sender, EventArgs e)
        {
            if (_busy) return;
            // 停止维持
            TimerResolution.Disable();
            _toggleButton.Text = "停止维持";
            _toggleButton.Invalidate();
            SetSubtitle("已停止维持并恢复系统默认调度。", Theme.TextSecondary);
            RefreshCurrent();
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            TimerResolution.Disable();
            _toggleButton.Text = "停止维持";
            _toggleButton.Invalidate();
            SetSubtitle("已恢复系统默认调度。", Theme.TextSecondary);
            RefreshCurrent();
        }    }
}
