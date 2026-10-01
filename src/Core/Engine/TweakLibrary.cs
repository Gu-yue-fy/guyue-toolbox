/* Core/Engine/TweakLibrary.cs — 优化项目录：分组装配、外部 Provider 注册、方案预设（电竞/日常）、配置导出导入。 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GuyueBox.Core
{
    /// <summary>扩展优化源：返回一批优化项，Id 全库唯一（重复被丢弃，内置优先）。</summary>
    public interface ITweakProvider
    {
        System.Collections.Generic.IEnumerable<ITweak> Provide();
    }

    public static partial class TweakLibrary
    {
        // ---------------- 分组常量（页面筛选与分组文件共用） ----------------
        public const string GGame = "游戏优化";
        public const string GPerformance = "性能优化";
        public const string GNetwork = "网络优化";
        public const string GPower = "电源与启动";
        public const string GPrivacy = "隐私与安全";
        public const string GSlim = "系统精简";
        public const string GAppearance = "外观与体验";
        public const string GServices = "系统服务";
        public const string GExtreme = "极限性能";
        public const string GAudio = "音频优化";

        // ---------------- 常用注册表路径常量（分组文件共用） ----------------
        private const string CplDesktop = @"Control Panel\Desktop";
        private const string CplDWM = @"Control Panel\Desktop\WindowMetrics";
        private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string ExplorerMain = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        // ---------------- Provider 注册 ----------------
        private static List<ITweak> _allCache;

        /// <summary>All() 的构建锁：允许后台线程并发预热而不重复构建（首次约 2 秒）。</summary>
        private static readonly object AllGate = new object();

        private static readonly List<ITweakProvider> _providers = new List<ITweakProvider>();

        /// <summary>
        /// 注册扩展优化源。必须在首次访问 <see cref="All"/> 之前调用
        /// （内置应用在 Program 里注册 TweakPackProvider：外部优化包 packs\*.json）。
        /// </summary>
        public static void RegisterProvider(ITweakProvider provider)
        {
            if (provider == null || _providers.Contains(provider)) return;
            _providers.Add(provider);
            _allCache = null;
        }

        /// <summary>
        /// 全部优化项（内置分组 + 已注册 Provider；构建一次后缓存。调用方不得修改返回列表）。
        /// 首次构建约 2 秒（近 400 项 + 硬件适用性探测），因此允许后台线程预热；
        /// 双检锁保证并发预热不会重复构建。
        /// </summary>
        public static List<ITweak> All()
        {
            List<ITweak> cached = _allCache;
            if (cached != null) return cached;
            lock (AllGate)
            {
                if (_allCache != null) return _allCache;
                return BuildAll();
            }
        }

        /// <summary>真正的构建过程（只在 AllGate 内调用）。</summary>
        private static List<ITweak> BuildAll()
        {
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
            list.AddRange(Tasks());
            list.AddRange(Consent());
            list.AddRange(Audio());
            list.AddRange(Enhanced());
            list.AddRange(PowerModel());
            list.AddRange(Extras());
            list.AddRange(ServicesMore());
            list.AddRange(MiscReg());
            list.AddRange(HardwareNet());
            list.AddRange(TasksMore());
            list.AddRange(MiscReg2());
            list.AddRange(Supplement());
            list.AddRange(Pro());
            for (int i = 0; i < _providers.Count; i++) list.AddRange(_providers[i].Provide());

            // Id 全库唯一护栏：内置项优先，扩展包重复 Id 一律丢弃，外部包不得覆盖内置项。
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

        // ===================================================================
        // 方案预设（Profiles）
        // ===================================================================

        /// <summary>「电竞模式」预设：安全优先的低延迟组合，全部可一键还原。谨慎项经确认后应用。
        /// v2.1 起剔除：mmcss_tuning、nagle_off、core_parking_off、power_high、nv_telemetry_off、
        /// input_buffer、dscp_game_marker——这几项收益存疑或属于伪优化，且部分（input_buffer/mmcss）
        /// 可能干扰键鼠输入线程，留在预设里反而负优化。</summary>
        public static readonly string[] EsportsIds = new string[]
        {
            "game_mode", "game_bar_off", "game_dvr_off",
            "mouse_accel_off",
            "mpo_off", "nic_power_save_off", "disk_lpm_off",
            "usb_suspend_off", "tasks_maintenance_xbox_off",
            "svc_XblAuthManager", "svc_XblGameSave", "svc_XboxNetApiSvc", "svc_XboxGipSvc",
            "gamebar_tips_off",
        };

        /// <summary>「日常模式」预设：退出电竞模式时逐项还原的清单（内容与进入列表一致，方向相反：
        /// EsportsIds 走 Apply、DailyIds 走 Revert）。独立成一份是为了将来日常组合与电竞组合分化时互不影响。
        /// 与 EsportsIds 保持同步剔除上述伪优化/负优化项。</summary>
        public static readonly string[] DailyIds = new string[]
        {
            "game_mode", "game_bar_off", "game_dvr_off",
            "mouse_accel_off",
            "mpo_off", "nic_power_save_off", "disk_lpm_off",
            "usb_suspend_off", "tasks_maintenance_xbox_off",
            "svc_XblAuthManager", "svc_XblGameSave", "svc_XboxNetApiSvc", "svc_XboxGipSvc",
            "gamebar_tips_off",
        };

        /// <summary>返回预设中标记为「谨慎」的项（调用方据此弹出风险确认）。</summary>
        public static List<string> RiskyInPreset(string[] ids)
        {
            Dictionary<string, ITweak> byId = Index();
            List<string> risky = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                ITweak t;
                if (byId.TryGetValue(ids[i], out t) && t.Risky && !risky.Contains(ids[i])) risky.Add(ids[i]);
            }
            return risky;
        }

        /// <summary>应用预设：对预设中每项执行 Apply（已启用/不适用跳过并记说明）。返回 [成功数, 跳过数, 失败数]。</summary>
        public static int[] ApplyPreset(string[] ids, List<string> report)
        {
            Dictionary<string, ITweak> byId = Index();
            int ok = 0, skip = 0, fail = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                ITweak t;
                if (!byId.TryGetValue(ids[i], out t))
                {
                    if (report != null) report.Add(ids[i] + "：无此优化项");
                    fail++;
                    continue;
                }
                try
                {
                    if (t.IsApplied()) { skip++; continue; }
                    if (TweakExecutor.Apply(t).IsOk)
                    {
                        ok++;
                        if (report != null) report.Add(t.Name + "：已启用");
                    }
                    else
                    {
                        fail++;
                        if (report != null) report.Add(t.Name + "：应用失败（权限或条件不满足）");
                    }
                }
                catch
                {
                    fail++;
                    if (report != null) report.Add(t.Name + "：应用异常");
                }
            }
            return new int[] { ok, skip, fail };
        }

        /// <summary>恢复预设：对预设中每项执行 Revert。返回 [还原数, 未还原数]。</summary>
        public static int[] RevertPreset(string[] ids, List<string> report)
        {
            Dictionary<string, ITweak> byId = Index();
            int ok = 0, fail = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                ITweak t;
                if (!byId.TryGetValue(ids[i], out t)) continue;
                try
                {
                    if (TweakExecutor.Revert(t).IsOk)
                    {
                        ok++;
                        if (report != null) report.Add(t.Name + "：已还原");
                    }
                    else
                    {
                        fail++;
                        if (report != null) report.Add(t.Name + "：还原失败");
                    }
                }
                catch
                {
                    fail++;
                    if (report != null) report.Add(t.Name + "：还原异常");
                }
            }
            return new int[] { ok, fail };
        }

        // ===================================================================
        // 配置导出 / 导入
        // ===================================================================

        /// <summary>导出当前配置：所有「已启用」的优化项 Id 写成 JSON（{"applied":[...]}）。</summary>
        public static void ExportState(string path)
        {
            List<ITweak> all = All();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n  \"applied\": [");
            bool first = true;
            for (int i = 0; i < all.Count; i++)
            {
                bool applied;
                try { applied = all[i].IsApplied(); }
                catch { continue; }
                if (!applied) continue;
                if (!first) sb.Append(",");
                sb.Append("\r\n    \"").Append(all[i].Id).Append("\"");
                first = false;
            }
            sb.Append(first ? "]" : "\r\n  ]");
            sb.Append("\r\n}\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>导入配置：对 JSON 中列出的项执行 Apply（其余项不动）。返回成功条数，失败的 Id 记入 errors。</summary>
        public static int ImportState(string path, List<string> errors)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            List<string> ids = new List<string>();
            foreach (Match m in Regex.Matches(json, "\"([a-zA-Z0-9_]+)\""))
            {
                string id = m.Groups[1].Value;
                if (id != "applied" && !ids.Contains(id)) ids.Add(id);
            }

            Dictionary<string, ITweak> byId = Index();
            int ok = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                ITweak t;
                if (!byId.TryGetValue(ids[i], out t)) continue; // 目标库没有的 Id 忽略（版本差异）
                try
                {
                    if (t.IsApplied()) { ok++; continue; }
                    if (TweakExecutor.Apply(t).IsOk) ok++;
                    else if (errors != null) errors.Add(ids[i] + "：应用失败（权限或条件不满足）");
                }
                catch (Exception ex)
                {
                    if (errors != null) errors.Add(ids[i] + "：" + ex.Message);
                }
            }
            return ok;
        }

        private static Dictionary<string, ITweak> Index()
        {
            Dictionary<string, ITweak> byId = new Dictionary<string, ITweak>(StringComparer.OrdinalIgnoreCase);
            List<ITweak> all = All();
            for (int i = 0; i < all.Count; i++)
            {
                if (!byId.ContainsKey(all[i].Id)) byId[all[i].Id] = all[i];
            }
            return byId;
        }
    }
}