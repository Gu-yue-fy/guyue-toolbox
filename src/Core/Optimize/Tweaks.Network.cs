/* 文件说明：优化项库「网络优化」组——拥塞控制、TCP/IP 参数、IPv6 隧道、DNS、SMB 调优。 */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private static IEnumerable<ITweak> Network()
        {
            List<ITweak> list = new List<ITweak>();

            // 「网卡节能全关」已由游戏优化组的 nic_power_save_off 覆盖（键集为其子集），此处不再重复。

            list.Add(new NicAdvancedTweak("nic_latency_off",
                "关闭网卡合并与流控（RSC / 包合并 / 流控）",
                "关闭接收段合并 (RSC)、数据包合并 (Packet Coalescing) 与流量控制，减少网卡缓冲合包带来的延迟，是竞技游戏网络的经典优化。",
                new string[] { "*RSCIPv4", "*PacketCoalescing", "*FlowControl" },
                new string[0]));

            list.Add(new NetshTweak("rsc_global_off",
                "关闭全局接收段合并 (RSC)",
                "全局禁用 TCP 接收段合并，与逐网卡 RSC 关闭配合，降低下载与游戏并存时的延迟。重启后仍保持。",
                new string[] { "int tcp set global rsc=disabled" },
                new string[] { "int tcp set global rsc=enabled" },
                "int tcp show global",
                new string[] { @"(接收段合并状态|Receive Segment Coalescing State)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("ecn_off",
                "关闭 TCP ECN 功能",
                "禁用显式拥塞通知，部分游戏服务器/路由对其兼容不佳，关闭后网络行为更传统稳定。",
                new string[] { "int tcp set global ecncapability=disabled" },
                new string[] { "int tcp set global ecncapability=enabled" },
                "int tcp show global",
                new string[] { @"(ECN 功能|ECN Capability)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("tcp_timestamps_off",
                "关闭 TCP 时间戳",
                "禁用 RFC 1323 时间戳，减少每个包 12 字节头部开销与协议处理，低延迟场景更干净。",
                new string[] { "int tcp set global timestamps=disabled" },
                new string[] { "int tcp set global timestamps=enabled" },
                "int tcp show global",
                new string[] { @"(RFC 1323 时间戳|RFC 1323 Timestamps)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetshTweak("tcp_heuristics_off",
                "关闭 TCP 窗口缩放启发式",
                "禁用按比例改变窗口的试探算法，避免回程流量干扰时窗口被意外缩小，竞技网络常用。",
                new string[] { "int tcp set heuristics disabled" },
                new string[] { "int tcp set heuristics enabled" },
                "int tcp show heuristics",
                new string[] { @"(窗口缩放启发|Window Scaling heuristics)\s*:\s*(disabled|已禁用)" },
                false));

            var tcpRetrans = RegTweak.Create("tcp_fast_retrans", GNetwork,
                "加速 TCP 重传（SYN×3 / 初始 RTO 1s）",
                "把 SYN 重传次数降为 3（MaxSynRetransmissions）、初始重传超时降为 1000ms（InitialRto），连接建立失败更快暴露、重连更快。",
                adminOnly: true);
            tcpRetrans.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxSynRetransmissions", 3));
            tcpRetrans.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "InitialRto", 1000));
            list.Add(tcpRetrans);

            list.Add(new NetshTweak("ipv6_tunnel_off",
                "禁用 IPv6 隧道（Teredo / ISATAP / 6to4）",
                "关闭三个过渡隧道技术，减少后台隧道探测与地址协商对网络的干扰。不影响原生 IPv6 上网。",
                new string[]
                {
                    "interface teredo set state disabled",
                    "interface isatap set state disabled",
                    "interface 6to4 set state disabled"
                },
                new string[]
                {
                    "interface teredo set state default",
                    "interface isatap set state default",
                    "interface 6to4 set state default"
                },
                "interface teredo show state",
                new string[] { @"(类型|Type|状态|State)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetBindingTweak());

            // ---- TCP 拥塞控制算法：拆成三项独立条目 ----
            // 原先按系统版本合成一项「拥塞控制优化（CTCP/BBR2）」，三个问题：
            //   · CTCP / CUBIC 是传统可选算法（Windows 10 1709 起都提供，Windows 11 的**默认就是 CUBIC**），
            //     BBR2 是新算法（Windows 11 22H2 / Build 22621、Server 2022 起才有）——
            //     混成一项让人分不清改的到底是哪个算法，也无法单独取舍；
            //   · Win11 初版（22000~22620）在原逻辑里 provider 为空 → 整项被判"不适用"，
            //     可它明明支持 CUBIC；
            //   · 三项改的是**同一个**系统项（internet 补充模板的 congestionprovider），天然互斥：
            //     同时只应启用一项，描述里已写明。
            int buildNum;
            {
                object buildVal = RegHelper.GetValue(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
                if (buildVal == null || !int.TryParse(buildVal.ToString(), out buildNum))
                    buildNum = Environment.OSVersion.Version.Build;
            }

            list.Add(MakeCongestionTweak("congestion_cubic", "TCP 拥塞控制：CUBIC", "cubic",
                buildNum >= 16299,
                "把 TCP 拥塞控制设为 CUBIC（Windows 10 1709 起内置，Windows 11 的默认算法）。"
                + "丢包后恢复更快、高带宽链路上吞吐更稳，通用推荐档。",
                "本机系统版本不提供 CUBIC，暂不适用。"));

            list.Add(MakeCongestionTweak("congestion_ctcp", "TCP 拥塞控制：CTCP", "ctcp",
                buildNum >= 10240,
                "把 TCP 拥塞控制设为 CTCP（Compound TCP，Windows 10 及更早的默认算法，与 CUBIC 同属传统算法）。"
                + "Windows 11 默认已是 CUBIC，换回 CTCP 一般更慢；仅当个别老设备/应用在 CTCP 下更稳时使用。",
                "本机系统版本不提供 CTCP，暂不适用。"));

            list.Add(MakeCongestionTweak("congestion_bbr2", "TCP 拥塞控制：BBR2", "bbr2",
                buildNum >= 22621,
                "把 TCP 拥塞控制设为 BBR2（Google BBR v2 的 Windows 实现，"
                + "Windows 11 22H2 / Build 22621 与 Server 2022 起提供）。"
                + "弱网与跨境高延迟链路下降延迟、降抖动更明显；算法较激进，个别链路反而变慢。",
                "本机系统版本不提供 BBR2（需 Windows 11 22H2 / Build 22621 或 Server 2022 及以上），暂不适用。"));

            var ipv6Off = RegTweak.Create("ipv6_off", GNetwork,
                "完全禁用 IPv6",
                "设 DisabledComponents=0xFF，彻底关闭 IPv6 协议栈。纯 IPv4 环境可减少干扰；使用 IPv6 宽带/内网发现的环境勿开。需重启生效。",
                adminOnly: true, risky: true);
            ipv6Off.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 255));
            list.Add(ipv6Off);

            // 注：早期版本在此写入的 Tcpip\ServiceProvider\* 与 Dnscache\Parameters\* 名称解析优先级键、
            // 以及 MaxFreeTcbs / TcpTimedWaitDelay，均为 Windows Vista+ 起不再读取的遗留参数（纯安慰剂），
            // 已从本项移除，仅保留以下现代系统仍生效的子项。
            var dnsPrio = RegTweak.Create("dns_priority", GNetwork,
                "DNS 解析与 TCP 连接优化",
                "扩大 TCP 临时端口池（MaxUserPort=65534）避免多连接时端口耗尽；延长 DNS 缓存 TTL（MaxCacheEntryTtlLimit=86400）并关闭负缓存（失败解析立即重试）；调短 TCP 保活间隔（KeepAliveTime/KeepAliveInterval），长连接更敏感。需重启生效。",
                adminOnly: true);
            // TCP 临时端口池
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort", 65534));
            // DNS 缓存强化 + 关闭负缓存
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheEntryTtlLimit", 86400));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NetFailureCacheTime", 0));
            // TCP 保活
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveTime", 60000));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveInterval", 1000));
            list.Add(dnsPrio);

            var smb = RegTweak.Create("lanman_smb_tuning", GNetwork,
                "SMB 文件共享吞吐调优",
                "加大 SMB 客户端（Lanman Workstation）的命令队列、收集计数与线程数，局域网拷贝与跨机读写吞吐更饱满。不用局域网共享的环境无感，可随时还原。",
                adminOnly: true);
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCmds", 100));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCollectionCount", 32));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxThreads", 30));
            list.Add(smb);

            return list;
        }

        /// <summary>
        /// 生成一个「TCP 拥塞控制算法」优化项。
        /// 三项共用同一条 netsh 命令（只有 provider 不同），改的是同一个系统项，因此彼此互斥。
        /// applicable = false 时不提供命令（应用会被 TweakExecutor 拒绝），只作"暂不适用"的可见条目。
        /// </summary>
        private static ITweak MakeCongestionTweak(string id, string name, string provider,
            bool applicable, string desc, string unavailable)
        {
            CommandTweak t = new CommandTweak();
            t.IdValue = id;
            t.GroupValue = GNetwork;
            t.NameValue = name;
            t.RiskyValue = true;
            t.DescriptionValue = (applicable ? desc : unavailable)
                + "与其它「TCP 拥塞控制」项互斥：它们改的是同一个系统项，同时只启用一项。"
                + "还原时恢复系统默认算法（Windows 11 为 CUBIC，Windows 10 为 CTCP）。";

            if (!applicable)
            {
                t.Probe = delegate { return false; };
                return t;
            }

            t.EnableFile = "netsh.exe";
            t.EnableArgs = "int tcp set supplemental internet congestionprovider=" + provider;
            t.RevertFile = "netsh.exe";
            t.RevertArgs = "int tcp set supplemental internet congestionprovider=default";
            t.Probe = delegate
            {
                Shell.Result r = Shell.Netsh("int tcp show supplemental");
                return r.All.IndexOf(provider, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            return t;
        }
    }
}