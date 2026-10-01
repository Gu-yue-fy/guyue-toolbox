using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GuyueBox.Core
{
    /// <summary>一条高级电源设置。</summary>
    public sealed class PowerSetting
    {
        /// <summary>稳定标识（界面与厂商预设按它取值）。</summary>
        public string Key = "";
        public string Name = "";
        /// <summary>所属子组 GUID（目前均为处理器电源管理）。</summary>
        public string SubGroup = PowerTuning.SubProcessor;
        public string Guid = "";
        /// <summary>true = 0~Max 范围型；false = 枚举型（取值即 Options 下标）。</summary>
        public bool IsRange;
        public int Max;
        public string Unit = "";
        /// <summary>枚举型选项名（下标即写入值）。</summary>
        public string[] Options;
        /// <summary>AMD / Intel 平台的推荐值（厂商专用计划用）。</summary>
        public int AmdValue;
        public int IntelValue;
        /// <summary>一句话说明（界面提示用）。</summary>
        public string Tip = "";
    }

    public static class PowerTuning
    {
        /// <summary>处理器电源管理子组。</summary>
        public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";

        // 以下子组 GUID 逐一对照本机 powercfg /q 输出核实（2026-10）；USB 子组本机不存在，未收录
        public const string SubDisk = "0012ee47-9041-4b5d-9b77-535fba8b1442";
        public const string SubPciExpress = "501a4d13-42af-4429-9fd1-a8218c268e20";
        public const string SubSleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        public const string SubVideo = "7516b95f-f776-4464-8c53-06167f40cc99";
        public const string SubMultimedia = "9596fb07-7961-4b85-bb83-fac98295962e";
        public const string SubBattery = "e73a048d-bf27-4f12-9731-8b2076e8891f";

        /// <summary>厂商专用计划的显示名。</summary>
        public static string VendorPlanName(bool amd)
        {
            return amd ? "古月 · AMD 游戏" : "古月 · Intel 游戏";
        }

        // ---------------- 设置定义 ----------------

        private static readonly List<PowerSetting> _settings = BuildSettings();

        public static List<PowerSetting> Settings()
        {
            return _settings;
        }

        public static PowerSetting ByKey(string key)
        {
            for (int i = 0; i < _settings.Count; i++)
            {
                if (_settings[i].Key == key) return _settings[i];
            }
            return null;
        }

        private static List<PowerSetting> BuildSettings()
        {
            List<PowerSetting> list = new List<PowerSetting>();

            list.Add(New("min_state", "最小处理器状态", "893dee8e-2bef-41e0-89c6-b55d0929964c",
                true, 100, "%", null, 5, 5,
                "空闲时的最低频率档。调低＝空闲更凉更省电、睿频空间更大；调高＝响应更快但温度与功耗上升。"));

            list.Add(New("max_state", "最大处理器状态", "bc5038f7-23e0-4960-96da-33abaf5935ec",
                true, 100, "%", null, 100, 100,
                "性能上限。只有 100% 才能跑到最高频率；调低可强制降温（笔记本降噪也常用这招）。"));

            list.Add(New("boost", "处理器性能提升模式", "be337238-0d82-4146-a960-4f3749d470c7",
                false, 0, "", new string[]
                {
                    "已禁用", "已启用", "高性能", "高效率", "高性能高效率", "积极且有保障", "高效、积极且有保障"
                }, 2, 1,
                "睿频策略。AMD 平台选「高性能」收益明显；Intel 平台「已启用」更稳（更激进的档位可能带来温度与功耗抖动）。"));

            list.Add(New("epp", "能效偏好 (EPP)", "36687f9e-e3a5-4dbf-b1dc-15eb381c6863",
                true, 100, "", null, 0, 0,
                "0 = 完全偏性能，100 = 完全偏省电。需要 BIOS 打开 CPPC / 硬件 P-States 才真正生效。"));

            list.Add(New("core_park", "最少核心数（核心停放）", "0cc5b647-c1df-4637-891a-dec35c318583",
                true, 100, "%", null, 100, 50,
                "允许被停放（休眠）的核心比例上限。调高＝更多核心常驻，减少唤醒抖动；调低＝更省电。"));

            list.Add(New("cool", "系统散热方式", "94D3A615-A899-4AC5-AE2B-E4D8F634367F",
                false, 0, "", new string[] { "被动", "主动" }, 1, 1,
                "主动＝优先靠风扇散热以维持性能；被动＝优先降频来减少噪音。"));

            list.Add(New("idle_disable", "处理器闲置禁用", "5d76a2ca-e8c0-402f-a133-2158492d58ad",
                false, 0, "", new string[] { "启用闲置", "禁用闲置" }, 0, 0,
                "「禁用闲置」会阻止核心进入深度闲置状态，延迟更低但更耗电。"));

            list.Add(New("throttle", "允许节流状态", "3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb",
                false, 0, "", new string[] { "关闭", "启用", "自动" }, 2, 2,
                "是否允许系统对处理器节流（过热/超规格时降频保护）。一般保持「自动」。"));

            // ---------------- 其它子组（GUID 逐一对照本机 powercfg /q 核实过，勿凭资料手写） ----------------

            // 硬盘子组
            list.Add(New("ahci_link_pm", "AHCI 链路电源管理 (HIPM/DIPM)", "0b2d69d7-a2a1-449c-9680-f91c70521c60",
                false, 0, "", new string[] { "Active", "HIPM", "HIPM+DIPM", "DIPM", "Lowest" }, 0, 0,
                "SATA 链路省电策略（档位名即驱动实际枚举值）。机械盘/老固态建议 Active（交互更跟手）。", SubDisk));
            list.Add(New("nvme_idle", "NVMe 空闲超时", "d639518a-e56d-4345-8af2-b9f32fb26109",
                true, 60000, "ms", null, 0, 0,
                "NVMe 盘进入低功耗前的空闲等待。调大＝更跟手、待机功耗略高；调小＝更省电但唤醒有延迟。", SubDisk));

            // PCI Express
            list.Add(New("pcie_aspm", "PCIe 链路状态电源管理", "ee12f906-d277-404b-b6da-e5fa1a576df5",
                false, 0, "", new string[] { "关闭", "适度省电", "最大省电" }, 0, 0,
                "关掉可消除网卡/显卡的链路唤醒延迟（DPC 抖动的常见来源）；笔记本求续航可选省电档。", SubPciExpress));

            // 睡眠
            list.Add(New("wake_timers", "允许使用唤醒定时器", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d",
                false, 0, "", new string[] { "已禁用", "已启用", "仅重要" }, 0, 0,
                "睡眠中被计划任务/自动维护唤醒的开关。睡眠总被莫名其妙唤醒就先关掉它。", SubSleep));
            list.Add(New("hybrid_sleep", "允许混合睡眠", "94ac6d29-73ce-41a6-809f-6363ba21b47e",
                false, 0, "", new string[] { "关", "开" }, 0, 0,
                "睡眠时同时写入休眠文件，断电也不丢会话（台式机建议开；部分机型无此项会自动隐藏）。", SubSleep));
            list.Add(New("unattend_sleep", "无人参与睡眠超时", "7bc4a2f9-d8fc-4469-b07b-33eb785aaca0",
                true, 86400, "秒", null, 0, 0,
                "系统被唤醒但无人操作后再睡回去的等待。过短会出现「唤醒后马上又睡回去」。", SubSleep));

            // 显示
            list.Add(New("adapt_brightness", "启用自适应亮度", "fbd9aa66-9553-4097-ba44-ed6e9d65eab8",
                false, 0, "", new string[] { "关", "开" }, 0, 0,
                "环境光自动调亮度。播放/看内容时忽明忽暗就关掉。", SubVideo));

            // 多媒体（本机为隐藏子组：探测不到的项会自动隐藏）
            list.Add(New("video_quality", "视频播放质量偏好", "5c5bb349-ad29-4c90-bba5-e18e1fcc4be8",
                false, 0, "", new string[] { "节能优先", "均衡", "最佳质量" }, 1, 1,
                "播放视频时的画质与功耗权衡。", SubMultimedia));

            // 电池（台式机没有电池子组，这些项会自动隐藏）
            list.Add(New("battery_low_level", "电池电量水平低", "8183ba9a-e910-48da-8769-14ae6dc1170a",
                true, 100, "%", null, 10, 10, "低于该百分比触发低电量通知与操作。", SubBattery));
            list.Add(New("battery_critical_level", "关键电池电量水平", "9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469",
                true, 100, "%", null, 5, 5, "低于该百分比触发关键电池操作。", SubBattery));
            list.Add(New("battery_reserve", "保留电池电量", "f3c5027d-cd16-4930-aa6b-90db844a8f00",
                true, 100, "%", null, 0, 0, "为关键操作预留的电量比例。", SubBattery));
            list.Add(New("battery_critical_action", "关键级别电池操作", "637ea02f-bbcb-4015-8e2c-a1c7b9c0b546",
                false, 0, "", new string[] { "不采取操作", "睡眠", "休眠", "关机" }, 2, 2, "电量到达临界值时的动作。", SubBattery));

            return list;
        }

        private static PowerSetting New(string key, string name, string guid, bool range, int max,
            string unit, string[] options, int amd, int intel, string tip, string sub = null)
        {
            PowerSetting s = new PowerSetting();
            s.Key = key;
            s.Name = name;
            s.SubGroup = string.IsNullOrEmpty(sub) ? SubProcessor : sub;
            s.Guid = guid;
            s.IsRange = range;
            s.Max = max;
            s.Unit = unit;
            s.Options = options;
            s.AmdValue = amd;
            s.IntelValue = intel;
            s.Tip = tip;
            return s;
        }

        /// <summary>把值渲染成可读文本（枚举型给选项名，范围型给数字 + 单位）。</summary>
        public static string Describe(PowerSetting s, int value)
        {
            if (s == null) return "";
            if (!s.IsRange)
            {
                if (s.Options != null && value >= 0 && value < s.Options.Length) return s.Options[value];
                return "未知(" + value + ")";
            }
            return value + s.Unit;
        }

        // ---------------- 基础调用 ----------------

        private static Shell.Result Run(string args, bool change)
        {
            return Shell.Run("powercfg.exe", args, 30000, isChange: change);
        }

        private static readonly Regex AcRx = new Regex(
            @"(?:交流|AC)[^:：]*[:：]\s*0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase);
        private static readonly Regex DcRx = new Regex(
            @"(?:直流|DC)[^:：]*[:：]\s*0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// 解除全部设置的隐藏（幂等，整个会话只做一次）。
        ///
        /// Windows 客户端把大多数处理器参数标记成 ATTRIB_HIDE：这种项在
        /// `powercfg /query <子组>` 的输出里**根本不出现**，`/query` 单独指定它也只回计划名。
        /// 不先解除隐藏，读取会一律得到"本机不支持"（实测踩过这个坑），写入也可能被忽略。
        /// 解除后这些项在系统「电源选项 → 高级设置」里也会变得可见 —— 这正是本功能的目的，
        /// 界面上已向用户说明。
        /// </summary>
        private static bool _revealed;

        public static void EnsureRevealed()
        {
            if (_revealed) return;
            _revealed = true;
            for (int i = 0; i < _settings.Count; i++)
            {
                string ignore;
                SetHidden(_settings[i], false, out ignore);
            }
        }

        /// <summary>
        /// 读取某计划下该设置的 AC / DC 值。返回 false 表示**本机确实没有这一项**
        /// （平台不支持；已排除"被隐藏"这种情形，见 EnsureRevealed）。
        /// </summary>
        public static bool TryRead(string planGuid, PowerSetting s, out int ac, out int dc)
        {
            ac = 0;
            dc = 0;
            if (s == null || string.IsNullOrEmpty(planGuid)) return false;
            EnsureRevealed();

            Shell.Result r = Run("/query " + planGuid + " " + s.SubGroup + " " + s.Guid, false);
            string text = r.All ?? "";
            Match ma = AcRx.Match(text);
            Match md = DcRx.Match(text);
            if (!ma.Success || !md.Success) return false;

            ac = Convert.ToInt32(ma.Groups[1].Value, 16);
            dc = Convert.ToInt32(md.Groups[1].Value, 16);
            return true;
        }

        /// <summary>
        /// 写入 AC / DC 值。写前先解除隐藏：被标记 ATTRIB_HIDE 的设置，直接写入可能被忽略。
        /// </summary>
        public static bool Set(string planGuid, PowerSetting s, int ac, int dc, out string error)
        {
            error = "";
            if (s == null || string.IsNullOrEmpty(planGuid)) { error = "参数无效。"; return false; }

            string ignore;
            SetHidden(s, false, out ignore);   // 解除隐藏（幂等；失败也继续尝试写入）

            Shell.Result a = Run("/setacvalueindex " + planGuid + " " + s.SubGroup + " " + s.Guid + " " + ac, true);
            if (!a.Ok) { error = "AC 写入失败：" + a.All.Trim(); return false; }

            Shell.Result d = Run("/setdcvalueindex " + planGuid + " " + s.SubGroup + " " + s.Guid + " " + dc, true);
            if (!d.Ok) { error = "DC 写入失败：" + d.All.Trim(); return false; }

            // 立即生效：改完不重新激活计划，部分设置要等下一次切换才应用
            Run("/setactive " + planGuid, true);
            return true;
        }

        /// <summary>隐藏 / 解除隐藏该设置（影响系统电源选项里是否可见），需要管理员权限。</summary>
        public static bool SetHidden(PowerSetting s, bool hidden, out string error)
        {
            error = "";
            if (s == null) { error = "参数无效。"; return false; }
            Shell.Result r = Run("-attributes " + s.SubGroup + " " + s.Guid +
                (hidden ? " +ATTRIB_HIDE" : " -ATTRIB_HIDE"), true);
            if (!r.Ok) { error = r.All.Trim(); return false; }
            return true;
        }

        /// <summary>复制一个电源计划并改名，输出新 GUID。</summary>
        public static bool Duplicate(string srcGuid, string newName, out string newGuid, out string error)
        {
            newGuid = "";
            error = "";
            Shell.Result dup = Run("/duplicatescheme " + srcGuid, true);
            if (!dup.Ok)
            {
                error = "复制计划失败：" + dup.All.Trim();
                return false;
            }

            Match m = Regex.Match(dup.All ?? "", @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");
            if (!m.Success)
            {
                error = "复制计划成功但没解析出新 GUID：" + dup.All.Trim();
                return false;
            }
            newGuid = m.Value;

            Shell.Result ren = Run("/changename " + newGuid + " \"" + newName + "\"", true);
            if (!ren.Ok)
            {
                error = "计划已创建但改名失败：" + ren.All.Trim();
                return false;
            }
            return true;
        }

        /// <summary>删除一个电源计划（内置计划删不掉时会把原因返回）。</summary>
        public static bool Remove(string planGuid, out string error)
        {
            error = "";
            Shell.Result r = Run("/delete " + planGuid, true);
            if (!r.Ok) { error = r.All.Trim(); return false; }
            return true;
        }

        /// <summary>本机上确实存在的设置（按 TryRead 探测，供界面只显示可用项）。</summary>
        public static List<PowerSetting> AvailableSettings(string planGuid, out List<string> acValues,
            out List<string> dcValues)
        {
            List<PowerSetting> list = new List<PowerSetting>();
            acValues = new List<string>();
            dcValues = new List<string>();
            EnsureRevealed();
            for (int i = 0; i < _settings.Count; i++)
            {
                int ac, dc;
                if (!TryRead(planGuid, _settings[i], out ac, out dc)) continue;
                list.Add(_settings[i]);
                acValues.Add(Describe(_settings[i], ac));
                dcValues.Add(Describe(_settings[i], dc));
            }
            return list;
        }

        /// <summary>
        /// 一键创建厂商专用计划：挑模板 → 复制改名 → 按厂商推荐值套参数 → 激活。
        /// report 里逐项列出结果（含"本机不支持，已跳过"），不隐瞒失败。
        /// </summary>
        public static bool CreateVendorPlan(bool amd, out string planGuid, out string report)
        {
            planGuid = "";
            StringBuilder sb = new StringBuilder();
            EnsureRevealed();

            // 模板计划：优先标准「高性能」；Win11 新版本可能已把它移除，此时退回当前激活计划
            string src = "";
            List<PowerPlan> plans = PowerPlans.List();
            for (int i = 0; i < plans.Count; i++)
            {
                if (string.Equals(plans[i].Guid, PowerPlans.SchemeHighPerformance,
                    StringComparison.OrdinalIgnoreCase)) { src = plans[i].Guid; break; }
            }
            string srcName = "高性能";
            if (src.Length == 0)
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].Active) continue;
                    src = plans[i].Guid;
                    srcName = plans[i].Name;
                    break;
                }
            }
            if (src.Length == 0)
            {
                report = "未找到可用作模板的电源计划（系统里连当前激活计划都读不到）。";
                return false;
            }
            sb.AppendLine("模板计划：" + srcName);
            sb.AppendLine();

            string name = VendorPlanName(amd);
            string guid, err;
            if (!Duplicate(src, name, out guid, out err))
            {
                report = sb.ToString() + "创建失败：" + err;
                return false;
            }
            planGuid = guid;
            sb.AppendLine("已创建：" + name + "（" + guid + "）");
            sb.AppendLine();

            int okCount = 0, skipCount = 0;
            for (int i = 0; i < _settings.Count; i++)
            {
                PowerSetting s = _settings[i];
                int ac, dc;
                if (!TryRead(guid, s, out ac, out dc))
                {
                    sb.AppendLine("· " + s.Name + "：本机不支持该项，已跳过");
                    skipCount++;
                    continue;
                }

                int want = amd ? s.AmdValue : s.IntelValue;
                string e2;
                if (Set(guid, s, want, want, out e2))
                {
                    sb.AppendLine("· " + s.Name + " → " + Describe(s, want));
                    okCount++;
                }
                else
                {
                    sb.AppendLine("· " + s.Name + "：设置失败（" + e2 + "）");
                }
            }

            string e3;
            if (PowerPlans.SetActive(guid, out e3))
            {
                sb.AppendLine();
                sb.AppendLine("已切换为当前使用的计划。");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("计划已创建但激活失败：" + e3);
            }

            sb.AppendLine();
            sb.AppendLine("成功 " + okCount + " 项，跳过 " + skipCount + " 项。");
            report = sb.ToString();
            return true;
        }
    }
}
