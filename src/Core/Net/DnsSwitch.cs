using System;
using System.Collections.Generic;
using System.Text;

namespace GuyueBox.Core
{
    public sealed class DnsPreset
    {
        public string Name = "";
        public string Primary = "";
        public string Secondary = "";
    }

    public static class DnsSwitch
    {
        public static readonly DnsPreset[] Presets = new DnsPreset[]
        {
            new DnsPreset { Name = "Cloudflare (1.1.1.1)", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new DnsPreset { Name = "Google (8.8.8.8)", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
            new DnsPreset { Name = "AdGuard (防广告)", Primary = "94.140.14.14", Secondary = "94.140.15.15" },
            new DnsPreset { Name = "OpenDNS", Primary = "208.67.222.222", Secondary = "208.67.220.220" }
        };

        public static List<string> ListAdapters()
        {
            List<string> list = new List<string>();
            Shell.Result r = Shell.Run("netsh.exe", "interface show interface", 20000);
            if (!r.Ok) return list;

            string[] lines = r.Output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Admin State", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("---")) continue;

                string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 4) continue;
                string name = string.Join(" ", tokens, 3, tokens.Length - 3).Trim();
                if (name.Length == 0) continue;
                if (string.Equals(name, "Loopback Pseudo-Interface 1", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(name);
            }
            return list;
        }

        public static string GetDns(string adapter)
        {
            Shell.Result r = Shell.Run("netsh.exe", "interface ip show dns name=\"" + adapter + "\"", 20000);
            if (!r.Ok) return "（无法读取）";

            string[] lines = r.Output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool inBlock = false;
            string mode = null;
            List<string> servers = new List<string>();

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("Configuration for interface", StringComparison.OrdinalIgnoreCase))
                {
                    string block = ExtractQuoted(line);
                    inBlock = string.Equals(block, adapter, StringComparison.OrdinalIgnoreCase);
                    mode = null;
                    continue;
                }
                if (!inBlock) continue;

                if (line.StartsWith("Register with which suffix", StringComparison.OrdinalIgnoreCase)) break;

                int col = line.IndexOf(':');
                if (line.IndexOf("DNS servers configured through DHCP", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mode = "dhcp";
                    AddServer(servers, line, col);
                }
                else if (line.IndexOf("Statically Configured DNS Servers", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mode = "static";
                    AddServer(servers, line, col);
                }
                else if (mode != null && col < 0 && line.Length > 0)
                {
                    servers.Add(line);
                }
            }

            if (servers.Count == 0) return "自动获取 (DHCP)";
            string prefix = (mode == "static") ? "静态: " : "DHCP: ";
            return prefix + string.Join("、", servers.ToArray());
        }

        public static bool Set(string adapter, string primary, string secondary, out string error)
        {
            error = "";
            Shell.Result set1 = Shell.Run("netsh.exe",
                "interface ip set dns name=\"" + adapter + "\" source=static addr=" + primary + " validate=no", 20000);
            if (!set1.Ok) { error = "设置主 DNS 失败：" + set1.All.Trim(); return false; }

            Shell.Result set2 = Shell.Run("netsh.exe",
                "interface ip add dns name=\"" + adapter + "\" addr=" + secondary + " index=2 validate=no", 20000);
            if (!set2.Ok) { error = "设置备用 DNS 失败：" + set2.All.Trim(); return false; }
            return true;
        }

        public static bool Reset(string adapter, out string error)
        {
            error = "";
            Shell.Result r = Shell.Run("netsh.exe",
                "interface ip set dns name=\"" + adapter + "\" source=dhcp validate=no", 20000);
            if (!r.Ok) { error = "恢复为 DHCP 失败：" + r.All.Trim(); return false; }
            return true;
        }

        private static void AddServer(List<string> servers, string line, int col)
        {
            if (col < 0) return;
            string v = line.Substring(col + 1).Trim();
            if (v.Length == 0 || string.Equals(v, "None", StringComparison.OrdinalIgnoreCase)) return;
            servers.Add(v);
        }

        private static string ExtractQuoted(string line)
        {
            int a = line.IndexOf('"');
            int b = line.LastIndexOf('"');
            if (a >= 0 && b > a) return line.Substring(a + 1, b - a - 1);
            return "";
        }
    }
}
