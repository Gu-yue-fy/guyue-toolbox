/* ============================================================
 * 文件说明：优化项库「Network」组的全部优化项声明。
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
        private static IEnumerable<ITweak> Network()
        {
            List<ITweak> list = new List<ITweak>();

            // 「网卡节能全关」已由游戏优化组的 nic_power_save_off 覆盖（键集为其子集），不再重复提供

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

            list.Add(new NetshTweak("tcp_fast_retrans",
                "加速 TCP 重传（SYN×3 / 初始 RTO 1s）",
                "把 SYN 重传次数降为 3、初始重传超时降为 1000ms，连接建立失败更快暴露、重连更快。",
                new string[] { "int tcp set global maxsynretransmissions=3", "int tcp set global initialrto=1000" },
                new string[] { "int tcp set global maxsynretransmissions=2", "int tcp set global initialrto=3000" },
                "int tcp show global",
                new string[]
                {
                    @"(最大 SYN 重新传输次数|Maximum SYN Retransmissions)\s*:\s*3",
                    @"(初始 RTO|Initial RTO)\s*:\s*1000"
                },
                false));

            list.Add(new NetshTweak("ipv6_tunnel_off",
                "禁用 IPv6 隧道（Teredo / ISATAP / 6to4）",
                "关闭三个过渡隧道技术，减少后台隧道探测与地址协商对网络的干扰。不影响原生 IPv6 上网。",
                new string[]
                {
                    "interface teredo set state disabled",
                    "int isatap set state disabled",
                    "int ipv6 6to4 set state disabled"
                },
                new string[]
                {
                    "interface teredo set state default",
                    "int isatap set state default",
                    "int ipv6 6to4 set state default"
                },
                "interface teredo show state",
                new string[] { @"(类型|Type|状态|State)\s*:\s*(disabled|已禁用)" },
                false));

            list.Add(new NetBindingTweak());

            list.Add(new NetshTweak("bbr2_on",
                "启用 BBRv2 拥塞控制 (Win11 22H2+)",
                "把 TCP 拥塞控制切换为 Google BBRv2，弱网/跨境环境下延迟更稳。需 Win11 22H2 及以上；不支持的模板会失败。",
                new string[]
                {
                    "int tcp set supplemental template=internet congestionprovider=bbr2",
                    "int tcp set supplemental template=internetcustom congestionprovider=bbr2",
                    "int tcp set supplemental template=datacenter congestionprovider=bbr2",
                    "int tcp set supplemental template=datacentercustom congestionprovider=bbr2",
                    "int tcp set supplemental template=compat congestionprovider=bbr2"
                },
                new string[]
                {
                    "int tcp set supplemental template=internet congestionprovider=default",
                    "int tcp set supplemental template=internetcustom congestionprovider=default",
                    "int tcp set supplemental template=datacenter congestionprovider=default",
                    "int tcp set supplemental template=datacentercustom congestionprovider=default",
                    "int tcp set supplemental template=compat congestionprovider=default"
                },
                "int tcp show supplemental",
                new string[] { @"bbr2" },
                true));

            RegTweak ipv6Off = new RegTweak();
            ipv6Off.IdValue = "ipv6_off";
            ipv6Off.GroupValue = GNetwork;
            ipv6Off.NameValue = "完全禁用 IPv6";
            ipv6Off.DescriptionValue = "设 DisabledComponents=0xFF，彻底关闭 IPv6 协议栈。纯 IPv4 环境可减少干扰；使用 IPv6 宽带/内网发现的环境勿开。需重启生效。";
            ipv6Off.AdminOnlyValue = true;
            ipv6Off.RiskyValue = true;
            ipv6Off.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 255));
            list.Add(ipv6Off);

            RegTweak dnsPrio = new RegTweak();
            dnsPrio.IdValue = "dns_priority";
            dnsPrio.GroupValue = GNetwork;
            dnsPrio.NameValue = "DNS 解析与 TCP 连接优化";
            dnsPrio.DescriptionValue = "调整名称解析顺序（hosts > 本地缓存 > DNS > NetBIOS），并扩大 TCP 连接表容量（MaxUserPort/MaxFreeTcbs）与缩短 TIME_WAIT 回收（TcpTimedWaitDelay=30），多连接场景不易卡顿。";
            dnsPrio.AdminOnlyValue = true;
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "Class", 8));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "DnsPriority", 6));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "HostsPriority", 5));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "LocalPriority", 4));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\ServiceProvider", "NetbtPriority", 7));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxUserPort", 65534));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "MaxFreeTcbs", 20000));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "TcpTimedWaitDelay", 30));
            // DNS 缓存强化 + TCP 保活（Juxic 网络方案的增量部分）
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxCacheEntryTtlLimit", 86400));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "MaxNegativeCacheTtl", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NegativeCacheTime", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NetFailureCacheTime", 0));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveTime", 60000));
            dnsPrio.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "KeepAliveInterval", 1000));
            list.Add(dnsPrio);

            RegTweak smb = new RegTweak();
            smb.IdValue = "lanman_smb_tuning";
            smb.GroupValue = GNetwork;
            smb.NameValue = "SMB 文件共享吞吐调优";
            smb.DescriptionValue = "加大 SMB 客户端（Lanman Workstation）的命令队列、收集计数与线程数，局域网拷贝与跨机读写吞吐更饱满。不用局域网共享的环境无感，可随时还原。";
            smb.AdminOnlyValue = true;
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCmds", 100));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxCollectionCount", 32));
            smb.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters", "MaxThreads", 30));
            list.Add(smb);

            return list;
        }
    }
}
