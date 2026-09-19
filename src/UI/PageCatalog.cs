/* ============================================================
 * 文件说明：页面目录：全部功能页的注册表（键名 -> 构造器），新增页面只需在此登记一行。
 * 项目：古月工具包（GuyueBox）
 *
 * 栏目结构按「领域」而非「工具分类」组织，
 * 每个优化领域是一个独立模块页（侧栏一级栏目），页内只有状态筛选与逐项开关。
 * ============================================================ */

using System;
using System.Collections.Generic;
using GuyueBox.Core;
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
            // ================= 概览 =================
            Module("概览", "dashboard", "系统概览", "dashboard", delegate { return new DashboardView(); }),
            // 修复中心：把"只看只扫"收口成"扫描 → 一键修复"的闭环，
            // 放概览组是因为它和系统概览同属"全局健康"入口
            Module("概览", "repair", "修复中心", "shield", delegate { return new RepairView(); }),

            // ================= 性能 =================
            // 全部优化项：跨领域的总表。
            // 原先另有 10 个「按分组过滤」的同类页面（性能加速 / 极限性能 / 游戏优化 / 网络优化 /
            // 系统服务 / 电源与启动 / 隐私与安全 / 系统精简 / 外观与体验 / 音频优化）——
            // 它们与页面内的分类 chip 完全等价，只会把侧栏撑成 11 个同类入口。
            // 已收敛：分类筛选在页内完成；概览磁贴改用 "optimize|分组名" 直接落到对应分类。
            Module("性能", "optimize", "全部优化项", "tune", delegate { return new OptimizeView(); }),
            // 内存优化：设计把"即时清理工具"单独成页——它改的是内存当前状态，
            // 与上面几页"改注册表策略"不是一类操作
            Module("性能", "memory", "内存优化", "memory", delegate { return new MemoryView(); }),
            // CPU 核心调度不再单列：它已并入「系统 → 进程与核心」的第二个页签，
            // 否则同一能力会在侧栏与页签里各出现一次
            // 性能基准 + 高精度计时器合成一页：两者都是"跑一次看结果"的低频工具，
            // 分列两个侧栏项会让侧栏塞满同类入口（能力不减，只是不再各占一格）
            Module("性能", "bench", "性能测试", "gauge", delegate
            {
                return new TabbedView("性能测试",
                    "跑分基准与计时器精度，低频工具集中在一处",
                    new string[] { "性能基准", "高精度计时器" },
                    new Func<ViewBase>[] {
                        delegate { return new BenchmarkView(); },
                        delegate { return new TimerView(); }
                    });
            }),

            // ================= 网络 =================
            // 网络诊断与 hosts 编辑同属"网络排查"：合并为一页两个页签
            Module("网络", "network", "网络中心", "network", delegate
            {
                return new TabbedView("网络中心",
                    "联网诊断 / DNS 与协议栈修复 / hosts 编辑",
                    new string[] { "网络诊断", "Hosts 编辑" },
                    new Func<ViewBase>[] {
                        delegate { return new NetworkView(); },
                        delegate { return new HostsEditorView(); }
                    });
            }),

            // ================= 系统 =================
            Module("系统", "power", "电源计划", "power", delegate { return new PowerPlansView(); }),
            Module("系统", "services", "系统配置", "services", delegate
            {
                return new TabbedView("系统配置",
                    "服务 / 计划任务 / 启动项 / 右键菜单 / 设备 / 还原点，系统内容集中管理",
                    new string[] { "服务管理", "计划任务", "启动项管理", "右键菜单", "设备管理", "系统还原点" },
                    new Func<ViewBase>[] {
                        delegate { return new ServicesView(); },
                        delegate { return new ScheduledTasksView(); },
                        delegate { return new StartupView(); },
                        delegate { return new ContextMenuView(); },
                        delegate { return new DeviceView(); },
                        delegate { return new RestoreView(); }
                    });
            }),
            // 进程管理 + CPU 核心调度合并：核心调度本来就是"选中某个进程后改它的属性"，
            // 与进程列表是同一件事的两半（审计也判定两页重叠）
            Module("系统", "process", "进程与核心", "process", delegate
            {
                return new TabbedView("进程与核心",
                    "查看与结束进程、设置进程的 CPU 亲和性与优先级",
                    new string[] { "进程列表", "CPU 核心调度" },
                    new Func<ViewBase>[] {
                        delegate { return new ProcessView(); },
                        delegate { return new CpuCoreView(); }
                    });
            }),
            Module("系统", "programs", "已安装程序", "apps", delegate { return new ProgramsView(); }),
            Module("系统", "hardware", "系统信息", "info", delegate { return new HardwareView(); }),

            // ================= 清理 =================
            Module("清理", "cleanup", "清理与磁盘", "clean", delegate
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

            // ================= 设置 =================
            // 优化包管理并入软件设置：它是低频配置项，不该单占一个侧栏格
            Module("设置", "settings", "软件设置", "feature", delegate
            {
                return new TabbedView("软件设置",
                    "界面偏好与外部优化包管理",
                    new string[] { "常规设置", "优化包管理" },
                    new Func<ViewBase>[] {
                        delegate { return new SettingsView(); },
                        delegate { return new PackView(); }
                    });
            }),
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
