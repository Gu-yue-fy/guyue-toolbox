using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// 强制卸载引擎：原生卸载程序之外的第二条路。
    /// 适用场景：卸载程序损坏/卡死、软件已删但注册表残留、绿色软件无卸载项。
    /// 能力：强杀进程 → 删除安装目录 → 扫描并清除注册表残留（卸载键/软件键）→ 清理残留目录与快捷方式。
    /// 全部动作记入 RegLog；删除不可恢复，调用方必须先让用户确认明细。
    /// </summary>
    public static class ProgramForcer
    {
        /// <summary>一次强制卸载的完整计划（供 UI 展示明细后确认执行）。</summary>
        public class Plan
        {
            public ProgramEntry Entry;
            public List<string> Processes = new List<string>();      // 将结束的进程（名 + PID）
            public List<string> Folders = new List<string>();        // 将删除的目录
            public List<string> Files = new List<string>();          // 将删除的快捷方式等文件
            public List<string> RegistryKeys = new List<string>();   // 将删除的注册表键（含主卸载键）
        }

        // ----------------------------------------------------------------
        // 计划阶段（全部只读，供用户确认）
        // ----------------------------------------------------------------

        /// <summary>生成强制卸载计划：列出将结束的进程 / 删除的目录与注册表键。</summary>
        public static Plan BuildPlan(ProgramEntry e)
        {
            Plan p = new Plan();
            p.Entry = e;
            if (e == null) return p;

            // ① 进程：安装目录下的可执行文件若在运行，一并列出
            try
            {
                if (e.HasLocation)
                {
                    string root = Path.GetFullPath(e.InstallLocation).TrimEnd('\\') + "\\";
                    foreach (Process proc in Process.GetProcesses())
                    {
                        try
                        {
                            string path = proc.MainModule == null ? null : proc.MainModule.FileName;
                            if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            {
                                p.Processes.Add(proc.ProcessName + " (PID " + proc.Id + ")");
                            }
                        }
                        catch { } // 系统进程无权限读路径，跳过
                        finally { try { proc.Dispose(); } catch { } }
                    }
                }
            }
            catch { }

            // ② 目录：安装目录 + 常见残留位置
            if (e.HasLocation && Directory.Exists(e.InstallLocation))
                TryAddFolder(p.Folders, e.InstallLocation);

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string[] candidates = new string[]
            {
                Path.Combine(local, "Programs", e.Name),
                Path.Combine(local, e.Name),
                Path.Combine(roaming, e.Name),
                Path.Combine(programData, e.Name)
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                // 只收窄匹配到"同名目录"，避免误删（如 Name 过短）
                if (e.Name.Length >= 3) TryAddFolder(p.Folders, candidates[i]);
            }

            // ③ 快捷方式：用户开始菜单 + 公共开始菜单
            TryAddShortcut(p.Files, Environment.SpecialFolder.Programs, e.Name);
            TryAddShortcut(p.Files, Environment.SpecialFolder.CommonPrograms, e.Name);

            // ④ 注册表：主卸载键 + 同名软件键 + 同发布者的同名子键
            //    同名/同发布者软件键要求键确实存在且"确属该程序"（DisplayName 同名或 InstallLocation 指向它），
            //    避免误删 HKCU\Software\Microsoft、HKLM\SOFTWARE\Intel 这类庞大且无关节点。
            if (!string.IsNullOrEmpty(e.KeyPath)) p.RegistryKeys.Add(e.KeyPath);
            foreach (string k in ScanUninstallLeftovers(e)) TryAddReg(p.RegistryKeys, k);
            TryAddRegIfOwned(p.RegistryKeys, @"HKCU\Software\" + e.Name, e);
            TryAddRegIfOwned(p.RegistryKeys, @"HKLM\SOFTWARE\" + e.Name, e);
            if (!string.IsNullOrEmpty(e.Publisher) && e.Publisher.Length >= 3)
            {
                TryAddRegIfOwned(p.RegistryKeys, @"HKCU\Software\" + e.Publisher, e);
                TryAddRegIfOwned(p.RegistryKeys, @"HKLM\SOFTWARE\" + e.Publisher, e);
            }

            return p;
        }

        private static void TryAddFolder(List<string> list, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (Directory.Exists(path) && !list.Contains(path)) list.Add(path);
            }
            catch { }
        }

        private static void TryAddShortcut(List<string> list, Environment.SpecialFolder folder, string name)
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                // 逐目录 try/catch 的安全遍历（实现统一在 DirWalk）：
                // SearchOption.AllDirectories 遇到无权限子目录会整体抛异常，
                // 导致一个快捷方式都找不到
                List<string> lnks = DirWalk.ListFiles(dir, "*" + name + "*.lnk");
                for (int i = 0; i < lnks.Count && list.Count < 20; i++)
                    if (!list.Contains(lnks[i])) list.Add(lnks[i]);
            }
            catch { }
        }

        private static void TryAddReg(List<string> list, string hiveKeyPath)
        {
            try
            {
                if (string.IsNullOrEmpty(hiveKeyPath) || list.Contains(hiveKeyPath)) return;
                RegistryHive hive;
                string sub;
                if (!SplitHive(hiveKeyPath, out hive, out sub)) return;
                using (RegistryKey k = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(sub, false))
                {
                    if (k != null) list.Add(hiveKeyPath);
                }
            }
            catch { }
        }

        /// <summary>扫描注册表里与本程序同名/同发布者/同安装目录的其他卸载键（残留句柄）。</summary>
        public static List<string> ScanUninstallLeftovers(ProgramEntry e)
        {
            List<string> hits = new List<string>();
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };

            for (int h = 0; h < hives.Length; h++)
            {
                for (int r = 0; r < roots.Length; r++)
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hives[h], RegistryView.Default)
                            .OpenSubKey(roots[r], false))
                        {
                            if (root == null) continue;
                            string[] subs = root.GetSubKeyNames();
                            for (int i = 0; i < subs.Length; i++)
                            {
                                using (RegistryKey k = root.OpenSubKey(subs[i], false))
                                {
                                    if (k == null) continue;
                                    string name = Convert.ToString(k.GetValue("DisplayName"));
                                    if (!string.Equals(name, e.Name, StringComparison.OrdinalIgnoreCase)) continue;
                                    string path = (hives[h] == RegistryHive.CurrentUser ? "HKCU\\" : "HKLM\\")
                                        + roots[r] + "\\" + subs[i];
                                    if (!hits.Contains(path)) hits.Add(path);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return hits;
        }

        /// <summary>同名/同发布者软件键的谨慎判定：键必须真实存在，且能确认它"属于该程序"才加入删除计划，
        /// 避免误删 HKCU\Software\Microsoft、HKLM\SOFTWARE\Intel 这类庞大且无关节点。</summary>
        private static void TryAddRegIfOwned(List<string> list, string hiveKeyPath, ProgramEntry e)
        {
            try
            {
                if (string.IsNullOrEmpty(hiveKeyPath) || list.Contains(hiveKeyPath)) return;
                if (e == null || e.Name == null || e.Name.Length < 3) return;
                RegistryHive hive;
                string sub;
                if (!SplitHive(hiveKeyPath, out hive, out sub)) return;
                using (RegistryKey k = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(sub, false))
                {
                    if (k == null) return;
                    bool owns = false;
                    object dn = k.GetValue("DisplayName");
                    if (dn != null && string.Equals(dn.ToString(), e.Name, StringComparison.OrdinalIgnoreCase)) owns = true;
                    if (!owns && e.HasLocation && !string.IsNullOrEmpty(e.InstallLocation))
                    {
                        object il = k.GetValue("InstallLocation");
                        if (il != null)
                        {
                            string ilStr = il.ToString().TrimEnd('\\');
                            string loc = e.InstallLocation.TrimEnd('\\');
                            if (ilStr.Equals(loc, StringComparison.OrdinalIgnoreCase) ||
                                ilStr.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0) owns = true;
                        }
                    }
                    if (owns) list.Add(hiveKeyPath);
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------
        // 执行阶段
        // ----------------------------------------------------------------

        /// <summary>执行计划：结束进程 → 删目录/快捷方式 → 删注册表键。返回动作报告；失败项记入 errors。</summary>
        public static string Execute(Plan plan, List<string> errors)
        {
            if (plan == null || plan.Entry == null) return "计划为空，未执行任何操作。";
            int killed = 0, folders = 0, files = 0, regs = 0;

            // ① 结束进程
            for (int i = 0; i < plan.Processes.Count; i++)
            {
                try
                {
                    int pid;
                    int sp = plan.Processes[i].LastIndexOf("PID ");
                    if (sp < 0) continue;
                    string num = plan.Processes[i].Substring(sp + 4).TrimEnd(')');
                    if (!int.TryParse(num, out pid)) continue;
                    Process p = Process.GetProcessById(pid);
                    p.Kill();
                    p.WaitForExit(5000);
                    killed++;
                }
                catch (Exception ex) { Add(errors, "结束进程 " + plan.Processes[i] + "：" + ex.Message); }
            }

            // ② 删除目录（先尝试，失败多为占用——提示用户重启后手动删）
            for (int i = 0; i < plan.Folders.Count; i++)
            {
                try
                {
                    Directory.Delete(plan.Folders[i], true);
                    folders++;
                    RegLog.Add("force_uninstall", "删除目录", plan.Folders[i]);
                }
                catch (Exception ex) { Add(errors, "目录 " + plan.Folders[i] + "：" + ex.Message); }
            }

            // ③ 删除快捷方式
            for (int i = 0; i < plan.Files.Count; i++)
            {
                try
                {
                    File.Delete(plan.Files[i]);
                    files++;
                    RegLog.Add("force_uninstall", "删除快捷方式", plan.Files[i]);
                }
                catch (Exception ex) { Add(errors, "文件 " + plan.Files[i] + "：" + ex.Message); }
            }

            // ④ 删除注册表键（自底向上：子键多时 DeleteSubKeyTree 直接递归）
            for (int i = 0; i < plan.RegistryKeys.Count; i++)
            {
                try
                {
                    RegistryHive hive;
                    string sub;
                    if (!SplitHive(plan.RegistryKeys[i], out hive, out sub)) continue;
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                    {
                        baseKey.DeleteSubKeyTree(sub, false);
                    }
                    regs++;
                    RegLog.Add("force_uninstall", "删除注册表键", plan.RegistryKeys[i]);
                }
                catch (Exception ex) { Add(errors, "注册表 " + plan.RegistryKeys[i] + "：" + ex.Message); }
            }

            RegLog.Add("force_uninstall", "强制卸载", plan.Entry.Name + "（进程 " + killed + " / 目录 " + folders +
                " / 快捷方式 " + files + " / 注册表 " + regs + "）");
            return "进程 " + killed + " 个、目录 " + folders + " 个、快捷方式 " + files +
                " 个、注册表键 " + regs + " 项已清理。";
        }

        private static void Add(List<string> errors, string msg)
        {
            if (errors != null && errors.Count < 30) errors.Add(msg);
        }

        private static bool SplitHive(string path, out RegistryHive hive, out string sub)
        {
            hive = RegistryHive.CurrentUser;
            sub = "";
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.LocalMachine;
                sub = path.Substring(5);
            }
            else if (path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHive.CurrentUser;
                sub = path.Substring(5);
            }
            else return false;
            return sub.Length > 0;
        }
    }
}
