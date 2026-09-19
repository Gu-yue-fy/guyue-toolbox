using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>一个已安装的程序（来自注册表 Uninstall 分支）。</summary>
    public sealed class ProgramEntry
    {
        public string Name = "";
        public string Publisher = "";
        public string Version = "";
        public long SizeBytes;
        public string InstallLocation = "";
        public string UninstallString = "";
        public string QuietUninstallString = "";
        public bool IsMsi;
        public string KeyPath = "";
        public string InstallDate = "";

        public bool HasLocation
        {
            get { return !string.IsNullOrEmpty(InstallLocation) && Directory.Exists(InstallLocation); }
        }

        public string SizeText
        {
            get { return SizeBytes > 0 ? SysInfo.FormatSize(SizeBytes) : "未知"; }
        }
    }

    /// <summary>
    /// 读取系统中已安装的软件清单（32/64 位、本机/当前用户）。
    /// 只读注册表，不修改任何东西。
    /// </summary>
    public static class Programs
    {
        public static List<ProgramEntry> Scan(bool includeUpdates)
        {
            // 4 个注册表分支互不相关，并行读取：Uninstall 子键常达数百~上千条，是加载慢的主因。
            // 每个分支先写进自己的字典，再按原顺序合并——保持与串行一致的去重语义（先出现者优先）
            RegistryHive[] hives = new RegistryHive[]
            {
                RegistryHive.LocalMachine, RegistryHive.LocalMachine,
                RegistryHive.CurrentUser, RegistryHive.CurrentUser
            };
            RegistryView[] views = new RegistryView[]
            {
                RegistryView.Registry64, RegistryView.Registry32,
                RegistryView.Registry64, RegistryView.Registry32
            };

            Dictionary<string, ProgramEntry>[] maps = new Dictionary<string, ProgramEntry>[hives.Length];
            Parallel.For(0, hives.Length,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(int i)
                {
                    Dictionary<string, ProgramEntry> m = new Dictionary<string, ProgramEntry>();
                    ReadBranch(hives[i], views[i], includeUpdates, m);
                    maps[i] = m;
                });

            Dictionary<string, ProgramEntry> map = new Dictionary<string, ProgramEntry>();
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null) continue;
                foreach (KeyValuePair<string, ProgramEntry> kv in maps[i])
                {
                    if (!map.ContainsKey(kv.Key)) map[kv.Key] = kv.Value;
                }
            }

            List<ProgramEntry> list = new List<ProgramEntry>(map.Values);
            list.Sort(delegate (ProgramEntry a, ProgramEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static void ReadBranch(RegistryHive hive, RegistryView view, bool includeUpdates,
            Dictionary<string, ProgramEntry> map)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uk = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uk == null) return;
                    foreach (string sub in uk.GetSubKeyNames())
                    {
                        using (RegistryKey k = uk.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            Parse(k, sub, includeUpdates, map);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void Parse(RegistryKey k, string sub, bool includeUpdates,
            Dictionary<string, ProgramEntry> map)
        {
            object nameObj = k.GetValue("DisplayName");
            string name = nameObj == null ? "" : nameObj.ToString();
            if (string.IsNullOrWhiteSpace(name)) return;

            object sysObj = k.GetValue("SystemComponent");
            if (sysObj != null)
            {
                try { if (Convert.ToInt32(sysObj) == 1) return; }
                catch { }
            }
            if (k.GetValue("ParentKeyName") != null) return;

            string release = k.GetValue("ReleaseType") as string;
            if (!includeUpdates && !string.IsNullOrEmpty(release))
            {
                if (release == "Security" || release == "Update" || release == "Hotfix" || release == "ServicePack")
                {
                    return;
                }
            }

            string uninstall = k.GetValue("UninstallString") as string;
            bool isMsi = false;
            object msiObj = k.GetValue("WindowsInstaller");
            if (msiObj != null)
            {
                try { isMsi = Convert.ToInt32(msiObj) == 1; }
                catch { }
            }

            string publisher = (k.GetValue("Publisher") as string ?? "").Trim();
            string version = (k.GetValue("DisplayVersion") as string ?? "").Trim();

            string key = (name.Trim() + "|" + publisher + "|" + version).ToLower();
            if (map.ContainsKey(key)) return;

            ProgramEntry e = new ProgramEntry();
            e.Name = name.Trim();
            e.Publisher = publisher;
            e.Version = version;
            e.InstallLocation = (k.GetValue("InstallLocation") as string ?? "").Trim();
            e.UninstallString = uninstall ?? "";
            e.QuietUninstallString = (k.GetValue("QuietUninstallString") as string ?? "").Trim();
            e.IsMsi = isMsi;
            e.KeyPath = sub;
            e.InstallDate = (k.GetValue("InstallDate") as string ?? "").Trim();

            object sizeObj = k.GetValue("EstimatedSize");
            if (sizeObj != null)
            {
                try { e.SizeBytes = Convert.ToInt64(sizeObj) * 1024L; }
                catch { e.SizeBytes = 0; }
            }

            map[key] = e;
        }

        /// <summary>启动卸载程序（交互式，必要时请求 UAC）。silent 优先使用静默卸载字符串。</summary>
        public static bool LaunchUninstall(ProgramEntry e, bool silent)
        {
            if (e == null || string.IsNullOrEmpty(e.UninstallString)) return false;

            string fileName;
            string args;

            if (e.IsMsi)
            {
                fileName = "msiexec.exe";
                args = (silent ? "/qn " : "/qb ") + "/x " + e.KeyPath;
            }
            else
            {
                string cmd = (silent && !string.IsNullOrEmpty(e.QuietUninstallString))
                    ? e.QuietUninstallString
                    : e.UninstallString;
                cmd = cmd.Trim();

                if (cmd.StartsWith("\""))
                {
                    int end = cmd.IndexOf('"', 1);
                    if (end < 0) { fileName = cmd.Substring(1); args = ""; }
                    else
                    {
                        fileName = cmd.Substring(1, end - 1);
                        args = cmd.Substring(end + 1).Trim();
                    }
                }
                else
                {
                    int sp = cmd.IndexOf(' ');
                    if (sp < 0) { fileName = cmd; args = ""; }
                    else { fileName = cmd.Substring(0, sp); args = cmd.Substring(sp + 1).Trim(); }
                }
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = args;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process.Start(psi)) { }
                return true;
            }
            catch
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = fileName;
                    psi.Arguments = args;
                    psi.UseShellExecute = true;
                    using (Process.Start(psi)) { }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
