﻿/* ============================================================
 * 文件说明：DNS 服务器切换：网卡枚举、读取与设置
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Text;

namespace GuyueBox.Core
{
    public sealed class DnsPreset
    {
        public string Region = "";
        public string Name = "";
        public string Primary = "";
        public string Secondary = "";
    }

    public static class DnsSwitch
    {
        /// <summary>列出可配置 DNS 的网卡名（结构化 API，避免 netsh 文本解析的列数/编码坑）。</summary>
        public static List<string> ListAdapters()
        {
            List<string> list = new List<string>();
            try
            {
                System.Net.NetworkInformation.NetworkInterface[] nics =
                    System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    System.Net.NetworkInformation.NetworkInterface ni = nics[i];
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                        continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                        continue;
                    if (string.Equals(ni.Name, "Loopback Pseudo-Interface 1", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!list.Contains(ni.Name)) list.Add(ni.Name);
                }
            }
            catch { }
            return list;
        }

        /// <summary>读取指定网卡的当前 DNS（注册表结构化读取，兼容 DHCP 与静态配置；不依赖 netsh 文本解析）。</summary>
        public static string GetDns(string adapter)
        {
            try
            {
                string guid = FindAdapterGuid(adapter);
                if (string.IsNullOrEmpty(guid)) return "（无法读取）";

                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid, false))
                {
                    if (key == null) return "";
                    string ns = Convert.ToString(key.GetValue("NameServer", ""));
                    if (!string.IsNullOrWhiteSpace(ns))
                    {
                        // 静态 DNS：逗号或空格分隔
                        return ns.Replace(',', ' ').Replace("  ", " ").Trim();
                    }
                    string dhcp = Convert.ToString(key.GetValue("DhcpNameServer", ""));
                    if (!string.IsNullOrWhiteSpace(dhcp)) return dhcp.Trim();
                    return ""; // 自动获取
                }
            }
            catch
            {
                return "（无法读取）";
            }
        }

        /// <summary>按连接名找网卡的 GUID（HKLM\...\Network\{类}\{GUID}\Connection\Name）。</summary>
        private static string FindAdapterGuid(string adapter)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Network\{4D36E972-E325-11CE-BFC1-08002BE10318}", false))
                {
                    if (root == null) return null;
                    string[] guids = root.GetSubKeyNames();
                    for (int i = 0; i < guids.Length; i++)
                    {
                        using (Microsoft.Win32.RegistryKey conn = root.OpenSubKey(guids[i] + "\\Connection", false))
                        {
                            if (conn == null) continue;
                            string name = Convert.ToString(conn.GetValue("Name"));
                            if (string.Equals(name, adapter, StringComparison.OrdinalIgnoreCase))
                                return guids[i];
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool Set(string adapter, string primary, string secondary, out string error)
        {
            error = "";
            Shell.Result set1 = Shell.Run("netsh.exe",
                "interface ip set dns name=\"" + adapter + "\" source=static addr=" + primary + " validate=no", 20000);
            if (!set1.Ok) { error = "设置主 DNS 失败：" + set1.All.Trim(); return false; }

            // 备用 DNS 为空（单节点服务器）时只设主 DNS，跳过 add 避免无效命令报错
            if (!string.IsNullOrWhiteSpace(secondary))
            {
                Shell.Result set2 = Shell.Run("netsh.exe",
                    "interface ip add dns name=\"" + adapter + "\" addr=" + secondary + " index=2 validate=no", 20000);
                if (!set2.Ok) { error = "设置备用 DNS 失败：" + set2.All.Trim(); return false; }
            }
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
    }
}
