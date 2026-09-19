using System;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================

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
            bool ok = true;

            // 类键（Control\Class\{Guid}）：只写已存在的类键，避免无驱动空类键
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry e = Entries[i];
                try
                {
                    if (!RegHelper.SetValueOnExisting(RegistryHive.LocalMachine, PathOf(e), "BasePriority",
                        e.BasePriority, RegistryValueKind.DWord, Id)) ok = false;
                    if (e.OverTarget < 0)
                    {
                        RegHelper.DeleteValue(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority", Id);
                    }
                    else
                    {
                        if (!RegHelper.SetValueOnExisting(RegistryHive.LocalMachine, PathOf(e), "OverTargetPriority",
                            e.OverTarget, RegistryValueKind.DWord, Id)) ok = false;
                    }
                }
                catch
                {
                    ok = false;
                }
            }

            // 显卡实例子键（0000-0009）：只写已存在的实例——单显卡机器绝不凭空制造 0001-0009 幽灵键
            const string GpuClass = ClassRoot + "\\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 10; i++)
            {
                string inst = GpuClass + "\\000" + i;
                try
                {
                    if (!RegHelper.SetValueOnExisting(RegistryHive.LocalMachine, inst, "BasePriority",
                        255, RegistryValueKind.DWord, Id)) ok = false;
                    if (!RegHelper.SetValueOnExisting(RegistryHive.LocalMachine, inst, "OverTargetPriority",
                        96, RegistryValueKind.DWord, Id)) ok = false;
                }
                catch
                {
                    ok = false;
                }
            }

            // 显卡链路（Control\Video\{GUID}\000x，GUID 每台机器不同，动态枚举已存在子键）
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
                                    string path = @"SYSTEM\CurrentControlSet\Control\Video\" + guids[g] + "\\" + subs[s];
                                    try
                                    {
                                        if (!RegHelper.SetValue(RegistryHive.LocalMachine, path, "BasePriority",
                                            255, RegistryValueKind.DWord, Id)) ok = false;
                                        if (!RegHelper.SetValue(RegistryHive.LocalMachine, path, "OverTargetPriority",
                                            96, RegistryValueKind.DWord, Id)) ok = false;
                                    }
                                    catch
                                    {
                                        ok = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                ok = false;
            }
            return ok;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }
}
