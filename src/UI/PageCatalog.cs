using System;
using System.Collections.Generic;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{
    /// <summary>
    /// 页面模块：功能页的自描述注册单元。
    /// 新增功能页只需在 <see cref="All"/> 清单里加一条数据——
    /// 侧栏分组、页签、命令面板与 UI 探针全部自动接入，无需改动其他代码。
    /// </summary>
    public sealed class PageModule
    {
        /// <summary>侧栏分组名（同组连续排列）。</summary>
        public string Group;

        /// <summary>唯一导航键（NavigateTo / 快捷方式使用，建议小写英文）。</summary>
        public string Key;

        /// <summary>显示名称。</summary>
        public string Name;

        /// <summary>图标名（IconPainter 支持的 case）。</summary>
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
            // ---- 首页 ----
            Module("首页", "dashboard", "系统概览", "dashboard", delegate { return new DashboardView(); }),

            // ---- 优化（性能相关聚拢）----
            Module("优化", "optimize", "优化中心", "tune", delegate { return new OptimizeView(); }),
            Module("优化", "power", "电源计划", "power", delegate { return new PowerPlansView(); }),
            Module("优化", "bench", "性能基准", "gauge", delegate { return new BenchmarkView(); }),

            // ---- 清理与磁盘（六合一）----
            Module("清理与磁盘", "cleanup", "清理与磁盘", "clean", delegate
            {
                return new TabbedView("清理与磁盘",
                    "垃圾 / 隐私 / 粉碎 / 空间 / 重复文件 / 磁盘健康，一站式清理与体检",
                    new string[] { "垃圾清理", "隐私清理", "文件粉碎", "空间分析", "重复文件", "磁盘健康" },
                    new Func<ViewBase>[] {
                        delegate { return new CleanerView(); },
                        delegate { return new PrivacyView(); },
                        delegate { return new ShredderView(); },
                        delegate { return new SpaceView(); },
                        delegate { return new DuplicateView(); },
                        delegate { return new DiskHealthView(); }
                    });
            }),

            // ---- 系统管理（服务/任务/启动项/右键/设备/还原点 六合一 + 进程 + 程序 + 网络）----
            Module("系统管理", "services", "系统配置", "services", delegate
            {
                return new TabbedView("系统配置",
                    "服务 / 计划任务 / 启动项 / 右键菜单 / 设备 / 还原点 / 系统维护，系统内容集中管理",
                    new string[] { "服务管理", "计划任务", "启动项管理", "右键菜单", "设备管理", "系统还原点", "系统维护" },
                    new Func<ViewBase>[] {
                        delegate { return new ServicesView(); },
                        delegate { return new ScheduledTasksView(); },
                        delegate { return new StartupView(); },
                        delegate { return new ContextMenuView(); },
                        delegate { return new DeviceView(); },
                        delegate { return new RestoreView(); },
                        delegate { return new MaintenanceView(); }
                    });
            }),
            Module("系统管理", "process", "进程管理", "process", delegate { return new ProcessView(); }),
            Module("系统管理", "programs", "已安装程序", "apps", delegate { return new ProgramsView(); }),
            Module("系统管理", "network", "网络中心", "network", delegate { return new NetworkView(); }),

            // ---- 通用 ----
            Module("通用", "settings", "软件设置", "feature", delegate { return new SettingsView(); }),
            Module("通用", "about", "关于与更新", "info", delegate { return new AboutView(); }),
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
