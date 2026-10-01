/* UI/Views/System/SettingsView.cs — 软件设置页。 */

using System;
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
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly InfoList _info = new InfoList();
        private readonly ToggleSwitch _animToggle = new ToggleSwitch();
        private readonly ToggleSwitch _updateToggle = new ToggleSwitch();
        private readonly ToggleSwitch _lightToggle = new ToggleSwitch();
        private readonly ToggleSwitch _restorePageToggle = new ToggleSwitch();
        private readonly Panel _swatchRow = new Panel();
        private readonly AccentButton[] _swatches = new AccentButton[Theme.Palette.Length];
        private readonly AccentButton _noticesBtn = new AccentButton();
        private readonly AccentButton _resetBtn = new AccentButton();
        private SettingRow _noticeRow;
        private SettingRow _resetRow;

        public SettingsView()
            : base("常规设置", "主题色 / 动画 / 更新偏好，改动即时生效")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "全部设置保存在本机注册表，随时更改，无需重启。";

            _info.Caption = "关于";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;
            _info.Add("软件版本", "v" + MainForm.AppVersion);
            _info.Add("优化项数量", "统计中…");
            _info.Add("设置位置", "HKCU\\Software\\GuyueBox\\Settings");
            _info.Invalidate();

            // 优化项总数在后台数：TweakLibrary.All() 实测约 2 秒，
            // 放在构造函数里等于「点进设置页先卡 2 秒」。
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int n = 0;
                try { n = TweakLibrary.All().Count; } catch { }
                Post(delegate
                {
                    _info.Clear();
                    _info.Add("软件版本", "v" + MainForm.AppVersion);
                    _info.Add("优化项数量", n + " 项");
                    _info.Add("设置位置", "HKCU\\Software\\GuyueBox\\Settings");
                    _info.Invalidate();
                });
            });

            AddAction("导出配置", "save", ButtonVariant.Secondary, OnExportClick, 110);
            AddAction("导入配置", "folder", ButtonVariant.Secondary, OnImportClick, 110);
            AddAction("操作日志", "list", ButtonVariant.Ghost, OnLogClick, 110);

            BuildThemeRow();
            BuildToggles();
            BuildMaintenance();
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

        /// <summary>操作日志弹窗：展示每次优化改动（来自 TweakExecutor.Recent()），支持撤销选中项。</summary>
        private void OnLogClick(object sender, EventArgs e)
        {
            Form dlg = new Form();
            dlg.Text = "操作日志（最近 50 条改动）";
            dlg.BackColor = Theme.WindowBg;
            dlg.Size = new Size(820, 520);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.MinimizeBox = false; dlg.MaximizeBox = false;
            dlg.Font = Theme.FontBody;

            DarkGrid grid = new DarkGrid();
            grid.ReadOnly = true;
            grid.UseOwnScrollbar = true;
            grid.Dock = DockStyle.Fill;
            grid.AddTextColumn("时间", 150, false);
            grid.AddTextColumn("操作", 80, false);
            grid.AddTextColumn("项目", 240, false);
            grid.AddFillColumn("说明", 300);
            grid.ColumnClickSort = true;

            AccentButton revertBtn = new AccentButton();
            revertBtn.Text = "撤销选中项";
            revertBtn.IconKind = "undo";
            revertBtn.Variant = ButtonVariant.Danger;
            revertBtn.Height = Theme.RowButtonHeight;
            revertBtn.Enabled = false;
            revertBtn.Click += delegate
            {
                if (grid.SelectedRows.Count == 0) return;
                TweakExecutor.Result row = grid.SelectedRows[0].Tag as TweakExecutor.Result;
                if (row == null || row.Source == null) { Dialog.Info(dlg, "无法撤销", "该记录无法定位到优化项。"); return; }
                if (row.Outcome != TweakExecutor.Outcome.Applied)
                {
                    Dialog.Info(dlg, "无需撤销", "仅「已应用」的改动可被撤销（该记录状态为" + OutcomeText(row) + "）。");
                    return;
                }
                if (!Dialog.ConfirmDanger(dlg, "撤销改动",
                    "把「" + row.Name + "」的注册表改动写回原值（优化中心里对应开关会同步回退）。",
                    "可撤销：基于应用前保存的原值；撤销后仍可再次应用。",
                    "只影响这一项；其他优化项与个人文件不受影响。",
                    "撤销", false)) return;
                TweakExecutor.Result r = TweakExecutor.Revert(row.Source);
                if (r.IsOk) { Dialog.Success(dlg, "已撤销", "该项的改动已还原。"); RefreshLog(grid); }
                else Dialog.Error(dlg, "撤销失败", r.Message + "（请查看日志或以管理员身份重试）");
            };

            grid.SelectionChanged += delegate
            {
                TweakExecutor.Result row = grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as TweakExecutor.Result : null;
                revertBtn.Enabled = row != null && row.Source != null && row.Outcome == TweakExecutor.Outcome.Applied;
            };

            RefreshLog(grid);
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

        /// <summary>用 TweakExecutor.Recent() 刷新操作日志网格（最新在前）。</summary>
        private static void RefreshLog(DarkGrid grid)
        {
            grid.Rows.Clear();
            IList<TweakExecutor.Result> items = TweakExecutor.Recent();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                TweakExecutor.Result r = items[i];
                int idx = grid.Rows.Add(
                    r.At.ToString("yyyy-MM-dd HH:mm:ss"),
                    OutcomeText(r),
                    r.Name,
                    r.Kind == "shell" ? r.Command : r.Message);
                grid.Rows[idx].Tag = r;
            }
        }

        /// <summary>将执行结果枚举翻译为操作日志里的中文短标签。</summary>
        private static string OutcomeText(TweakExecutor.Result r)
        {
            if (r.Kind != "tweak")
            {
                if (r.Outcome == TweakExecutor.Outcome.Applied) return "已执行";
                if (r.Outcome == TweakExecutor.Outcome.Failed) return "失败";
                return r.Outcome.ToString();
            }
            if (r.Outcome == TweakExecutor.Outcome.Applied) return "已应用";
            if (r.Outcome == TweakExecutor.Outcome.Reverted) return "已还原";
            if (r.Outcome == TweakExecutor.Outcome.Failed) return "失败";
            if (r.Outcome == TweakExecutor.Outcome.NotApplicable) return "不适用";
            return r.Outcome.ToString();
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
                b.Radius = 17;            // 圆形色块，选中环更清晰
                b.PaintBackColor = true;  // 必须显式开启：变体着色路径不会绘制 BackColor
                b.BackColor = Theme.Palette[i];
                b.Click += delegate
                {
                    AppSettings.AccentIndex = index;
                    // 走统一的「重映射 + 重绘」路径，让构造时写入的强调色底色也一并更新
                    if (MainForm.Current != null) MainForm.Current.ApplyThemeAndRefresh(AppSettings.LightTheme);
                    RefreshSwatches(); // 色板重新取色，并把选中环迁到新项
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

            _lightToggle.Checked = AppSettings.LightTheme;
            _lightToggle.CheckedChanged += delegate
            {
                AppSettings.LightTheme = _lightToggle.Checked;
                if (MainForm.Current != null) MainForm.Current.ApplyThemeAndRefresh(_lightToggle.Checked);
                RefreshSwatches();
                SetSubtitle(_lightToggle.Checked ? "已切换为浅色主题。" : "已切换为深色主题。", Theme.Success);
            };

            _restorePageToggle.Checked = AppSettings.RestoreLastPage;
            _restorePageToggle.CheckedChanged += delegate
            {
                AppSettings.RestoreLastPage = _restorePageToggle.Checked;
                SetSubtitle(_restorePageToggle.Checked
                    ? "下次启动将回到本次关闭时所在的页面。"
                    : "下次启动将从首页开始。", Theme.Success);
            };
        }

        /// <summary>
        /// 配色方案切换后主题色板会整体替换，色块需按当前方案重新取色；
        /// 同时把选中环迁移到 AppSettings.AccentIndex 对应项（否则用户看不出当前用的是哪个色）。
        /// </summary>
        private void RefreshSwatches()
        {
            int selected = AppSettings.AccentIndex;
            for (int i = 0; i < _swatches.Length && i < Theme.Palette.Length; i++)
            {
                if (_swatches[i] == null) continue;
                _swatches[i].BackColor = Theme.Palette[i];
                _swatches[i].Selected = i == selected;
                _swatches[i].Invalidate();
            }
        }

        private static SettingRow MakeToggleRow(string title, string desc, ToggleSwitch toggle)
        {
            return new SettingRow(title, desc, toggle);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            // 按设计的分区结构组织：纯标题页头 + 若干分区（各带彩色分区标识）。
            // 本页不放 StatStrip：设计设置页没有统计卡，且 StatStrip 至少要有一项数据
            // 才有意义——只设 Caption 会渲染成一张空白卡片。
            AddFull(MakeSection("外观与语言", "主题色 · 深色 / 浅色配色", Theme.Accent), 44, 8);
            AddFull(_swatchRow, 56, 0);
            AddFull(MakeToggleRow("浅色主题",
                "使用浅色配色（明亮环境下更清晰）。切换即时生效、无需重启。",
                _lightToggle), 64, Theme.GapSection);

            AddFull(MakeSection("常规行为", "动画 · 更新检查 · 启动页", Theme.Warning), 44, 8);
            AddFull(MakeToggleRow("界面动画",
                "页面切换、磁贴弹入、体检得分等过渡效果。关闭后界面即时响应、更省资源。",
                _animToggle), 64, Theme.GapTight);
            AddFull(MakeToggleRow("启动时检查更新",
                "联网获取新版本提示，可在「关于与更新」页手动检查。",
                _updateToggle), 64, Theme.GapTight);
            AddFull(MakeToggleRow("启动时回到上次页面",
                "下次启动直接打开本次关闭时所在的页面；关闭则始终从首页开始。",
                _restorePageToggle), 64, Theme.GapSection);

            AddFull(MakeSection("配置维护", "提示条记忆 · 恢复默认", Theme.Danger), 44, 8);
            AddFull(_noticeRow, 64, Theme.GapTight);
            AddFull(_resetRow, 64, Theme.GapSection);

            FlowLayoutPanel infoRow = MakeRow(0, 0);
            infoRow.Controls.Add(_info);
            AddRow(infoRow);
            _info.Height = 132;

            Body.Resize += delegate { RefreshLayout(); };
        }

        /// <summary>分区标题（设计设置页的分区语言：竖条 + 标题 + 说明）。</summary>
        private static SectionTitle MakeSection(string title, string hint, Color tone)
        {
            SectionTitle s = new SectionTitle();
            s.TitleText = title;
            s.HintText = hint;
            s.Tone = tone;
            return s;
        }

        /// <summary>
        /// 配置维护行：提示条记忆恢复 + 一键恢复默认设置。
        /// 单独成行放在配置区末尾，避免与日常开关混在一起误触。
        /// </summary>
        private void BuildMaintenance()
        {
            // 文案说清它到底恢复什么：原先叫「全部恢复」，与同一屏的「恢复默认」几乎同义
            _noticesBtn.Text = "重新显示提示条";
            _noticesBtn.IconKind = "undo";
            _noticesBtn.Variant = ButtonVariant.Secondary;
            _noticesBtn.Height = Theme.RowButtonHeight;
            _noticesBtn.FitToText(110);
            _noticesBtn.Click += delegate
            {
                AppSettings.ClearNotices();
                RefreshNoticeCount();
                SetSubtitle("已清除提示条记忆，各页面的提示会重新出现。", Theme.Success);
            };

            _resetBtn.Text = "恢复默认设置";
            _resetBtn.IconKind = "refresh";
            _resetBtn.Variant = ButtonVariant.Danger;
            _resetBtn.Height = Theme.RowButtonHeight;
            _resetBtn.FitToText(118);
            _resetBtn.Click += delegate
            {
                if (!Dialog.ConfirmDanger(this, "恢复默认设置",
                    "把主题色、配色方案、动画、启动检查更新、启动页与提示条记忆恢复为初始值。",
                    "可撤销：恢复后可随时重新设置任意一项，不需要重装或重启。",
                    "不会动你已应用的系统优化项（那些请在优化中心逐项关闭），也不动窗口位置与「首次运行」标记。",
                    "恢复默认", false)) return;

                AppSettings.ResetAll();
                if (MainForm.Current != null)
                    MainForm.Current.ApplyThemeAndRefresh(AppSettings.LightTheme);
                OnActivated(); // 把开关与色块同步到默认值
                RefreshNoticeCount();
                SetSubtitle("已恢复默认设置。", Theme.Success);
            };

            _noticeRow = new SettingRow("已关闭的页面提示", NoticeDesc(), _noticesBtn);
            _resetRow = new SettingRow("恢复默认设置",
                "主题色 / 配色方案 / 动画 / 更新偏好 / 提示条记忆回到初始值。", _resetBtn);
        }

        /// <summary>提示条恢复行的描述文案（含当前已关闭条数）。</summary>
        private static string NoticeDesc()
        {
            int n = AppSettings.DismissedNoticeCount();
            return n == 0
                ? "所有页面提示都在显示中。关闭过的提示可在此一键恢复。"
                : "当前已关闭 " + n + " 条页面提示。点击右侧按钮可让它们全部重新出现。";
        }

        private void RefreshNoticeCount()
        {
            if (_noticeRow != null) _noticeRow.Desc = NoticeDesc();
        }

        public override void OnActivated()
        {
            _animToggle.SetCheckedSilent(AppSettings.Animations);
            _updateToggle.SetCheckedSilent(AppSettings.AutoUpdateCheck);
            _lightToggle.SetCheckedSilent(AppSettings.LightTheme);
            _restorePageToggle.SetCheckedSilent(AppSettings.RestoreLastPage);
            RefreshSwatches();
            RefreshNoticeCount();
        }
    }
    /// <summary>标题 + 描述 + 右侧控件（开关 / 按钮）的设置行。</summary>
    internal sealed class SettingRow : RoundPanel
    {
        private string _title;
        private string _desc;

        public SettingRow(string title, string desc, Control right)
        {
            _title = title ?? "";
            _desc = desc ?? "";

            BackColor = Theme.CardBg;
            Radius = Theme.RadiusCard;
            Height = 64;
            Margin = new Padding(0, 0, 0, 10);
            Tag = "stretch";

            right.Location = new Point(0, 0);
            ToggleSwitch toggle = right as ToggleSwitch;
            if (toggle != null)
            {
                toggle.CheckedChanged += delegate { Invalidate(); };
                // 开关本身没有文字，读屏要以所在行的标题作为可访问名称
                toggle.AccessibleName = _title;
            }
            Controls.Add(right);

            Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                g.DrawString(_title, Theme.FontBodyBold, GdiCache.Brush(Theme.TextPrimary),
                    new Rectangle(16, 10, Math.Max(60, Width - 130), 20));
                g.DrawString(_desc, Theme.FontSmall, GdiCache.Brush(Theme.TextMuted),
                    new Rectangle(16, 34, Math.Max(60, Width - 130), 22), GdiCache.Ellipsis);
            };

            // 右侧控件按自身尺寸垂直居中（按钮比开关宽，不能写死偏移）
            Resize += delegate
            {
                right.Location = new Point(Math.Max(16, Width - right.Width - 20),
                    (Height - right.Height) / 2);
            };
        }

        /// <summary>描述文案可动态更新（如展示已关闭提示条数量）。</summary>
        public string Desc
        {
            set
            {
                _desc = value ?? "";
                Invalidate();
            }
        }
    }
}
