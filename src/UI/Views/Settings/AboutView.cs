/* UI/Views/Home/AboutView.cs — 关于与更新页。 */

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
            AddAction("打开项目主页", "globe", ButtonVariant.Secondary, OnOpenProject);
            _checkButton = AddAction("检查更新", "refresh", ButtonVariant.Primary, OnCheckUpdate, 128);
            AddAction("自定义更新源", "doc", ButtonVariant.Ghost, OnSetSource, 140);
            AddAction("复制版本信息", "copy", ButtonVariant.Secondary, OnCopyInfo, 140);
            AddAction("打开程序目录", "folder", ButtonVariant.Ghost, OnOpenAppFolder, 140);

            _info.Caption = "版本信息";
            _info.IconKind = "info";
            _info.CaptionColor = Theme.Accent;
            // 4 行：当前版本 / 最新版本 / 更新方式 / 更新源。
            // 之前没定高，卡片沿用默认高度 → 第 3 行起被裁掉（"更新方式"一直看不见）。
            _info.Height = InfoList.HeightFor(4);

            FlowLayoutPanel row = MakeRow(0, 0);
            row.Controls.Add(_info);
            AddRow(row);
            LoadInfo();

            // 软件概览：让本页不只是版本号，而是完整的能力与规则说明
            var overview = new InfoList();
            overview.Caption = "软件概览";
            overview.IconKind = "feature";
            overview.CaptionColor = Theme.Accent;
            // 优化项总数改到后台数：TweakLibrary.All() 实测约 2 秒，放在构造函数里
            // 等于「点开关于页先白屏 2 秒」。行文案先占位，数完回填。
            InfoList ov = overview;
            Action<string> fillOverview = delegate (string tweakText)
            {
                ov.Clear();
                ov.Add("功能页面", PageCatalog.All.Count + " 个（14 大类功能，页面顶部按页签切换）");
                ov.Add("优化项", tweakText);
                ov.Add("开源协议", "MIT（完全免费，无功能限制、无广告）");
                ov.Add("更新方式", "GitHub Releases 自动检查，SHA256 校验防篡改");
                ov.Add("数据安全", "每次修改注册表前自动备份原值");
                ov.Invalidate();
            };
            fillOverview("统计中…");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int n = 0;
                try { n = TweakLibrary.All().Count; } catch { }
                Post(delegate { fillOverview(n + " 项，全部支持一键还原"); });
            });
            AddFull(overview, 250, 14);
        }

        private void LoadInfo()
        {
            _info.Clear();
            _info.Add("当前版本", CurrentVersion());
            UpdateInfo latest = UpdateChecker.Latest;
            _info.Add("最新版本", latest != null && latest.Ok ? latest.Version : "未检查");
            _info.Add("更新方式", "GitHub Releases（自动比对版本与校验）");
            _info.Add("更新源", UpdateChecker.UpdateSource());
            _info.Invalidate();
            RefreshLayout();
        }

        /// <summary>复制一段可直接粘贴进反馈帖的版本信息（含系统版本与权限状态）。</summary>
        private void OnCopyInfo(object sender, EventArgs e)
        {
            string text =
                "古月工具箱 " + CurrentVersion() + "\r\n"
                + "操作系统：" + Environment.OSVersion.VersionString + "\r\n"
                + "系统版本：" + SystemInfoLine() + "\r\n"
                + "运行权限：" + (Native.IsElevatedCached ? "管理员" : "标准用户") + "\r\n"
                + "优化项数：" + TweakLibrary.All().Count + "\r\n"
                + "更新源：" + UpdateChecker.UpdateSource() + "\r\n"
                + "生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            try
            {
                Clipboard.SetText(text);
                SetSubtitle("版本信息已复制到剪贴板。", Theme.Success);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "复制失败", ex.Message);
            }
        }

        private static string SystemInfoLine()
        {
            try
            {
                SystemSnapshot s = SysInfo.Capture(false, -1);
                if (s != null && !string.IsNullOrEmpty(s.OsVersion)) return s.OsVersion;
            }
            catch
            {
            }
            return "未读取到";
        }

        private void OnOpenAppFolder(object sender, EventArgs e)
        {
            try
            {
                string exe = Application.ExecutablePath;
                Shell.OpenSelect(exe);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "无法打开", ex.Message);
            }
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
                Shell.OpenUrl(UpdateChecker.ProjectUrl);
            }
            catch (Exception ex)
            {
                Dialog.Error(this, "无法打开", ex.Message);
            }
        }
    }
}
