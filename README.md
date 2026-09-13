# 古月工具包（GuyueBox）

一个面向 Windows 的原生系统优化工具，使用 **C# / WinForms（.NET Framework 4.x）** 编写。
**不需要安装任何 SDK**——直接用系统自带的 `csc.exe` 编译，单个 exe 即可运行。

- 深色主题、全自绘 UI（无图片 / 图标字体资源）
- 每一项优化**自动备份原值、可独立还原**
- 硬件感知：N 卡 / A 卡 / Intel、Intel / AMD CPU 的专属优化自动适配，不适用的项自动失效
- 完全免费开源，无激活、无功能限制

---

## 编译与运行

```powershell
# 一键编译（使用系统 csc.exe，无需 SDK）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 发版打包：编译 → SHA256 → 生成 update.json / SHA256.txt（输出到 bin\release）
powershell -ExecutionPolicy Bypass -File .\tools\make-release.ps1 -Version 1.2.0 -Notes "..."
```

产物：`bin\GuyueBox.exe`（绿色单文件，建议以管理员身份运行以解锁全部功能）。

## 功能结构（5 组 · 11 页）

| 分组 | 页面 |
| --- | --- |
| **首页** | 系统概览（实时资源统计 · 一键体检评分 · 0.5ms 高精度计时器 · 硬件信息 · 导出报告） |
| **优化** | 优化中心（176 项） · 电源计划（硬件感知推荐） · 性能基准（9 项测试 + 排行榜） |
| **清理与磁盘** | 清理与磁盘（垃圾清理 / 隐私清理 / 文件粉碎 / 空间分析 / 重复文件 / 磁盘健康 SMART，六合一） |
| **系统管理** | 系统配置（服务 / 计划任务 / 启动项 / 右键菜单 / 设备 / 系统还原点 / 系统维护，七合一） · 进程管理（优先级 / 核心绑定） · 已安装程序（含 Winget 安装） · 网络中心（状态卡 / 诊断 / DNS / 端口） |
| **通用** | 软件设置 · 关于与更新（GitHub 自动更新） |

## 优化中心（176+ 项）

开关式注册表 / 服务优化，覆盖八类：

| 类别 | 数量 | 说明 |
| --- | --- | --- |
| 游戏优化 | 68 | MMCSS 调度、DWM 呈现、输入链路、USB/网卡省电、内核低延迟、DSCP QoS、MSI 中断、磁盘 LPM 等 |
| 系统服务 | 33 | 遥测 / 索引 / Xbox / 智能卡等，含推荐禁用清单 |
| 性能优化 | 16 | NTFS 加速、Svchost 合并、保留存储、更新策略 |
| 网络优化 | 12 | EEE/RSC/ECN、TCP 启发式、Nagle、BBRv2、IPv6 |
| 隐私与安全 | 11 | 遥测、广告 ID、Copilot、Recall AI 等 |
| 系统精简 | 17 | 推送内容、传递优化、错误报告、步骤记录器、SMB1、手机连接、Xbox 服务、Edge 预启动等 |
| 外观与体验 | 6 | 扩展名显示、经典右键、任务栏偏好 |
| 电源与启动 | 5 | 休眠 / 快速启动 / 高性能计划 / USB 选择性暂停 |
| 极限性能 | 9 | 内核缓解 / Defender / Svchost 合并（谨慎项，仅推荐专用游戏机） |

安全机制：
- 每项修改前自动备份注册表原值，**关闭开关即还原**
- 谨慎项（Risky）应用前二次确认
- 厂商专属项（N 卡 / A 卡 / Intel）在无关硬件上**自动判定不适用**，拒绝写入无效键
- 方案库：保存当前优化组合为方案、一键应用/同步、导出导入

## 界面导览

- **左侧导航栏**：按功能分组的全部页面入口，底部显示版本号与当前权限状态（绿点=管理员）。
- **顶栏**：软件 Logo 与当前页面标题；标题右侧是该页的专属操作按钮（如「一键体检」「整理内存」）。
- **命令面板**：任意页面按 `Ctrl + K`，输入名称直达功能（不想翻菜单时的快捷方式）。
- **页面提示条**：每页顶部的说明性提示，只占一行；右上角有 `×` 关闭按钮——点关后按页面记忆，**之后不再出现**，不会反复挡视野。
- **优化中心**：左上分类切换 + 搜索框 + 「只看已启用/未启用/推荐/谨慎」快捷筛选；每行一个开关，右键行可查看详情。
- **状态栏**：底部常驻，右侧显示管理员权限状态；需要提权的操作会自动触发 UAC 确认。
- **主题与动画**：「软件设置」页可切换 6 种主题色、关闭动画（低配机更顺滑）。

## 核心文件说明

| 文件 | 职责 |
| --- | --- |
| `src/Program.cs` | 入口：单实例、全局异常兜底、提权声明 |
| `src/UI/MainForm.cs` | 主窗体框架与页面路由 |
| `src/UI/PageCatalog.cs` | 页面注册表（加页只需登记一行） |
| `src/UI/Views/Optimize/ViewBase.cs` | 页面基类（标题/按钮区/布局器/滚动） |
| `src/UI/Views/Optimize/OptimizeView.cs` | 优化中心（搜索/筛选/开关执行） |
| `src/Core/Optimize/Tweaks.cs` | 优化项声明库（176 项） |
| `src/Core/Infra/RegHelper.cs` | 注册表备份-写入-还原闭环 |
| `src/Core/Infra/UpdateChecker.cs` | 更新检查与静默升级 |
| `src/Core/Infra/AppSettings.cs` | 用户设置持久化 |
| `src/Core/System/SysInfo.cs` | 系统信息采集（WMI/原生 API） |
| `src/UI/Controls/Controls.cs` | 自绘控件库（按钮/状态卡/提示条等） |
| `src/UI/Controls/ScrollHost.cs` | 自绘滚动容器 |

每个核心源码文件的**文件头部**都有同款「文件说明」注释块，IDE 里打开即可看到。

## 架构

```
入口 Program.cs（单实例 / 全局异常兜底 / 提权声明）
  └─ UI   页面目录 PageCatalog（数据驱动加页）
          命令层 CommandHub（每个操作一个命令，统一执行与异常兜底）
          自绘控件库（DarkGrid / ScrollHost / 主题 / 图标绘制）
  └─ Core 五大业务域
          Optimize（优化项库，ITweakProvider 可扩展）
          Clean（垃圾 / 隐私 / 粉碎 / 重复 / 空间）
          Net（诊断 / DNS / 端口）
          System（服务 / 进程 / 设备 / 启动项 / 还原点 / 系统信息）
          Tool（性能基准）
  └─ Infra 基础设施
          RegHelper（备份-写入-还原闭环）
          Shell（命令执行，控制台编码自适应）
          UpdateChecker（GitHub Releases 更新）
```

## 仓库布局

```
build.ps1            一键编译（系统 csc，无 SDK 依赖）
update.json          更新源（客户端轮询此文件）
src/                 源码（Program.cs / app.manifest / AssemblyInfo.cs + Core/ + UI/）
tools/               工具链（发版打包 / 更新源生成 / 图标生成）
bin/                 编译产物（gitignore）
bin/release/         发版包（exe + update.json + SHA256）
```

工具链（`tools/`）：`make-release.ps1` 发版打包（编译 → 校验 → 生成 update.json / SHA256）、`make-update-json.ps1` 更新源生成、`make-icon.ps1` 图标生成。

## 更新机制

采用 GitHub Releases 发布：

1. 打 tag（如 `v1.0.1`）建 Release，上传 exe 作为 asset
2. 更新 `update.json`（version / url / sha256 / notes）提交到仓库
3. 老版本用户在「关于与更新」一键检查、下载（自动 SHA256 校验）、静默替换重启

---

## License

MIT——自由使用、修改与分发。
