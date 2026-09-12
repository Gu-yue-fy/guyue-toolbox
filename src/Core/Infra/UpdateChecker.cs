using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SysToolbox.Core
{
    public sealed class UpdateInfo
    {
        public bool Ok;
        public string Error = "";
        public string Version = "";
        public string Url = "";
        public string Sha256 = "";
        public string Notes = "";

        public bool IsNewerThan(string currentVersion)
        {
            long mine = VersionNumber(currentVersion);
            long theirs = VersionNumber(Version);
            return mine > 0 && theirs > mine;
        }

        private static long VersionNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            string[] parts = text.Split('.');
            long a = 0, b = 0, c = 0;
            try
            {
                if (parts.Length > 0) a = long.Parse(parts[0], CultureInfo.InvariantCulture);
                if (parts.Length > 1) b = long.Parse(parts[1], CultureInfo.InvariantCulture);
                if (parts.Length > 2) c = long.Parse(parts[2], CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
            // 各段钳制到 4 位，避免进位碰撞（如 b>=1000 时与 a+1 混淆）
            if (a < 0) a = 0; if (a > 9999) a = 9999;
            if (b < 0) b = 0; if (b > 9999) b = 9999;
            if (c < 0) c = 0; if (c > 9999) c = 9999;
            return a * 100000000L + b * 10000L + c;
        }
    }

    /// <summary>
    /// 在线更新：从更新源拉取 version.json，比对版本号，下载新程序包并校验 SHA256，
    /// 最后通过外部脚本替换程序本体并重启。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>
        /// 默认更新源（GitHub 开源发布方式，推荐）：
        /// 1. 发版时在 GitHub 建 Release（tag 如 v1.0.1），上传 SysToolbox.exe 作为 asset；
        /// 2. 把 update.json（version/url/sha256 字段）提交到仓库，此处填 raw 直链；
        ///    url 字段填 Release asset 的下载地址（浏览器可达的直链）。
        /// 也可在「关于与更新」页自定义更新源（仍兼容任意托管 update.json 的地址）。
        /// </summary>
        public const string DefaultUpdateUrl = "https://raw.githubusercontent.com/Gu-yue-fy/guyue-toolbox-site/main/update.json";

        /// <summary>项目主页（「关于与更新」页跳转用）。发布前把 your-name 替换为实际 GitHub 用户名。</summary>
        public const string ProjectUrl = "https://github.com/Gu-yue-fy/guyue-toolbox";

        /// <summary>更新源是否已配置为真实地址（仍是 your-name 占位符时返回 false，检查/跳转会给出明确提示而非 404）。</summary>
        public static bool Configured
        {
            get { return DefaultUpdateUrl.IndexOf("your-name", StringComparison.OrdinalIgnoreCase) < 0; }
        }

        /// <summary>项目主页是否已配置（同上）。</summary>
        public static bool ProjectConfigured
        {
            get { return ProjectUrl.IndexOf("your-name", StringComparison.OrdinalIgnoreCase) < 0; }
        }

        private const string ConfigPath = @"Software\SysToolbox";

        /// <summary>最近一次检查结果（进程内缓存）。</summary>
        public static UpdateInfo Latest;

        /// <summary>更新源地址：注册表可覆盖默认值。</summary>
        public static string UpdateSource()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ConfigPath, false))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("UpdateUrl");
                        if (v != null && !string.IsNullOrEmpty(v.ToString())) return v.ToString();
                    }
                }
            }
            catch
            {
            }
            return DefaultUpdateUrl;
        }

        public static void SetUpdateSource(string url)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(ConfigPath, true))
                {
                    if (string.IsNullOrEmpty(url)) k.DeleteValue("UpdateUrl", false);
                    else k.SetValue("UpdateUrl", url, RegistryValueKind.String);
                }
            }
            catch
            {
            }
        }

        /// <summary>拉取更新描述文件。失败时 Ok=false 并带 Error。</summary>
        public static UpdateInfo Check()
        {
            UpdateInfo info = new UpdateInfo();
            if (!Configured)
            {
                info.Ok = false;
                info.Error = "更新源未配置：发布前需把 UpdateChecker.DefaultUpdateUrl 改为实际地址，或在「关于与更新」页自定义更新源。";
                return info;
            }
            try
            {
                string url = UpdateSource();
                string json;
                using (WebClient wc = new WebClient())
                {
                    wc.Headers["Cache-Control"] = "no-cache";
                    wc.Headers["User-Agent"] = "SysToolbox-UpdateCheck"; // GitHub 要求非空 UA
                    wc.Encoding = Encoding.UTF8;
                    string target = url;
                    // 仅对 http(s) 加缓存穿透参数（file:// 等本地测试地址不支持 query）
                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        target = url + (url.IndexOf('?') >= 0 ? "&" : "?") + "t=" +
                            Environment.TickCount.ToString(CultureInfo.InvariantCulture);
                    }
                    json = wc.DownloadString(target);
                }

                info.Version = JsonField(json, "version");
                info.Url = JsonField(json, "url");
                info.Sha256 = JsonField(json, "sha256");
                info.Notes = JsonField(json, "notes");
                info.Ok = info.Version.Length > 0 && info.Url.Length > 0;
                if (!info.Ok) info.Error = "更新描述文件内容不完整";
            }
            catch (Exception ex)
            {
                info.Ok = false;
                info.Error = ex.Message;
            }

            Latest = info;
            return info;
        }

        /// <summary>下载新版本到临时目录并校验 SHA256。返回本地文件路径。</summary>
        public static bool Download(UpdateInfo info, out string file, out string error)
        {
            return Download(info, out file, out error, null);
        }

        /// <summary>下载新版本；onPercent 在工作线程回调 0-100 的下载进度。</summary>
        public static bool Download(UpdateInfo info, out string file, out string error, Action<int> onPercent)
        {
            file = "";
            error = "";
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "SysToolbox_update_" +
                    DateTime.Now.Ticks.ToString(CultureInfo.InvariantCulture) + ".exe");

                using (WebClient wc = new WebClient())
                {
                    if (onPercent != null)
                    {
                        wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e)
                        {
                            try { onPercent(e.ProgressPercentage); } catch { }
                        };
                    }
                    wc.DownloadFile(info.Url, tmp);
                }

                // 供应链安全：update.json 必须提供 SHA256，缺失则拒绝安装（管理员工具被替换的后果严重）
                if (string.IsNullOrEmpty(info.Sha256))
                {
                    error = "更新描述缺少 SHA256 校验值，已拒绝安装（防供应链投毒）。";
                    return false;
                }
                string actual = FileSha256(tmp);
                if (!string.Equals(actual, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    error = "文件校验失败（SHA256 不匹配），已取消更新。";
                    return false;
                }

                file = tmp;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 启动外部替换脚本（等待本程序退出 → 覆盖 → 重启），由调用方随后退出程序。
        /// 用 PowerShell（UTF-8 BOM 脚本）替代 cmd 批处理——中文安装路径在 ANSI 批处理下会乱码导致复制失败。
        /// </summary>
        public static bool ApplyUpdate(string newExe)
        {
            try
            {
                string current = Assembly.GetEntryAssembly().Location;
                string script = Path.Combine(Path.GetTempPath(), "SysToolbox_update.ps1");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Start-Sleep -Seconds 2");
                sb.AppendLine("Get-Process SysToolbox -ErrorAction SilentlyContinue | Stop-Process -Force");
                sb.AppendLine("Start-Sleep -Seconds 1");
                sb.AppendLine("Copy-Item -LiteralPath '" + newExe.Replace("'", "''") + "' -Destination '" +
                    current.Replace("'", "''") + "' -Force");
                sb.AppendLine("Start-Process -FilePath '" + current.Replace("'", "''") + "'");

                File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true)); // BOM：中文路径不乱码
                Shell.Run("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"", 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>JSON 字符串字段提取（处理 \" \n \t 等转义；不引入完整解析器）。</summary>
        private static string JsonField(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return "";
            string needle = "\"" + field + "\"";
            int idx = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx + needle.Length);
            if (colon < 0) return "";
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return "";

            StringBuilder sb = new StringBuilder();
            i++;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == '"' || n == '\\' || n == '/') sb.Append(n);
                    else if (n == 'n') sb.AppendLine();
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'r') sb.Append('\r');
                    else sb.Append(n);
                    i += 2;
                    continue;
                }
                if (c == '"') return sb.ToString();
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string FileSha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(file))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }
    }
}
