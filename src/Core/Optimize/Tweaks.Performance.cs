/* 文件说明：优化项库「性能优化」组——桌面动画、CPU 调度、NTFS、内存与会话池、svchost 合并。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Performance()
        {
            List<ITweak> list = new List<ITweak>();

            var menuAnim = RegTweak.Create("menu_anim", GPerformance,
                "关闭窗口与菜单动画",
                "关闭任务栏动画、菜单淡入淡出和窗口最小化动画，并把菜单弹出延迟清零（原「悬停弹出加速」已并入本项）。界面响应更干脆，不影响游戏帧数。");
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDesktop, "MenuShowDelay", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, CplDWM, "MinAnimate", 0));
            menuAnim.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "TaskbarAnimations", 0));
            list.Add(menuAnim);

            var vfx = RegTweak.Create("visual_fx", GPerformance,
                "视觉效果调整为最佳性能",
                "关闭窗口阴影、列表阴影、标题栏透明与动画，优先保证系统流畅度。桌面观感会变朴素；游戏帧数不受影响，纯手感向。");
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerMain, "VisualFXSetting", 2));
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0));
            // 列表框阴影与标题栏透明（借鉴 optimizerDuck 的 DisableVisualEffects）
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser, ExplorerAdv, "ListviewShadow", 0));
            vfx.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0));
            list.Add(vfx);

            // Win32PrioritySeparation 三档（同键多档，共享备份 ID，可互相切换、都还原到系统默认）
            // 注：原有第 4 档「激进游戏档 0xFFFF3F91」已删除——它用了与「游戏推荐档」相同的 Id，
            // 被库按"先到先留"静默丢弃（永远不显示），值本身也不是合法的调度位组合。
            var fore = RegTweak.Create("win32_priority_game", GPerformance,
                "CPU 调度：游戏推荐档 (38)",
                "Win32PrioritySeparation=0x26：短量子 + 可变量子 + 高前台提升。前台游戏获得明显优先权，输入响应与帧间隔更平滑。社区最广泛推荐的游戏值。需重启生效。",
                backupId: "win32_priority", adminOnly: true, recommended: true);
            fore.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38));
            list.Add(fore);

            var foreMulti = RegTweak.Create("win32_priority_multi", GPerformance,
                "CPU 调度：多开后台档 (24)",
                "Win32PrioritySeparation=0x18：长量子 + 固定 + 无前台提升。所有进程公平分配 CPU，适合挂机下载、渲染、开服务器等多开场景。需重启生效。",
                backupId: "win32_priority", adminOnly: true);
            foreMulti.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 24));
            list.Add(foreMulti);

            var foreDefault = RegTweak.Create("win32_priority_default", GPerformance,
                "CPU 调度：系统默认档 (2)",
                "Win32PrioritySeparation=2：恢复 Windows 默认，由系统按「处理器计划」设置自动解析。不折腾时的稳妥选择。需重启生效。",
                backupId: "win32_priority", adminOnly: true);
            foreDefault.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2));
            list.Add(foreDefault);


            // SystemResponsiveness（MMCSS 后台保留配额）
            // 该配额最低有效值为 10，设 0 不会生效为 0%（系统按默认处理），故不提供低于 10 的档位。
            var resp10 = RegTweak.Create("system_responsiveness_10", GGame,
                "MMCSS 后台配额：游戏推荐档 (10)",
                "SystemResponsiveness=10：后台保留配额从默认 20% 减到 10%（该配额的最低有效值就是 10，无法设为 0），把更多调度余量让给前台游戏，兼顾后台基本流畅。多数玩家的推荐值。",
                backupId: "system_responsiveness", adminOnly: true, recommended: true);
            resp10.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 10));
            list.Add(resp10);

            var ntfs = RegTweak.Create("ntfs_lastaccess", GPerformance,
                "NTFS 访问加速（关闭访问时间与 8.3 短名）",
                "关闭 NTFS 上次访问时间记录并禁用 8.3 短文件名生成，减少磁盘无谓 I/O 与目录枚举开销（老旧 16 位程序可能依赖短名）。需重启生效。",
                adminOnly: true);
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", 1));
            ntfs.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1));
            list.Add(ntfs);

            var ntfsMeta = RegTweak.Create("ntfs_meta_opt", GPerformance,
                "NTFS 元数据优化",
                "把 NtfsDisableFileMetadataOptimization=3，让 NTFS 按更激进档缓存文件元数据，小文件密集读写（游戏库、依赖包）更顺。需重启生效。",
                adminOnly: true);
            ntfsMeta.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableFileMetadataOptimization", 3));
            list.Add(ntfsMeta);

            var startupDelay = RegTweak.Create("startup_delay_zero", GPerformance,
                "取消开机启动项延迟",
                "Windows 默认在开机后依次延迟启动应用，清零后启动项立即加载，进入游戏桌面更快就绪。");
            startupDelay.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0));
            list.Add(startupDelay);

            var fastShutdown = RegTweak.Create("fast_shutdown", GPerformance,
                "加快关机与注销速度",
                "缩短系统等待应用退出的超时并自动结束无响应程序。未保存的工作可能丢失，请先保存再关机。",
                adminOnly: true, risky: true);
            fastShutdown.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                CplDesktop, "AutoEndTasks", 1));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "WaitToKillAppTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000"));
            fastShutdown.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                CplDesktop, "LowLevelHooksTimeout", "1000"));
            // HiberbootEnabled 已拆分到独立项 disable_fast_startup：两项同键会互相覆盖还原值
            list.Add(fastShutdown);

            var hover = RegTweak.Create("mouse_hover_fast", GPerformance,
                "鼠标悬停提示加速",
                "悬停缩略图与提示信息的等待时间从 400ms 降到 10ms，浏览文件更跟手。");
            hover.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseHoverTime", "10"));
            list.Add(hover);

            var ntfsGaming = RegTweak.Create("ntfs_gaming", GPerformance,
                "NTFS 内存与 MFT 优化",
                "加大文件系统元数据缓存（NtfsMemoryUsage=2）并扩大 MFT 保留区，大量小文件读写（游戏库）更顺。需重启生效。",
                adminOnly: true);
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMemoryUsage", 2));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsMftZoneReservation", 4));
            ntfsGaming.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsQuotaNotifyRate", 36000));
            list.Add(ntfsGaming);

            var sessionPool = RegTweak.Create("session_pool", GPerformance,
                "内核会话池扩充（激进档）",
                "把内核会话池大小设为 SessionPoolSize=0xB (11)、会话视图大小设为 SessionViewSize=0x84 (132)。能容纳更多内核会话与映射视图，但值偏激进、为内存管理压测所得，桌面场景可能出现资源异常，默认不建议，出现异常关闭本项即可。需重启生效。",
                adminOnly: true, risky: true);
            sessionPool.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "SessionPoolSize", 0xB));
            sessionPool.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "SessionViewSize", 0x84));
            list.Add(sessionPool);

            var maintOff = RegTweak.Create("maintenance_off", GPerformance,
                "禁用系统自动维护与容错堆",
                "关闭后台自动维护计划与容错堆 (FTH)，消除空闲时段的自动扫描/修复活动。",
                adminOnly: true);
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
            list.Add(SvchostSplitTweak.CreateFuseAll());

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
    }
}