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

        public static string FlushDns()
        {
            Shell.Result r = Shell.Run("ipconfig.exe", "/flushdns", 30000);
            return string.IsNullOrEmpty(r.All) ? "本地 DNS 解析缓存已刷新。" : r.All;
        }

        public static string RenewDhcp()
        {
            StringBuilder sb = new StringBuilder();
            Shell.Result a = Shell.Run("ipconfig.exe", "/release", 60000);
            sb.AppendLine(a.All);
            Shell.Result b = Shell.Run("ipconfig.exe", "/renew", 120000);
            sb.Append(b.All);
            string text = sb.ToString().Trim();
            return string.IsNullOrEmpty(text) ? "IP 地址已重新获取。" : text;
        }

        public static string ResetWinsock()
        {
            Shell.Result r = Shell.Run("netsh.exe", "winsock reset", 60000);
            return string.IsNullOrEmpty(r.All)
                ? "Winsock 目录已重置，需要重启计算机后生效。"
                : r.All + "\r\n\r\n（需要重启计算机后生效）";
        }

        public static string ResetTcpIp()
        {
            Shell.Result r = Shell.Run("netsh.exe", "int ip reset", 60000);
            return string.IsNullOrEmpty(r.All)
                ? "TCP/IP 协议栈已重置，需要重启计算机后生效。"
                : r.All + "\r\n\r\n（需要重启计算机后生效）";
        }

        public static string ClearArpCache()
        {
            Shell.Result r = Shell.Netsh("interface ip delete arpcache");
            return string.IsNullOrEmpty(r.All) ? "ARP 缓存已清空。" : r.All;
        }

        public static string SetDns(string adapterName, string primary, string secondary)
        {
            string args = "interface ip set dns name=\"" + adapterName + "\" static " + primary + " primary";
            Shell.Result r1 = Shell.Netsh(args);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(r1.All);

            if (!string.IsNullOrWhiteSpace(secondary))
            {
                Shell.Result r2 = Shell.Netsh("interface ip add dns name=\"" + adapterName + "\" " + secondary + " index=2");
                sb.AppendLine(r2.All);
            }
            string text = sb.ToString().Trim();
            return string.IsNullOrEmpty(text) ? "DNS 服务器已更新。" : text;
        }

        public static string SetDnsAutomatic(string adapterName)
        {
            Shell.Result r = Shell.Netsh("interface ip set dns name=\"" + adapterName + "\" source=dhcp");
            return string.IsNullOrEmpty(r.All) ? "DNS 已恢复为自动获取。" : r.All;
        }

        /// <summary>应用一套保守的 TCP 调优参数。</summary>
        public static string ApplyTcpTuning()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Shell.Netsh("int tcp set global autotuninglevel=normal").All);
            sb.AppendLine(Shell.Netsh("int tcp set global ecncapability=enabled").All);
            sb.AppendLine(Shell.Netsh("int tcp set global rss=enabled").All);
            sb.AppendLine(Shell.Netsh("int tcp set global timestamps=disabled").All);
            sb.AppendLine(Shell.Netsh("int tcp set heuristics disabled").All);
            string text = sb.ToString().Trim();
            return string.IsNullOrEmpty(text) ? "TCP 参数已优化。" : text;
        }

        public static string RestoreTcpDefaults()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Shell.Netsh("int tcp set global autotuninglevel=normal").All);
            sb.AppendLine(Shell.Netsh("int tcp set heuristics default").All);
            string text = sb.ToString().Trim();
            return string.IsNullOrEmpty(text) ? "TCP 参数已恢复系统默认。" : text;
        }

        /// <summary>
        /// 读取各网卡的接口 MTU（netsh interface ipv4 show subinterfaces）。
        /// 返回「接口名 → MTU」列表（接口名保留原始中文名）。读取失败返回空列表。
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
                    // 形如：  12    1500    1500   1500  "以太网"
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                        line, @"^(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(.+)$");
                    if (!m.Success) continue;
                    int mtu;
                    if (!int.TryParse(m.Groups[2].Value, out mtu)) continue;
                    string name = m.Groups[5].Value.Trim().Trim('"');
                    if (name.Length == 0) continue;
                    if (name.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0) continue; // 回环接口无意义
                    result.Add(new KeyValuePair<string, int>(name, mtu));
                }
            }
            catch { }
            return result;
        }

        public static string GetIpConfig()
        {
            Shell.Result r = Shell.Run("ipconfig.exe", "/all", 30000);
            return r.All;
        }

        /// <summary>探测本机公网出口（仅读取，不发送任何数据）。</summary>
        public static List<string> GetLocalDnsServers()
        {
            List<string> servers = new List<string>();
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    if (nics[i].OperationalStatus != OperationalStatus.Up) continue;
                    try
                    {
                        foreach (IPAddress d in nics[i].GetIPProperties().DnsAddresses)
                        {
                            string s = d.ToString();
                            if (!servers.Contains(s)) servers.Add(s);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return servers;
        }
    }
}
