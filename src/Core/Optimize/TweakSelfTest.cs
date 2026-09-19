/* ============================================================
 * 文件说明：优化项目录自检（护栏）。
 *
 * 借鉴成熟的源码级架构护栏测试思路（其
 * Tests/Changes/OptimizationProviderCanonicalArchitectureTests.cs 用一组
 * 不变量锁住目录：Id 唯一性、目录规模、每项必须走安全适配器）。
 * 本项目是单体 WinForms、无测试框架，因此做成**程序内自检**：
 * 由 Program 的 --selftest 开关调用，只读、不写注册表、无副作用。
 *
 * 它把这类问题从「靠自觉」变成「可自动发现」：
 *   · 改动 100+ 个优化项里的某一个，Id 撞车或忘了还原
 *   · 两个项写同一个注册表值却没共享 BackupId（还原值会互相污染）
 *   · CommandTweak 漏写 RevertFile（点了就回不去）
 *   · 数值类型与 RegistryValueKind 不匹配
 *
 * 注意：设计「只允许单一文件写注册表」的边界规则**不适用于本项目**
 * （本项目的注册表写入分散在 17 个文件里，强行收敛需要大改架构），故未照搬。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static class TweakSelfTest
    {
        public sealed class Result
        {
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public int Checked;

            public bool Passed { get { return Failures.Count == 0; } }
        }

        public static Result Run()
        {
            Result r = new Result();

            List<ITweak> all;
            try { all = TweakLibrary.All(); }
            catch (Exception ex)
            {
                r.Failures.Add("无法构建优化项目录：" + ex.Message);
                return r;
            }
            r.Checked = all.Count;

            CheckIdentity(r, all);
            CheckRegTweaks(r, all);
            CheckCommandTweaks(r, all);
            CheckKeyCollisions(r, all);
            CheckRiskFlags(r, all);
            CheckGroups(r, all);

            return r;
        }

        // ---------------- 身份与文案 ----------------

        private static void CheckIdentity(Result r, List<ITweak> all)
        {
            Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Count; i++)
            {
                ITweak t = all[i];
                if (t == null) { r.Failures.Add("第 " + i + " 项为空。"); continue; }

                if (string.IsNullOrEmpty(t.Id)) { r.Failures.Add("第 " + i + " 项缺少 Id。"); continue; }
                if (string.IsNullOrEmpty(t.Name)) r.Failures.Add("[" + t.Id + "] 缺少名称。");
                if (string.IsNullOrEmpty(t.Group)) r.Failures.Add("[" + t.Id + "] 缺少分组。");
                if (string.IsNullOrEmpty(t.Description)) r.Failures.Add("[" + t.Id + "] 缺少说明。");

                string exist;
                if (seen.TryGetValue(t.Id, out exist))
                {
                    r.Failures.Add("Id 重复：" + t.Id + "（与 " + exist + " 冲突，后者会被静默丢弃）。");
                }
                else
                {
                    seen[t.Id] = t.Id;
                }
            }
        }

        // ---------------- 注册表型优化项 ----------------

        private static void CheckRegTweaks(Result r, List<ITweak> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                RegTweak rt = all[i] as RegTweak;
                if (rt == null) continue;

                if (rt.Enable.Count == 0)
                {
                    r.Failures.Add("[" + rt.Id + "] 没有任何目标写入（Enable 为空），应用与状态判定都无意义。");
                    continue;
                }

                for (int k = 0; k < rt.Enable.Count; k++)
                {
                    RegWrite w = rt.Enable[k];
                    if (w == null) { r.Failures.Add("[" + rt.Id + "] 第 " + k + " 条写入为空。"); continue; }
                    if (string.IsNullOrEmpty(w.Path)) r.Failures.Add("[" + rt.Id + "] 第 " + k + " 条写入缺少注册表路径。");
                    // 空值名 = 写入该键的默认值（如 Win11 经典右键菜单的 CLSID 技巧），合法但不常见，仅提示
                    if (string.IsNullOrEmpty(w.Name))
                        r.Warnings.Add("[" + rt.Id + "] 第 " + k + " 条写入值名为空（将写入键的默认值）。");
                    if (w.Delete) continue;

                    // 类型与值必须匹配：Str() 传 int、或 Dword() 传字符串都会在这里暴露
                    if (w.Kind == RegistryValueKind.DWord && !(w.Value is int))
                        r.Failures.Add("[" + rt.Id + "] 值 " + w.Name + " 声明为 DWord 但值不是 int（实际 " + TypeName(w.Value) + "）。");
                    else if (w.Kind == RegistryValueKind.String && !(w.Value is string))
                        r.Failures.Add("[" + rt.Id + "] 值 " + w.Name + " 声明为 String 但值不是 string（实际 " + TypeName(w.Value) + "）。");
                    else if (w.Kind == RegistryValueKind.Binary && !(w.Value is byte[]))
                        r.Failures.Add("[" + rt.Id + "] 值 " + w.Name + " 声明为 Binary 但值不是 byte[]（实际 " + TypeName(w.Value) + "）。");
                }

                // 可还原性：既不用备份、又没有显式还原写入 = 点了回不去
                if (!rt.UseBackupOnRevert && rt.RevertWrites.Count == 0)
                {
                    r.Failures.Add("[" + rt.Id + "] 关闭了备份还原且没有 RevertWrites，无法撤销。");
                }
            }
        }

        // ---------------- 命令型优化项 ----------------

        private static void CheckCommandTweaks(Result r, List<ITweak> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                CommandTweak ct = all[i] as CommandTweak;
                if (ct == null) continue;

                if (string.IsNullOrEmpty(ct.EnableFile))
                    r.Failures.Add("[" + ct.Id + "] 缺少 EnableFile，无法应用。");
                // CommandTweak.Revert() 在 RevertFile 为空时直接返回 false —— 等于点了回不去
                if (string.IsNullOrEmpty(ct.RevertFile))
                    r.Failures.Add("[" + ct.Id + "] 缺少 RevertFile，无法还原。");
                if (ct.Probe == null)
                    r.Warnings.Add("[" + ct.Id + "] 未设置 Probe，状态永远显示为「未启用」。");
            }
        }

        // ---------------- 同键冲突 ----------------

        private static void CheckKeyCollisions(Result r, List<ITweak> all)
        {
            // key = hive|path|valueName  ->  写入它的所有 (项 Id, 备份 Id)
            Dictionary<string, List<string[]>> owners = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < all.Count; i++)
            {
                RegTweak rt = all[i] as RegTweak;
                if (rt == null) continue;

                for (int k = 0; k < rt.Enable.Count; k++)
                {
                    RegWrite w = rt.Enable[k];
                    if (w == null || string.IsNullOrEmpty(w.Path) || string.IsNullOrEmpty(w.Name)) continue;

                    string key = w.Hive + "|" + w.Path + "|" + w.Name;
                    List<string[]> list;
                    if (!owners.TryGetValue(key, out list)) { list = new List<string[]>(); owners[key] = list; }
                    list.Add(new string[] { rt.Id, BackupIdOf(rt) });
                }
            }

            foreach (KeyValuePair<string, List<string[]>> kv in owners)
            {
                if (kv.Value.Count < 2) continue;

                // 共享同一个 BackupId = 有意的「同键多档」（如 Win32PrioritySeparation 三档），允许
                bool shared = true;
                string first = kv.Value[0][1];
                if (string.IsNullOrEmpty(first)) shared = false;
                else
                {
                    for (int i = 1; i < kv.Value.Count; i++)
                    {
                        if (!string.Equals(kv.Value[i][1], first, StringComparison.OrdinalIgnoreCase)) { shared = false; break; }
                    }
                }

                if (shared) continue;

                string ids = "";
                for (int i = 0; i < kv.Value.Count; i++) ids += (i > 0 ? ", " : "") + kv.Value[i][0];
                r.Failures.Add("同键冲突：" + kv.Key + " 被多个项写入且未共享 BackupId（" + ids +
                    "）。它们会互相覆盖还原值，应合并或指定相同的 BackupIdValue。");
            }
        }

        // ---------------- 风险标记 ----------------

        private static void CheckRiskFlags(Result r, List<ITweak> all)
        {
            for (int i = 0; i < all.Count; i++)
            {
                ITweak t = all[i];
                if (t == null) continue;
                // 一键推荐会直接应用推荐项，因此推荐项不允许同时是风险项
                if (t.Recommended && t.Risky)
                    r.Failures.Add("[" + t.Id + "] 同时被标记为「推荐」与「有风险」：一键推荐会应用危险项。");
            }
        }

        // ---------------- 分组 ----------------

        private static void CheckGroups(Result r, List<ITweak> all)
        {
            string[] known = new string[]
            {
                TweakLibrary.GPerformance, TweakLibrary.GAppearance, TweakLibrary.GPrivacy,
                TweakLibrary.GServices, TweakLibrary.GPower, TweakLibrary.GGame,
                TweakLibrary.GNetwork, TweakLibrary.GSlim, TweakLibrary.GExtreme,
                TweakLibrary.GAudio
            };

            HashSet<string> set = new HashSet<string>(known, StringComparer.Ordinal);
            HashSet<string> unknown = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < all.Count; i++)
            {
                ITweak t = all[i];
                if (t == null || string.IsNullOrEmpty(t.Group)) continue;
                // 外部优化包可能自带分组，只提示不判失败
                if (!set.Contains(t.Group)) unknown.Add(t.Group);
            }

            foreach (string g in unknown) r.Warnings.Add("分组「" + g + "」不在内置分组列表中（外部优化包自带分组属正常）。");
        }

        private static string BackupIdOf(RegTweak rt)
        {
            return string.IsNullOrEmpty(rt.BackupIdValue) ? rt.IdValue : rt.BackupIdValue;
        }

        private static string TypeName(object v)
        {
            return v == null ? "null" : v.GetType().Name;
        }
    }
}
