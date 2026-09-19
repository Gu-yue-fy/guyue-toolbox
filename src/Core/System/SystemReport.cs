﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GuyueBox.Core
{
    /// <summary>生成一份可读的本机系统报告（文本）。</summary>
    public static class SystemReport
    {
        public static string Build()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("古月工具包 · 系统报告");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(new string('=', 52));

            SystemSnapshot s;
            try { s = SysInfo.Capture(true, -1); }
            catch { s = new SystemSnapshot(); }

            sb.AppendLine();
            sb.AppendLine("【基本】");
            Append(sb, "计算机名", s.ComputerName);
            Append(sb, "当前用户", s.UserName);
            Append(sb, "运行权限", s.Elevated ? "管理员" : "普通用户");
            Append(sb, "操作系统", s.OsName);
            Append(sb, "系统版本", s.OsVersion);
            Append(sb, "系统架构", s.OsArch);

            sb.AppendLine();
            sb.AppendLine("【硬件】");
            Append(sb, "处理器", s.CpuName);
            Append(sb, "物理核心", s.CpuCores > 0 ? s.CpuCores + " 核" : "—");
            Append(sb, "逻辑线程", s.CpuThreads > 0 ? s.CpuThreads + " 线程" : "—");
            Append(sb, "显卡", s.GpuName);
            Append(sb, "主板", s.BaseBoard);
            Append(sb, "BIOS", s.BiosVersion);

            sb.AppendLine();
            sb.AppendLine("【内存】");
            Append(sb, "总内存", SysInfo.FormatSize(s.Memory.TotalBytes));
            Append(sb, "已用", SysInfo.FormatSize(s.Memory.UsedBytes));
            Append(sb, "可用", SysInfo.FormatSize(s.Memory.AvailBytes));
            Append(sb, "占用率", s.Memory.UsedPercent.ToString("0.0") + " %");

            sb.AppendLine();
            sb.AppendLine("【磁盘】");
            for (int i = 0; i < s.Disks.Count; i++)
            {
                DiskInfo d = s.Disks[i];
                sb.AppendLine("  " + d.Name + " " + d.Label + " (" + d.DriveType + ")");
                sb.AppendLine("    总量 " + SysInfo.FormatSize(d.TotalBytes) +
                    "，已用 " + SysInfo.FormatSize(d.UsedBytes) +
                    "，可用 " + SysInfo.FormatSize(d.FreeBytes) +
                    "（" + d.UsedPercent.ToString("0.0") + "%）");
            }
            if (s.Disks.Count == 0) sb.AppendLine("  （未检测到固定磁盘）");

            sb.AppendLine();
            sb.AppendLine("【运行】");
            Append(sb, "已运行时长", s.Uptime.ToString());
            Append(sb, "本次启动", s.BootTime == DateTime.MinValue ? "—" : s.BootTime.ToString("yyyy-MM-dd HH:mm:ss"));

            sb.AppendLine();
            sb.AppendLine("【软件与服务】");
            int programs = 0;
            try { programs = Programs.Scan(false).Count; } catch { }
            Append(sb, "已安装程序", programs + " 项");

            int rp = 0;
            try { rp = RestorePoints.List().Count; } catch { }
            Append(sb, "系统还原点", rp + " 个");

            int svcTotal = 0, svcRun = 0;
            try
            {
                List<ServiceInfo> svcs = ServiceManager.List();
                svcTotal = svcs.Count;
                for (int i = 0; i < svcs.Count; i++) if (svcs[i].IsRunning) svcRun++;
            }
            catch { }
            Append(sb, "Windows 服务", svcTotal + " 项（运行中 " + svcRun + "）");

            sb.AppendLine();
            sb.AppendLine(new string('=', 52));
            sb.AppendLine("由「古月工具包」生成。本报告仅作信息汇总，不含任何敏感凭据。");

            return sb.ToString();
        }

        public static bool Save(out string path, out string content)
        {
            content = Build();
            path = "";
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                path = Path.Combine(dir, "系统报告_" + stamp + ".txt");
                File.WriteAllText(path, content, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Append(StringBuilder sb, string label, string value)
        {
            sb.AppendLine("  " + label.PadRight(8, '　') + "：" + (value == null ? "" : value));
        }
    }
}
