﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
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
            _summary.CaptionColor = Theme.Accent;

            _info.Caption = "关于";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;
            _info.Add("软件版本", "v" + MainForm.AppVersion);
            _info.Add("优化项数量", TweakLibrary.All().Count + " 项");
            _info.Add("设置位置", "HKCU\\Software\\GuyueBox\\Settings");
            _info.Invalidate();

            AddAction("导出配置", "save", ButtonVariant.Secondary, OnExportClick, 110);
            AddAction("导入配置", "folder", ButtonVariant.Secondary, OnImportClick, 110);
            AddAction("操作日志", "list", ButtonVariant.Ghost, OnLogClick, 110);

            BuildThemeRow();
            BuildToggles();
            BuildLayout();
        }

        /// <summary>导出当前优化配置（已启用项 Id 清单 JSON）。</summary>
        private void OnExportClick(object sender, EventArgs e)
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "配置文件 (*.json)|*.json";
                dlg.FileName = "GuyueBox_配置_" + DateTime.Now.ToString("yyyyMMdd");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    TweakLibrary.ExportState(dlg.FileName);
                    Dialog.Success(this, "导出完成", "当前优化配置已导出到：\r\n" + dlg.FileName +
                        "\r\n\r\n在其他机器上用「导入配置」即可一键复刻。");
                }
                catch (Exception ex) { Dialog.Error(this, "导出失败", ex.Message); }
            }
        }

        /// <summary>导入优化配置（应用 JSON 中列出的项；未列出的保持现状）。</summary>
        private void OnImportClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "配置文件 (*.json)|*.json";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                List<string> errors = new List<string>();
                int ok;
                try { ok = TweakLibrary.ImportState(dlg.FileName, errors); }
                catch (Exception ex) { Dialog.Error(this, "导入失败", ex.Message); return; }

                if (errors.Count > 0)
                {
                    string text = "成功 " + ok + " 项，失败 " + errors.Count + " 项：\r\n";
                    for (int i = 0; i < errors.Count && i < 8; i++) text += "· " + errors[i] + "\r\n";
                    Dialog.Warn(this, "导入完成", text);
                }
                else
                {
                    Dialog.Success(this, "导入完成", "成功应用 " + ok + " 项优化配置。");
                }
                SetSubtitle("配置导入完成（" + ok + " 项）。", Theme.Success);
            }
        }

        /// <summary>操作日志弹窗：展示注册表操作记录，支持按组回滚。</summary>
        private void OnLogClick(object sender, EventArgs e)
        {
            Form dlg = new Form();
            dlg.Text = "操作日志（最近 400 条）";
            dlg.BackColor = Theme.WindowBg;
            dlg.Size = new Size(780, 520);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.MinimizeBox = false; dlg.MaximizeBox = false;
            dlg.Font = Theme.FontBody;

            DarkGrid grid = new DarkGrid();
            grid.ReadOnly = true;
            grid.UseOwnScrollbar = true;
            grid.Dock = DockStyle.Fill;
            grid.AddTextColumn("时间", 130, false);
            grid.AddTextColumn("动作", 90, false);
            grid.AddFillColumn("详情", 300);
            grid.ColumnClickSort = true;

            AccentButton revertBtn = new AccentButton();
            revertBtn.Text = "还原选中行所属的整组改动";
            revertBtn.Variant = ButtonVariant.Danger;
            revertBtn.Height = 32;
            revertBtn.Enabled = false;
            revertBtn.Click += delegate
            {
                if (grid.SelectedRows.Count == 0) return;
                string bid = grid.SelectedRows[0].Tag as string;
                if (string.IsNullOrEmpty(bid)) { Dialog.Info(dlg, "无法回滚", "该记录不属于任何优化组（系统操作）。"); return; }
                if (!Dialog.Confirm(dlg, "按组回滚", "将还原该优化组的全部注册表改动（含勾选状态，优化中心里对应开关会回退）。继续？")) return;
                bool ok = RegHelper.Restore(bid);
                if (ok) Dialog.Success(dlg, "已还原", "该组的全部改动已还原。");
                else Dialog.Error(dlg, "还原失败", "部分条目还原失败，请查看日志或以管理员身份重试。");
            };

            List<string> lines = RegLog.Lines();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string time = line.Length > 17 ? line.Substring(0, 17) : line;
                string rest = line.Length > 19 ? line.Substring(19) : "";
                string action = rest, detail = "";
                int sp = rest.IndexOf("  ");
                if (sp > 0) { action = rest.Substring(0, sp); detail = rest.Substring(sp + 2); }
                int idx = grid.Rows.Add(time, action, detail);
                grid.Rows[idx].Tag = RegLog.BackupIdAt(i);
            }
            grid.SelectionChanged += delegate { revertBtn.Enabled = grid.SelectedRows.Count > 0; };

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 50;
            bottom.BackColor = Theme.WindowBg;
            revertBtn.Left = 12;
            revertBtn.Top = 9;
            bottom.Controls.Add(revertBtn);

            dlg.Controls.Add(grid);
            dlg.Controls.Add(bottom);
            dlg.ShowDialog(this);
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
            AddFull(_notice, 34, 12);

            FlowLayoutPanel sumRow = MakeRow(0, 14);
            sumRow.Controls.Add(_summary);
            AddRow(sumRow);

            AddFull(_swatchRow, 56, 0);
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
