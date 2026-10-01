using System;
using System.Collections.Generic;
using GuyueBox.Core;

namespace GuyueBox.Core
{
    /// <summary>单个 Windows 可选功能。</summary>
    public sealed class FeatureInfo
    {
        public string Name = "";
        public string State = "";   // Enabled / Disabled / EnablePending ...
    }

    /// <summary>Windows 可选功能（组件）枚举与启停。</summary>
    public static class FeatureManager
    {
        /// <summary>列出全部可选功能。失败返回空列表。</summary>
        public static List<FeatureInfo> List()
        {
            List<FeatureInfo> list = new List<FeatureInfo>();
            string ps = "Get-WindowsOptionalFeature -Online | ForEach-Object { $_.FeatureName + '|' + $_.State }";
            Shell.Result r = Shell.Run("powershell.exe", "-NoProfile -Command \"" + ps + "\"", 120000, false);
            if (r == null || !r.Ok || string.IsNullOrEmpty(r.Output)) return list;
            string[] lines = r.Output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int bar = line.LastIndexOf('|');
                FeatureInfo f = new FeatureInfo();
                if (bar < 0) f.Name = line.Trim();
                else { f.Name = line.Substring(0, bar).Trim(); f.State = line.Substring(bar + 1).Trim(); }
                list.Add(f);
            }
            return list;
        }

        /// <summary>启用或禁用指定功能（变更类，进入操作日志）。需管理员。返回是否成功。</summary>
        public static bool Set(string name, bool enable)
        {
            string verb = enable ? "Enable-WindowsOptionalFeature" : "Disable-WindowsOptionalFeature";
            string ps = verb + " -Online -FeatureName '" + name.Replace("'", "''") + "' -NoRestart";
            Shell.Result r = Shell.Run("powershell.exe", "-NoProfile -Command \"" + ps + "\"", 600000, true);
            return r != null && r.Ok;
        }
    }
}
