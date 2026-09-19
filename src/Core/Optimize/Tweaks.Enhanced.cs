/* ============================================================
 * 文件说明：优化项库「增强项」：高价值单点优化。
 *           含：
 *             - NicKeywordTweak：按「驱动已暴露的关键字」写指定目标值的网卡高级属性项
 *               （NicAdvancedTweak 的目标值硬编码为 0/24，无法表达 LSO=1、缓冲区=512、校验和=3）
 *             - 常规 RegTweak 单点项 + 3 个网卡卸载项
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 网卡高级属性项（可配置目标值）：遍历显卡/网卡类 {4d36e972} 下已存在的 4 位实例子键，
    /// 只覆盖驱动**已暴露**的关键字，绝不凭空创建关键字（避免造出无效的厂商关键字键值）。
    /// </summary>
    public sealed class NicKeywordTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _keywords;
        private readonly string _value;
        private readonly bool _risky;

        public NicKeywordTweak(string id, string name, string desc,
            string[] keywords, string value, bool risky)
        {
            _id = id;
            _name = name;
            _desc = desc;
            _keywords = keywords ?? new string[0];
            _value = value;
            _risky = risky;
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NicClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(NicClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private static string[] ExistingKeywords(string path)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(path, false))
                {
                    if (k == null) return null;
                    return k.GetValueNames();
                }
            }
            catch
            {
                return null;
            }
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            int seen = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string[] existing = ExistingKeywords(paths[i]);
                if (existing == null) continue;

                for (int v = 0; v < _keywords.Length; v++)
                {
                    if (Array.IndexOf(existing, _keywords[v]) < 0) continue; // 驱动未暴露：跳过
                    object cur = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], _keywords[v]);
                    if (cur == null) return false;
                    if (!string.Equals(cur.ToString(), _value, StringComparison.OrdinalIgnoreCase)) return false;
                    seen++;
                }
            }
            return seen > 0; // 至少有一个真实存在的关键字达标
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(_id);
            int written = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string[] existing = ExistingKeywords(paths[i]);
                if (existing == null) continue;

                for (int v = 0; v < _keywords.Length; v++)
                {
                    if (Array.IndexOf(existing, _keywords[v]) < 0) continue;
                    if (RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], _keywords[v],
                        _value, RegistryValueKind.String, _id))
                    {
                        written++;
                    }
                }
            }
            return written > 0;
        }

        public bool Revert()
        {
            return RegHelper.Restore(_id);
        }
    }

    public static partial class TweakLibrary
    {
        /// <summary>增强项：高价值单点优化。</summary>
        private static IEnumerable<ITweak> Enhanced()
        {
            List<ITweak> list = new List<ITweak>();

            // ---------------- 显卡 ----------------

            RegTweak forceGpu = new RegTweak();
            forceGpu.IdValue = "gpu_force_high_perf";
            forceGpu.GroupValue = GGame;
            forceGpu.NameValue = "全局强制使用高性能显卡（独显）";
            forceGpu.DescriptionValue =
                "把所有应用的 GPU 偏好统一设为「高性能」，双显卡笔记本插电游戏时避免误用核显。" +
                "代价：独显常驻待机会明显增加耗电与发热，不插电时建议关闭本项。";
            forceGpu.RiskyValue = true;
            forceGpu.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "GpuPreference=2;"));
            list.Add(forceGpu);

            // ---------------- 外设 / 输入 ----------------

            RegTweak winKey = new RegTweak();
            winKey.IdValue = "win_key_off";
            winKey.GroupValue = GGame;
            winKey.NameValue = "禁用 Win 键（防游戏中误触）";
            winKey.DescriptionValue =
                "屏蔽 Win 键，避免全屏游戏时误按弹出开始菜单导致掉帧/切出。" +
                "代价：平时也用不了 Win 键快捷键（如 Win+D 回桌面、Win+Shift+S 截图），关闭本项即恢复。";
            winKey.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoWinKeys", 1));
            list.Add(winKey);

            // ---------------- 文件系统 ----------------

            RegTweak longPaths = new RegTweak();
            longPaths.IdValue = "long_paths_on";
            longPaths.GroupValue = GPerformance;
            longPaths.NameValue = "启用 NTFS 长路径支持";
            longPaths.DescriptionValue =
                "解除 260 字符路径长度限制（LongPathsEnabled=1），避免深层目录或依赖包解压时报「路径过长」。" +
                "对开发者与重度游戏玩家友好；个别老旧程序可能异常，关闭本项即可还原（需重启生效）。";
            longPaths.AdminOnlyValue = true;
            longPaths.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1));
            list.Add(longPaths);

            // ---------------- 隐私 ----------------

            RegTweak clipHistory = new RegTweak();
            clipHistory.IdValue = "clipboard_history_off";
            clipHistory.GroupValue = GPrivacy;
            clipHistory.NameValue = "关闭剪贴板历史记录";
            clipHistory.DescriptionValue =
                "关闭 Win+V 剪贴板历史（EnableClipboardHistory=0），避免复制的密码、密钥等文本被系统留存，也避免随账号跨设备同步。";
            clipHistory.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Clipboard", "EnableClipboardHistory", 0));
            list.Add(clipHistory);

            // ---------------- 电源 / 系统还原 ----------------

            RegTweak rpFreq = new RegTweak();
            rpFreq.IdValue = "restore_point_freq_zero";
            rpFreq.GroupValue = GPower;
            rpFreq.NameValue = "允许频繁创建系统还原点";
            rpFreq.DescriptionValue =
                "把「两次创建还原点的最小间隔」由默认 24 小时改为 0，" +
                "让优化前自动创建的还原点不会被系统静默跳过（与「系统还原点」页互补）。";
            rpFreq.AdminOnlyValue = true;
            rpFreq.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
                "SystemRestorePointCreationFrequency", 0));
            list.Add(rpFreq);

            // ---------------- 网卡卸载三件套 ----------------

            list.Add(new NicKeywordTweak(
                "nic_lso_on",
                "启用网卡大数据包发送卸载 (LSO)",
                "把驱动暴露的 LSO 关键字设为开启（*LsoV1IPv4 / *LsoV2IPv4 / *LsoV2IPv6 = 1），" +
                "由网卡直接切分大包，降低高吞吐时的 CPU 占用。只写驱动已暴露的关键字，不凭空造键。",
                new string[] { "*LsoV1IPv4", "*LsoV2IPv4", "*LsoV2IPv6" }, "1", false));

            list.Add(new NicKeywordTweak(
                "nic_checksum_offload_on",
                "启用网卡校验和卸载",
                "把 IPv4/IPv6 的 TCP/UDP 校验和计算交给网卡处理（=3），降低下载与联机时的 CPU 占用。" +
                "只写驱动已暴露的关键字。",
                new string[]
                {
                    "*IPChecksumOffloadIPv4", "*TCPChecksumOffloadIPv4", "*UDPChecksumOffloadIPv4",
                    "*TCPChecksumOffloadIPv6", "*UDPChecksumOffloadIPv6"
                }, "3", false));

            list.Add(new NicKeywordTweak(
                "nic_buffers_up",
                "增大网卡收发缓冲区（512）",
                "把网卡收发缓冲区设为 512（*ReceiveBuffers / *TransmitBuffers），" +
                "减少高并发/高丢包场景下的丢包。个别老网卡驱动对缓冲区大小敏感，出现异常请关闭本项。",
                new string[] { "*ReceiveBuffers", "*TransmitBuffers" }, "512", true));

            // ---------------- 外观与体验（低风险，立竿见影） ----------------

            RegTweak thumbDelay = new RegTweak();
            thumbDelay.IdValue = "thumb_hover_delay_off";
            thumbDelay.GroupValue = GAppearance;
            thumbDelay.NameValue = "移除缩略图悬停延迟";
            thumbDelay.DescriptionValue =
                "取消资源管理器里鼠标悬停在文件上等待约 0.4 秒才弹出缩略图/提示的延迟，浏览文件夹更跟手。";
            thumbDelay.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ExtendedUIHoverTime", 1));
            list.Add(thumbDelay);

            RegTweak startupSound = new RegTweak();
            startupSound.IdValue = "startup_sound_off";
            startupSound.GroupValue = GAppearance;
            startupSound.NameValue = "禁用开机启动声音";
            startupSound.DescriptionValue =
                "关闭 Windows 登录时的启动音（DisableStartupSound=1）。开机更安静，也不会在夜间突然出声。";
            startupSound.AdminOnlyValue = true;
            startupSound.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableStartupSound", 1));
            list.Add(startupSound);

            RegTweak lowDisk = new RegTweak();
            lowDisk.IdValue = "low_disk_warning_off";
            lowDisk.GroupValue = GSlim;
            lowDisk.NameValue = "关闭磁盘空间不足警告气泡";
            lowDisk.DescriptionValue =
                "关闭「磁盘空间不足」托盘气泡提示（NoLowDiskSpaceChecks=1）。" +
                "注意：这只是关闭提醒，并不会释放空间——真的空间紧张时请用「清理与磁盘」页处理。";
            lowDisk.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLowDiskSpaceChecks", 1));
            list.Add(lowDisk);

            // ---------------- 隐私（策略键，均为可还原） ----------------

            RegTweak inventory = new RegTweak();
            inventory.IdValue = "inventory_collector_off";
            inventory.GroupValue = GPrivacy;
            inventory.NameValue = "停用兼容性清单收集器";
            inventory.DescriptionValue =
                "禁用应用程序兼容性清单收集（DisableInventory=1），减少后台扫描已安装程序与驱动生成兼容性清单的开销。";
            inventory.AdminOnlyValue = true;
            inventory.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", 1));
            list.Add(inventory);

            RegTweak ait = new RegTweak();
            ait.IdValue = "app_impact_telemetry_off";
            ait.GroupValue = GPrivacy;
            ait.NameValue = "关闭应用影响遥测";
            ait.DescriptionValue =
                "关闭应用影响遥测（AITEnable=0），系统不再持续评估每个程序对启动/关机耗时的影响。";
            ait.AdminOnlyValue = true;
            ait.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0));
            list.Add(ait);

            RegTweak cloudSpeech = new RegTweak();
            cloudSpeech.IdValue = "cloud_speech_off";
            cloudSpeech.GroupValue = GPrivacy;
            cloudSpeech.NameValue = "关闭云端语音识别";
            cloudSpeech.DescriptionValue =
                "禁止输入个性化使用云端语音识别（AllowInputPersonalization=0）。本机语音输入仍可用，只是不走联网识别。";
            cloudSpeech.AdminOnlyValue = true;
            cloudSpeech.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\InputPersonalization", "AllowInputPersonalization", 0));
            list.Add(cloudSpeech);

            RegTweak licenseTel = new RegTweak();
            licenseTel.IdValue = "license_telemetry_off";
            licenseTel.GroupValue = GPrivacy;
            licenseTel.NameValue = "阻止许可证状态遥测";
            licenseTel.DescriptionValue =
                "阻止软件保护平台上报许可证状态（NoGenTicket=1），减少激活相关后台联网与日志写入。";
            licenseTel.AdminOnlyValue = true;
            licenseTel.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows NT\CurrentVersion\Software Protection Platform",
                "NoGenTicket", 1));
            list.Add(licenseTel);

            // ---------------- 网络 ----------------

            RegTweak qosSched = new RegTweak();
            qosSched.IdValue = "qos_scheduler_limit_off";
            qosSched.GroupValue = GNetwork;
            qosSched.NameValue = "解除 QoS 数据包调度的带宽预留";
            qosSched.DescriptionValue =
                "把 QoS 数据包调度程序的「可保留带宽」上限设为 0（NonBestEffortLimit=0），" +
                "避免系统为 QoS 预留带宽。家用环境下通常可略微改善吞吐。";
            qosSched.AdminOnlyValue = true;
            qosSched.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0));
            list.Add(qosSched);

            // ---------------- 内存管理 ----------------

            RegTweak pagefileClear = new RegTweak();
            pagefileClear.IdValue = "pagefile_clear_off";
            pagefileClear.GroupValue = GPerformance;
            pagefileClear.NameValue = "关机不擦除页面文件";
            pagefileClear.DescriptionValue =
                "关闭「关机时清除虚拟内存页面文件」（ClearPageFileAtShutdown=0），显著缩短关机耗时。" +
                "代价：关机后页面文件内容不会被擦除（对普通用户无实际影响，涉密环境勿开）。";
            pagefileClear.AdminOnlyValue = true;
            pagefileClear.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "ClearPageFileAtShutdown", 0));
            list.Add(pagefileClear);

            RegTweak sysCache = new RegTweak();
            sysCache.IdValue = "system_cache_client";
            sysCache.GroupValue = GPerformance;
            sysCache.NameValue = "系统缓存偏向客户端模式";
            sysCache.DescriptionValue =
                "把 LargeSystemCache 设为 0（客户端/工作站模式），让系统优先保证前台程序的缓存命中，" +
                "对游戏与桌面应用更友好（服务器文件共享场景才需要设为 1）。";
            sysCache.AdminOnlyValue = true;
            sysCache.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "LargeSystemCache", 0));
            list.Add(sysCache);

            return list;
        }
    }
}
