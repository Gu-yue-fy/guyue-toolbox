using System;
using System.Collections.Generic;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>一块物理磁盘的详细属性与可靠性计数器（读不到的字段为空串）。</summary>
    public sealed class DiskDetail
    {
        public string FriendlyName = "";
        public string SerialNumber = "";
        public string FirmwareVersion = "";
        public string BusType = "";
        public string MediaType = "";
        public string SizeText = "";
        public string HealthStatus = "";
        public string SpindleSpeed = "";

        // ---- 可靠性计数器（部分盘/驱动不提供）----
        public string Temperature = "";
        public string PowerOnHours = "";
        public string StartStopCycles = "";
        public string ReadErrors = "";
        public string WriteErrors = "";
        /// <summary>磨损值：各厂商语义不同（有的给百分比消耗，有的给剩余寿命），原样展示并在页面标注。</summary>
        public string WearPercent = "";

        // ---- 分区 ----
        public string PartitionStyle = "";
        public string PartitionCount = "";
        public string IsSystem = "";
        public string IsBoot = "";

        /// <summary>非空表示详情不完整（未找到磁盘或读取失败），内容为原因。</summary>
        public string Error = "";

        public bool HasReliability
        {
            get
            {
                return Temperature.Length > 0 || PowerOnHours.Length > 0 || StartStopCycles.Length > 0
                    || ReadErrors.Length > 0 || WriteErrors.Length > 0 || WearPercent.Length > 0;
            }
        }
    }

    /// <summary>单盘详情读取。</summary>
    public static class Disks
    {
        /// <summary>
        /// 读取指定物理磁盘的详情（按 FriendlyName 匹配）。失败时返回的对象的 Error 非空，其余字段按已读到部分填充。
        /// </summary>
        public static DiskDetail ReadDetail(string friendlyName)
        {
            DiskDetail d = new DiskDetail();
            d.FriendlyName = friendlyName == null ? "" : friendlyName;
            if (string.IsNullOrEmpty(d.FriendlyName))
            {
                d.Error = "未指定磁盘。";
                return d;
            }

            try
            {
                string name = d.FriendlyName.Replace("'", "''");
                StringBuilder s = new StringBuilder();
                s.Append("$p = Get-PhysicalDisk -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -eq '");
                s.Append(name);
                s.Append("' } | Select-Object -First 1; ");
                s.Append("if (-not $p) { Write-Output 'ERR|未找到该物理磁盘（可能已被移除）。'; return }; ");
                s.Append("$r = $p | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; ");
                s.Append("$k = $p | Get-Disk -ErrorAction SilentlyContinue; ");
                s.Append("$v = @([string]$p.FriendlyName, [string]$p.SerialNumber, [string]$p.FirmwareVersion, [string]$p.BusType, [string]$p.MediaType, [string]$p.Size, [string]$p.HealthStatus, [string]$p.SpindleSpeed); ");
                s.Append("if ($r) { $v += @([string]$r.Temperature, [string]$r.PowerOnHours, [string]$r.StartStopCycleCount, [string]$r.ReadErrorsTotal, [string]$r.WriteErrorsTotal, [string]$r.Wear) } else { $v += @('','','','','','') }; ");
                s.Append("if ($k) { $v += @([string]$k.PartitionStyle, [string]$k.NumberOfPartitions, [string]$k.IsSystem, [string]$k.IsBoot) } else { $v += @('','','','') }; ");
                s.Append("Write-Output ($v -join '|')");

                Shell.Result r = Shell.Run("powershell.exe",
                    "-NoProfile -Command \"" + s.ToString() + "\"", 60000);
                string text = (r.Output ?? "").Trim();
                if (text.Length == 0)
                {
                    d.Error = r.All.Trim();
                    if (d.Error.Length == 0) d.Error = "PowerShell 未返回内容（退出码 " + r.ExitCode + "）。";
                    return d;
                }

                string[] f = text.Split('|');
                if (f.Length > 0 && f[0] == "ERR")
                {
                    d.Error = f.Length > 1 ? f[1] : "未找到该物理磁盘。";
                    return d;
                }
                if (f.Length < 8)
                {
                    d.Error = "返回格式不完整：" + text;
                    return d;
                }

                d.FriendlyName = Pick(f, 0, d.FriendlyName);
                d.SerialNumber = Pick(f, 1, "");
                d.FirmwareVersion = Pick(f, 2, "");
                d.BusType = MapBusType(Pick(f, 3, ""));
                d.MediaType = MapMediaType(Pick(f, 4, ""));
                d.SizeText = FormatSize(Pick(f, 5, ""));
                d.HealthStatus = MapHealth(Pick(f, 6, ""));
                d.SpindleSpeed = MapSpindle(Pick(f, 7, ""));

                if (f.Length >= 14)
                {
                    d.Temperature = Pick(f, 8, "");
                    d.PowerOnHours = Pick(f, 9, "");
                    d.StartStopCycles = Pick(f, 10, "");
                    d.ReadErrors = Pick(f, 11, "");
                    d.WriteErrors = Pick(f, 12, "");
                    d.WearPercent = Pick(f, 13, "");
                }
                if (f.Length >= 18)
                {
                    d.PartitionStyle = MapPartitionStyle(Pick(f, 14, ""));
                    d.PartitionCount = Pick(f, 15, "");
                    d.IsSystem = MapBool(Pick(f, 16, ""));
                    d.IsBoot = MapBool(Pick(f, 17, ""));
                }
            }
            catch (Exception ex)
            {
                d.Error = ex.Message;
            }
            return d;
        }

        private static string Pick(string[] f, int i, string fallback)
        {
            if (i >= f.Length) return fallback;
            string v = (f[i] ?? "").Trim();
            return v.Length == 0 ? fallback : v;
        }

        /// <summary>STORAGE_BUS_TYPE 数字 → 中文（只列常见值，其余原样展示数字）。</summary>
        private static string MapBusType(string v)
        {
            switch (v)
            {
                case "1": return "SCSI";
                case "2": return "ATAPI";
                case "3": return "ATA";
                case "7": return "USB";
                case "8": return "RAID";
                case "9": return "iSCSI";
                case "10": return "SAS";
                case "11": return "SATA";
                case "12": return "SD";
                case "13": return "MMC";
                case "14": return "虚拟磁盘";
                case "16": return "存储空间";
                case "17": return "NVMe";
                case "18": return "SCM";
                case "19": return "UFS";
                case "": return "";
                default: return "总线类型 " + v;
            }
        }

        private static string MapMediaType(string v)
        {
            switch (v)
            {
                case "3": return "HDD（机械）";
                case "4": return "SSD（固态）";
                case "5": return "SCM";
                case "": return "";
                default: return "未指定";
            }
        }

        private static string MapHealth(string v)
        {
            switch (v)
            {
                case "0": return "健康";
                case "1": return "警告";
                case "2": return "故障";
                case "": return "";
                default: return "未知";
            }
        }

        private static string MapSpindle(string v)
        {
            if (v.Length == 0 || v == "0") return "—";   // SSD 恒为 0
            return v + " RPM";
        }

        private static string MapPartitionStyle(string v)
        {
            switch (v)
            {
                case "0": return "MBR";
                case "1": return "GPT";
                case "2": return "RAW（未初始化）";
                case "": return "";
                default: return "未知";
            }
        }

        private static string MapBool(string v)
        {
            if (v.Length == 0) return "";
            return string.Equals(v, "True", StringComparison.OrdinalIgnoreCase) ? "是" : "否";
        }

        private static string FormatSize(string bytesText)
        {
            ulong bytes;
            if (!ulong.TryParse(bytesText, out bytes)) return "";
            return SysInfo.FormatSize(bytes);
        }
    }
}
