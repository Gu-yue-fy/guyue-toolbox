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

        private const string CplDesktop = @"Control Panel\Desktop";
        private const string CplDWM = @"Control Panel\Desktop\WindowMetrics";
        private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ExplorerMain = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        private static List<ITweak> _allCache;
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
            for (int i = 0; i < _providers.Count; i++)
            {
                list.AddRange(_providers[i].Provide());
            }
            _allCache = list;
            return list;
        }

        public static List<string> RecommendedIds()
        {
            return new List<string>(new string[]
            {
                "menu_anim", "visual_fx", "startup_delay_zero",
                "show_file_ext", "disable_shake",
                "ad_id_off", "copilot_off", "bing_search_off",
                "svc_DiagTrack", "svc_dmwappushservice", "svc_RemoteRegistry"
            });
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
