/* ============================================================
 * 文件说明：优化项库「Power」组的全部优化项声明。
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
        private static IEnumerable<ITweak> Power()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(new ModernStandbyOffTweak());

            CommandTweak hibernate = new CommandTweak();
            hibernate.IdValue = "hibernate_off";
            hibernate.GroupValue = GPower;
            hibernate.NameValue = "关闭休眠功能";
            hibernate.DescriptionValue = "删除 hiberfil.sys，可释放与内存等大的磁盘空间（常见 4~32 GB）。" +
                "关闭后'快速启动'也会一并失效。";
            hibernate.RiskyValue = true;
            hibernate.EnableFile = "powercfg.exe";
            hibernate.EnableArgs = "/hibernate off";
            hibernate.RevertFile = "powercfg.exe";
            hibernate.RevertArgs = "/hibernate on";
            hibernate.Probe = delegate
            {
                return RegHelper.GetInt(RegistryHive.LocalMachine,
                    @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled", 1) == 0;
            };
            list.Add(hibernate);

            CommandTweak highPerf = new CommandTweak();
            highPerf.IdValue = "power_high";
            highPerf.GroupValue = GPower;
            highPerf.NameValue = "启用高性能电源计划";
            highPerf.DescriptionValue = "锁定到高性能电源方案，CPU 不再为省电而降频。笔记本续航会下降。";
            highPerf.RiskyValue = true;
            highPerf.EnableFile = "powercfg.exe";
            highPerf.EnableArgs = "/setactive " + SchemeHighPerformance;
            highPerf.RevertFile = "powercfg.exe";
            highPerf.RevertArgs = "/setactive " + SchemeBalanced;
            highPerf.Probe = delegate
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/getactivescheme", 15000);
                return r.All.IndexOf(SchemeHighPerformance, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            list.Add(highPerf);

            // 快速启动改为 RegTweak：走 RegHelper 备份链（旧版 reg.exe 直写无备份、还原硬编码 =1 会丢系统原值）
            RegTweak fastBoot = new RegTweak();
            fastBoot.IdValue = "disable_fast_startup";
            fastBoot.GroupValue = GPower;
            fastBoot.NameValue = "关闭快速启动";
            fastBoot.DescriptionValue = "避免快速启动导致的部分驱动异常、双系统时间错误等问题。与「关闭休眠」相互独立，可单独开关。";
            fastBoot.RiskyValue = true;
            fastBoot.AdminOnlyValue = true;
            fastBoot.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0));
            list.Add(fastBoot);

            CommandTweak ultimate = new CommandTweak();
            ultimate.IdValue = "ultimate_perf";
            ultimate.GroupValue = GPower;
            ultimate.NameValue = "启用终极性能电源计划";
            ultimate.DescriptionValue = "终极性能计划移除节能节流与小核调度延迟，适合追求极限性能的高端台式机（笔记本/家用慎用）。";
            ultimate.RiskyValue = true;
            ultimate.EnableFile = "cmd.exe";
            ultimate.EnableArgs = "/c powercfg /duplicatescheme " + SchemeUltimate + " && powercfg /setactive " + SchemeUltimate;
            ultimate.RevertFile = "powercfg.exe";
            ultimate.RevertArgs = "/setactive " + SchemeBalanced;
            ultimate.Probe = delegate
            {
                Shell.Result r = Shell.Run("powercfg.exe", "/getactivescheme", 15000);
                return r.All.IndexOf(SchemeUltimate, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            list.Add(ultimate);

            return list;
        }

        public const string SchemeHighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string SchemeBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string SchemeUltimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        /// <summary>一键优化时使用的推荐项 id 集合。</summary>
    }
}
