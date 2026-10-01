// 文件说明：应用精简页（调用 scripts\appx.ps1 列出/卸载 UWP 预装应用，虚拟模式网格）

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views.Apps
{
    /// <summary>
    /// 应用精简：列出「可安全移除」「谨慎」两类的 UWP 预装应用，多选后卸载（仅当前用户级包）。
    /// </summary>
    public sealed class AppxView : ViewBase
    {
        private sealed class AppxRow
        {
            public string Name;
            public string FullName;
            public string Category;
            public bool Selected;
        }

        private const string CategorySafe = "可安全移除";

        private readonly DarkGrid _grid = new DarkGrid();
        private readonly NoticeBar _notice = new NoticeBar();
        private readonly Panel _empty = new Panel();
        private readonly Label _emptyTitle = new Label();
        private readonly Label _emptySub = new Label();
        private readonly AccentButton _emptyButton = new AccentButton();

        private readonly List<AppxRow> _rows = new List<AppxRow>();
        private bool _busy;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）。</summary>
        public override bool IsBusy { get { return _busy; } }
        private bool _scanned;
        private AccentButton _scanButton;
        private AccentButton _removeButton;

        public AppxView()
            : base("应用精简", "卸载可安全移除的 UWP 预装应用，释放空间与后台占用")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "绿色分类「可安全移除」可直接卸载；琥珀分类「谨慎」卸载后若需找回，可在 Microsoft Store 重新安装。仅卸载当前用户的包，不触碰系统组件。";

            _scanButton = AddAction("扫描应用", "refresh", ButtonVariant.Primary, delegate { StartScan(); }, 118);
            _removeButton = AddAction("卸载所选", "trash", ButtonVariant.Danger, OnRemoveClick, 118);
            _removeButton.Enabled = false;

            BuildGrid();
            BuildEmpty();
            BuildLayout();
        }



        private void BuildGrid()
        {
            // 虚拟模式：行数据在 _rows 列表，绘制时按需填充；勾选列列级可编辑、其余列只读
            _grid.VirtualMode = true;
            _grid.ReadOnly = false;
            _grid.UseOwnScrollbar = true;
            _grid.Columns.Add(new DarkCheckColumn());
            _grid.CheckOnRowClick = true;
            _grid.AddTextColumn("", 34, false);        // 行前色块（按分类上色）
            _grid.AddTextColumn("名称", 230, false);
            _grid.AddTextColumn("分类", 110, false);
            _grid.AddFillColumn("包全名", 240);

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellValuePushed += OnCellValuePushed;
            _grid.CellFormatting += OnCellFormatting;
        }

        private void BuildEmpty()
        {
            _empty.BackColor = Theme.CardBg;
            _empty.Visible = false;

            _emptyTitle.Text = "未发现可精简的应用";
            _emptyTitle.ForeColor = Theme.TextPrimary;
            _emptyTitle.Font = Theme.FontSubTitle;
            _emptyTitle.TextAlign = ContentAlignment.MiddleCenter;

            _emptySub.Text = "扫描后这里会列出「可安全移除」与「谨慎」两类的 UWP 预装应用；谨慎类可在 Microsoft Store 重新安装。";
            _emptySub.ForeColor = Theme.TextMuted;
            _emptySub.Font = Theme.FontSmall;
            _emptySub.TextAlign = ContentAlignment.MiddleCenter;

            _emptyButton.Text = "立即扫描";
            _emptyButton.IconKind = "refresh";
            _emptyButton.Variant = ButtonVariant.Primary;
            _emptyButton.Size = new Size(120, 34);
            _emptyButton.Click += delegate { StartScan(); };

            _empty.Controls.Add(_emptyTitle);
            _empty.Controls.Add(_emptySub);
            _empty.Controls.Add(_emptyButton);

            _empty.Resize += delegate { LayoutEmpty(); };
        }

        private void LayoutEmpty()
        {
            int w = _empty.ClientSize.Width;
            int h = _empty.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int top = h / 2 - 48;
            if (top < 16) top = 16;
            _emptyTitle.SetBounds(24, top, Math.Max(60, w - 48), 28);
            _emptySub.SetBounds(48, top + 34, Math.Max(60, w - 96), 40);
            _emptyButton.SetBounds(w / 2 - 60, top + 86, 120, 34);
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);
            AddFull(_grid, 340, 0);
            AddFull(_empty, 300, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
        }

        /// <summary>空状态与网格二选一：无数据时显示空状态提示，否则网格撑满剩余高度。</summary>
        private void Relayout()
        {
            bool empty = _rows.Count == 0 && !_busy;
            _grid.Visible = !empty;
            _empty.Visible = empty;

            int extraUsed = 34 + 12; // 提示条高度 + 下方间距
            if (empty)
            {
                int avail = ViewportHeight - (Body.Padding.Top + Body.Padding.Bottom + extraUsed);
                if (avail < 220) avail = 220;
                if (_empty.Height != avail) _empty.Height = avail;
                RefreshLayout();
            }
            else
            {
                LayoutFill(_grid, extraUsed, 220);
            }
        }

        public override void OnActivated()
        {
            if (!_scanned) StartScan();
        }

        // --------------------------------------------------------------
        // 数据取数（后台线程执行 PowerShell 脚本）
        // --------------------------------------------------------------

        private static string ScriptPath()
        {
            try { return Path.Combine(Application.StartupPath, "scripts", "appx.ps1"); }
            catch { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts", "appx.ps1"); }
        }

        private static object TryParse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return Json.Parse(text); }
            catch { return null; }
        }

        private string ReadApps(out List<AppxRow> rows)
        {
            rows = new List<AppxRow>();
            try
            {
                string script = ScriptPath();
                if (!File.Exists(script)) return "找不到脚本 " + script;

                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Action list",
                    120000, Encoding.UTF8);

                if (!r.Ok)
                {
                    Dictionary<string, object> em = Json.AsObject(TryParse(r.Output));
                    string eMsg = Json.Str(em, "Error", "");
                    if (eMsg.Length > 0) return eMsg;
                    return r.Error != null && r.Error.Length > 0 ? r.Error.Trim() : "PowerShell 退出码 " + r.ExitCode;
                }

                List<object> arr = Json.AsArray(Json.Parse(r.Output));
                if (arr == null) return "脚本返回的数据格式异常";

                for (int i = 0; i < arr.Count; i++)
                {
                    Dictionary<string, object> m = Json.AsObject(arr[i]);
                    if (m == null) continue;
                    AppxRow row = new AppxRow();
                    row.Name = Json.Str(m, "Name", "");
                    row.FullName = Json.Str(m, "FullName", "");
                    row.Category = Json.Str(m, "Category", "谨慎");
                    if (string.IsNullOrEmpty(row.FullName)) continue;
                    rows.Add(row);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private string RemoveApps(List<string> pkgs, out int ok, out int failed)
        {
            ok = 0;
            failed = 0;
            try
            {
                string script = ScriptPath();
                if (!File.Exists(script)) return "找不到脚本 " + script;

                string joined = string.Join(";", pkgs.ToArray());
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Action remove -Pkgs \"" + joined + "\"",
                    120000, Encoding.UTF8, isChange: true);

                if (!r.Ok)
                {
                    Dictionary<string, object> em = Json.AsObject(TryParse(r.Output));
                    string eMsg = Json.Str(em, "Error", "");
                    if (eMsg.Length > 0) return eMsg;
                    return r.Error != null && r.Error.Length > 0 ? r.Error.Trim() : "PowerShell 退出码 " + r.ExitCode;
                }

                Dictionary<string, object> m = Json.AsObject(TryParse(r.Output));
                if (m == null) return "脚本返回的数据格式异常";

                List<object> rem = Json.AsArray(Json.Get(m, "Removed"));
                List<object> fail = Json.AsArray(Json.Get(m, "Failed"));
                ok = rem == null ? 0 : rem.Count;
                failed = fail == null ? 0 : fail.Count;
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // --------------------------------------------------------------
        // 扫描 / 卸载流程
        // --------------------------------------------------------------

        private void StartScan()
        {
            if (_busy) return;
            _busy = true;
            _scanned = true;
            _scanButton.Enabled = false;
            _removeButton.Enabled = false;
            _empty.Visible = false;
            _grid.Visible = true;
            Relayout();
            SetSubtitle("正在读取已安装应用…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<AppxRow> rows;
                string error = ReadApps(out rows);

                Post(delegate
                {
                    _busy = false;
                    _scanButton.Enabled = true;

                    if (error != null)
                    {
                        SetSubtitle("扫描失败：" + error, Theme.Danger);
                        Dialog.Error(this, "扫描失败",
                            "无法读取已安装应用：\r\n" + error +
                            "\r\n\r\n请确认 scripts\\appx.ps1 存在，并允许 PowerShell 执行。");
                        return;
                    }

                    _rows.Clear();
                    _rows.AddRange(rows);
                    Render();
                });
            });
        }

        private void Render()
        {
            _grid.SuspendLayout();
            if (_grid.RowCount != _rows.Count) _grid.RowCount = _rows.Count;
            _grid.ResumeLayout();
            if (_rows.Count == 0) _grid.ClearSelection();
            _grid.Invalidate();

            int safe = 0;
            int caution = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (IsSafe(_rows[i].Category)) safe++;
                else caution++;
            }

            SetSubtitle(_rows.Count > 0
                ? "共 " + _rows.Count + " 个可精简应用（可安全移除 " + safe + " · 谨慎 " + caution + "）。"
                : "未发现可精简的应用。", _rows.Count > 0 ? Theme.Success : Theme.TextSecondary);
            Relayout();
            UpdateRemoveButton();
        }

        private void OnRemoveClick(object sender, EventArgs e)
        {
            if (_busy) return;

            List<AppxRow> targets = SelectedRows();
            if (targets.Count == 0)
            {
                Dialog.Info(this, "未选择", "请先勾选要卸载的应用。");
                return;
            }

            if (!Dialog.Confirm(this, "卸载所选应用",
                "将卸载 " + targets.Count + " 个应用。\r\n\r\n谨慎类应用可通过 Microsoft Store 重新安装；卸载只作用于当前用户，不影响系统组件。是否继续？"))
                return;

            _busy = true;
            _scanButton.Enabled = false;
            _removeButton.Enabled = false;
            SetSubtitle("正在卸载 " + targets.Count + " 个应用…", Theme.Warning);

            List<string> pkgs = new List<string>();
            for (int i = 0; i < targets.Count; i++) pkgs.Add(targets[i].FullName);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok;
                int failed;
                string error = RemoveApps(pkgs, out ok, out failed);

                Post(delegate
                {
                    _busy = false;
                    _scanButton.Enabled = true;
                    _removeButton.Enabled = false;

                    if (error != null)
                    {
                        SetSubtitle("卸载失败：" + error, Theme.Danger);
                        Dialog.Error(this, "卸载失败", error);
                        return;
                    }

                    if (failed > 0)
                    {
                        Toast("已卸载 " + ok + " 个，失败 " + failed + " 个",
                            "谨慎类应用可通过 Microsoft Store 重新安装。", ToastKind.Warning);
                        SetSubtitle("卸载完成：成功 " + ok + " 个，失败 " + failed + " 个。", Theme.Warning);
                    }
                    else
                    {
                        Toast("卸载完成", "已卸载 " + ok + " 个应用。", ToastKind.Success);
                        SetSubtitle("已卸载 " + ok + " 个应用。", Theme.Success);
                    }

                    StartScan(); // 重新扫描刷新列表
                });
            });
        }

        // --------------------------------------------------------------
        // 网格数据填充与选择
        // --------------------------------------------------------------

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            AppxRow row = _rows[e.RowIndex];

            switch (e.ColumnIndex)
            {
                case 0: e.Value = row.Selected; break;
                case 1: e.Value = "●"; break;
                case 2: e.Value = row.Name; break;
                case 3: e.Value = row.Category; break;
                case 4: e.Value = row.FullName; break;
            }
        }

        private void OnCellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.ColumnIndex != 0) return;
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;

            bool selected = false;
            try { selected = Convert.ToBoolean(e.Value); }
            catch { }
            _rows[e.RowIndex].Selected = selected;
            UpdateRemoveButton();
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            // 行前色块与分类列按分类上色（可安全移除=Success，谨慎=Warning）
            if (e.ColumnIndex == 1 || e.ColumnIndex == 3)
            {
                e.CellStyle.ForeColor = CategoryColor(_rows[e.RowIndex].Category);
                e.FormattingApplied = true;
            }
        }

        private static bool IsSafe(string category)
        {
            return string.Equals(category, CategorySafe, StringComparison.OrdinalIgnoreCase);
        }

        private static Color CategoryColor(string category)
        {
            return IsSafe(category) ? Theme.Success : Theme.Warning;
        }

        private List<AppxRow> SelectedRows()
        {
            List<AppxRow> list = new List<AppxRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected) list.Add(_rows[i]);
            }
            return list;
        }

        private void UpdateRemoveButton()
        {
            int n = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Selected) n++;
            }
            _removeButton.Enabled = !_busy && n > 0;
            _removeButton.Text = n > 0 ? "卸载所选 (" + n + ")" : "卸载所选";
            _removeButton.FitToText(118);
            LayoutActions();
        }
    }
}