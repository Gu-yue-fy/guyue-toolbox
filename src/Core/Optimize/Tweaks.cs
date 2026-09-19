/* ============================================================
 * 文件说明：优化项库：全部注册表优化的声明（分组/描述/启用与还原动作）。新增优化项 = 追加一个 RegTweak 声明；Id 全局唯一由冒烟测试守护。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public interface ITweak
    {
        string Id { get; }
        string Group { get; }
        string Name { get; }
        string Description { get; }
        bool AdminOnly { get; }
        bool Risky { get; }
        bool Recommended { get; }
        bool IsApplied();
        bool Apply();
        bool Revert();
    }

    // ===================================================================
    // 通用优化项：用一组注册表写入描述「开启」状态与「还原」状态
    // ===================================================================

    public static partial class TweakLibrary
    {
        public const string GPerformance = "性能优化";
        public const string GAppearance = "外观与体验";
        public const string GPrivacy = "隐私与安全";
        public const string GServices = "系统服务";
        public const string GPower = "电源与启动";
        public const string GGame = "游戏优化";
        public const string GNetwork = "网络优化";
        public const string GSlim = "系统精简";
        public const string GExtreme = "极限性能";
        public const string GAudio = "音频优化";

        private const string CplDesktop = @"Control Panel\Desktop";
        private const string CplDWM = @"Control Panel\Desktop\WindowMetrics";
        private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ExplorerMain = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        private static List<ITweak> _allCache;
        // 扩展 Provider 注册表：内置应用当前未注册任何外部 Provider（ITweakProvider 是面向第三方的扩展点），
        // 在首次访问 All() 之前调用 RegisterProvider 即可让扩展项自动接入优化中心/推荐/方案库。
        private static readonly List<ITweakProvider> _providers = new List<ITweakProvider>();

        /// <summary>
        /// 注册扩展优化 Provider。须在首次访问 <see cref="All"/> 之前调用（重复注册同实例会被忽略）；
        /// 注册后清空缓存，Provider 提供的项自动进入优化中心、一键推荐与方案库。
        /// </summary>
        public static void RegisterProvider(ITweakProvider provider)
        {
            if (provider == null || _providers.Contains(provider)) return;
            _providers.Add(provider);
            _allCache = null;
        }

        /// <summary>全部优化项（内置分组 + 已注册 Provider；构建一次后缓存。调用方不得修改返回列表）。</summary>
        public static List<ITweak> All()
        {
            if (_allCache != null) return _allCache;
            List<ITweak> list = new List<ITweak>();
            list.AddRange(Game());
            list.AddRange(Network());
            list.AddRange(Performance());
            list.AddRange(Power());
            list.AddRange(Privacy());
            list.AddRange(Slim());
            list.AddRange(Appearance());
            list.AddRange(Services());
            list.AddRange(Extreme());
            list.AddRange(Tasks());    // 任务调度：16 个计划任务 → 4 个批量开关
            list.AddRange(Consent());  // 隐私授权：14 个 ConsentStore 权限 → 1 个批量开关
            list.AddRange(Audio());    // 音频优化：4 项
            list.AddRange(Enhanced()); // 增强项：高价值单点（独显/防误触/长路径/剪贴板/还原点/网卡卸载）
            list.AddRange(PowerModel()); // 进阶项：CPU 电源模型（仅插电档）+ NetBIOS + Edge 游戏助手
            list.AddRange(Extras());     // 补充项：资源管理器 / 任务栏体验项（HKCU，无需管理员，可还原）
            for (int i = 0; i < _providers.Count; i++)
            {
                list.AddRange(_providers[i].Provide());
            }

            // Id 全库唯一护栏：内置项优先，扩展 Provider / 优化包中的重复 Id 一律丢弃
            // （外部包不得靠重名覆盖内置项的行为，也不得互相覆盖）。
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ITweak> unique = new List<ITweak>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                ITweak t = list[i];
                if (t == null || string.IsNullOrEmpty(t.Id)) continue;
                if (!seen.Add(t.Id)) continue;
                unique.Add(t);
            }

            _allCache = unique;
            return unique;
        }

        /// <summary>
        /// 导出当前配置：把所有「已启用」的优化项 Id 写成简单 JSON
        /// （{"applied":["id1","id2",...]}）。换机/重装后在目标机器导入即可一键复刻。
        /// </summary>
        public static void ExportState(string path)
        {
            List<ITweak> all = All();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n  \"applied\": [");
            bool first = true;
            for (int i = 0; i < all.Count; i++)
            {
                bool applied;
                try { applied = all[i].IsApplied(); } catch { continue; }
                if (!applied) continue;
                if (!first) sb.Append(",");
                sb.Append("\r\n    \"").Append(all[i].Id).Append("\"");
                first = false;
            }
            sb.Append(first ? "]" : "\r\n  ]");
            sb.Append("\r\n}\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// 导入配置：对 JSON 中列出的项执行 Apply（其余项不动，不影响目标机器现状）。
        /// 返回成功应用的条数；失败的 Id 记入 errors（权限不足/硬件不适用等）。
        /// </summary>
        public static int ImportState(string path, List<string> errors)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            List<string> ids = new List<string>();
            foreach (Match m in Regex.Matches(json, "\"([a-zA-Z0-9_]+)\""))
            {
                string id = m.Groups[1].Value;
                if (id != "applied" && !ids.Contains(id)) ids.Add(id);
            }

            Dictionary<string, ITweak> byId = new Dictionary<string, ITweak>();
            List<ITweak> all = All();
            for (int i = 0; i < all.Count; i++) byId[all[i].Id] = all[i];

            int ok = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                ITweak t;
                if (!byId.TryGetValue(ids[i], out t)) continue; // 目标库没有的 Id 忽略（版本差异）
                try
                {
                    if (t.IsApplied()) { ok++; continue; } // 已生效视为成功
                    if (t.Apply()) ok++;
                    else if (errors != null) errors.Add(ids[i] + "：应用失败（权限或条件不满足）");
                }
                catch (Exception ex)
                {
                    if (errors != null) errors.Add(ids[i] + "：" + ex.Message);
                }
            }
            return ok;
        }
    }
}
