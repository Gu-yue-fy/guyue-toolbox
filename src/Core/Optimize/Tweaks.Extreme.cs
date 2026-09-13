/* ============================================================
 * 文件说明：优化项库「Extreme」组的全部优化项声明。
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
        private static IEnumerable<ITweak> Extreme()
        {
            List<ITweak> list = new List<ITweak>();

            // 1. 内核漏洞利用缓解全关
            RegTweak mitigation = new RegTweak();
            mitigation.IdValue = "mitigation_off";
            mitigation.GroupValue = GExtreme;
            mitigation.NameValue = "关闭内核漏洞利用缓解";
            mitigation.DescriptionValue = "关闭 SEHOP、异常链校验、CFG 导出/XFG 抑制与用户态缓解策略，消除这些缓解措施在每个进程/每次调用上的开销。代价：内核漏洞利用防护大幅下降，请勿在此状态下浏览不可信网站或运行来路不明程序。需重启生效。";
            mitigation.AdminOnlyValue = true;
            mitigation.RiskyValue = true;
            const string Kernel = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
            // 内核缓解掩码在注册表中是 REG_BINARY 位图（32 字节全 0），写字符串不被内核解析
            mitigation.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine, Kernel,
                "MitigationOptions", new byte[32]));
            mitigation.Enable.Add(RegWrite.Binary(RegistryHive.LocalMachine, Kernel,
                "MitigationAuditOptions", new byte[32]));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "KernelSEHOPEnabled", 0));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableExceptionChainValidation", 1));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableControlFlowGuardXfg", 1));
            mitigation.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine, Kernel, "DisableControlFlowGuardExportSuppression", 1));
            list.Add(mitigation);

            // 2. 漏洞驱动黑名单关闭
            RegTweak vuln = new RegTweak();
            vuln.IdValue = "vuln_blocklist_off";
            vuln.GroupValue = GExtreme;
            vuln.NameValue = "关闭漏洞驱动黑名单";
            vuln.DescriptionValue = "微软维护的已知漏洞驱动（容易被利用读内核内存）黑名单不再强制拦截。部分底层工具（RWEverything 类、IMOD 修改）需要它才能加载驱动。代价：恶意驱动更容易被加载。需重启生效。";
            vuln.AdminOnlyValue = true;
            vuln.RiskyValue = true;
            vuln.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable", 0));
            list.Add(vuln);

            // 3. BCD 安全机制精简
            BcdTweak bcdSec = new BcdTweak("bcd_security_off",
                "BCD 安全机制精简（DEP / 完整性 / ELAM，谨慎）",
                "DEP 设为 AlwaysOff、关闭完整性检查与早期反恶意软件（ELAM）驱动、关闭隔离上下文与 TPM 启动熵。消除 DEP 检查与安全启动链的开销。代价：恶意代码防护大幅下降，仅建议离线/专用游戏机使用。重启生效。",
                new string[]
                {
                    "/set nx AlwaysOff",
                    "/set nointegritychecks yes",
                    "/set disableelamdrivers yes",
                    "/set isolatedcontext no",
                    "/set tpmbootentropy ForceDisable"
                },
                new string[]
                {
                    "/set nx OptIn",
                    "/deletevalue nointegritychecks",
                    "/deletevalue disableelamdrivers",
                    "/deletevalue isolatedcontext",
                    "/deletevalue tpmbootentropy"
                },
                new string[] { @"nx\s+AlwaysOff", @"nointegritychecks\s+Yes" },
                new string[] { @"nx\s+OptIn" },
                true);
            bcdSec.GroupOverride = GExtreme;
            list.Add(bcdSec);

            // 4. Defender 全家桶禁用
            RegTweak defender = new RegTweak();
            defender.IdValue = "defender_off";
            defender.GroupValue = GExtreme;
            defender.NameValue = "禁用 Windows Defender 全家桶";
            defender.DescriptionValue = "停止 Defender 防病毒、实时监控、网络检查、安全中心与 WdFilter/WdBoot 内核驱动，并关闭 SmartScreen 与示例上报。游戏加载与进程创建不再被实时扫描拖慢——代价：失去全部系统级病毒防护，请配合本工具「安全检查」页或第三方方案。需重启生效。";
            defender.AdminOnlyValue = true;
            defender.RiskyValue = true;
            string[] defServices = new string[] { "WinDefend", "WdNisSvc", "Sense", "wscsvc" };
            for (int i = 0; i < defServices.Length; i++)
            {
                defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + defServices[i], "Start", 4));
            }
            string[] defDrivers = new string[] { "WdFilter", "WdBoot", "WdNisDrv" };
            for (int i = 0; i < defDrivers.Length; i++)
            {
                defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\" + defDrivers[i], "Start", 4));
            }
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", 1));
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Defender\Spynet", "SpyNetReporting", 0));
            defender.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", 0));
            defender.Enable.Add(RegWrite.Str(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off"));
            list.Add(defender);

            // 5. UAC 完全关闭
            RegTweak uac = new RegTweak();
            uac.IdValue = "uac_off";
            uac.GroupValue = GExtreme;
            uac.NameValue = "关闭用户账户控制 (UAC)";
            uac.DescriptionValue = "EnableLUA=0 完全关闭 UAC（不再弹提权确认、不再拆分令牌），部分老游戏与工具在非拆分令牌下表现更好。代价：所有程序默认以完整管理员权限运行。需重启生效。";
            uac.AdminOnlyValue = true;
            uac.RiskyValue = true;
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0));
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin", 0));
            uac.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "PromptOnSecureDesktop", 0));
            list.Add(uac);

            // 6. GPU 超时检测关闭（TDR）
            RegTweak tdr = new RegTweak();
            tdr.IdValue = "tdr_off";
            tdr.GroupValue = GExtreme;
            tdr.NameValue = "关闭 GPU 超时检测与恢复 (TDR)";
            tdr.DescriptionValue = "TdrLevel=0：显卡驱动不再因单帧计算超时被系统重置，超长编译的 shader 与重度负载不再触发黑屏闪退。代价：显卡真死机时系统不会自动恢复（直接黑屏，只能重启）。需重启生效。";
            tdr.AdminOnlyValue = true;
            tdr.RiskyValue = true;
            tdr.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrLevel", 0));
            list.Add(tdr);

            // 8. Defender 删除级（备份服务键后 sc delete）
            list.Add(new DefenderRemoveTweak());

            // 9. 防火墙全禁
            RegTweak fw = new RegTweak();
            fw.IdValue = "firewall_off";
            fw.GroupValue = GExtreme;
            fw.NameValue = "禁用 Windows 防火墙（全 Profile）";
            fw.DescriptionValue = "关闭域/专用/公用三个 Profile 的防火墙，并禁用防火墙服务（mpssvc）——省去每个网络连接的过滤规则匹配开销。代价：入站出站全部不设防，仅建议有路由器 NAT 保护的家用环境使用。还原时自动回写原状态。";
            fw.AdminOnlyValue = true;
            fw.RiskyValue = true;
            string[] profiles = new string[] { "Domain Profile", "Standard Profile", "Public Profile" };
            for (int i = 0; i < profiles.Length; i++)
            {
                fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + profiles[i], "EnableFirewall", 0));
                fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profiles[i],
                    "EnableFirewall", 0));
            }
            fw.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\mpssvc", "Start", 4));
            list.Add(fw);

            // 10. GPU 中断优先级提升（动态查询 IRQ）
            list.Add(new GpuIrqPriorityTweak());

            return list;
        }

        /// <summary>
        /// Defender 删除级：关闭篡改保护 → 禁用计划任务 → 备份并删除 Defender 服务与内核驱动注册表键 →
        /// 移除安全中心 UWP。还原时从备份重建服务键（完整恢复，无需系统更新）。
        /// </summary>
        public sealed class DefenderRemoveTweak : ITweak
        {
            private static readonly string[] ServiceNames = new string[]
            {
                "WinDefend", "WdNisSvc", "Sense", "WdFilter", "WdBoot", "WdNisDrv"
            };
            private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";
            private const string BackupRoot = @"SOFTWARE\GuyueBox\Backup\defender_remove\Services";
            private const string TasksPath = @"\Microsoft\Windows\Windows Defender\";

            public string Id { get { return "defender_remove"; } }
            public string Group { get { return TweakLibrary.GExtreme; } }
            public string Name { get { return "删除 Windows Defender（服务 / 驱动 / 任务 / UI）"; } }
            public string Description
            {
                get { return "删除级卸载：先关篡改保护，再禁用全部 Defender 计划任务，备份后删除 4 个服务与 3 个内核驱动的注册表键，并移除安全中心 UWP——进程创建、文件读写完全不再经过任何扫描钩子（比禁用更彻底，残留为 0）。还原时从备份完整重建服务键。建议先做系统备份；需要杀毒时还原本项即可。"; }
            }
            public bool AdminOnly { get { return true; } }
            public bool Risky { get { return true; } }
            public bool Recommended { get { return false; } }

            private static void CopyKey(Microsoft.Win32.RegistryKey src, Microsoft.Win32.RegistryKey dst)
            {
                foreach (string name in src.GetValueNames())
                {
                    try { dst.SetValue(name, src.GetValue(name, null), src.GetValueKind(name)); }
                    catch { }
                }
                foreach (string sub in src.GetSubKeyNames())
                {
                    try
                    {
                        using (Microsoft.Win32.RegistryKey s = src.OpenSubKey(sub))
                        using (Microsoft.Win32.RegistryKey d = dst.CreateSubKey(sub))
                        {
                            CopyKey(s, d);
                        }
                    }
                    catch { }
                }
            }

            private static void BackupService(string name)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey src = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(ServicesRoot + "\\" + name, false))
                    {
                        if (src == null) return; // 已删除，无需备份
                        using (Microsoft.Win32.RegistryKey dst = Microsoft.Win32.Registry.LocalMachine
                            .CreateSubKey(BackupRoot + "\\" + name))
                        {
                            CopyKey(src, dst);
                        }
                    }
                }
                catch { }
            }

            private static void RestoreService(string name)
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey src = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(BackupRoot + "\\" + name, false))
                    {
                        if (src == null) return;
                        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(ServicesRoot + "\\" + name, false);
                        using (Microsoft.Win32.RegistryKey dst = Microsoft.Win32.Registry.LocalMachine
                            .CreateSubKey(ServicesRoot + "\\" + name))
                        {
                            CopyKey(src, dst);
                        }
                    }
                }
                catch { }
            }

            private static bool RunTasks(bool disable)
            {
                string[] tasks = new string[]
                {
                    "Scheduled Scan", "Cache Maintenance", "Cleanup", "Verification"
                };
                bool all = true;
                for (int i = 0; i < tasks.Length; i++)
                {
                    string verb = disable ? "/disable" : "/enable";
                    if (!Shell.Run("schtasks.exe", "/Change /TN \"" + TasksPath + tasks[i] + "\" " + verb, 30000).Ok)
                        all = false;
                }
                return all;
            }

            public bool IsApplied()
            {
                // 主服务键已不存在即视为已删除
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(ServicesRoot + "\\" + ServiceNames[0], false))
                {
                    return k == null;
                }
            }

            public bool Apply()
            {
                // 前置：关闭篡改保护
                try
                {
                    Microsoft.Win32.Registry.SetValue(
                        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Features",
                        "TamperProtection", 0, Microsoft.Win32.RegistryValueKind.DWord);
                }
                catch { }

                bool ok = true;
                if (!RunTasks(true)) ok = false;
                for (int i = 0; i < ServiceNames.Length; i++)
                {
                    BackupService(ServiceNames[i]);
                    Shell.Result r = Shell.Run("sc.exe", "delete " + ServiceNames[i], 30000);
                    if (!r.Ok) ok = false;
                }

                // 移除安全中心 UWP（尽力而为）
                Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Get-AppxPackage *SecHealthUI* | Remove-AppxPackage\"", 120000);

                return ok;
            }

            public bool Revert()
            {
                bool ok = true;
                for (int i = 0; i < ServiceNames.Length; i++)
                {
                    RestoreService(ServiceNames[i]);
                    // 确认键回来了
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(ServicesRoot + "\\" + ServiceNames[i], false))
                    {
                        if (k == null) ok = false;
                    }
                }
                if (!RunTasks(false)) ok = false;

                // 安全中心 UWP 重新注册（尽力而为）
                Shell.Run("powershell.exe",
                    "-NoProfile -Command \"Get-AppxPackage -AllUsers *SecHealthUI* | ForEach-Object { Add-AppxPackage -Register ($_.InstallLocation + '\\AppxManifest.xml') -DisableDevelopmentMode }\"",
                    120000);
                return ok;
            }
        }

        /// <summary>
        /// GPU 中断优先级：动态查询显卡当前 IRQ 号，把对应 IRQnPriority 设为 High，
        /// 显卡中断优先于其他设备被 CPU 处理（单 GPU 平台收益最明显）。
        /// </summary>
        public sealed class GpuIrqPriorityTweak : ITweak
        {
            public string Id { get { return "gpu_irq_priority"; } }
            public string Group { get { return TweakLibrary.GExtreme; } }
            public string Name { get { return "显卡中断优先级提升"; } }
            public string Description
            {
                get { return "自动查询显卡占用的 IRQ 号，把该中断的处理优先级提到 High，渲染中断插队其他设备。多 GPU 或共享 IRQ 平台收益有限。需重启生效。"; }
            }
            public bool AdminOnly { get { return true; } }
            public bool Risky { get { return true; } }
            public bool Recommended { get { return false; } }

            private static string GpuIrq()
            {
                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"(gwmi -q 'select * from Win32_PnPAllocatedResource' | Where-Object {$_.Antecedent -like '*Win32_IRQResource*'} | Where-Object {$_.Dependent -match 'Display'} | Select-Object -First 1).Antecedent.Split('=')[-1]\"",
                    60000);
                string s = (r.All ?? "").Trim();
                if (s.Length == 0 || !char.IsDigit(s[0])) return "";
                return s;
            }

            private static string KeyPath(string irq)
            {
                return @"SYSTEM\CurrentControlSet\Control\PriorityControl";
            }

            public bool IsApplied()
            {
                string irq = GpuIrq();
                if (irq.Length == 0) return false;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", "IRQ" + irq + "Priority");
                try { return v != null && Convert.ToInt32(v) == 1; }
                catch { return false; }
            }

            public bool Apply()
            {
                string irq = GpuIrq();
                if (irq.Length == 0) return false;
                RegHelper.BeginBackup(Id);
                return RegHelper.SetValue(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\PriorityControl", "IRQ" + irq + "Priority", 1,
                    RegistryValueKind.DWord, Id);
            }

            public bool Revert()
            {
                return RegHelper.Restore(Id);
            }
        }

        // ---------------- 系统精简（砍掉普通用户用不到的后台组件） ----------------

    }
}
