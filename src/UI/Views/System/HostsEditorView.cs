using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// Hosts 编辑器：查看与编辑系统 hosts 文件（自动备份原文件）。
    /// 注意：本页是通用编辑器，与此前已移除的「广告屏蔽」无关。
    /// </summary>
    public sealed class HostsEditorView : ViewBase
    {
        private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";

        private readonly NoticeBar _notice = new NoticeBar();
        private readonly TextBox _editor = new TextBox();
        private readonly Panel _editorPanel = new Panel();
        private AccentButton _saveButton;
        private AccentButton _reloadButton;
        private bool _dirty;

        public HostsEditorView()
            : base("Hosts 编辑", "查看与编辑系统 hosts 解析文件")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            _notice.NoticeText = "hosts 用于本地域名解析（如把测试域名指到本机）。保存前会自动备份原文件为 hosts.bak，可随时还原。";

            _editor.Multiline = true;
            _editor.ScrollBars = ScrollBars.Both;
            _editor.BorderStyle = BorderStyle.None;
            _editor.BackColor = Theme.CardBg;
            _editor.ForeColor = Theme.TextPrimary;
            _editor.Font = new Font("Consolas", 9.5F);
            _editor.WordWrap = false;
            _editor.TextChanged += delegate { if (!_dirty) { _dirty = true; UpdateButtons(); } };

            _editorPanel.BackColor = Theme.CardBg;
            _editorPanel.Margin = new Padding(0, 0, 0, 12);
            _editorPanel.Tag = "stretch";
            _editorPanel.Controls.Add(_editor);
            _editorPanel.Resize += delegate
            {
                _editor.SetBounds(8, 8, _editorPanel.Width - 16, _editorPanel.Height - 16);
            };

            _saveButton = AddAction("保存", "check", ButtonVariant.Primary, OnSave, 96);
            _reloadButton = AddAction("重新加载", "refresh", ButtonVariant.Secondary, delegate { LoadFile(); }, 116);
            AddAction("还原备份", "history", ButtonVariant.Secondary, OnRestoreBackup, 116);
            AddAction("打开所在目录", "folder", ButtonVariant.Ghost, OnOpenFolder, 140);

            BuildLayout();
        }

        public override bool IsBusy
        {
            get { return false; }
        }

        private void BuildLayout()
        {
            AddFull(_notice, 34, 12);
            AddFull(_editorPanel, 0, 0);
            Body.Resize += delegate { RefreshLayout(); };
            LayoutEditor();
        }

        private void LayoutEditor()
        {
            // 编辑器占满通知栏以下剩余空间（至少 300px）
            int bodyH = Body.Parent != null ? Body.Parent.Height : 600;
            _editorPanel.Height = Math.Max(300, bodyH - HeaderHeight - 140);
        }

        private void UpdateButtons()
        {
            _saveButton.Text = _dirty ? "保存*" : "保存";
            _saveButton.Invalidate();
        }

        private void LoadFile()
        {
            try
            {
                _dirty = false;
                _editor.Text = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";
                UpdateButtons();
                SetSubtitle("已加载 hosts（" + _editor.Text.Length + " 字符）。", Theme.TextSecondary);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "读取失败", ex.Message);
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(HostsPath))
                {
                    File.Copy(HostsPath, HostsPath + ".bak", true);
                }
                // UTF-8 无 BOM：保留用户写入的非 ASCII 注释（hosts 文件兼容 UTF-8）
            File.WriteAllText(HostsPath, _editor.Text, new System.Text.UTF8Encoding(false));
                _dirty = false;
                UpdateButtons();
                SetSubtitle("已保存（原文件备份为 hosts.bak）。刷新 DNS 缓存可用「网络修复」页。", Theme.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "保存失败",
                    ex.Message + "\r\n\r\nhosts 在系统目录，需要管理员权限（程序通常已具备）。");
            }
        }

        private void OnRestoreBackup(object sender, EventArgs e)
        {
            string bak = HostsPath + ".bak";
            if (!File.Exists(bak))
            {
                Dialog.Info(this, "没有备份", "尚未保存过任何修改（hosts.bak 不存在）。");
                return;
            }
            if (!Dialog.Confirm(this, "还原备份", "用 hosts.bak 覆盖当前 hosts 吗？")) return;
            try
            {
                File.Copy(bak, HostsPath, true);
                LoadFile();
                SetSubtitle("已从备份还原。", Theme.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "还原失败", ex.Message);
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            Shell.OpenSelect(HostsPath);
        }

        private bool _resizeHooked;

        public override void OnActivated()
        {
            if (_editor.Text.Length == 0 && !_dirty) LoadFile();
            if (!_resizeHooked) { _resizeHooked = true; Body.Resize += delegate { LayoutEditor(); }; }
        }
    }
}
