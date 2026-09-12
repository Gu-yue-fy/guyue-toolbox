using System;
using System.IO;
using System.Threading.Tasks;
using YourApp.Updater;

// ====== 控制台示例（也可在 WinForms/WPF 的按钮事件里照抄）======

class Program
{
    static async Task Main()
    {
        Console.WriteLine("正在检查更新...");
        var update = await UpdateChecker.CheckForUpdateAsync();

        if (update == null)
        {
            Console.WriteLine("已经是最新版本。");
            return;
        }

        Console.WriteLine($"发现新版本 v{update.Version}" + (update.Date != "" ? $"（{update.Date}）" : ""));
        Console.WriteLine("更新内容：\n" + update.ReleaseNotes);

        if (!string.IsNullOrEmpty(update.DownloadUrl))
        {
            Console.Write("是否下载并更新？(y/n) ");
            if (Console.ReadKey().Key == ConsoleKey.Y)
                await DownloadAndRunAsync(update.DownloadUrl);
        }
        else if (!string.IsNullOrEmpty(update.ReleasePage))
        {
            Console.WriteLine("即将打开发布页下载...");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.ReleasePage) { UseShellExecute = true });
        }
    }

    static async Task DownloadAndRunAsync(string url)
    {
        string fileName = Path.GetFileName(new Uri(url).LocalPath);
        string dest = Path.Combine(Path.GetTempPath(), fileName);

        using var client = new System.Net.Http.HttpClient();
        byte[] data = await client.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(dest, data);

        Console.WriteLine($"已下载到 {dest}，正在启动安装程序...");
        // 启动安装包（安装完成后由安装程序负责关闭/重启你的软件）
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
    }
}

/* ====== WinForms / WPF 集成片段 ======
private async void 检查更新ToolStripMenuItem_Click(object sender, EventArgs e)
{
    var update = await UpdateChecker.CheckForUpdateAsync();
    if (update == null) { MessageBox.Show("已是最新版本。"); return; }

    var msg = $"发现新版本 v{update.Version}\n\n{update.ReleaseNotes}\n\n是否前往下载？";
    if (MessageBox.Show(msg, "更新可用", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

    string target = !string.IsNullOrEmpty(update.DownloadUrl) ? update.DownloadUrl : update.ReleasePage;
    if (!string.IsNullOrEmpty(target))
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
}
*/
