using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace YourApp.Updater
{
    /// <summary>
    /// 从「自托管的更新清单 update.json」检查更新。
    /// 该文件与官网一起放在 GitHub Pages 上（官网仓库 guyue-toolbox-site，如 https://Gu-yue-fy.github.io/guyue-toolbox-site/update.json），
    /// 由网站与客户端共用：无需 token、不限流、国内访问快。
    /// </summary>
    public class UpdateChecker
    {
        // ====== 更新清单地址（GitHub Pages 上的 update.json，免费托管）======
        private const string UpdateManifestUrl = "https://Gu-yue-fy.github.io/guyue-toolbox-site/update.json";

        private static readonly HttpClient _client = new HttpClient();

        /// <summary>检查是否有新版本。无更新或出错时返回 null。</summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync()
        {
            try
            {
                string json = await _client.GetStringAsync(UpdateManifestUrl);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var info = new UpdateInfo
                {
                    Version      = root.GetProperty("version").GetString(),
                    ReleaseNotes = root.TryGetProperty("notes", out var n) ? n.GetString() : "",
                    Date         = root.TryGetProperty("date", out var d) ? d.GetString() : ""
                };

                // 按当前操作系统挑对应平台的安装包直链
                if (root.TryGetProperty("assets", out var assets))
                {
                    if (OperatingSystem.IsWindows() && assets.TryGetProperty("win",   out var w)) info.DownloadUrl = w.GetString();
                    else if (OperatingSystem.IsMacOS() && assets.TryGetProperty("mac", out var m)) info.DownloadUrl = m.GetString();
                    else if (OperatingSystem.IsLinux() && assets.TryGetProperty("linux", out var l)) info.DownloadUrl = l.GetString();
                }
                // 清单未给当前平台直链时，退回发布页让用户手动下载
                if (string.IsNullOrEmpty(info.DownloadUrl) && root.TryGetProperty("releasePage", out var rp))
                    info.ReleasePage = rp.GetString();

                return IsNewer(info.Version, GetCurrentVersion()) ? info : null;
            }
            catch
            {
                // 网络异常 / 清单缺失时不打断用户，返回 null 表示本次不更新
                return null;
            }
        }

        /// <summary>读取程序集版本作为当前版本（推荐，避免写死）。</summary>
        public static string GetCurrentVersion()
        {
            var v = typeof(UpdateChecker).Assembly.GetName().Version;
            return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        public static bool IsNewer(string latest, string current)
            => CompareVersions(latest, current) > 0;

        /// <summary>语义化版本比较：latest>current 返回 1，相等 0，小于 -1。</summary>
        public static int CompareVersions(string a, string b)
        {
            a = (a ?? "0").TrimStart('v', 'V');
            b = (b ?? "0").TrimStart('v', 'V');
            var va = a.Split('.').Select(s => int.TryParse(s, out var x) ? x : 0).ToArray();
            var vb = b.Split('.').Select(s => int.TryParse(s, out var x) ? x : 0).ToArray();
            int len = Math.Max(va.Length, vb.Length);
            for (int i = 0; i < len; i++)
            {
                int x = i < va.Length ? va[i] : 0;
                int y = i < vb.Length ? vb[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
    }

    /// <summary>一次更新检查的返回结果。</summary>
    public class UpdateInfo
    {
        public string Version      { get; set; }  // 最新版本号，如 "1.2.0"
        public string DownloadUrl  { get; set; }  // 当前平台安装包直链（可空）
        public string ReleasePage { get; set; }   // 发布页地址（无直链时跳转）
        public string ReleaseNotes { get; set; }  // 更新说明
        public string Date         { get; set; }  // 发布日期 yyyy-MM-dd
    }
}
