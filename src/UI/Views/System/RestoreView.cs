using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public sealed class RestoreView : ViewBase
    {
        private readonly DarkGrid _grid = new DarkGrid();
        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();

        private readonly List<RestorePoint> _points = new List<RestorePoint>();
        private bool _busy;
        private bool _loaded;
        private AccentButton _createButton;
        private AccentButton _deleteButton;

        public RestoreView()
            : base("系统还原点", "查看、创建与删除系统还原点，给系统改动上保险")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "还原点可在安装驱动/软件/改注册表前回滚系统。创建与删除需要管理员权限，且系统还原需在系统盘上先开启。";

            _summary.Caption = "还原点";
            _summary.IconKind = "history";
            _summary.CaptionColor = Theme.Accent;

            _createButton = AddAction("创建还原点", "plus", ButtonVariant.Primary, OnCreateClick, 130);
            _deleteButton = AddAction("删除选中", "trash", ButtonVariant.Danger, OnDeleteClick, 118);
            AddAction("刷新", "refresh", ButtonVariant.Secondary, delegate { Load(); }, 92);

            BuildGrid();
            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return _busy; }
        }

        private void BuildGrid()
        {
            _grid.ReadOnly = true;
            _grid.UseOwnScrollbar = true;
            _grid.AddFillColumn("描述", 200);
            _grid.AddTextColumn("类型", 130, false);
            _grid.AddTextColumn("创建时间", 150, false);
            _grid.AddTextColumn("序号", 80, true);

            _grid.SelectionChanged += delegate { UpdateActions(); };
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);

            FlowLayoutPanel row = MakeRow(0, 18);
            row.Controls.Add(_summary);
            AddRow(row);

            AddFull(_grid, 320, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        private void Relayout()
        {
            LayoutGrid(_grid, _summary, 0, 220);
        }

        public override void OnActivated()
        {
            if (!_loaded) Load();
        }

        // --------------------------------------------------------------

        private void Load()
        {
            if (_busy) return;
            _busy = true;
            _loaded = true;
            _deleteButton.Enabled = false;
            SetSubtitle("正在读取系统还原点…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<RestorePoint> result = null;
                try { result = RestorePoints.List(); }
                catch { result = new List<RestorePoint>(); }

                Post(delegate
                {
                    _busy = false;
                    _points.Clear();
                    _points.AddRange(result);
                    Render();

                    _summary.Clear();
                    _summary.Add("还原点数量", _points.Count + " 个");
                    _summary.Add("最新创建", _points.Count > 0 ? _points[0].CreatedText : "无",
                        _points.Count > 0 ? Theme.Success : Theme.TextPrimary);
                    _summary.Invalidate();

                    SetSubtitle(_points.Count > 0
                        ? "已读取 " + _points.Count + " 个系统还原点。"
                        : "未检测到还原点，可能系统还原未开启或本盘未受保护。",
                        _points.Count > 0 ? Theme.Success : Theme.Warning);
                    Relayout();
                });
            });
        }

        private void Render()
        {
            _grid.Rows.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                RestorePoint p = _points[i];
                int idx = _grid.Rows.Add(p.Description, p.TypeText, p.CreatedText, p.Sequence.ToString());
                _grid.Rows[idx].Tag = p;
            }
            _grid.ClearSelection();
            UpdateActions();
        }

        private void UpdateActions()
        {
            bool has = _grid.SelectedRows.Count > 0;
            _deleteButton.Enabled = has && !_busy;
        }

        // --------------------------------------------------------------

        private void OnCreateClick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (!Native.IsElevated())
            {
                bool go = Dialog.Confirm(this, "需要管理员权限",
                    "创建系统还原点需要管理员权限。\r\n\r\n是否以管理员身份重新启动本程序？");
                if (go && Shell.RestartElevated(""))
                {
                    Application.Exit();
                }
                else if (go)
                {
                    Dialog.Error(this, "提权失败", "未能以管理员身份启动，请右键程序选择「以管理员身份运行」。");
                }
                return;
            }

            string name = Dialog.Input(this, "创建还原点",
                "为这个还原点取一个名称（便于以后识别）：",
                "手动还原点 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            _busy = true;
            _createButton.Enabled = false;
            SetSubtitle("正在创建还原点…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = RestorePoints.Create(name, out error);
                Post(delegate
                {
                    _busy = false;
                    _createButton.Enabled = true;
                    if (ok)
                    {
                        Dialog.Success(this, "已创建", "系统还原点「" + name + "」已创建。");
                        Load();
                    }
                    else
                    {
                        Dialog.Error(this, "创建失败",
                            "无法创建还原点：\r\n" + error + "\r\n\r\n请确认系统还原已在系统盘上开启，并以管理员身份运行。");
                        SetSubtitle("创建失败：" + error, Theme.Danger);
                    }
                });
            });
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0)
            {
                Dialog.Info(this, "未选择还原点", "请先在列表中点击选中一个还原点（整行高亮），再删除。");
                return;
            }
            RestorePoint p = _grid.SelectedRows[0].Tag as RestorePoint;
            if (p == null) return;

            if (!Dialog.ConfirmDanger(this, "删除还原点",
                "删除还原点「" + p.Description + "」（序号 " + p.Sequence + "）。",
                "不可撤销：该还原点会被彻底移除，无法找回。",
                "只影响这一个还原点；其他还原点与当前系统文件不受影响。",
                "删除", true))
                return;

            _busy = true;
            _deleteButton.Enabled = false;
            SetSubtitle("正在删除还原点…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = RestorePoints.Delete(p.Sequence, out error);
                Post(delegate
                {
                    _busy = false;
                    if (ok)
                    {
                        Dialog.Success(this, "已删除", "还原点「" + p.Description + "」已删除。");
                        Load();
                    }
                    else
                    {
                        _deleteButton.Enabled = true;
                        Dialog.Error(this, "删除失败", "无法删除还原点：\r\n" + error);
                        SetSubtitle("删除失败：" + error, Theme.Danger);
                    }
                });
            });
        }    }
}
