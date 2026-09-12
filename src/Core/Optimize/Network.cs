using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================

    // ===================================================================
    // Nagle 禁用（低延迟网络）：需要写入每个 TCP/IP 接口，动态项
    // ===================================================================

    public sealed class NagleTweak : ITweak
    {
        private const string InterfacesPath =
            @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        public string Id { get { return "nagle_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "禁用 Nagle 算法（降低网络延迟）"; } }
        public string Description
        {
            get { return "对全部网卡接口禁用 Nagle 合包与延迟确认，竞技游戏实测可降低数毫秒网络延迟。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static List<string> InterfacePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(InterfacesPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        list.Add(InterfacesPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public bool IsApplied()
        {
            List<string> paths = InterfacePaths();
            if (paths.Count == 0) return false;
            for (int i = 0; i < paths.Count; i++)
            {
                object ack = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "TcpAckFrequency");
                object noDelay = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "TCPNoDelay");
                if (ack == null || noDelay == null) return false;
                try
                {
                    if (Convert.ToInt32(ack) != 1 || Convert.ToInt32(noDelay) != 1) return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> paths = InterfacePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(Id);
            bool ok = true;
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TcpAckFrequency", 1,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TCPNoDelay", 1,
                        RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "TcpDelAckTicks", 0,
                        RegistryValueKind.DWord, Id);
                }
                catch
                {
                    ok = false;
                }
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    /// <summary>
    /// 游戏 DSCP 46（EF 加速转发）QoS 标记。
    /// 为选定的游戏 exe 写入 QoS 策略（HKLM\...\QoS），出站包打 DSCP 46 标记，
    /// 支持按包优先级调度的路由器/运营商会优先转发游戏流量。































































































































































































































    public sealed class DscpTweak : ITweak
    {
        private const string QosKey = @"SOFTWARE\Policies\Microsoft\Windows\QoS";

        public string Id { get { return "dscp_game_marker"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "游戏网络 DSCP 优先标记 (QoS)"; } }
        public string Description
        {
            get { return "选择游戏 exe，为其出站网络包打 DSCP 46（EF 加速转发）标记——支持 QoS 调度的路由器/运营商会优先转发游戏流量，降低网络延迟抖动。同时禁用该游戏的 DX 无边框窗口化模式。仅对支持 DSCP 的网络环境生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private const string ApplyScript =
            "Add-Type -AssemblyName System.Windows.Forms\r\n" +
            "$d = New-Object System.Windows.Forms.OpenFileDialog\r\n" +
            "$d.Filter = '游戏程序 (*.exe)|*.exe'\r\n" +
            "$d.Title = 'Select game executable'\r\n" +
            "if ($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 1 }\r\n" +
            "$n = [IO.Path]::GetFileName($d.FileName)\r\n" +
            "$k = 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\QoS\\' + $n\r\n" +
            "New-Item -Path $k -Force | Out-Null\r\n" +
            "Set-ItemProperty -Path $k -Name 'Application Name' -Value $n -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name 'Version' -Value '1.0' -Type String\r\n" +
            "foreach ($p in 'Protocol','Local Port','Local IP','Local IP Prefix Length','Remote Port','Remote IP','Remote IP Prefix Length') { Set-ItemProperty -Path $k -Name $p -Value '*' -Type String }\r\n" +
            "Set-ItemProperty -Path $k -Name 'DSCP Value' -Value '46' -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name 'Throttle Rate' -Value '-1' -Type String\r\n" +
            "Set-ItemProperty -Path $k -Name \"Don't use NLA\" -Value '1' -Type String\r\n" +
            "Set-ItemProperty -Path 'HKCU:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers' -Name $d.FileName -Value '~ DISABLEDXMAXIMIZEDWINDOWEDMODE HIGHDPIAWARE' -Type String\r\n";

        private const string RevertScript =
            "$base = 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\QoS'\r\n" +
            "if (Test-Path $base) { Get-ChildItem $base | ForEach-Object { $v = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).'DSCP Value'; if ($v -eq '46') { Remove-Item $_.PSPath -Force } } }\r\n" +
            "$lay = 'HKCU:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers'\r\n" +
            "if (Test-Path $lay) { $p = Get-ItemProperty $lay; $p.PSObject.Properties | Where-Object { $_.Value -like '*DISABLEDXMAXIMIZEDWINDOWEDMODE*' } | ForEach-Object { Remove-ItemProperty -Path $lay -Name $_.Name -ErrorAction SilentlyContinue } }\r\n";

        private static bool RunScript(string script)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "guyuebox_dscp.ps1");
                System.IO.File.WriteAllText(path, script,
                    new System.Text.UTF8Encoding(true));
                return Shell.Run("powershell.exe",
                    "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + path + "\"", 120000).Ok;
            }
            catch
            {
                return false;
            }
        }

        public bool IsApplied()
        {
            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(QosKey))
                {
                    if (baseKey == null) return false;
                    foreach (string sub in baseKey.GetSubKeyNames())
                    {
                        using (RegistryKey k = baseKey.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("DSCP Value");
                            object nla = k.GetValue("Don't use NLA");
                            if (v != null && v.ToString() == "46" &&
                                nla != null && nla.ToString() == "1") return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public bool Apply()
        {
            return RunScript(ApplyScript) && IsApplied();
        }

        public bool Revert()
        {
            return RunScript(RevertScript);
        }
    }

    /// <summary>
    /// 逐个网卡（网络适配器类驱动子键）关闭省电特性：节能以太网、选择性暂停、
    /// 关机降速、电源节省模式等，消除竞技游戏中网卡省电导致的延迟抖动。
    /// </summary>
    public sealed class NicPowerTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        /// <summary>键名 → 写入值（网卡高级属性多为 REG_SZ；PnPCapabilities 为 DWORD）。</summary>
        private static readonly string[] StrKeys = new string[]
        {
            "*DeviceSleepOnDisconnect", "*EEE", "*ModernStandbyWoLMagicPacket", "*SelectiveSuspend",
            "*WakeOnMagicPacket", "*WakeOnPattern", "AutoPowerSaveModeEnabled", "EEELinkAdvertisement",
            "EeePhyEnable", "EnableGreenEthernet", "EnableModernStandby", "GigaLite",
            "PowerDownPll", "PowerSavingMode", "ReduceSpeedOnPowerDown", "S5WakeOnLan",
            "SavePowerNowEnabled", "ULPMode", "WakeOnLink", "WakeOnSlot", "WakeUpModeCap"
        };

        public string Id { get { return "nic_power_save_off"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "网卡省电全关（降低延迟抖动）"; } }
        public string Description
        {
            get { return "对每个网卡关闭节能以太网 (EEE)、选择性暂停、电源节省模式等省电特性。省电机制会让网卡间歇性降速，是 WiFi/有线游戏延迟抖动的常见原因。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        /// <summary>只取形如 0000 的驱动实例子键（跳过 Properties 等）。</summary>
        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NicClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(NicClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;
            for (int i = 0; i < paths.Count; i++)
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                {
                    if (k == null) continue;
                    foreach (string name in StrKeys)
                    {
                        object v = k.GetValue(name);
                        if (v != null && v.ToString() != "0") return false;
                    }
                    object pnp = k.GetValue("PnPCapabilities");
                    if (pnp != null)
                    {
                        try
                        {
                            if (Convert.ToInt32(pnp) != 24) return false;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(Id);
            for (int i = 0; i < paths.Count; i++)
            {
                try
                {
                    for (int n = 0; n < StrKeys.Length; n++)
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], StrKeys[n], "0",
                            RegistryValueKind.String, Id);
                    }
                    RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "PnPCapabilities", 24,
                        RegistryValueKind.DWord, Id);
                }
                catch
                {
                }
            }
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    // ===================================================================
    // 网卡高级属性（动态：每个驱动暴露的关键字逐一处理，与 NIC调整.bat 同思路）
    // ===================================================================

    public sealed class NicAdvancedTweak : ITweak
    {
        private const string NicClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _keys;      // REG_SZ 关键字
        private readonly string[] _dwordKeys; // REG_DWORD 关键字

        public NicAdvancedTweak(string id, string name, string desc, string[] keys, string[] dwordKeys)
        {
            _id = id;
            _name = name;
            _desc = desc;
            _keys = keys ?? new string[0];
            _dwordKeys = dwordKeys ?? new string[0];
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static List<string> DevicePaths()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NicClassPath, false))
                {
                    if (root == null) return list;
                    string[] subs = root.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        if (subs[i].Length != 4) continue;
                        bool digits = true;
                        for (int k = 0; k < 4; k++)
                        {
                            if (!char.IsDigit(subs[i][k])) { digits = false; break; }
                        }
                        if (digits) list.Add(NicClassPath + "\\" + subs[i]);
                    }
                }
            }
            catch
            {
            }
            return list;
        }

        private bool Matches(object current, string key)
        {
            bool isDword = Array.IndexOf(_dwordKeys, key) >= 0;
            try
            {
                if (isDword) return Convert.ToInt32(current) == 24;
                return string.Equals(current.ToString(), "0", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public bool IsApplied()
        {
            List<string> paths = DevicePaths();
            int seen = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                {
                    if (k == null) continue;
                    CheckGroup(k.GetValueNames(), paths[i], ref seen);
                }
            }
            return seen > 0;
        }

        private void CheckGroup(string[] existing, string path, ref int seen)
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                if (Array.IndexOf(existing, _keys[i]) < 0) continue;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, path, _keys[i]);
                if (v == null || !Matches(v, _keys[i])) return; // 存在但未达标 → 未应用
                seen++;
            }
            for (int i = 0; i < _dwordKeys.Length; i++)
            {
                if (Array.IndexOf(existing, _dwordKeys[i]) < 0) continue;
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, path, _dwordKeys[i]);
                if (v == null || !Matches(v, _dwordKeys[i])) return;
                seen++;
            }
        }

        public bool Apply()
        {
            List<string> paths = DevicePaths();
            if (paths.Count == 0) return false;

            RegHelper.BeginBackup(_id);
            int written = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                string[] existing;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(paths[i], false))
                    {
                        if (k == null) continue;
                        existing = k.GetValueNames();
                    }
                }
                catch
                {
                    continue;
                }

                for (int v = 0; v < _keys.Length; v++)
                {
                    // 与原脚本一致：只覆盖驱动暴露（已存在）的关键字
                    if (Array.IndexOf(existing, _keys[v]) < 0) continue;
                    try
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], _keys[v], "0",
                            RegistryValueKind.String, _id);
                        written++;
                    }
                    catch
                    {
                    }
                }
                for (int v = 0; v < _dwordKeys.Length; v++)
                {
                    if (Array.IndexOf(existing, _dwordKeys[v]) < 0) continue;
                    try
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], _dwordKeys[v], 24,
                            RegistryValueKind.DWord, _id);
                        written++;
                    }
                    catch
                    {
                    }
                }
            }
            return written > 0;
        }

        public bool Revert()
        {
            return RegHelper.Restore(_id);
        }
    }

    /// <summary>
    /// netsh 类优化项：apply/revert 走命令，probe 解析 show 输出（中英双语，
    /// 通过 chcp 65001 强制 UTF-8 输出避免乱码）。
    /// </summary>
    public sealed class NetshTweak : ITweak
    {
        private readonly string _id;
        private readonly string _name;
        private readonly string _desc;
        private readonly string[] _apply;
        private readonly string[] _revert;
        private readonly string _show;
        private readonly string[] _patterns; // 每条都必须在输出中匹配到
        private readonly bool _risky;

        public NetshTweak(string id, string name, string desc, string[] apply, string[] revert,
            string show, string[] patterns, bool risky)
        {
            _id = id; _name = name; _desc = desc;
            _apply = apply; _revert = revert; _show = show; _patterns = patterns; _risky = risky;
        }

        public string Id { get { return _id; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        public bool IsApplied()
        {
            string text = RunNetsh(_show);
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < _patterns.Length; i++)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, _patterns[i],
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// 运行 netsh 并自适应解码输出：新系统 (Win11 24H2+) 重定向输出为 UTF-8，
        /// 旧系统为系统 ANSI（中文 GBK/936）。按原始字节先试严格 UTF-8，失败回退 GBK。
        /// </summary>
        private static string RunNetsh(string arguments)
        {
            try
            {
                using (System.Diagnostics.Process p = new System.Diagnostics.Process())
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = "netsh.exe";
                    psi.Arguments = arguments;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    p.StartInfo = psi;
                    p.Start();

                    byte[] stdout = ReadAll(p.StandardOutput.BaseStream);
                    byte[] stderr = ReadAll(p.StandardError.BaseStream);
                    p.WaitForExit(30000);

                    return DecodeAuto(stdout) + "\r\n" + DecodeAuto(stderr);
                }
            }
            catch
            {
                return "";
            }
        }

        private static byte[] ReadAll(System.IO.Stream stream)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                byte[] buf = new byte[8192];
                while (true)
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static string DecodeAuto(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            try
            {
                // 严格 UTF-8：出现非法序列即抛异常，走 GBK 回退
                return new System.Text.UTF8Encoding(false, true).GetString(data);
            }
            catch
            {
                try { return System.Text.Encoding.GetEncoding(936).GetString(data); }
                catch { return System.Text.Encoding.GetEncoding(0).GetString(data); } // OEM 代码页（netsh 输出实际编码）
            }
        }

        public bool Apply()
        {
            for (int i = 0; i < _apply.Length; i++)
            {
                Shell.Result r = Shell.Netsh(_apply[i]);
                if (!r.Ok) return false;
            }
            return true;
        }

        public bool Revert()
        {
            for (int i = 0; i < _revert.Length; i++)
            {
                Shell.Netsh(_revert[i]);
            }
            return true;
        }
    }

    /// <summary>
    /// 网络协议栈精简：对全部网卡禁用非必要协议绑定（LLDP / LLTDIO / IP Helper /
    /// 响应程序 / SMB 服务端与客户端），减少后台广播与协议处理。走 PowerShell NetAdapter Binding。
    /// </summary>
    public sealed class NetBindingTweak : ITweak
    {
        private const string ComponentIds = "ms_lldp,ms_lltdio,ms_implat,ms_rspndr,ms_server,ms_msclient,ms_netbt";
        private static readonly string[] IdArray = new string[]
        {
            "ms_lldp", "ms_lltdio", "ms_implat", "ms_rspndr", "ms_server", "ms_msclient", "ms_netbt"
        };

        public string Id { get { return "net_binding_slim"; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return "网络协议栈精简（禁用发现与共享协议）"; } }
        public string Description
        {
            get
            {
                return "对全部网卡禁用 LLDP / LLTDIO / IP-Helper / 响应程序 / 微软文件共享与客户端 / NetBIOS 绑定，"
                    + "减少后台广播流量与名称解析干扰。代价：无法局域网共享文件、部分网络发现功能失效。游戏机/单机环境适用。";
            }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        private static string Ps(string script)
        {
            Shell.Result r = Shell.Run("powershell.exe",
                "-NoProfile -Command \"" + script + "\"", 60000);
            return r.All ?? "";
        }

        public bool IsApplied()
        {
            string text = Ps("(Get-NetAdapterBinding -Name '*' -ErrorAction SilentlyContinue | " +
                "Where-Object { $_.ComponentID -in @('" +
                string.Join("','", IdArray) + "') }) | " +
                "ForEach-Object { $_.ComponentID + '=' + $_.Enabled }");
            if (string.IsNullOrEmpty(text)) return false;

            int seen = 0;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                for (int k = 0; k < IdArray.Length; k++)
                {
                    if (!line.StartsWith(IdArray[k] + "=", StringComparison.Ordinal)) continue;
                    seen++;
                    if (!line.EndsWith("=False", StringComparison.Ordinal)) return false;
                }
            }
            return seen > 0;
        }

        public bool Apply()
        {
            Ps("Disable-NetAdapterBinding -Name '*' -ComponentID " + ComponentIds +
                " -ErrorAction SilentlyContinue");
            return true; // 部分机器无对应绑定也算完成
        }

        public bool Revert()
        {
            Ps("Enable-NetAdapterBinding -Name '*' -ComponentID " + ComponentIds +
                " -ErrorAction SilentlyContinue");
            return true;
        }
    }
}
