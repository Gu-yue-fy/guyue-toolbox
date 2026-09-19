using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
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
            AddAction("打开项目主页", "network", ButtonVariant.Ghost, OnOpenProject);
            _checkButton = AddAction("检查更新", "refresh", ButtonVariant.Primary, OnCheckUpdate, 128);
            AddAction("自定义更新源", "doc", ButtonVariant.Ghost, OnSetSource, 140);

            _info.Caption = "版本信息";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;

            FlowLayoutPanel row = MakeRow(320, 0);
            row.Controls.Add(_info);
            AddRow(row);
            LoadInfo();

            // 软件概览：让本页不只是版本号，而是完整的能力与规则说明
            var overview = new InfoList();
            overview.Caption = "软件概览";
            overview.IconKind = "feature";
            overview.CaptionColor = Theme.Accent;
            overview.Add("功能页面", PageCatalog.All.Count + " 个（概览 / 优化 / 清理 / 系统管理 / 网络 / 设置）");
            overview.Add("优化项", TweakLibrary.All().Count + " 项，全部支持一键还原");
            overview.Add("开源协议", "MIT（完全免费，无功能限制、无广告）");
            overview.Add("更新方式", "GitHub Releases 自动检查，SHA256 校验防篡改");
            overview.Add("数据安全", "每次修改注册表前自动备份原值");
            AddFull(overview, 250, 14);
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
            // MainForm.AppVersion 转发自 AppInfo.Version，程序集版本也由它派生，
            // 因此界面与 exe 属性不会出现两个版本号
            return "v" + MainForm.AppVersion;
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

        /// <summary>自定义更新源（清空则恢复默认 GitHub 源）。</summary>
        private void OnSetSource(object sender, EventArgs e)
        {
            string current = UpdateChecker.UpdateSource();
            string input = Dialog.Input(this, "自定义更新源",
                "填入托管 update.json 的地址（留空恢复默认 GitHub 源）：", current);
            if (input == null) return;
            input = input.Trim();
            if (input.Length > 0 && !input.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !input.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                Dialog.Info(this, "地址无效", "更新源必须以 http(s):// 或 file: 开头。");
                return;
            }
            UpdateChecker.SetUpdateSource(input);
            SetSubtitle(input.Length == 0
                ? "已恢复默认 GitHub 更新源。"
                : "更新源已设置：" + input, Theme.Success);
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
                using (System.Diagnostics.Process.Start(UpdateChecker.ProjectUrl)) { }
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "无法打开", ex.Message);
            }
        }    }
}
