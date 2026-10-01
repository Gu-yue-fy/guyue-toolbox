using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>单个驱动的信息。</summary>
    public sealed class DriverInfo
    {
        public string DeviceName = "";
        public string Provider = "";
        public string Version = "";
        public string Date = "";
        public bool IsSigned = true;
        /// <summary>关注说明：未数字签名 / 第三方驱动等，空表示正常。</summary>
        public string Note = "";
    }

    /// <summary>
    /// 驱动枚举。用 WMI（Win32_PnPSignedDriver）拿结构化数据，逐行管道分隔解析，
    /// 比解析 pnputil 文本稳定；无签名驱动标红，第三方驱动提示但不告警。
    /// </summary>
    public static class DriverScanner
    {
        /// <summary>枚举已安装驱动，标记无数字签名的驱动（潜在风险）。失败返回空列表。</summary>
        public static List<DriverInfo> Scan()
        {
            List<DriverInfo> list = new List<DriverInfo>();
            // 单行管道分隔，避免解析多行文本；DeviceName 含 | 极罕见，可忽略
            string ps = "Get-CimInstance Win32_PnPSignedDriver | ForEach-Object { ($_.DeviceName + '|' + "
                + "$_.DriverProviderName + '|' + $_.DriverVersion + '|' + "
                + "$(if($_.DriverDate){$_.DriverDate.ToString('yyyy-MM-dd')}else{''}) + '|' + $_.IsSigned) }";
            Shell.Result r = Shell.Run("powershell.exe", "-NoProfile -Command \"" + ps + "\"", 90000, false);
            if (r == null || !r.Ok || string.IsNullOrEmpty(r.Output)) return list;

            string[] lines = r.Output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] f = line.Split('|');
                if (f.Length < 5) continue;
                DriverInfo d = new DriverInfo();
                d.DeviceName = f[0].Trim();
                d.Provider = f[1].Trim();
                d.Version = f[2].Trim();
                d.Date = f[3].Trim();
                bool signed = true;
                bool.TryParse(f[4].Trim(), out signed);
                d.IsSigned = signed;
                if (!signed) d.Note = "未数字签名";
                else if (!string.IsNullOrEmpty(d.Provider) &&
                         d.Provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) < 0 &&
                         d.Provider.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) < 0)
                    d.Note = "第三方驱动";
                list.Add(d);
            }
            return list;
        }
    }
}
