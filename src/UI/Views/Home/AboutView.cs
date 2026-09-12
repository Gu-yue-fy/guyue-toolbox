using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using SysToolbox.Core;

namespace SysToolbox.UI.Views
{
    /// <summary>
    /// 关于与更新：版本信息、检查更新（GitHub Releases 方式）与项目主页入口。
    /// 更新方式：仓库内托管 update.json（version/url/sha256），程序比对版本后
    /// 下载新程序包、校验 SHA256，由外部脚本静默替换并重启。
    /// </summary>
    public sealed class AboutView : ViewBase
    {
        private readonly InfoList _info = new InfoList();
        private readonly NoticeBar _notice = new NoticeBar();
        private AccentButton _checkButton;

        public AboutView()
            : base("关于与更新", "查看版本、获取最新版本与项目动态")
        {
            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Accent;
            _notice.NoticeText = "本程序完全免费并开放源代码，无任何功能限制。更新数据来自 GitHub Releases。";

            AddAction("打开项目主页", "network", ButtonVariant.Ghost, OnOpenProject);
            _checkButton = AddAction("检查更新", "refresh", ButtonVariant.Primary, OnCheckUpdate, 128);

            _info.Caption = "版本信息";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;

            AddFull(_notice, 42, 16);
            FlowLayoutPanel row = MakeRow(320, 0);
            row.Controls.Add(_info);
            AddRow(row);
            LoadInfo();
        }

        private void LoadInfo()
        {
            _info.Clear();
            _info.Add("当前版本", CurrentVersion());
            UpdateInfo latest = UpdateChecker.Latest;
            _info.Add("最新版本", latest != null && latest.Ok ? latest.Version : "未检查");
            _info.Add("更新方式", "GitHub Releases（自动比对版本与校验）");
            _info.Invalidate();
            RefreshLayout();
        }

        private static string CurrentVersion()
        {
            return "v" + MainForm.AppVersion; // 单一版本来源：MainForm.AppVersion（与程序集版本一致）
        }

        private void OnCheckUpdate(object sender, EventArgs e)
        {
            if (_checkButton == null || !_checkButton.Enabled) return;
            _checkButton.Enabled = false;
            _checkButton.Text = "检查中…";
            _checkButton.Invalidate();
            SetSubtitle("正在连接更新源…", Theme.Warning);

            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = UpdateChecker.Check();
                Post(delegate
                {
                    _checkButton.Enabled = true;
                    _checkButton.Text = "检查更新";
                    _checkButton.Invalidate();
                    LoadInfo();

                    if (!info.Ok)
                    {
                        SetSubtitle("检查失败：" + info.Error, Theme.Danger);
                        return;
                    }
                    if (!info.IsNewerThan(CurrentVersion().TrimStart('v')))
                    {
                        SetSubtitle("已是最新版本。", Theme.Success);
                        return;
                    }
                    SetSubtitle("发现新版本 " + info.Version + "，可下载安装。", Theme.Success);
                    OfferDownload(info);
                });
            });
        }

        private void OfferDownload(UpdateInfo info)
        {
            if (!Dialog.Confirm(this, "发现新版本",
                "最新版本：" + info.Version + "\r\n\r\n" +
                (info.Notes.Length > 0 ? "更新说明：\r\n" + info.Notes + "\r\n\r\n" : "") +
                "是否现在下载并安装？（自动校验完整性，安装后自动重启）"))
            {
                SetSubtitle("已跳过 " + info.Version + "，可稍后在「关于与更新」手动检查。", Theme.TextSecondary);
                return;
            }

            SetSubtitle("正在下载 " + info.Version + "…", Theme.Warning);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file, error;
                bool ok = UpdateChecker.Download(info, out file, out error, delegate (int percent)
                {
                    Post(delegate { SetSubtitle("正在下载 " + info.Version + "…" + percent + "%", Theme.Warning); });
                });
                Post(delegate
                {
                    if (!ok)
                    {
                        SetSubtitle("下载失败：" + error, Theme.Danger);
                        return;
                    }
                    if (UpdateChecker.ApplyUpdate(file))
                    {
                        SetSubtitle("下载完成，正在安装并重启…", Theme.Success);
                        Application.Exit();
                    }
                    else
                    {
                        SetSubtitle("安装失败。", Theme.Danger);
                    }
                });
            });
        }

        private void OnOpenProject(object sender, EventArgs e)
        {
            if (!UpdateChecker.ProjectConfigured)
            {
                Dialog.Info(this, "未配置项目主页",
                    "项目主页地址尚未配置（UpdateChecker.ProjectUrl 仍是占位符），发布前请填入实际地址。");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(UpdateChecker.ProjectUrl);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "无法打开", ex.Message);
            }
        }

        private void Post(ThreadStart action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch
            {
            }
        }
    }
}
