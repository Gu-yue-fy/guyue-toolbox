using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>优化项适用性条件：满足时项才可应用/显示；不满足即"本机不适用"（默认隐藏）。</summary>
    public interface ICondition
    {
        bool Satisfied();
        string Reason { get; }
    }

    /// <summary>恒满足：无门控项使用，便于统一接口。</summary>
    public sealed class AlwaysCondition : ICondition
    {
        public bool Satisfied() { return true; }
        public string Reason { get { return ""; } }
    }

    /// <summary>显卡厂商条件：vendor 为 "N"/"A"/"I" 之一，可逗号组合（如 "N,A"）。</summary>
    public sealed class GpuVendorCondition : ICondition
    {
        private readonly string _vendors;
        public GpuVendorCondition(string vendors) { _vendors = vendors; }
        public bool Satisfied()
        {
            string v = GpuLatencyTweak.DetectVendor();
            if (string.IsNullOrEmpty(v)) return false;
            foreach (string part in _vendors.Split(','))
            {
                if (v.IndexOf(part.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
        public string Reason { get { return "需要 " + _vendors + " 系列显卡"; } }
    }

    /// <summary>CPU 厂商条件：vendor 为 "Intel"/"AMD"，可逗号组合。</summary>
    public sealed class CpuVendorCondition : ICondition
    {
        private readonly string _vendors;
        public CpuVendorCondition(string vendors) { _vendors = vendors; }
        public bool Satisfied()
        {
            string v = _vendors.ToLowerInvariant();
            if (v.Contains("intel") && CpuVendor.IsIntel) return true;
            if (v.Contains("amd") && CpuVendor.IsAmd) return true;
            return false;
        }
        public string Reason { get { return "需要 " + _vendors + " 处理器"; } }
    }

    /// <summary>Windows 内部版本下限条件：Build >= minBuild 时满足。</summary>
    public sealed class WindowsBuildCondition : ICondition
    {
        private readonly int _min;
        public WindowsBuildCondition(int minBuild) { _min = minBuild; }
        public bool Satisfied() { return Environment.OSVersion.Version.Build >= _min; }
        public string Reason { get { return "需要 Windows 内部版本 " + _min + " 及以上"; } }
    }

    /// <summary>提权条件：仅管理员身份运行时满足（外部扩展包声明 when.elevated 用它）。</summary>
    public sealed class ElevatedCondition : ICondition
    {
        public bool Satisfied() { return Native.IsElevated(); }
        public string Reason { get { return "需要以管理员身份运行"; } }
    }

    /// <summary>
    /// 条件组合：全部满足才算满足。
    /// 外部扩展包的一条 "when" 里可能同时写了显卡厂商 + 系统版本 + 提权，就拼成一个 AllCondition。
    /// </summary>
    public sealed class AllCondition : ICondition
    {
        private readonly List<ICondition> _parts;

        public AllCondition(List<ICondition> parts)
        {
            _parts = parts == null ? new List<ICondition>() : parts;
        }

        public bool Satisfied()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (!_parts[i].Satisfied()) return false;
            }
            return true;
        }

        /// <summary>给出第一条不满足的条件的说明，供界面提示"为什么不适用"。</summary>
        public string Reason
        {
            get
            {
                for (int i = 0; i < _parts.Count; i++)
                {
                    if (!_parts[i].Satisfied()) return _parts[i].Reason;
                }
                return "";
            }
        }
    }

    /// <summary>适用性辅助：统一判断任意 ITweak 是否对本机适用（无门控类型恒适用）。</summary>
    public static class TweakApplicability
    {
        /// <summary>本机是否适用：RegTweak / GpuInstanceTweak 按其门控判定，其余类型恒适用。</summary>
        public static bool IsApplicable(this ITweak t)
        {
            RegTweak rt = t as RegTweak;
            if (rt != null) return rt.IsApplicable;
            GpuInstanceTweak gt = t as GpuInstanceTweak;
            if (gt != null) return gt.IsApplicable;
            return true;
        }

        /// <summary>取该项的门控条件（可用于 UI 提示原因），无门控返回 null。</summary>
        public static ICondition ConditionOf(this ITweak t)
        {
            RegTweak rt = t as RegTweak;
            if (rt != null) return rt.Condition;
            GpuInstanceTweak gt = t as GpuInstanceTweak;
            if (gt != null) return gt.Condition;
            return null;
        }
    }
}
