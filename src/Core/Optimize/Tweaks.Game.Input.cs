/* 文件说明：优化项库「Game」组——全屏优化与系统干扰、输入外设、内核隔离与声卡 / 手柄。 */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {

        /// <summary>段 5：全屏优化 (FSO)、UWP 后台、键盘响应、粘滞键弹窗、通知、游戏栏面板、商店自动更新、预读取。</summary>
        private static void GameFullscreenAndSystemNoise(List<ITweak> list)
        {
            var fso = RegTweak.Create("fso_off", GGame,
                "关闭全屏优化 (FSO)",
                "让游戏独占全屏而不是边框化合成，配合全屏游戏可降低一帧以上的显示延迟。");
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2));
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1));
            fso.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"System\GameConfigStore", "GameDVR_EFSEFeatureFlags", 0));
            list.Add(fso);

            var bgApps = RegTweak.Create("bgapps_off", GGame,
                "禁止 UWP 应用后台运行",
                "策略级 + 用户级双重阻断：后台 UWP 应用不再占用网络与 CPU。副作用：邮件/天气等磁贴停止自动刷新。",
                adminOnly: true);
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2));
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1));
            bgApps.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0));
            list.Add(bgApps);

            list.Add(new NagleTweak());

            var keyboard = RegTweak.Create("keyboard_gaming", GGame,
                "键盘响应调到最快",
                "按键重复延迟设为最短、重复速度设为最快，游戏内选单与打字跟手。");
            keyboard.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Keyboard", "KeyboardDelay", 0));
            keyboard.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Control Panel\Keyboard", "KeyboardSpeed", 31));
            list.Add(keyboard);

            var sticky = RegTweak.Create("sticky_keys_off", GGame,
                "关闭粘滞键 / 筛选键弹窗",
                "游戏中连按 Shift 或长按键时不再弹出辅助功能询问窗口打断操作。");
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\StickyKeys", "Flags", "506"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\ToggleKeys", "Flags", "58"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\Keyboard Response", "Flags", "122"));
            sticky.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Accessibility\MouseKeys", "Flags", "0"));
            list.Add(sticky);

            var notify = RegTweak.Create("notify_off", GGame,
                "关闭所有通知弹窗",
                "全局禁止应用推送横幅通知（含专注助手的横幅），全屏游戏时不再被弹窗切出或分心。系统更新的提醒也不受影响（由更新策略单独控制）。");
            notify.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0));
            list.Add(notify);

            var gamebarTips = RegTweak.Create("gamebar_tips_off", GGame,
                "关闭游戏栏启动面板与提示",
                "不再弹出游戏栏欢迎面板与新手提示，减少干扰。");
            gamebarTips.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\GameBar", "ShowStartupPanel", 0));
            gamebarTips.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\GameBar", "GamePanelStartupTipIndex", 3));
            list.Add(gamebarTips);

            var storeUpdate = RegTweak.Create("store_autoupdate_off", GGame,
                "禁止微软商店自动更新",
                "阻止商店应用在游戏时悄悄下载更新抢带宽与磁盘。需要更新时手动打开商店即可。",
                adminOnly: true);
            storeUpdate.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload", 2));
            list.Add(storeUpdate);

            var prefetcher = RegTweak.Create("prefetcher_off", GGame,
                "关闭预读取 Prefetcher",
                "停止系统预读加速机制，减少开机与游戏时的后台磁盘活动。固态硬盘适用；机械硬盘用户勿开。",
                adminOnly: true, risky: true);
            prefetcher.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 0));
            list.Add(prefetcher);
        }

        /// <summary>段 6：内核隔离/内存完整性、内核常驻内存、鼠标 1:1 原生移动与驱动响应、显存低延迟、GPU 能耗统计、声卡省电、手柄 Xbox 键。</summary>
        private static void GameIsolationAndPeripherals(List<ITweak> list)
        {
            var hvci = RegTweak.Create("hvci_off", GGame,
                "关闭内核隔离 / 内存完整性 (HVCI)",
                "关闭基于虚拟化的安全（VBS）后常见可提升 3~8% 帧数，需重启生效。代价是降低对内核级攻击的防护，安全性要求高的环境勿开。",
                adminOnly: true, risky: true);
            hvci.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0));
            list.Add(hvci);

            var paging = RegTweak.Create("paging_executive", GGame,
                "内核常驻内存 (DisablePagingExecutive)",
                "禁止系统把内核代码与驱动换出到页面文件，降低偶发性卡顿尖峰，需重启生效。内存 16GB 以上推荐。",
                adminOnly: true);
            paging.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "DisablePagingExecutive", 1));
            list.Add(paging);

            var mouse1to1 = RegTweak.Create("mouse_1to1", GGame,
                "鼠标 1:1 原生移动（第 6 格）",
                "把指针速度固定在第 6 格（无缩放插值），配合「关闭鼠标加速度」获得传感器原生计数——非第 6 格时指针按比例丢帧/加速，瞄准会漂移。游戏内灵敏度不受影响。");
            mouse1to1.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "MouseSensitivity", "10"));
            // MarkC 标准 1:1 修正曲线：消除 Windows 默认 X 曲线的非线性加速段
            mouse1to1.Enable.Add(RegWrite.Binary(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SmoothMouseXCurve",
                new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00 }));
            mouse1to1.Enable.Add(RegWrite.Binary(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SmoothMouseYCurve",
                new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00,
                    }));
            list.Add(mouse1to1);

            var vidAlloc = RegTweak.Create("vidmem_alloc_low", GGame,
                "显存低延迟分配模式",
                "EnableLowLatencyVidmemAlloc=1，让驱动优先走低延迟显存分配路径，减少游戏加载纹理时的卡顿尖峰。需重启生效。",
                adminOnly: true);
            vidAlloc.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "EnableLowLatencyVidmemAlloc", 1));
            list.Add(vidAlloc);

            var wddm = RegTweak.Create("wddm_opt", GGame,
                "显卡 WDDM 低延迟模式",
                "放宽显卡驱动超时检测（TdrDelay=2 / TdrDdiDelay=3），短时高负载不再被误判为驱动无响应而复位，减少画面卡死与掉帧。仅 NVIDIA 显卡适用，需重启生效。",
                adminOnly: true, risky: true);
            wddm.ApplicableWhen(new GpuVendorCondition("N"));
            wddm.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay", 2));
            wddm.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay", 3));
            list.Add(wddm);

            list.Add(new NvTelemetryTweak());

            var gpuEnergy = RegTweak.Create("gpu_energy_drv_off", GGame,
                "禁用 GPU 能耗统计驱动",
                "GpuEnergyDrv 只为任务管理器的 GPU 能耗列提供数据，禁用后省去每次显存查询的开销，不影响游戏与显卡功能。",
                adminOnly: true);
            gpuEnergy.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\GpuEnergyDrv", "Start", 4));
            list.Add(gpuEnergy);

            list.Add(new NicPowerTweak());

            var audioIdle = RegTweak.Create("audio_idle_off", GGame,
                "声卡省电禁用（消除音频延迟/爆音）",
                "对声卡设备关闭空闲节电（ConservationIdleTime/PerformanceIdleTime/IdlePowerState 清零），避免游戏语音与音效因声卡休眠产生延迟或杂音。",
                adminOnly: true);
            const string AudioClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}";
            byte[] zero4 = new byte[] { 0, 0, 0, 0 };
            for (int i = 0; i < 10; i++)
            {
                string sub = AudioClass + "\\000" + i + "\\PowerSettings";
                // OnlyIfExists：不存在的声卡实例跳过，不制造幽灵设备键
                RegWrite w1 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "ConservationIdleTime", zero4);
                RegWrite w2 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "PerformanceIdleTime", zero4);
                RegWrite w3 = RegWrite.Binary(RegistryHive.LocalMachine, sub, "IdlePowerState", zero4);
                w1.OnlyIfExists = true; w2.OnlyIfExists = true; w3.OnlyIfExists = true;
                audioIdle.Enable.Add(w1);
                audioIdle.Enable.Add(w2);
                audioIdle.Enable.Add(w3);
            }
            list.Add(audioIdle);

            var mouseFix = RegTweak.Create("mouse_fixes", GGame,
                "鼠标驱动响应修正",
                "关闭鼠标驱动去抖延迟（DebounceTime=0），让高回报率鼠标的移动数据原样送达游戏。绝对/相对指针转换处理由「输入精简」统一管理。",
                adminOnly: true);
            mouseFix.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "DebounceTime", 0));
            list.Add(mouseFix);

            list.Add(new UsbSuspendTweak());
            list.Add(new UsbPowerTweak());

            var snapOff = RegTweak.Create("mouse_snap_off", GGame,
                "关闭指针自动吸附默认按钮",
                "禁用「自动将指针移动到对话框默认按钮」，防止指针在弹窗出现时被强制移位。");
            snapOff.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                @"Control Panel\Mouse", "SnapToDefaultButton", "0"));
            list.Add(snapOff);

            var nexus = RegTweak.Create("nexus_off", GGame,
                "手柄 Xbox 键不再唤出游戏栏",
                "游戏中长按手柄 Xbox 键不再弹出游戏栏覆盖层，避免误按切屏卡顿。");
            nexus.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0));
            list.Add(nexus);
        }
    }
}
