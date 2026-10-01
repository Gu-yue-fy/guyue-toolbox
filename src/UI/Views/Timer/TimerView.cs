/* UI/Views/Optimize/TimerView.cs — 计时器分辨率页（实测寻优 + 手动指定，维持线程持续请求）。 */

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
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

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }

        private AccentButton MakeInlineButton(string text, ButtonVariant v, EventHandler onClick, int width)
        {
            AccentButton b = new AccentButton();
            b.Text = text;
            b.IconKind = "power";
            b.Variant = v;
            b.Height = Theme.RowButtonHeight;
            b.FitToText(width);   // width 是「最小宽度」：文字更长时自动放宽
            b.Margin = new Padding(6, 0, 0, 0);
            b.Click += onClick;
            return b;
        }

        public TimerView()
            : base("计时器分辨率", "实测寻优 + 手动指定系统计时器分辨率，降低游戏帧时间抖动")
        {
            _notice.NoticeIcon = "clock";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "开启后持续请求所选精度（系统会主动回落，需持续维持）；关闭或退出本工具即自动还原。";

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
            // 初始必须是「开启维持」：此前初始文案就是「停止维持」危险色，
            // 没开启时点它等于再关一次（空操作），「恢复默认」后也会残留错误状态。
            _toggleButton = AddAction("开启维持", "power", ButtonVariant.Secondary, OnToggleClick, 130);
            AddAction("恢复默认", "undo", ButtonVariant.Ghost, OnRestoreClick, 110);

            // 维持状态轮询（500ms）：当前精度变化实时反映到卡片
            _poll = new System.Windows.Forms.Timer { Interval = 500 };
            _poll.Tick += delegate { if (Visible) RefreshCurrent(); };

            BuildLayout();
            // 不在这里 Start：页面切走后计时器必须停（见 OnVisibleChanged），
            // 否则离开本页仍每 500ms 做一次 P/Invoke 查询并触发卡片重绘
            if (Visible) _poll.Start();
            RefreshCurrent();
        }



        private void BuildLayout()
        {
            AddFull(_notice, 34, 14);

            FlowLayoutPanel row = MakeRowFixed(128, 14);
            row.Controls.Add(_cardCurrent);
            row.Controls.Add(_cardBest);
            row.Controls.Add(_cardDefault);
            AddRow(row);

            // 常用档位（一键按该值开启，省去手填与寻优）
            AddRow(BuildPresetRow());

            // 自定义精度输入行
            FlowLayoutPanel customRow = MakeRow(0, 12);
            customRow.Controls.Add(MakeLabel("自定义精度", 100, true));
            _customInput.BorderStyle = BorderStyle.FixedSingle;
            _customInput.Font = Theme.FontBody;
            _customInput.Size = new Size(120, 30);
            _customInput.Text = "0.5033";
            customRow.Controls.Add(ThemeInput.Wrap(_customInput));
            Label unit = MakeLabel("ms（可填范围 0.5 – 1.0）", 200, false);
            unit.Margin = new Padding(8, 8, 0, 0);
            customRow.Controls.Add(unit);
            _applyCustom = MakeInlineButton("按此值开启", ButtonVariant.Secondary, OnApplyCustom, 130);
            _applyCustom.Margin = new Padding(12, 0, 0, 0);
            customRow.Controls.Add(_applyCustom);
            AddRow(customRow);

            FlowLayoutPanel statusRow = MakeRow(0, 0);
            statusRow.Controls.Add(_statusLabel);
            AddRow(statusRow);

            FlowLayoutPanel infoRow = MakeRow(0, 0);
            Label info = new Label();
            // 说明只留两条要点：原理细节收进提示条，页面上不再铺六行说明书
            info.Text = "支持范围 0.5 – 1.0ms；「自动寻优」会逐档实测取本机延迟最低的档。\r\n" +
                "不是越低越好：值越低唤醒越频繁、功耗越高——游戏用 0.5～1ms，日常建议恢复默认。";
            info.ForeColor = Theme.TextMuted;
            info.Font = Theme.FontSmall;
            info.AutoSize = true;
            info.Margin = new Padding(0, 8, 0, 0);
            infoRow.Controls.Add(info);
            AddRow(infoRow);

            RefreshCurrent();
        }

        /// <summary>
        /// 档位：实际可调范围就是 0.5 – 1.0ms（再低系统给不出来，再高等于放松精度、对降抖动没意义），
        /// 所以只保留范围内的档，原来的 2ms 档已去掉。
        /// </summary>
        private static readonly double[] PresetMs = new double[] { 0.5, 0.5033, 1.0 };
        private static readonly string[] PresetNames = new string[] { "0.5ms 极致", "0.5033ms 最优", "1ms 平衡" };

        private FlowLayoutPanel BuildPresetRow()
        {
            FlowLayoutPanel row = MakeRow(0, 12);
            row.Controls.Add(MakeLabel("常用档位", 100, true));
            for (int i = 0; i < PresetMs.Length; i++)
            {
                double value = PresetMs[i];
                AccentButton b = MakeInlineButton(PresetNames[i], ButtonVariant.Secondary,
                    delegate { OnPreset(value); }, 150);
                b.IconKind = "clock";
                b.Margin = new Padding(0, 0, 8, 0);
                row.Controls.Add(b);
            }
            Label hint = MakeLabel("← 点档位直接开启维持", 0, false);
            hint.Margin = new Padding(4, 8, 0, 0);
            row.Controls.Add(hint);
            return row;
        }

        /// <summary>按档位值开启维持（与自定义输入同一条路径，只是值由档位按钮给出）。</summary>
        private void OnPreset(double ms)
        {
            if (_busy) return;
            _busy = true;
            SetSubtitle("正在按档位 " + ms.ToString("0.####") + "ms 开启维持…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                double applied;
                bool ok = TimerResolution.Start(ms, out applied);
                Post(delegate
                {
                    _busy = false;
                    if (ok && applied > 0)
                    {
                        _bestApplied = applied;
                        _cardBest.SetData("实测最佳档", applied.ToString("0.####") + "ms", 100,
                            "档位按钮指定（请求 " + ms.ToString("0.####") + "ms）", Theme.Success);
                        RefreshCurrent();   // 立即同步按钮与卡片，不等 500ms 轮询
                        SetSubtitle("已按档位生效：请求 " + ms.ToString("0.####") + "ms，实际 "
                            + applied.ToString("0.####") + "ms（系统已取整到可达值）。", Theme.Success);
                    }
                    else
                    {
                        SetStatusFail();
                    }
                });
            });
        }

        private void SetStatusFail()
        {
            SetSubtitle("开启失败（系统可能限制了计时器分辨率）。", Theme.Danger);
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
                ? "维持中 · 目标 " + TimerResolution.TargetMs.ToString("0.####") + "ms（系统实际 " + cur.ToString("0.####") + "ms）· 退出即还原"
                : "当前为系统默认调度（15.6ms 节拍）。";
            SyncToggle(active);
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
                        RefreshCurrent();
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
                System.Globalization.CultureInfo.InvariantCulture, out ms) || ms < 0.5 || ms > 1.0)
            {
                Dialog.Info(this, "数值无效",
                    "请输入 0.5 – 1.0 之间的毫秒值（例如 0.5033 或 1）。\r\n\r\n"
                    + "系统实际只在这个范围内接受精度请求：再低它给不出来，再高等于放松节拍、对降抖动没有意义。");
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
                        _bestApplied = applied;
                        RefreshCurrent();
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

        /// <summary>
        /// 按钮状态跟随真实的维持状态（文字 / 图标 / 权重三处一起改）。
        /// 这是本页此前最容易出错的地方：按钮曾被硬写成「停止维持 + 危险色」，
        /// 未开启时点它只是空操作，「恢复默认」之后也会残留错误状态。
        /// </summary>
        private void SyncToggle(bool active)
        {
            string text = active ? "停止维持" : "开启维持";
            string icon = active ? "stop" : "power";
            if (_toggleButton.Text != text) _toggleButton.Text = text;
            if (_toggleButton.IconKind != icon) _toggleButton.IconKind = icon;
        }

        /// <summary>开启/停止维持（真正的双向开关）。</summary>
        private void OnToggleClick(object sender, EventArgs e)
        {
            if (_busy) return;

            if (TimerResolution.IsKeeping)
            {
                TimerResolution.Disable();
                SetSubtitle("已停止维持并恢复系统默认调度。", Theme.TextSecondary);
                RefreshCurrent();
                return;
            }

            // 开启：优先用输入框里的值，其次用上次实测值，最后退到最优档 0.5033ms
            double ms = 0.5033;
            double parsed;
            if (double.TryParse(_customInput.Text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed)
                && parsed >= 0.5 && parsed <= 1.0) ms = parsed;
            else if (_bestApplied > 0) ms = _bestApplied;

            OnPreset(ms);   // 与档位按钮同一条开启路径（含失败提示与状态同步）
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            TimerResolution.Disable();
            SetSubtitle("已恢复系统默认调度。", Theme.TextSecondary);
            RefreshCurrent();
        }

        /// <summary>
        /// 页面不可见时停掉轮询。此前计时器在构造里 Start 后就一直活着：
        /// 切走之后仍每 500ms 做一次 NtQueryTimerResolution 查询并刷新三张卡片
        /// （无效 P/Invoke + 无意义重绘，且页面被释放前的这段时间都算在内）。
        /// </summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (_poll == null) return;
            if (Visible)
            {
                if (!_poll.Enabled) _poll.Start();
                RefreshCurrent();
            }
            else if (_poll.Enabled)
            {
                _poll.Stop();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 计时器必须停掉并释放：此前它一直活着，页面销毁后仍每 500ms 回调一次
                // （写已释放的控件，异常被全局兜底吞掉，表现为偶发错误弹窗 + 对象不回收）。
                try { _poll.Stop(); _poll.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
