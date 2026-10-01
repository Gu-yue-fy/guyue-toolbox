/* 文件说明：页面目录：全部功能页的注册表（键名 -> 构造器），新增页面只需在此登记一行。 */

using System;
using System.Collections.Generic;
using GuyueBox.Core;
using GuyueBox.UI.Views;
using GuyueBox.UI.Views.Apps;

namespace GuyueBox.UI
{
    /// <summary>
    /// 页面模块：功能页的自描述注册单元。
    /// 新增功能页只需在 <see cref="All"/> 清单里加一条数据——
    /// 侧栏大功能、顶部页签栏、命令面板与 UI 探针全部自动接入，无需改动其他代码。
    /// </summary>
    public sealed class PageModule
    {
        /// <summary>大功能名（同组连续排列，对应侧栏一项 + 一条页签栏）。</summary>
        public string Group;

        /// <summary>唯一导航键（NavigateTo / 快捷方式使用；取 Core 类型名小写）。</summary>
        public string Key;

        /// <summary>页签显示名称（必须与页面标题逐字一致）。</summary>
        public string Name;

        /// <summary>图标名（IconPainter 支持的 case；同组内不重复）。</summary>
        public string Icon;

        /// <summary>页面工厂（懒加载，仅在首次导航时创建实例）。</summary>
        public Func<ViewBase> Factory;
    }

    /// <summary>
    /// 功能页目录：全部页面模块的唯一注册点。
    /// </summary>
    public static class PageCatalog
    {
        public static readonly List<PageModule> All = new List<PageModule>
        {
            // ================= 概览 =================
            Module("概览", "dashboard", "系统概览", "dashboard", delegate { return new DashboardView(); }),
            Module("概览", "repair", "修复中心", "shield", delegate { return new RepairView(); }),

            // ================= 优化 =================
            Module("优化", "optimize", "全部优化项", "tune", delegate { return new OptimizeView(); }),

            // ================= 磁盘工具 =================
            Module("磁盘工具", "cleaner", "清理", "clean", delegate { return new CleanHubView(); }),
            Module("磁盘工具", "shredder", "文件粉碎", "trash", delegate { return new ShredderView(); }),
            Module("磁盘工具", "space", "空间分析", "disk", delegate { return new SpaceView(); }),
            Module("磁盘工具", "duplicate", "重复文件", "dup", delegate { return new DuplicateView(); }),
            Module("磁盘工具", "diskhealth", "磁盘健康", "warn", delegate { return new DiskHealthView(); }),
            Module("磁盘工具", "unlock", "解除占用", "unlock", delegate { return new UnlockView(); }),

            // ================= 电源计划 =================
            Module("电源计划", "powerplans", "电源计划", "power", delegate { return new PowerPlansView(); }),
            Module("电源计划", "powertuning", "高级电源设置", "bolt", delegate { return new PowerTuningView(); }),

            // ================= 内存优化 =================
            Module("内存优化", "memory", "内存优化", "memory", delegate { return new MemoryView(); }),

            // ================= 计时器分辨率 =================
            Module("计时器分辨率", "timer", "计时器分辨率", "clock", delegate { return new TimerView(); }),

            // ================= 性能跑分 =================
            Module("性能跑分", "benchmark", "性能跑分", "gauge", delegate { return new PerfRunView(); }),

            // ================= N卡设置 =================
            // （原「显卡」组按用户习惯改名，组内成员不变）
            Module("N卡设置", "nvidia", "N 卡设置", "pc", delegate { return new NvidiaCplView(); }),
            Module("N卡设置", "gpuspoof", "显卡伪装", "image", delegate { return new GpuSpoofView(); }),

            // ================= 进程管理 =================
            // （原「处理器」组改名，组内成员不变）
            Module("进程管理", "process", "进程管理", "process", delegate { return new ProcessView(); }),
            Module("进程管理", "cpucore", "CPU 核心调度", "cpu", delegate { return new CpuCoreView(); }),

            // ================= 系统 =================
            Module("系统", "sysinfo", "系统信息", "pc", delegate { return new HardwareView(); }),
            Module("系统", "restore", "系统还原点", "restore", delegate { return new RestoreView(); }),

            // ================= 启动与后台 =================
            Module("启动与后台", "startup", "启动项管理", "startup", delegate { return new StartupView(); }),
            Module("启动与后台", "services", "服务管理", "services", delegate { return new ServicesView(); }),
            Module("启动与后台", "tasks", "计划任务", "task", delegate { return new ScheduledTasksView(); }),
            Module("启动与后台", "device", "设备管理", "device", delegate { return new DeviceView(); }),
            Module("启动与后台", "driver", "驱动一览", "maint", delegate { return new DriverView(); }),
            Module("启动与后台", "features", "可选功能", "feature", delegate { return new FeatureView(); }),
            // 右键菜单属于"系统外壳/后台集成"这一类，与启动项/服务/计划任务同组
            Module("启动与后台", "contextmenu", "右键菜单", "menu", delegate { return new ContextMenuView(); }),

            // ================= 网络 =================
            Module("网络", "network", "网络诊断", "network", delegate { return new NetworkView(); }),
            Module("网络", "dns", "DNS 切换", "globe", delegate { return new DnsView(); }),
            Module("网络", "mtu", "MTU 优化", "search", delegate { return new MtuView(); }),

            // ================= 应用 =================
            Module("应用", "programs", "已安装程序", "apps", delegate { return new ProgramsView(); }),
            Module("应用", "appx", "应用精简", "recycle", delegate { return new AppxView(); }),

            // ================= 设置 =================
            Module("设置", "settings", "常规设置", "gear", delegate { return new SettingsView(); }),
            Module("设置", "packs", "优化包管理", "folder", delegate { return new PackView(); }),
            Module("设置", "about", "关于与更新", "info", delegate { return new AboutView(); }),
        };

        /// <summary>按 Key 查找模块。</summary>
        public static PageModule Find(string key)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Key, key, StringComparison.OrdinalIgnoreCase)) return All[i];
            }
            return null;
        }

        private static PageModule Module(string group, string key, string name, string icon, Func<ViewBase> factory)
        {
            PageModule m = new PageModule();
            m.Group = group;
            m.Key = key;
            m.Name = name;
            m.Icon = icon;
            m.Factory = factory;
            return m;
        }
    }
}
