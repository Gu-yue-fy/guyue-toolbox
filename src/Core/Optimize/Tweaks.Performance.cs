/* ============================================================
 * 文件说明：优化项库「Performance」组的全部优化项声明。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Performance()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak menuAnim = new RegTweak();
            menuAnim.IdValue = "menu_anim";
            menuAnim.GroupValue = GPerformance;
            menuAnim.NameValue = "关闭窗口与菜单动画";
            menuAnim.DescriptionValue = "关闭任务栏动画、菜单淡入淡出和窗口最小化动画，并把菜单弹出延迟清零（原「悬停弹出加速」已并入本项）。界面响应更干脆，不影响游戏帧数。";
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDesktop, "MenuShowDelay", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDWM, "MinAnimate", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "TaskbarAnimations", 0));
            list.Add(menuAnim);

            RegTweak vfx = new RegTweak();
            vfx.IdValue = "visual_fx";
            vfx.GroupValue = GPerformance;
            vfx.NameValue = "视觉效果调整为最佳性能";
            vfx.DescriptionValue = "关闭窗口阴影、透明效果与动画，优先保证系统流畅度。桌面观感会变朴素；游戏帧数不受影响，纯手感向。";
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerMain, "VisualFXSetting", 2));
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0));
            list.Add(vfx);

            // Win32PrioritySeparation 三档（同键多档，共享备份 ID，可互相切换、都还原到系统默认）
            RegTweak fore = new RegTweak();
            fore.IdValue = "win32_priority_game";
            fore.BackupIdValue = "win32_priority";
            fore.GroupValue = GPerformance;
            fore.NameValue = "CPU 调度：游戏推荐档 (38)";
            fore.DescriptionValue = "Win32PrioritySeparation=0x26：短量子 + 可变量子 + 高前台提升。前台游戏获得明显优先权，输入响应与帧间隔更平滑。社区最广泛推荐的游戏值。重启后生效。";
            fore.AdminOnlyValue = true;
            fore.RecommendedValue = true;
            fore.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38));
            list.Add(fore);

            RegTweak foreMulti = new RegTweak();
            foreMulti.IdValue = "win32_priority_multi";
            foreMulti.BackupIdValue = "win32_priority";
            foreMulti.GroupValue = GPerformance;
            foreMulti.NameValue = "CPU 调度：多开后台档 (24)";
            foreMulti.DescriptionValue = "Win32PrioritySeparation=0x18：长量子 + 固定 + 无前台提升。所有进程公平分配 CPU，适合挂机下载、渲染、开服务器等多开场景。重启后生效。";
            foreMulti.AdminOnlyValue = true;
            foreMulti.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 24));
            list.Add(foreMulti);

            RegTweak foreDefault = new RegTweak();
            foreDefault.IdValue = "win32_priority_default";
            foreDefault.BackupIdValue = "win32_priority";
            foreDefault.GroupValue = GPerformance;
            foreDefault.NameValue = "CPU 调度：系统默认档 (2)";
            foreDefault.DescriptionValue = "Win32PrioritySeparation=2：恢复 Windows 默认，由系统按「处理器计划」设置自动解析。不折腾时的稳妥选择。重启后生效。";
            foreDefault.AdminOnlyValue = true;
            foreDefault.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2));
            list.Add(foreDefault);

            // SystemResponsiveness（MMCSS 后台保留配额）
            // 注意：该配额最低有效值为 10（设 0 不会生效为 0%，系统按默认处理）——
            // 早期的「极致档 (0)」是无效项，已按实际生效边界移除。
            RegTweak resp10 = new RegTweak();
            resp10.IdValue = "system_responsiveness_10";
            resp10.BackupIdValue = "system_responsiveness";
            resp10.GroupValue = GGame;
            resp10.NameValue = "MMCSS 后台配额：游戏推荐档 (10)";
            resp10.DescriptionValue = "SystemResponsiveness=10：后台保留配额从默认 20% 减到 10%（该配额的最低有效值就是 10，无法设为 0），把更多调度余量让给前台游戏，兼顾后台基本流畅。多数玩家的推荐值。";
            resp10.AdminOnlyValue = true;
            resp10.RecommendedValue = true;
            resp10.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10));
            list.Add(resp10);

            RegTweak ntfs = new RegTweak();
            ntfs.IdValue = "ntfs_lastaccess";
            ntfs.GroupValue = GPerformance;
            ntfs.NameValue = "NTFS 访问加速（关闭访问时间与 8.3 短名）";
            ntfs.DescriptionValue = "关闭 NTFS 上次访问时间记录并禁用 8.3 短文件名生成，减少磁盘无谓 I/O 与目录枚举开销（老旧 16 位程序可能依赖短名）。重启后生效。";
            ntfs.AdminOnlyValue = true;
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1));
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1));
            list.Add(ntfs);

            RegTweak startupDelay = new RegTweak();
            startupDelay.IdValue = "startup_delay_zero";
            startupDelay.GroupValue = GPerformance;
            startupDelay.NameValue = "取消开机启动项延迟";
            startupDelay.DescriptionValue = "Windows 默认在开机后依次延迟启动应用，清零后启动项立即加载，进入游戏桌面更快就绪。";
            startupDelay.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0));
            list.Add(startupDelay);

            RegTweak fastShutdown = new RegTweak();
            fastShutdown.IdValue = "fast_shutdown";
            fastShutdown.GroupValue = GPerformance;
            fastShutdown.NameValue = "加快关机与注销速度";
            fastShutdown.DescriptionValue = "缩短系统等待应用退出的超时并自动结束无响应程序。未保存的工作可能丢失，请先保存再关机。";
            fastShutdown.AdminOnlyValue = true;
            fastShutdown.RiskyValue = true;
            fastShutdown.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                CplDesktop, "AutoEndTasks", 1));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "WaitToKillAppTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "LowLevelHooksTimeout", "1000"));
            // HiberbootEnabled（快速启动）已拆分到独立的 disable_fast_startup 项：两项同键会互相覆盖还原值
            list.Add(fastShutdown);

            RegTweak hover = new RegTweak();
            hover.IdValue = "mouse_hover_fast";
            hover.GroupValue = GPerformance;
            hover.NameValue = "鼠标悬停提示加速";
            hover.DescriptionValue = "悬停缩略图与提示信息的等待时间从 400ms 降到 10ms，浏览文件更跟手。";
            hover.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseHoverTime", "10"));
            list.Add(hover);

            RegTweak ntfsGaming = new RegTweak();
            ntfsGaming.IdValue = "ntfs_gaming";
            ntfsGaming.GroupValue = GPerformance;
            ntfsGaming.NameValue = "NTFS 内存与 MFT 优化";
            ntfsGaming.DescriptionValue = "加大文件系统元数据缓存（NtfsMemoryUsage=2）并扩大 MFT 保留区，大量小文件读写（游戏库）更顺。需重启生效。";
            ntfsGaming.AdminOnlyValue = true;
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 2));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMftZoneReservation", 4));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsQuotaNotifyRate", 36000));
            list.Add(ntfsGaming);

            RegTweak maintOff = new RegTweak();
            maintOff.IdValue = "maintenance_off";
            maintOff.GroupValue = GPerformance;
            maintOff.NameValue = "禁用系统自动维护与容错堆";
            maintOff.DescriptionValue = "关闭后台自动维护计划与容错堆 (FTH)，消除空闲时段的自动扫描/修复活动。";
            maintOff.AdminOnlyValue = true;
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "MaintenanceDisabled", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "WakeUp", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\ScheduledDiagnostics", "EnabledExecution", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\EnergyEstimation\TaggedEnergy", "DisableTaggedEnergyLogging", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\EnergyEstimation\TaggedEnergy", "TelemetryMaxApplication", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "SleepStudyDisabled", 1));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Reliability", "TimeStampInterval", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\FTH", "Enabled", 0));
            maintOff.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\NvCache", "OptimizeBootAndResume", 1));
            list.Add(maintOff);

            list.Add(new SvchostSplitTweak());

            CommandTweak memCompress = new CommandTweak();
            memCompress.IdValue = "mem_compression_off";
            memCompress.GroupValue = GPerformance;
            memCompress.NameValue = "禁用内存压缩";
            memCompress.DescriptionValue = "关闭系统的内存压缩存储，省下压缩/解压的 CPU 开销（代价是相同内容占更多物理内存）。16GB 以上内存推荐。";
            memCompress.EnableFile = "powershell.exe";
            memCompress.EnableArgs = "-NoProfile -Command \"Disable-MMAgent -MemoryCompression\"";
            memCompress.RevertFile = "powershell.exe";
            memCompress.RevertArgs = "-NoProfile -Command \"Enable-MMAgent -MemoryCompression\"";
            memCompress.Probe = delegate
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Write-Output ((Get-MMAgent).MemoryCompression)\"", 60000);
                string text = r.All ?? "";
                return text.IndexOf("False", StringComparison.Ordinal) >= 0;
            };
            list.Add(memCompress);

            return list;
        }

        // ---------------- 游戏与硬件 ----------------

    }
}
