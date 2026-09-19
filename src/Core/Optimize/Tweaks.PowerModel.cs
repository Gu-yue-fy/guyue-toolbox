/* ============================================================
 * 文件说明：优化项库「进阶项」：CPU 电源模型调优 + 网络暴露面 + Edge 覆盖层。
 *           - PowerSettingTweak：通用 powercfg 电源设置项（读当前方案默认值做还原）
 *           - NetbiosTweak：遍历 NetBT 接口子键禁用 NetBIOS
 *           电源项一律「只改交流电（AC）档、不动电池档」，避免笔记本离电后功耗失控。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    /// <summary>powercfg 电源设置的读写与默认值查询（当前活动方案）。</summary>
    internal static class PowerCfg
    {
        /// <summary>「处理器电源管理」子组 GUID。</summary>
        public const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";

        private const string UserPowerSchemes =
            @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        /// <summary>当前活动电源方案 GUID（无则返回空串）。</summary>
        public static string ActiveScheme()
        {
            object v = RegHelper.GetValue(RegistryHive.LocalMachine, UserPowerSchemes, "ActivePowerScheme");
            return v == null ? "" : v.ToString();
        }

        /// <summary>当前方案下某个电源设置的注册表路径。</summary>
        public static string SettingPath(string subgroup, string settingGuid)
        {
            string scheme = ActiveScheme();
            if (string.IsNullOrEmpty(scheme)) return "";
            return UserPowerSchemes + "\\" + scheme + "\\" + subgroup + "\\" + settingGuid;
        }

        /// <summary>写入电源设置（dc 传 -1 表示只改交流电档，电池档保持方案默认）。</summary>
        public static bool Set(string subgroup, string settingGuid, int ac, int dc)
        {
            bool ok = Shell.Run("powercfg.exe",
                "/setacvalueindex scheme_current " + subgroup + " " + settingGuid + " " + ac, 30000).Ok;
            if (dc >= 0)
            {
                ok &= Shell.Run("powercfg.exe",
                    "/setdcvalueindex scheme_current " + subgroup + " " + settingGuid + " " + dc, 30000).Ok;
            }
            ok &= Shell.Run("powercfg.exe", "/setactive scheme_current", 30000).Ok;
            return ok;
        }

        /// <summary>该设置在指定方案下的出厂默认值（缺省返回 -1）。</summary>
        public static int DefaultOf(string subgroup, string settingGuid, string schemeGuid, string which)
        {
            if (string.IsNullOrEmpty(schemeGuid)) return -1;
            object v = RegHelper.GetValue(RegistryHive.LocalMachine,
                @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + subgroup + "\\" + settingGuid +
                @"\DefaultPowerSchemeValues\" + schemeGuid, which);
            try { return v == null ? -1 : Convert.ToInt32(v); }
            catch { return -1; }
        }
    }

    /// <summary>
    /// 通用电源设置项：把「处理器电源管理」子组下的某个设置改成目标值。
    /// 还原时读取该设置在**当前方案**的出厂默认值写回（电源子树由 Power 服务托管，必须走 powercfg）。
    /// dc 传 -1 表示只改交流电档，电池档不动。
    /// </summary>
    public sealed class PowerSettingTweak : ITweak
    {
        private readonly string _id;
        private readonly string _group;
        private readonly string _name;
        private readonly string _desc;
        private readonly string _settingGuid;
        private readonly int _ac;
        private readonly int _dc;
        private readonly bool _risky;
        private readonly Func<bool> _applicable;

        public PowerSettingTweak(string id, string group, string name, string desc,
            string settingGuid, int ac, int dc, bool risky, Func<bool> applicable)
        {
            _id = id;
            _group = group;
            _name = name;
            _desc = desc;
            _settingGuid = settingGuid;
            _ac = ac;
            _dc = dc;
            _risky = risky;
            _applicable = applicable;
        }

        public string Id { get { return _id; } }
        public string Group { get { return _group; } }
        public string Name { get { return _name; } }
        public string Description { get { return _desc; } }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return _risky; } }
        public bool Recommended { get { return false; } }

        private bool Applicable()
        {
            try { return _applicable == null || _applicable(); }
            catch { return true; }
        }

        public bool IsApplied()
        {
            if (!Applicable()) return false;
            string path = PowerCfg.SettingPath(PowerCfg.SubProcessor, _settingGuid);
            if (string.IsNullOrEmpty(path)) return false;

            object ac = RegHelper.GetValue(RegistryHive.LocalMachine, path, "ACSettingIndex");
            if (ac == null) return false;

            try
            {
                if (Convert.ToInt32(ac) != _ac) return false;
                if (_dc >= 0)
                {
                    object dc = RegHelper.GetValue(RegistryHive.LocalMachine, path, "DCSettingIndex");
                    if (dc == null || Convert.ToInt32(dc) != _dc) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Apply()
        {
            if (!Applicable()) return false;
            return PowerCfg.Set(PowerCfg.SubProcessor, _settingGuid, _ac, _dc);
        }

        public bool Revert()
        {
            string scheme = PowerCfg.ActiveScheme();
            int ac = PowerCfg.DefaultOf(PowerCfg.SubProcessor, _settingGuid, scheme, "ACSettingIndex");
            int dc = PowerCfg.DefaultOf(PowerCfg.SubProcessor, _settingGuid, scheme, "DCSettingIndex");
            if (ac < 0) return false;
            if (dc < 0) dc = ac; // 取不到默认值时用 AC 档兜底，避免两档不一致
            return PowerCfg.Set(PowerCfg.SubProcessor, _settingGuid, ac, dc);
        }
    }

    /// <summary>
    /// 禁用 NetBIOS over TCP/IP：遍历 NetBT\Parameters\Interfaces 下每个已存在的接口子键，
    /// 把 NetbiosOptions 设为 2（禁用）。只处理已存在的接口，不凭空造键。
    /// </summary>
    public sealed class NetbiosTweak : ITweak
    {
        private const string Root = @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces";

        public string Id { get { return "netbios_off"; } }
        public string Group { get { return TweakLibrary.GNetwork; } }
        public string Name { get { return "禁用 NetBIOS over TCP/IP"; } }
        public string Description
        {
            get
            {
                return "把每个网络接口的 NetBIOS 设为「禁用」（NetbiosOptions=2），关闭基于 NetBIOS 的名称服务与旧式共享发现，减小暴露面。" +
                    "只影响 NetBIOS，不影响 TCP/IP 本身；局域网中依赖旧式名称解析的老设备可能受影响，关闭本项即还原。";
            }
        }
        public bool AdminOnly { get { return true; } }
        public bool Risky { get { return true; } }
        public bool Recommended { get { return false; } }

        private static string[] Interfaces()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Root, false))
                {
                    if (k == null) return new string[0];
                    string[] subs = k.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++) list.Add(Root + "\\" + subs[i]);
                }
            }
            catch
            {
            }
            return list.ToArray();
        }

        public bool IsApplied()
        {
            string[] paths = Interfaces();
            if (paths.Length == 0) return false;

            int seen = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                object v = RegHelper.GetValue(RegistryHive.LocalMachine, paths[i], "NetbiosOptions");
                if (v == null) continue; // 该接口尚未设置过：视为未达标，交由 Apply 写入
                try
                {
                    if (Convert.ToInt32(v) != 2) return false;
                    seen++;
                }
                catch
                {
                    return false;
                }
            }
            return seen > 0;
        }

        public bool Apply()
        {
            string[] paths = Interfaces();
            if (paths.Length == 0) return false;

            RegHelper.BeginBackup(Id);
            int written = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                if (RegHelper.SetValue(RegistryHive.LocalMachine, paths[i], "NetbiosOptions", 2,
                    RegistryValueKind.DWord, Id))
                {
                    written++;
                }
            }
            return written > 0;
        }

        public bool Revert()
        {
            return RegHelper.Restore(Id);
        }
    }

    public static partial class TweakLibrary
    {
        /// <summary>进阶项：CPU 电源模型（仅插电档）+ NetBIOS + Edge 游戏助手。</summary>
        private static IEnumerable<ITweak> PowerModel()
        {
            List<ITweak> list = new List<ITweak>();

            list.Add(new PowerSettingTweak(
                "power_min_state_ac100", GPower,
                "插电时处理器最低状态 100%（不降频）",
                "把处理器最低性能状态设为 100%，插电时 CPU 不再降到低频，消除升频延迟带来的顿挫与帧时间尖峰。" +
                "代价：待机功耗与发热明显上升。仅改交流电（插电）档，电池档保持方案默认。",
                "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, -1, true, null));

            list.Add(new PowerSettingTweak(
                "power_perf_up_fast", GPower,
                "插电时更快升频（性能提升阈值 1）",
                "把处理器性能提升阈值设为 1%，负载一有上升就立刻拉高频率，减少升频滞后造成的顿挫。仅改交流电档。",
                "06cadf0e-64ed-448a-8927-ce7bf90eb35d", 1, -1, false, null));

            list.Add(new PowerSettingTweak(
                "power_perf_down_late", GPower,
                "插电时更晚降频（性能降低阈值 100）",
                "把处理器性能降低阈值设为 100%，负载回落后不急于降频，避免频率来回抖动。仅改交流电档。",
                "4b92d758-5a24-4851-a470-815d78aee119", 100, -1, false, null));

            list.Add(new PowerSettingTweak(
                "power_hetero_sched_perf", GPower,
                "大小核调度偏向性能核",
                "把异类线程调度策略设为优先使用性能核（P 核），避免游戏线程被调度到能效核造成掉帧。" +
                "仅在检测到大小核 CPU 的本机可用（普通对称多核机器上本项自动判定不适用）。仅改交流电档。",
                "93b8b6dc-0698-4d1c-9ee4-0644e900c85d", 2, -1, false,
                delegate { return CpuTopology.IsHybrid; }));

            list.Add(new NetbiosTweak());

            RegTweak edgeAssist = new RegTweak();
            edgeAssist.IdValue = "edge_game_assist_off";
            edgeAssist.GroupValue = GAppearance;
            edgeAssist.NameValue = "禁用 Edge 游戏助手";
            edgeAssist.DescriptionValue =
                "关闭 Edge 在游戏时弹出的「游戏助手」覆盖层（当前用户设置 + 机器策略双写），避免全屏游戏被打断。" +
                "含机器策略写入，需要管理员权限。";
            edgeAssist.AdminOnlyValue = true;
            edgeAssist.Enable.Add(RegWrite.Dword(RegistryHive.CurrentUser,
                @"Software\Microsoft\Edge\GameAssist", "Enabled", 0));
            edgeAssist.Enable.Add(RegWrite.Dword(RegistryHive.LocalMachine,
                @"SOFTWARE\Policies\Microsoft\Edge", "GameAssistEnabled", 0));
            list.Add(edgeAssist);

            return list;
        }
    }
}
