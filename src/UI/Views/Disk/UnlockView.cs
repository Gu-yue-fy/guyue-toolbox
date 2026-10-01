using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>解除占用：查「文件被哪个进程占着」，并可结束该进程。</summary>
    public sealed class UnlockView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _stats = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly TextBox _pathBox = new TextBox();

        private readonly List<Unlocker.LockInfo> _locks = new List<Unlocker.LockInfo>();
        private string _path;
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }

        private AccentButton _killButton;

        public UnlockView()
            : base("解除占用", "文件删不掉、移不动？查出占用它的进程并结束")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "选择文件或文件夹 → 查询占用。列表里是正占用该文件的进程；结束进程前请确认它没有未保存的工作。";

            _stats.Caption = "占用情况";
            _stats.IconKind = "unlock";
            _stats.CaptionColor = Theme.Warning;

            AddAction("选择文件", "plus", ButtonVariant.Secondary, OnPickFile, 116);
            AddAction("选择文件夹", "folder", ButtonVariant.Secondary, OnPickFolder, 124);
            AddAction("查询占用", "search", ButtonVariant.Primary, OnQuery, 116);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, OnQuery, 96);
            _killButton = AddAction("结束选中进程", "close", ButtonVariant.Danger, OnKillSelected, 150);

            BuildGrid();
            BuildLayout();
            UpdateStats();
        }



        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.MultiSelect = true;
            _grid.AddTextColumn("进程", 160, false);
            _grid.AddTextColumn("PID", 70, false);
            _grid.AddTextColumn("类型", 100, false);
            _grid.AddTextColumn("服务", 120, false);
            _grid.AddFillColumn("程序路径", 240);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel pathRow = MakeRowFixed(34, 10);
            pathRow.Controls.Add(MakeLabel("目标", 44, true));
            _pathBox.ReadOnly = true;
            _pathBox.Font = Theme.FontSmall;
            _pathBox.Size = new Size(560, 26);
            Native.SetCue(_pathBox, "尚未选择文件或文件夹");
            pathRow.Controls.Add(ThemeInput.Wrap(_pathBox));
            AddRow(pathRow);

            FlowLayoutPanel statRow = MakeRow(0, 12);
            statRow.Controls.Add(_stats);
            AddRow(statRow);

            AddFull(_grid, 280, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        private void Relayout()
        {
            // 固定占位：提示条 34+12 + 路径行 34+10 = 90
            LayoutGrid(_grid, _stats, 90, 200);
        }

        // --------------------------------------------------------------

        private Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = bold ? Theme.TextPrimary : Theme.TextMuted;
            l.Font = bold ? Theme.FontBodyBold : Theme.FontSmall;
            l.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            l.Size = new Size(width, 26);
            l.Margin = new Padding(0, 0, 10, 0);
            return l;
        }

        private void UpdateStats()
        {
            _stats.Clear();
            _stats.Add("目标", string.IsNullOrEmpty(_path) ? "未选择" : System.IO.Path.GetFileName(_path));
            if (_locks.Count == 0)
            {
                _stats.Add("占用进程", string.IsNullOrEmpty(_path) ? "—" : "无", Theme.Success);
            }
            else
            {
                _stats.Add("占用进程", _locks.Count + " 个", Theme.Danger);
            }
            _stats.Invalidate();
            Relayout();
        }

        private void SetButtonsEnabled(bool on)
        {
            for (int i = 0; i < Actions.Count; i++) Actions[i].Enabled = on;
            if (_killButton != null) _killButton.Enabled = on;
        }

        // --------------------------------------------------------------

        private void OnPickFile(object sender, EventArgs e)
        {
            if (_busy) return;
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "选择被占用的文件";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            SetTarget(dlg.FileName);
        }

        private void OnPickFolder(object sender, EventArgs e)
        {
            if (_busy) return;
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择文件夹（查询其第一层文件的占用情况）";
            if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath)) return;
            SetTarget(dlg.SelectedPath);
        }

        private void SetTarget(string path)
        {
            _path = path;
            _pathBox.Text = path;
            _locks.Clear();
            _grid.Rows.Clear();
            UpdateStats();
            SetSubtitle("已选择目标，点「查询占用」开始。", Theme.TextSecondary);
        }

        private void OnQuery(object sender, EventArgs e)
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_path))
            {
                Dialog.Info(this, "未选择目标", "请先选择要排查的文件或文件夹。");
                return;
            }

            string path = _path;
            _busy = true;
            SetButtonsEnabled(false);
            SetSubtitle("正在查询占用…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string expandErr;
                List<string> files = Unlocker.ExpandPaths(path, out expandErr);

                List<Unlocker.LockInfo> locks = new List<Unlocker.LockInfo>();
                string err = expandErr;
                if (files.Count > 0)
                {
                    string qerr;
                    locks = Unlocker.FindLockers(files, out qerr);
                    err = qerr;
                }

                int fileCount = files.Count;
                Post(delegate { ShowLocks(locks, err, fileCount); });
            });
        }

        private void ShowLocks(List<Unlocker.LockInfo> locks, string error, int fileCount)
        {
            _busy = false;
            SetButtonsEnabled(true);

            if (!string.IsNullOrEmpty(error))
            {
                _stats.Clear();
                _stats.Add("目标", System.IO.Path.GetFileName(_path));
                _stats.Add("状态", "查询失败", Theme.Danger);
                _stats.Invalidate();
                SetSubtitle("查询失败：" + error, Theme.Danger);
                Dialog.Error(this, "查询失败", error);
                return;
            }

            _locks.Clear();
            _grid.Rows.Clear();
            for (int i = 0; i < locks.Count; i++)
            {
                Unlocker.LockInfo li = locks[i];
                int idx = _grid.Rows.Add(li.Name, li.Pid.ToString(), li.AppType, li.Service, li.Path);
                _grid.Rows[idx].Tag = li;
            }
            _locks.AddRange(locks);
            _grid.ClearSelection();
            UpdateStats();

            if (locks.Count == 0)
            {
                SetSubtitle(fileCount == 0 ? "没有可查的文件。" : "没有被占用，可以放心删除或移动。（已查 " + fileCount + " 个文件）",
                    Theme.Success);
            }
            else
            {
                SetSubtitle("发现 " + locks.Count + " 个占用进程" +
                    (_locks.Count > 0 && HasCritical() ? "，其中含关键进程，请谨慎处理。" : "。"), Theme.Warning);
            }
        }

        private bool HasCritical()
        {
            for (int i = 0; i < _locks.Count; i++)
            {
                if (_locks[i].AppType == "关键进程" || _locks[i].Pid <= 4) return true;
            }
            return false;
        }

        // --------------------------------------------------------------

        private void OnKillSelected(object sender, EventArgs e)
        {
            if (_busy) return;
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先在列表里选择要结束的占用进程。");
                return;
            }

            List<Unlocker.LockInfo> targets = new List<Unlocker.LockInfo>();
            for (int i = 0; i < _grid.SelectedRows.Count; i++)
            {
                Unlocker.LockInfo li = _grid.SelectedRows[i].Tag as Unlocker.LockInfo;
                if (li != null && li.Pid > 4) targets.Add(li);
            }
            if (targets.Count == 0)
            {
                Dialog.Warn(this, "无法结束", "系统关键进程（PID ≤ 4）不允许结束。");
                return;
            }

            string list = "";
            for (int i = 0; i < targets.Count && i < 8; i++)
                list += "　" + targets[i].Name + "（PID " + targets[i].Pid + "）\r\n";

            if (!Dialog.Confirm(this, "结束占用进程",
                "将结束 " + targets.Count + " 个进程：\r\n" + list +
                "\r\n未保存的修改会丢失；系统服务类进程结束后可能影响相关功能。"))
                return;

            _busy = true;
            SetButtonsEnabled(false);
            SetSubtitle("正在结束进程…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                List<string> errors = new List<string>();
                for (int i = 0; i < targets.Count; i++)
                {
                    string err;
                    if (Unlocker.Kill(targets[i].Pid, out err)) ok++;
                    else errors.Add(targets[i].Name + "（PID " + targets[i].Pid + "）：" + err);
                }

                int okCount = ok;
                Post(delegate { AfterKill(okCount, errors); });
            });
        }

        private void AfterKill(int ok, List<string> errors)
        {
            _busy = false;
            SetButtonsEnabled(true);

            string text = "已结束 " + ok + " 个进程。";
            if (errors.Count > 0)
            {
                text += "\r\n失败 " + errors.Count + " 个：\r\n";
                for (int i = 0; i < errors.Count && i < 6; i++) text += "· " + errors[i] + "\r\n";
            }

            // 结束后自动复查一次，让列表反映最新占用情况
            OnQuery(null, EventArgs.Empty);
            SetSubtitle("结束进程完成，正在复查占用…", ok > 0 ? Theme.Success : Theme.Warning);
            if (errors.Count > 0) Dialog.Warn(this, "结束进程完成（有失败项）", text);
        }
    }
}
