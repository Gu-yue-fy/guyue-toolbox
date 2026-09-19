/* ============================================================
 * 文件说明：优化项库「音频优化」组：4 项。
 *           说明：音频独占模式优先级、音频采样率调度两项会与 mmcss_deep
 *           对 Tasks\Audio 的写入冲突，故不提供。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private const string AudioKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio";
        private const string AudioDuckingKey = @"SOFTWARE\Microsoft\Multimedia\Audio";

        /// <summary>音频优化组：关闭系统音效处理链，降低音频链路延迟与中断。</summary>
        private static IEnumerable<ITweak> Audio()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak enh = new RegTweak();
            enh.IdValue = "audio_enhancements_off";
            enh.GroupValue = GAudio;
            enh.NameValue = "禁用系统音频增强";
            enh.DescriptionValue = "关闭 Windows 音频效果处理（均衡器/响度等系统级增强），减少音频链路上的额外处理，降低延迟与爆音概率。应用后若某些音效软件失效，关闭本项即可还原。";
            enh.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioKey, "DisableSystemEffects", 1));
            list.Add(enh);

            RegTweak spatial = new RegTweak();
            spatial.IdValue = "audio_spatial_off";
            spatial.GroupValue = GAudio;
            spatial.NameValue = "禁用空间音效";
            spatial.DescriptionValue = "关闭 Windows Sonic / 杜比全景声等空间音频处理。空间音效会引入额外混音与延迟，竞技类游戏建议关闭；观影时可按需重新开启。";
            spatial.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioKey, "EnableSpatialAudio", 0));
            list.Add(spatial);

            RegTweak delayed = new RegTweak();
            delayed.IdValue = "audio_service_delayed_start_off";
            delayed.GroupValue = GAudio;
            delayed.NameValue = "音频服务不延迟启动";
            delayed.DescriptionValue = "把 Audiosrv 音频服务的延迟自动启动改为随系统启动，避免登录后音频设备需要等待数秒才可用（对开机即用音频/直播场景更友好）。";
            delayed.AdminOnlyValue = true;
            delayed.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Audiosrv", "DelayedAutoStart", 0));
            list.Add(delayed);

            RegTweak ducking = new RegTweak();
            ducking.IdValue = "audio_comm_ducking_off";
            ducking.GroupValue = GAudio;
            ducking.NameValue = "关闭通信自动降低音量";
            ducking.DescriptionValue = "关闭「检测到通信活动时自动降低其他声音音量」（UserDuckingPreference=3），避免游戏/音乐声音被无端压低。";
            ducking.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, AudioDuckingKey, "UserDuckingPreference", 3));
            list.Add(ducking);

            return list;
        }
    }
}
