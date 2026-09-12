using System;
using System.Drawing;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    /// <summary>
    /// 软件设置：主题色、动画开关、启动检查更新。改动即时生效并保存。
    /// </summary>
    public sealed class SettingsView : ViewBase
    {
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly InfoList _info = new InfoList();
        private readonly ToggleSwitch _animToggle = new ToggleSwitch();
        private readonly ToggleSwitch _updateToggle = new ToggleSwitch();
        private readonly Panel _swatchRow = new Panel();
        private readonly AccentButton[] _swatches = new AccentButton[Theme.Palette.Length];

        public SettingsView()
            : base("软件设置", "主题色 / 动画 / 更新偏好，改动即时生效")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "全部设置保存在本机注册表，随时更改，无需重启。";

            _summary.Caption = "软件设置";
            _summary.IconKind = "feature";
            _summary.CaptionColor = Theme.Cyan;

            _info.Caption = "关于";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;
            _info.Add("软件版本", "v" + MainForm.AppVersion);
            _info.Add("优化项数量", TweakLibrary.All().Count + " 项");
            _info.Add("设置位置", "HKCU\\Software\\SysToolbox\\Settings");
            _info.Invalidate();

            BuildThemeRow();
            BuildToggles();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return false; }
        }

        private void BuildThemeRow()
        {
            _swatchRow.BackColor = Theme.CardBg;
            _swatchRow.Height = 56;
            _swatchRow.Margin = new Padding(0, 0, 0, 14);
            _swatchRow.Tag = "stretch";
            _swatchRow.Paint += delegate (object s, PaintEventArgs e)
            {
                Gfx.DrawTextEllipsis(e.Graphics, "主题色", Theme.FontBody, Theme.TextPrimary,
                    new Rectangle(14, 0, 120, _swatchRow.Height));
            };

            for (int i = 0; i < Theme.Palette.Length; i++)
            {
                int index = i;
                AccentButton b = new AccentButton();
                b.Text = "";
                b.Size = new Size(34, 34);
                b.Variant = ButtonVariant.Ghost;
                b.BackColor = Theme.Palette[i];
                b.Click += delegate
                {
                    AppSettings.AccentIndex = index;
                    Theme.ApplyAccent(index);
                    MainForm.Current.RefreshAll();
                };
                _swatches[i] = b;
                _swatchRow.Controls.Add(b);
            }

            _swatchRow.Resize += delegate
            {
                for (int i = 0; i < _swatches.Length; i++)
                {
                    _swatches[i].Location = new Point(110 + i * 46, 11);
                }
            };
        }

        private void BuildToggles()
        {
            _animToggle.Checked = AppSettings.Animations;
            _animToggle.CheckedChanged += delegate
            {
                AppSettings.Animations = _animToggle.Checked;
                SetSubtitle(_animToggle.Checked ? "动画已开启。" : "动画已关闭，界面切换更省资源。",
                    Theme.Success);
            };

            _updateToggle.Checked = AppSettings.AutoUpdateCheck;
            _updateToggle.CheckedChanged += delegate
            {
                AppSettings.AutoUpdateCheck = _updateToggle.Checked;
                SetSubtitle(_updateToggle.Checked ? "启动时将自动检查更新。" : "启动时不再自动检查更新。",
                    Theme.Success);
            };
        }

        private static ToggleRow MakeToggleRow(string title, string desc, ToggleSwitch toggle)
        {
            ToggleRow row = new ToggleRow(title, desc, toggle);
            return row;
        }

        private void BuildLayout()
        {
            AddFull(_notice, 42, 16);

            FlowLayoutPanel sumRow = MakeRow(0, 14);
            sumRow.Controls.Add(_summary);
            AddRow(sumRow);

            AddFull(_swatchRow, 0, 0);
            AddFull(MakeToggleRow("界面动画",
                "页面切换、磁贴弹入、体检得分等过渡效果。关闭后界面即时响应、更省资源。",
                _animToggle), 64, 0);
            AddFull(MakeToggleRow("启动时检查更新",
                "联网获取新版本提示，可在「关于与更新」页手动检查。",
                _updateToggle), 64, 14);

            FlowLayoutPanel infoRow = MakeRow(0, 0);
            infoRow.Controls.Add(_info);
            AddRow(infoRow);
            _info.Height = 150;

            Body.Resize += delegate { RefreshLayout(); };
        }

        public override void OnActivated()
        {
            _animToggle.SetCheckedSilent(AppSettings.Animations);
            _updateToggle.SetCheckedSilent(AppSettings.AutoUpdateCheck);
        }
    }

    /// <summary>标题 + 描述 + 开关的设置行。</summary>
    internal sealed class ToggleRow : RoundPanel
    {
        public ToggleRow(string title, string desc, ToggleSwitch toggle)
        {
            BackColor = Theme.CardBg;
            Radius = 11;
            Height = 64;
            Margin = new Padding(0, 0, 0, 10);
            Tag = "stretch";

            toggle.Location = new Point(0, 0);
            toggle.CheckedChanged += delegate { Invalidate(); };
            Controls.Add(toggle);

            Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                g.DrawString(title, Theme.FontBodyBold, GdiCache.Brush(Theme.TextPrimary),
                    new Rectangle(16, 10, Math.Max(60, Width - 100), 20));
                g.DrawString(desc, Theme.FontSmall, GdiCache.Brush(Theme.TextMuted),
                    new Rectangle(16, 34, Math.Max(60, Width - 100), 22), GdiCache.Ellipsis);
            };

            Resize += delegate { toggle.Location = new Point(Width - 54, (Height - 22) / 2); };
        }
    }
}
