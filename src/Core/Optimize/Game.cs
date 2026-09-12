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

    /// <summary>
    /// 游戏进程高优先级（IFEO）：为选定的游戏 exe 写入 PerfOptions
    /// CpuPriorityClass=3（高）+ IoPriority=3，系统启动该游戏时自动提权。
    /// 黑白包「永劫无间优先级」方案的通用化。
    /// </summary>
    public sealed class GamePriorityTweak : ITweak
    {
        private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        public string Id { get { return "game_priority_ifeo"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "游戏进程高优先级 (IFEO)"; } }
        public string Description
        {
            get { return "选择游戏 exe，通过映像劫持选项让系统启动该游戏时自动赋予高 CPU 优先级与高 IO 优先级，后台任务不再抢占游戏时间片。选错程序也无妨，还原即可移除。"; }
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
            "$k = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\' + $n + '\\PerfOptions'\r\n" +
            "New-Item -Path $k -Force | Out-Null\r\n" +
            "Set-ItemProperty -Path $k -Name 'CpuPriorityClass' -Value 3 -Type DWord\r\n" +
            "Set-ItemProperty -Path $k -Name 'IoPriority' -Value 3 -Type DWord\r\n";

        private const string RevertScript =
            "$root = 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options'\r\n" +
            "if (Test-Path $root) { Get-ChildItem $root | Where-Object { Test-Path ($_.PSPath + '\\PerfOptions') } | ForEach-Object { $p = Get-ItemProperty ($_.PSPath + '\\PerfOptions'); if ($p.CpuPriorityClass -eq 3 -and $p.IoPriority -eq 3) { Remove-Item ($_.PSPath + '\\PerfOptions') -Force } } }\r\n";

        private static bool RunScript(string script)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "guyuebox_gameprio.ps1");
                System.IO.File.WriteAllText(path, script, new System.Text.UTF8Encoding(true));
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
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(IfeoRoot))
                {
                    if (root == null) return false;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (!sub.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        using (RegistryKey k = root.OpenSubKey(sub + "\\PerfOptions"))
                        {
                            if (k == null) continue;
                            object cpu = k.GetValue("CpuPriorityClass");
                            object io = k.GetValue("IoPriority");
                            if (cpu != null && Convert.ToInt32(cpu) == 3) return true;
                            if (io != null && Convert.ToInt32(io) == 3) return true;
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
    /// 设备类中断优先级：按类 GUID 设置驱动 ISR/DPC 的 BasePriority / OverTargetPriority，
    /// 网络 / 键鼠最高、显卡次之、存储音频再次（与游戏系统包的分级表一致）。

    public sealed class DevicePriorityTweak : ITweak
    {
        private sealed class Entry
        {
            public string Guid;
            public int BasePriority;
            public int OverTarget; // -1 = 删除该值（用系统默认）

            public Entry(string guid, int basePriority, int overTarget)
            {
                Guid = guid;
                BasePriority = basePriority;
                OverTarget = overTarget;
            }
        }

        private static readonly Entry[] Entries = new Entry[]
        {
            new Entry("4d36e972-e325-11ce-bfc1-08002be10318", 200, -1),  // 网络适配器
            new Entry("4d36e96b-e325-11ce-bfc1-08002be10318", 200, 64),  // 键盘
            new Entry("4d36e96f-e325-11ce-bfc1-08002be10318", 200, 80),  // 鼠标
            new Entry("745a17a0-74d3-11d0-b6fe-00a0c90f57da", 200, -1),  // 人体学输入设备
            new Entry("4d36e968-e325-11ce-bfc1-08002be10318", 255, 96),  // 显示适配器
            new Entry("50127dc3-0f36-415e-a6cc-4cb3be910b65", 28, -1),   // 处理器
            new Entry("4d36e97d-e325-11ce-bfc1-08002be10318", 28, -1),   // 系统设备
            new Entry("4d36e967-e325-11ce-bfc1-08002be10318", 22, -1),   // 磁盘驱动器
            new Entry("4d36e97b-e325-11ce-bfc1-08002be10318", 22, -1),   // 存储控制器
            new Entry("4d36e96c-e325-11ce-bfc1-08002be10318", 18, -1),   // 声音视频游戏控制器
            new Entry("4d36e973-e325-11ce-bfc1-08002be10318", 18, -1),   // 媒体类
        };

        private const string ClassRoot = @"SYSTEM\CurrentControlSet\Control\Class";

        public string Id { get { return "device_priority"; } }
        public string Group { get { return TweakLibrary.GGame; } }
        public string Name { get { return "设备中断优先级分级（网/键鼠最高）"; } }
        public string Description
        {
            get { return "按设备类别设置驱动中断优先级：网络与键鼠最高、显卡次之、存储再次，让输入与网络中断永远优先被 CPU 处理。需重启生效。"; }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return false; } }
        public bool Recommended { get { return false; } }

        private static string PathOf(Entry e)
        {
            return ClassRoot + "\\{" + e.Guid + "}";
        }

        private static bool BaseOk(object v, int target)
        {
            try { return v != null && Convert.ToInt32(v) == target; }
            catch { return false; }
        }

        public bool IsApplied()
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry e = Entries[i];
                object baseV = RegHelper.GetValue(RegistryHive.LocalMachine, PathOf(e), "BasePriority");
                if (!BaseOk(baseV, e.BasePriority)) return false;
                object overV = RegHelper.GetValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority");
                if (e.OverTarget < 0)
                {
                    if (overV != null) return false;
                }
                else
                {
                    if (!BaseOk(overV, e.OverTarget)) return false;
                }
            }
            return true;
        }

        public bool Apply()
        {
            RegHelper.BeginBackup(Id);
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry e = Entries[i];
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, PathOf(e), "BasePriority",
                        e.BasePriority, RegistryValueKind.DWord, Id);
                    if (e.OverTarget < 0)
                    {
                        RegHelper.DeleteValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority", Id);
                    }
                    else
                    {
                        RegHelper.SetValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority",
                            e.OverTarget, RegistryValueKind.DWord, Id);
                    }
                }
                catch
                {
                }
            }

            // 显卡实例子键（0000-0009）：与类键同步拉高
            const string GpuClass = ClassRoot + "\\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i,
                        "BasePriority", 255, RegistryValueKind.DWord, Id);
                    RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\000" + i,
                        "OverTargetPriority", 96, RegistryValueKind.DWord, Id);
                }
                catch
                {
                }
            }

            // 显卡链路（Control\Video\{GUID}\000x，GUID 每台机器不同，动态枚举）
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Video", false))
                {
                    if (root != null)
                    {
                        string[] guids = root.GetSubKeyNames();
                        for (int g = 0; g < guids.Length; g++)
                        {
                            using (RegistryKey dev = Registry.LocalMachine.OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g], false))
                            {
                                if (dev == null) continue;
                                string[] subs = dev.GetSubKeyNames();
                                for (int s = 0; s < subs.Length; s++)
                                {
                                    if (subs[s].Length != 4 || !char.IsDigit(subs[s][0])) continue;
                                    try
                                    {
                                        RegHelper.SetValue(RegistryHive.LocalMachine,
                                            @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g] + "\\" + subs[s],
                                            "BasePriority", 255, RegistryValueKind.DWord, Id);
                                        RegHelper.SetValue(RegistryHive.LocalMachine,
                                            @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g] + "\\" + subs[s],
                                            "OverTargetPriority", 96, RegistryValueKind.DWord, Id);
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            return true;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }
}
