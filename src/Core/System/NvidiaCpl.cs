using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>
    /// N 卡控制面板预设：把 NVIDIA 控制面板里最常用的「3D 设置」与「电源管理模式」写成注册表项，
    /// 提供「电竞竞技 / 最高画质 / 省电静音」三套预设与「还原默认」。
    /// 电源管理项位于 HKLM 显示适配器类（需管理员）；3D 全局项位于 HKCU 的 NvProfile（无需管理员）。
    /// </summary>
    public sealed class NvidiaCpl
    {
        public const string GpuClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        public const string NvProfile = @"Software\NVIDIA Corporation\Global\NvProfile\Global";

        public sealed class SettingDef
        {
            public string Name;
            public bool IsPm;        // true=HKLM 电源管理项（按 GPU 实例），false=HKCU 3D 全局项
            public string Caption;
            public string Desc;
            public Dictionary<int, string> Labels = new Dictionary<int, string>();
        }

        public sealed class Preset
        {
            public string Name;
            public string Desc;
            public Dictionary<string, int> Pm = new Dictionary<string, int>();       // 电源管理值名 -> 值
            public Dictionary<string, int> Profile = new Dictionary<string, int>();  // 3D 值名 -> 值
        }

        public static readonly List<SettingDef> Settings = new List<SettingDef>
        {
            PM("PowerMizerLevelAC", "电源管理模式（插电）", "GPU 频率策略：最高性能 / 平衡 / 最高省电",
                new int[]{1,2,3}, new string[]{"最高性能","平衡","最高省电"}),
            PM("PreferredPerfLevel", "首选性能级别", "驱动级频率倾向，与上面联动",
                new int[]{1,2,3}, new string[]{"最高性能","平衡","最高省电"}),
            PM("PowerMizerLevel", "电源管理模式（整体）", "同族的整体频率策略：部分驱动版本只认这一条，与插电/电池两档互补",
                new int[]{1,2,3}, new string[]{"最高性能","平衡","最高省电"}),
            PM("PowerMizerLevelDC", "电源管理模式（电池）", "笔记本电池档；台式机写入无效（当前值显示「默认(0)」即证明未生效）",
                new int[]{1,2,3}, new string[]{"最高性能","平衡","最高省电"}),
            PM("PowerMizerEnable", "自适应调频 (PowerMizer)", "开启＝按负载调频（凉、省电）；关闭＝偏向锁定高性能（温度上升）",
                new int[]{1,0}, new string[]{"开启（自适应）","关闭（偏向全速）"}),
            PM("PerfLevelSrc", "频率档来源策略", "社区通行的驱动键（0x2222/0x2233/0x3322）：从平衡到锁定高频；异常时用「还原默认」清除",
                new int[]{8738,8755,13090}, new string[]{"平衡 (0x2222)","游戏偏性能 (0x2233)","锁定高频 (0x3322)"}),
            HK("LowLatencyMode", "低延迟模式", "降低输入延迟，竞技首选",
                new int[]{0,1,2}, new string[]{"关闭","开启","超高"}),
            HK("VSyncMode", "垂直同步", "防撕裂但会增加输入延迟",
                new int[]{0,1,2,3}, new string[]{"关闭","开启","自适应","快"}),
            HK("MaxPreRenderedFrames", "最大预渲染帧数", "越小延迟越低，越大越平滑",
                new int[]{0,1,2,3}, new string[]{"驱动默认","1","2","3"}),
            HK("TextureFilteringQuality", "纹理过滤 - 质量", "性能与画质取舍",
                new int[]{1,2,3}, new string[]{"高性能","质量","高质量"}),
            HK("AnisotropicFiltering", "各向异性过滤", "倾斜表面纹理清晰度",
                new int[]{1,2,3,4,5}, new string[]{"关闭","2x","4x","8x","16x"}),
            HK("ShaderCache", "着色器缓存", "减少重复编译卡顿",
                new int[]{0,1}, new string[]{"关闭","开启"}),
            HK("TripleBuffer", "三重缓冲", "仅配合垂直同步使用",
                new int[]{0,1}, new string[]{"关闭","开启"}),
            HK("ThreadedOptimization", "线程优化", "让驱动用多核 CPU 工作",
                new int[]{0,1,2}, new string[]{"关闭","开启","自动"}),
        };

        public static readonly List<Preset> Presets = new List<Preset>
        {
            new Preset {
                Name = "电竞竞技",
                Desc = "最低延迟：电源最高性能、低延迟超高、关垂直同步、预渲染1帧、纹理高性能、关各向异性",
                Pm = Dict("PowerMizerLevelAC",1, "PreferredPerfLevel",1),
                Profile = Dict("LowLatencyMode",2, "VSyncMode",0, "MaxPreRenderedFrames",1,
                    "TextureFilteringQuality",1, "AnisotropicFiltering",1, "ShaderCache",1, "TripleBuffer",0, "ThreadedOptimization",1)
            },
            new Preset {
                Name = "最高画质",
                Desc = "画质优先：平衡电源、开垂直同步、高质量纹理、16x 各向异性、开三重缓冲",
                Pm = Dict("PowerMizerLevelAC",2, "PreferredPerfLevel",2),
                Profile = Dict("LowLatencyMode",0, "VSyncMode",1, "MaxPreRenderedFrames",3,
                    "TextureFilteringQuality",3, "AnisotropicFiltering",5, "ShaderCache",1, "TripleBuffer",1, "ThreadedOptimization",1)
            },
            new Preset {
                Name = "省电静音",
                Desc = "省电优先：最高省电电源、关低延迟、自适应垂直同步、纹理高性能、关各向异性",
                Pm = Dict("PowerMizerLevelAC",3, "PreferredPerfLevel",3),
                Profile = Dict("LowLatencyMode",0, "VSyncMode",2, "MaxPreRenderedFrames",0,
                    "TextureFilteringQuality",1, "AnisotropicFiltering",1, "ShaderCache",1, "TripleBuffer",0, "ThreadedOptimization",0)
            }
        };

        private static Dictionary<string, int> Dict(params object[] kv)
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = (int)kv[i + 1];
            return d;
        }

        private static SettingDef PM(string name, string caption, string desc, int[] vals, string[] labels)
        {
            SettingDef s = new SettingDef();
            s.Name = name; s.IsPm = true; s.Caption = caption; s.Desc = desc;
            for (int i = 0; i < vals.Length; i++) s.Labels[vals[i]] = labels[i];
            return s;
        }

        private static SettingDef HK(string name, string caption, string desc, int[] vals, string[] labels)
        {
            SettingDef s = new SettingDef();
            s.Name = name; s.IsPm = false; s.Caption = caption; s.Desc = desc;
            for (int i = 0; i < vals.Length; i++) s.Labels[vals[i]] = labels[i];
            return s;
        }

        public static bool IsNvidia()
        {
            try
            {
                List<GpuSpoof.GpuEntry> gpus = GpuSpoof.Detect();
                for (int i = 0; i < gpus.Count; i++)
                    if ((gpus[i].DriverDesc ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            catch { }
            return false;
        }

        public static Dictionary<string, int> ReadCurrent()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            string inst = FirstInstance();
            foreach (SettingDef s in Settings)
            {
                int v = 0;
                if (s.IsPm)
                {
                    if (inst != null) v = ReadInt(RegistryHive.LocalMachine, GpuClass + "\\" + inst, s.Name);
                }
                else
                {
                    v = ReadInt(RegistryHive.CurrentUser, NvProfile, s.Name);
                }
                d[s.Name] = v;
            }
            return d;
        }

        public static string Friendly(SettingDef s, int v)
        {
            string label;
            if (s.Labels.TryGetValue(v, out label)) return label;
            return "默认(" + v + ")";
        }

        /// <summary>
        /// 逐项写入一个值（界面"逐项可调"用）。写入路径与 Apply(Preset) 完全一致：
        /// 电源管理项写 HKLM 的所有 GPU 实例，3D 全局项写 HKCU 的 NvProfile；
        /// 写前同样挂到 nv_cpl 备份组（页面顶部的「还原默认」仍可整组恢复）。
        /// </summary>
        public static bool SetItem(SettingDef s, int value, out string error)
        {
            error = "";
            RegHelper.BeginBackup("nv_cpl");
            try
            {
                if (s.IsPm)
                {
                    int fail = 0, instances = 0;
                    using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass, false))
                    {
                        if (cls != null)
                        {
                            foreach (string sub in cls.GetSubKeyNames())
                            {
                                if (!IsInstance(sub)) continue;
                                instances++;
                                if (!RegHelper.SetValue(RegistryHive.LocalMachine, GpuClass + "\\" + sub,
                                    s.Name, value, RegistryValueKind.DWord, "nv_cpl")) fail++;
                            }
                        }
                    }
                    if (instances == 0) { error = "未找到 NVIDIA 显示适配器实例"; return false; }
                    if (fail > 0) { error = "写入失败（通常是没有管理员权限）"; return false; }
                    return true;
                }

                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(NvProfile))
                {
                    if (root == null) { error = "无法打开 3D 全局设置键"; return false; }
                    root.SetValue(s.Name, value, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>
        /// 逐项恢复驱动默认：删除我们写入的值（值不存在时驱动走默认）。
        /// 电源管理项在所有 GPU 实例上删；3D 全局项在 NvProfile 上删。
        /// 若要恢复"写入前的原值"，仍用页面顶部的「还原默认」（走 nv_cpl 备份组）。
        /// </summary>
        public static bool ResetItem(SettingDef s, out string error)
        {
            error = "";
            try
            {
                if (s.IsPm)
                {
                    using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass, true))
                    {
                        if (cls != null)
                        {
                            foreach (string sub in cls.GetSubKeyNames())
                            {
                                if (!IsInstance(sub)) continue;
                                using (RegistryKey k = cls.OpenSubKey(sub, true))
                                {
                                    if (k != null) k.DeleteValue(s.Name, false);
                                }
                            }
                        }
                    }
                    return true;
                }

                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(NvProfile, true))
                {
                    if (root != null) root.DeleteValue(s.Name, false);
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool Apply(Preset p, out string error)
        {
            error = "";
            RegHelper.BeginBackup("nv_cpl");
            try
            {
                // 电源管理：遍历所有 GPU 实例写 HKLM（需管理员）
                // 注意：写入结果必须累计——此前丢弃返回值，HKLM 写不进去（权限/键不存在）
                // 也会返回 true，界面照样弹"已应用"，用户以为生效了。
                int ok = 0, fail = 0;
                int gpuInstances = 0;
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass, false))
                {
                    if (cls != null)
                    {
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (!IsInstance(sub)) continue;
                            gpuInstances++;
                            string path = GpuClass + "\\" + sub;
                            foreach (var kv in p.Pm)
                            {
                                if (RegHelper.SetValue(RegistryHive.LocalMachine, path, kv.Key, kv.Value, RegistryValueKind.DWord, "nv_cpl")) ok++;
                                else fail++;
                            }
                        }
                    }
                }
                // 3D 全局：写 HKCU（无需管理员）
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(NvProfile))
                {
                    if (root == null)
                    {
                        error = "无法打开 3D 全局设置键（HKCU\\" + NvProfile + "）";
                        return false;
                    }
                    foreach (var kv in p.Profile)
                        root.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord);
                }

                if (gpuInstances == 0)
                {
                    error = "未找到 NVIDIA 显示适配器实例，电源管理项未写入（3D 设置已写入）";
                    return false;
                }
                if (fail > 0)
                {
                    error = "部分项写入失败：" + ok + " 项成功 / " + fail + " 项失败"
                        + (ok == 0 ? "（通常是没有管理员权限）" : "");
                    return false;
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool Restore(out string error)
        {
            error = "";
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass, true))
                {
                    if (cls != null)
                    {
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (!IsInstance(sub)) continue;
                            using (RegistryKey k = cls.OpenSubKey(sub, true))
                            {
                                if (k == null) continue;
                                foreach (SettingDef s in Settings)
                                    if (s.IsPm && ValueExists(k, s.Name)) k.DeleteValue(s.Name, false);
                            }
                        }
                    }
                }
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(NvProfile, true))
                {
                    if (root != null)
                        foreach (SettingDef s in Settings)
                            if (!s.IsPm && ValueExists(root, s.Name)) root.DeleteValue(s.Name, false);
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static string FirstInstance()
        {
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(GpuClass))
                {
                    if (cls == null) return null;
                    foreach (string sub in cls.GetSubKeyNames())
                        if (IsInstance(sub)) return sub;
                }
            }
            catch { }
            return null;
        }

        private static int ReadInt(RegistryHive hive, string path, string name)
        {
            object o = RegHelper.GetValue(hive, path, name);
            if (o == null) return 0;
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private static bool ValueExists(RegistryKey k, string name)
        {
            try { return k.GetValue(name) != null; } catch { return false; }
        }

        private static bool IsInstance(string name)
        {
            if (name.Length != 4) return false;
            foreach (char c in name) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
