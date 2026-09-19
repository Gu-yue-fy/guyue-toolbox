/* ============================================================
 * 文件说明：由优化项现有数据推导「四段式详情」（设计详情抽屉的内容结构）：
 *           这是什么 / 为什么会这样 / 怎么处理 / 风险与影响 / 是否可撤销。
 *
 * 为什么不给 234 项人工写文案：那是 1000+ 段文字，改一处就要维护一遍，
 * 且极易与实现脱节。这里全部由 Id / 分组 / 描述 / 风险标记 / 权限要求推导，
 * 且"怎么处理"只陈述本工具真实存在的机制（备份链 → 写入 → 可还原），
 * 不编造不存在的步骤。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    internal static class TweakDetails
    {
        /// <summary>分组 → 该组优化在"什么场景下值得开"（第二段"为什么会这样"）。</summary>
        private static string ScenarioOf(string group)
        {
            switch (group)
            {
                case "性能加速":
                    return "系统默认设置偏向兼容性而非响应速度；这些项让 CPU 调度、内存管理与文件系统更倾向于即时响应，代价是极少数老程序可能不兼容。";
                case "极限性能":
                    return "这是以稳定性换性能的档位：默认值为了兼容所有硬件而保守，激进值能降低延迟与抖动，但对供电、散热与驱动质量更敏感。";
                case "游戏优化":
                    return "游戏对输入延迟与帧间隔稳定最敏感；系统默认的后台调度、全屏优化与 GPU 抢占策略会引入额外延迟。";
                case "网络优化":
                    return "Windows 默认的 TCP 参数面向通用场景（含省电与兼容），在低延迟场景下并非最优。";
                case "系统服务":
                    return "Windows 预装了大量面向企业与商店场景的服务，个人使用中多为常驻但不必要。";
                case "电源与启动":
                    return "默认电源策略与开机流程优先兼顾省电与多设备兼容；固定使用场景下可以更直接。";
                case "隐私与安全":
                    return "系统默认开启遥测、广告标识与活动历史上报；这些数据出了本机就不再受你控制。";
                case "系统精简":
                    return "预装组件与冗余项会占用内存、磁盘与后台线程，且多数在个人使用中从不触发。";
                case "外观与体验":
                    return "资源管理器、任务栏与右键菜单的默认行为偏保守（含动画、延迟与折叠），熟练用户往往需要更直接的操作反馈。";
                case "音频优化":
                    return "音频栈默认为多设备共用与通信场景让步（采样率重采样、通信降音、独占模式被禁）。";
                default:
                    return "该项调整的是系统默认值中偏保守的那一部分。";
            }
        }

        /// <summary>把一条优化项推导为四段式详情。</summary>
        public static DetailInfo Build(ITweak t)
        {
            DetailInfo d = new DetailInfo();
            if (t == null) return d;

            string group = string.IsNullOrEmpty(t.Group) ? TweakPackProvider.GPack : t.Group;

            d.Title = t.Name + "（" + group + "）";
            d.Icon = t.Risky ? "warn" : "tune";
            d.Accent = t.Risky ? Theme.Warning : Theme.Accent;

            // ① 这是什么：名称 + 该优化项自己的描述（描述已是逐项撰写的）
            d.What = string.IsNullOrEmpty(t.Description) ? t.Name : t.Description;

            // ② 为什么会这样：分组场景
            d.Why = ScenarioOf(group);

            // ③ 怎么处理：本工具的真实机制（不写不存在的步骤）
            string how = "应用时通过 RegHelper 写入，并在写入前备份原值（备份链）；" +
                "还原时按备份写回原值，不是硬编码地赋回默认值。";
            how += t.AdminOnly
                ? " 该项需要管理员权限（涉及 HKLM 或系统服务）。"
                : " 该项只需当前用户权限。";
            if (!string.IsNullOrEmpty(t.Id)) how += " 标识：" + t.Id + "。";

            // 透明：列出将写入的确切注册表值（数据取自优化项本身，永不与实现脱节）。
            // ⚠ 右栏宽度只有 ~264px，完整路径一条要折 2~3 行；而右栏排版是
            // 「剩余空间不够一段就整段跳过」——清单过长会把后面的段落全部挤掉
            // （表现为"点了优化项右边没有说明"）。因此这里只内联前 3 条，
            // 且路径缩写为末两级目录，保证本段高度有上界
            RegTweak reg = t as RegTweak;
            if (reg != null && reg.Enable != null && reg.Enable.Count > 0)
            {
                System.Text.StringBuilder w = new System.Text.StringBuilder();
                w.Append("\r\n\r\n将写入 ").Append(reg.Enable.Count).Append(" 个注册表值：");
                int max = reg.Enable.Count < 3 ? reg.Enable.Count : 3;
                for (int i = 0; i < max; i++)
                {
                    RegWrite r = reg.Enable[i];
                    if (r == null) continue;
                    string hive =
                        r.Hive == Microsoft.Win32.RegistryHive.LocalMachine ? "HKLM" :
                        r.Hive == Microsoft.Win32.RegistryHive.CurrentUser ? "HKCU" :
                        r.Hive == Microsoft.Win32.RegistryHive.ClassesRoot ? "HKCR" :
                        r.Hive == Microsoft.Win32.RegistryHive.Users ? "HKU" :
                        r.Hive.ToString();
                    string p = r.Path == null ? "" : r.Path;
                    string[] segs = p.Split('\\');
                    // 缩写路径：只保留末两级（…\Explorer\Advanced），避免长路径折行过多
                    string shortPath = segs.Length > 2
                        ? "…\\" + segs[segs.Length - 2] + "\\" + segs[segs.Length - 1]
                        : p;
                    w.Append("\r\n· ").Append(hive).Append('\\').Append(shortPath);
                    if (string.IsNullOrEmpty(r.Name))
                    {
                        w.Append("（默认值）");
                    }
                    else
                    {
                        w.Append(" → ").Append(r.Name);
                        if (r.Delete) w.Append(" = 删除该值");
                        else
                        {
                            byte[] bin = r.Value as byte[];
                            w.Append(" = ").Append(bin != null
                                ? "二进制 " + bin.Length + " 字节"
                                : Convert.ToString(r.Value));
                        }
                    }
                }
                if (reg.Enable.Count > max) w.Append("\r\n· …等 ").Append(reg.Enable.Count).Append(" 项");
                how += w.ToString();
            }
            d.How = how;

            // ④ 风险与影响
            if (t.Risky)
            {
                d.Risk = "标记为「谨慎」：可能影响某些程序的兼容性或依赖于它的系统功能，建议先单项启用、确认无异常后再继续。";
                d.RiskTone = true;
            }
            else
            {
                d.Risk = "标记为「安全」：属于普遍适用且影响面可控的调整。";
            }

            // ⑤ 是否可撤销（用户决策时最先看的一句）
            bool applied = false;
            try { applied = t.IsApplied(); }
            catch { }
            d.Reversible = "可撤销：应用前的原值已进入备份链，可在本页关闭该项，或在「软件设置 → 操作日志」按优化组整体回滚。";
            d.Reversible += applied ? " 当前状态：已启用。" : " 当前状态：未启用。";
            d.Irreversible = false;

            return d;
        }
    }
}
