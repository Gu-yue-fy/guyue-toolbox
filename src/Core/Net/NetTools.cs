﻿/* ============================================================
 * 文件说明：网络诊断与端口占用：flushdns、协议栈重置、netstat 解析
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace GuyueBox.Core
{
    public sealed class AdapterInfo
    {
        public string Name;
        public string Description;
        public string Type;
        public string Status;
        public string MacAddress;
        public long Speed;
        public List<string> IPv4 = new List<string>();
        public List<string> IPv6 = new List<string>();
        public List<string> Gateway = new List<string>();
        public List<string> Dns = new List<string>();
        public bool DhcpEnabled;

        public string SpeedText
        {
            get
            {
                if (Speed <= 0) return "-";
                double mbps = Speed / 1000000.0;
                if (mbps >= 1000) return (mbps / 1000.0).ToString("0.0") + " Gbps";
                return mbps.ToString("0") + " Mbps";
            }
        }
    }
    public sealed class PingResult
    {
        public string Host;
        public string Address;
        public int Sent;
        public int Received;
        public long MinMs = long.MaxValue;
        public long MaxMs;
        public long AvgMs;
        public int Ttl = -1;

        public double LossPercent
        {
            get { return Sent == 0 ? 0 : (Sent - Received) * 100.0 / Sent; }
        }
    }
    public sealed class PortInfo
    {
        public string Protocol;
        public string LocalAddress;
        public int LocalPort;
        public string State;
        public int OwningPid;
        public string ProcessName;
    }

    public static class NetTools
    {
        // ---------------- 网卡 ----------------

        public static List<AdapterInfo> ListAdapters()
        {
            List<AdapterInfo> list = new List<AdapterInfo>();
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    NetworkInterface nic = nics[i];
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    AdapterInfo a = new AdapterInfo();
                    a.Name = nic.Name;
                    a.Description = nic.Description;
                    a.Type = nic.NetworkInterfaceType.ToString();
                    switch (nic.OperationalStatus)
                    {
                        case OperationalStatus.Up: a.Status = "已连接"; break;
                        case OperationalStatus.Down: a.Status = "未连接"; break;
                        default: a.Status = nic.OperationalStatus.ToString(); break;
                    }
                    a.Speed = nic.Speed;

                    try
                    {
                        byte[] mac = nic.GetPhysicalAddress().GetAddressBytes();
                        StringBuilder sb = new StringBuilder();
                        for (int m = 0; m < mac.Length; m++)
                        {
                            if (m > 0) sb.Append('-');
                            sb.Append(mac[m].ToString("X2"));
                        }
                        a.MacAddress = sb.ToString();
                    }
                    catch
                    {
                        a.MacAddress = "-";
                    }

                    try
                    {
                        IPInterfaceProperties props = nic.GetIPProperties();

                        foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                                a.IPv4.Add(ua.Address.ToString() + " / " + ua.IPv4Mask);
                            else if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                string s = ua.Address.ToString();
                                if (s.IndexOf("fe80", StringComparison.OrdinalIgnoreCase) == 0) continue;
                                a.IPv6.Add(s);
                            }
                        }

                        foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                        {
                            if (gw.Address != null) a.Gateway.Add(gw.Address.ToString());
                        }

                        foreach (IPAddress dns in props.DnsAddresses)
                        {
                            a.Dns.Add(dns.ToString());
                        }

                        try
                        {
                            IPv4InterfaceProperties v4 = props.GetIPv4Properties();
                            if (v4 != null) a.DhcpEnabled = v4.IsDhcpEnabled;
                        }
                        catch
                        {
                        }
                    }
                    catch
                    {
                    }

                    list.Add(a);
                }
            }
            catch
            {
            }

            list.Sort(delegate (AdapterInfo x, AdapterInfo y)
            {
                bool xu = x.Status == "已连接";
                bool yu = y.Status == "已连接";
                if (xu != yu) return xu ? -1 : 1;
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        // ---------------- 连通性测试 ----------------

        public static PingResult Ping(string host, int count, int timeoutMs)
        {
            PingResult r = new PingResult();
            r.Host = host;
            r.Sent = count;
            if (string.IsNullOrWhiteSpace(host))
            {
                r.MinMs = 0; r.AvgMs = 0; r.MaxMs = 0; // 避免 long.MaxValue 直接进 UI
                return r;
            }
            if (count <= 0) count = 4;
            r.Sent = count;

            long total = 0;
            try
            {
                using (Ping ping = new Ping())
                {
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            PingReply reply = ping.Send(host, timeoutMs);
                            if (reply != null && reply.Status == IPStatus.Success)
                            {
                                r.Received++;
                                r.Address = reply.Address == null ? "" : reply.Address.ToString();
                                r.Ttl = reply.Options == null ? r.Ttl : reply.Options.Ttl;
                                long ms = reply.RoundtripTime;
                                total += ms;
                                if (ms < r.MinMs) r.MinMs = ms;
                                if (ms > r.MaxMs) r.MaxMs = ms;
                            }
                        }
                        catch
                        {
                        }

                        if (i < count - 1) System.Threading.Thread.Sleep(120);
                    }
                }
            }
            catch
            {
            }

            if (r.Received > 0)
            {
                r.AvgMs = total / r.Received;
            }
            else
            {
                r.MinMs = 0;
                r.MaxMs = 0;
                r.AvgMs = 0;
            }
            return r;
        }

        /// <summary>
        /// 带「禁止分片 (DF)」的单次探测。payload 为 ICMP 数据字节数（不含 IP 头 20 + ICMP 头 8）：
        /// 能以 payload 成功往返 ⇒ 路径上允许通过 payload + 28 字节的包，即路径 MTU ≥ payload + 28。
        /// 返回 PacketTooBig / 超时都视为不通过（MTU 寻优据此逐级收敛）。
        /// </summary>
        public static bool PingDontFragment(string host, int payload, int timeoutMs, out long roundtripMs)
        {
            roundtripMs = -1;
            if (string.IsNullOrWhiteSpace(host) || payload < 0) return false;
            try
            {
                using (Ping ping = new Ping())
                {
                    PingOptions opt = new PingOptions(64, true); // dontFragment = true
                    PingReply reply = ping.Send(host, timeoutMs, new byte[payload], opt);
                    if (reply == null || reply.Status != IPStatus.Success) return false;
                    roundtripMs = reply.RoundtripTime;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>设置指定网卡的 IPv4 MTU（netsh persistent，重启后仍生效）。</summary>
        public static Shell.Result SetMtu(string adapter, int mtu)
        {
            return Shell.Run("netsh.exe",
                "interface ipv4 set subinterface \"" + adapter + "\" mtu=" + mtu + " store=persistent", 30000);
        }

        /// <summary>寻优结果记忆文件（网卡名 → 最近一次寻优的最优 MTU）。</summary>
        private static string MtuMemoryPath
        {
            get
            {
                return System.IO.Path.Combine(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GuyueBox"), "mtu-best.txt");
            }
        }

        /// <summary>读取「网卡 → 最优 MTU」记忆（键不区分大小写；无记录返回空表）。</summary>
        public static Dictionary<string, int> LoadBestMtu()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = MtuMemoryPath;
                if (!System.IO.File.Exists(path)) return map;
                string[] lines = System.IO.File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    int bar = lines[i].LastIndexOf('|');
                    if (bar <= 0) continue;
                    int mtu;
                    if (!int.TryParse(lines[i].Substring(bar + 1).Trim(), out mtu)) continue;
                    if (mtu < 1000) continue;
                    string name = lines[i].Substring(0, bar).Trim();
                    if (name.Length > 0) map[name] = mtu;
                }
            }
            catch
            {
            }
            return map;
        }

        /// <summary>记录某网卡本次寻优的最优 MTU（同网卡覆盖，最多保留 50 条）。</summary>
        public static bool SaveBestMtu(string adapter, int mtu)
        {
            if (string.IsNullOrEmpty(adapter) || mtu < 1000) return false;
            try
            {
                Dictionary<string, int> map = LoadBestMtu();
                map[adapter.Trim()] = mtu;

                List<string> lines = new List<string>();
                int n = 0;
                foreach (KeyValuePair<string, int> kv in map)
                {
                    if (kv.Key.IndexOf('|') >= 0) continue;
                    lines.Add(kv.Key + "|" + kv.Value);
                    n++;
                    if (n >= 50) break;
                }
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MtuMemoryPath));
                System.IO.File.WriteAllLines(MtuMemoryPath, lines.ToArray(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------- 端口 ----------------

        /// <summary>
        /// 枚举全部 TCP/UDP 端口占用（netstat -ano 解析）：
        /// 附带占用进程 PID 与进程名（IPGlobalProperties 的 API 拿不到 PID，必须走 netstat）。
        /// </summary>
        public static List<PortInfo> GetListeningPorts()
        {
            var list = new List<PortInfo>();

            // PID → 进程名
            Dictionary<int, string> names = new Dictionary<int, string>();
            try
            {
                System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcesses();
                for (int i = 0; i < ps.Length; i++)
                {
                    try { names[ps[i].Id] = ps[i].ProcessName; }
                    catch { }
                    ps[i].Dispose();
                }
            }
            catch
            {
            }

            try
            {
                Shell.Result r = Shell.Run("netstat.exe", "-ano", 30000);
                if (r.Ok)
                {
                    string[] rows = (r.All ?? "").Split('\n');
                    for (int i = 0; i < rows.Length; i++)
                    {
                        string line = rows[i].Trim();
                        if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) &&
                            !line.StartsWith("UDP", StringComparison.OrdinalIgnoreCase)) continue;

                        string[] t = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length < 3) continue;

                        PortInfo p = new PortInfo();
                        p.Protocol = t[0].ToUpperInvariant();
                        int colon = t[1].LastIndexOf(':');
                        if (colon < 0) continue;
                        p.LocalAddress = t[1].Substring(0, colon);
                        int port;
                        if (!int.TryParse(t[1].Substring(colon + 1), out port)) continue;
                        p.LocalPort = port;

                        if (string.Equals(p.Protocol, "TCP", StringComparison.OrdinalIgnoreCase) && t.Length >= 5)
                        {
                            p.State = t[3];
                            int pid;
                            if (int.TryParse(t[4], out pid)) p.OwningPid = pid;
                        }
                        else if (t.Length >= 4) // UDP：无 State 列
                        {
                            p.State = "Listening";
                            int pid;
                            if (int.TryParse(t[3], out pid)) p.OwningPid = pid;
                        }
                        else continue;

                        if (p.OwningPid > 0)
                        {
                            string nm;
                            if (names.TryGetValue(p.OwningPid, out nm)) p.ProcessName = nm;
                        }
                        list.Add(p);
                    }
                }
            }
            catch
            {
            }

            list.Sort(delegate (PortInfo a, PortInfo b) { return a.LocalPort.CompareTo(b.LocalPort); });
            return list;
        }

        // ---------------- 常用修复动作 ----------------

        // 修复动作统一返回 Shell.Result：调用方用 ExitCode（r.Ok）判定成败，
        // 不再依赖命令输出里的英文 "fail" 关键字（中文系统 netsh/ipconfig 输出为本地化文本）。
        public static Shell.Result FlushDns()
        {
            return Shell.Run("ipconfig.exe", "/flushdns", 30000);
        }

        public static Shell.Result RenewDhcp()
        {
            Shell.Result a = Shell.Run("ipconfig.exe", "/release", 60000);
            Shell.Result b = Shell.Run("ipconfig.exe", "/renew", 120000);
            Shell.Result r = new Shell.Result();
            r.ExitCode = a.Ok && b.Ok ? 0 : (a.ExitCode != 0 ? a.ExitCode : b.ExitCode);
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(a.All)) sb.AppendLine(a.All);
            if (!string.IsNullOrEmpty(b.All)) sb.Append(b.All);
            r.Output = sb.ToString().Trim();
            r.Error = (a.Error + "\r\n" + b.Error).Trim();
            return r;
        }

        public static Shell.Result ResetWinsock()
        {
            return Shell.Run("netsh.exe", "winsock reset", 60000);
        }

        public static Shell.Result ResetTcpIp()
        {
            return Shell.Run("netsh.exe", "int ip reset", 60000, isChange: true);
        }

        public static Shell.Result ClearArpCache()
        {
            return Shell.Netsh("interface ip delete arpcache", true);
        }

        /// <summary>
        /// 读取各网卡的接口 MTU（netsh interface ipv4 show subinterfaces）。
        /// 返回「接口名 → MTU」列表（接口名保留原始中文名）。读取失败返回空列表。
        /// netsh 列布局随系统版本不同：
        ///   旧版「Idx Met MTU 状态 名称」（MTU 为第 2~3 个数字）；
        ///   Win11 新版「MTU MediaSenseState BytesIn BytesOut 名称」（MTU 为第 1 个数字）。
        /// 因此不再按固定列号取值，而是在 4 个数字列中找落在合理 MTU 区间
        /// [576, 65535] 的最大值（跃点/状态是 1~75 的小数字，字节计数动辄上亿，
        /// 4294967295 回环值超界——三者天然被区间滤掉，两种布局都能命中 MTU）。
        /// </summary>
        public static List<KeyValuePair<string, int>> GetMtuList()
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();
            try
            {
                Shell.Result r = Shell.Run("netsh.exe", "interface ipv4 show subinterfaces", 20000);
                string[] lines = (r.All ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    // 形如：  1500                1     635496181      13489494  以太网（Win11）
                    //   或：   12      35       1500   connected    以太网（旧版）
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                        line, @"^(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(.+)$");
                    if (!m.Success) continue;
                    int mtu = -1;
                    // 新格式（Win11+）：第一列即 MTU，直接命中即可。
                    // 旧格式：第一列是 Idx/Met（< 576），MTU 在后续列中。
                    int g1;
                    if (int.TryParse(m.Groups[1].Value, out g1) && g1 >= 576 && g1 <= 65535)
                    {
                        mtu = g1;
                    }
                    else
                    {
                        for (int g = 2; g <= 4; g++)
                        {
                            int v;
                            if (int.TryParse(m.Groups[g].Value, out v) && v >= 576 && v <= 65535)
                            {
                                mtu = v;
                                break;
                            }
                        }
                    }
                    if (mtu < 0) continue;
                    string name = m.Groups[5].Value.Trim().Trim('"');
                    if (name.Length == 0) continue;
                    if (name.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0) continue; // 回环接口无意义
                    result.Add(new KeyValuePair<string, int>(name, mtu));
                }
            }
            catch { }
            return result;
        }
    }
}
