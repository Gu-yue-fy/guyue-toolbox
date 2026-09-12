# 古月工具包（GuyueBox）

一个面向 Windows 的原生系统优化工具包，使用 **C# / WinForms（.NET Framework 4.x）** 编写。
**不需要安装任何 SDK**——直接用系统自带的 `csc.exe` 编译，产出单个绿色 exe 即可运行。

- 深色主题、全自绘 UI（无图片 / 图标字体资源），6 款主题色可切换
- **170+ 项**开关式注册表 / 服务优化，每一项**改动前自动备份原值、关开关即还原**
- 硬件感知：N 卡 / A 卡 / Intel、Intel / AMD CPU 的专属项自动适配，无关硬件的项自动判定「不适用」，拒绝写入无效键
- 高危项应用前二次确认，并**自动创建系统还原点**（24 小时内去重）
- 完全免费开源，无激活、无功能限制、无广告

> 官网：https://gu-yue-fy.github.io/guyue-toolbox-site/ · 发行版：https://github.com/Gu-yue-fy/guyue-toolbox/releases

---

## 编译与运行

```powershell
# 一键编译（使用系统 csc.exe，无需 SDK）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 一键验证：编译 + 冒烟测试 + 全页面 UI 探针
powershell -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

产物：`bin\GuyueBox.exe`（绿色单文件，建议以管理员身份运行以解锁全部功能）。

## 功能结构（5 组 · 11 个功能页）

> 页面由 `src/UI/PageCatalog.cs` 数据驱动注册；其中「清理与磁盘」「系统配置」为「六合一」标签页，
> 11 个注册页共计 **21 个子页面**。

| 分组 | 页面 |
| --- | --- |
| **首页** | 系统概览（实时资源统计 · 硬件信息 · 一键体检评分 · 0.5ms 高精度计时器 · 导出报告） |
| **优化** | 优化中心（170+ 项） · 电源计划（含「终极性能」，硬件感知推荐） · 性能基准（9 项测试 + 综合评分） |
| **清理与磁盘** | 六合一：垃圾清理 · 隐私清理 · 文件粉碎 · 空间分析 · 重复文件 · 磁盘健康（SMART） |
| **系统管理** | 系统配置（六合一：服务 · 计划任务 · 启动项 · 右键菜单 · 设备管理 · 系统还原点） · 进程管理 · 已安装程序 · 网络中心 |
| **通用** | 软件设置（6 色主题等） · 关于与更新 |

## 优化中心（170+ 项）

开关式注册表 / 服务优化，覆盖 **9 大类**（数量以代码 `src/Core/Optimize/Tweaks.cs` 为准，合计 170+）：

| 类别 | 约计 | 说明 |
| --- | --- | --- |
| 游戏优化 | 68 | MMCSS 调度、DWM 呈现、输入链路、USB / 网卡省电、MSI 中断、内核低延迟、DSCP QoS 等 |
| 系统服务 | 33 | 遥测 / 索引 / Xbox / 智能卡 / 诊断等，含推荐禁用清单 |
| 性能优化 | 16 | NTFS 加速、Svchost 合并、保留存储、更新策略 |
| 网络优化 | 12 | EEE / RSC / ECN、TCP 启发式、Nagle、BBRv2、IPv6 |
| 隐私与安全 | 11 | 遥测、广告 ID、Copilot、Recall AI 等 |
| 系统精简 | 10 | 推送内容、传递优化、错误报告、开始菜单推荐区 |
| 极限性能 | 9 | 内核缓解 / 漏洞驱动黑名单 / BCD 精简 / Defender 等（谨慎项，仅推荐离线或专用游戏机） |
| 外观与体验 | 6 | 显示扩展名、经典右键、任务栏偏好、关闭 shake |
| 电源与启动 | 5 | 休眠、高性能计划、快速启动、终极性能、Modern Standby |

安全机制：
- 每项修改前自动备份注册表 / 服务原值，**关闭开关即还原**（多档共享备份，避免污染）
- 谨慎项（Risky）应用前二次确认，并自动创建系统还原点
- 厂商专属项（N 卡 / A 卡 / Intel、Intel / AMD）在无关硬件上**自动判定不适用**，拒绝写入无效键

## 系统要求

- **系统**：Windows 7 / 8.1 / 10 / 11（32 / 64 位均可，AnyCPU）
- **运行时**：.NET Framework 4.x（系统自带，无需安装 SDK）
- **权限**：建议管理员身份运行（`app.manifest` 声明 `requireAdministrator`；非提权时状态栏与首页会给出提示）
- **快捷键**：`F5` 刷新当前页、`Ctrl+Tab` / `Ctrl+Shift+Tab` 切换页面

## 更新机制

采用 GitHub Releases + `update.json` 清单：

1. 打 tag（如 `v1.2.0`）建 Release，上传编译好的 exe 作为 asset
2. 仓库根目录的 `update.json` 写入本次发布信息：
   ```json
   {
     "version": "1.2.0",
     "url": "https://github.com/Gu-yue-fy/guyue-toolbox/releases/download/v1.2.0/GuyueBox.exe",
     "sha256": "<安装包 SHA256>",
     "notes": "本次更新说明"
   }
   ```
3. 桌面端在「关于与更新」读取该清单（默认地址 `https://raw.githubusercontent.com/Gu-yue-fy/guyue-toolbox/main/update.json`，可用注册表 `HKCU\Software\GuyueBox\UpdateUrl` 覆盖），发现新版本后一键下载；
   **安装包强制 SHA256 校验**，缺失即拒装、不匹配即取消，随后静默替换并重启。

## 架构

```
入口 Program.cs（单实例 / 全局异常兜底 / 提权声明）
  └─ UI   页面目录 PageCatalog（数据驱动加页）
          命令层 AppCommand（每个操作一个命令，统一执行与异常兜底）
          自绘控件库（DarkGrid / ScrollHost / NavSideItem / 主题 / 图标绘制）
  └─ Core 五大业务域
          Optimize（优化项库，ITweakProvider 可扩展）
          Clean（垃圾 / 隐私 / 粉碎 / 重复 / 空间）
          Net（诊断 / DNS / 端口）
          System（服务 / 计划任务 / 启动项 / 右键菜单 / 设备 / 进程 / 还原点 / 系统信息）
          Tool（性能基准 / 右键菜单）
  └─ Infra 基础设施
          RegHelper（备份 - 写入 - 还原闭环）
          Shell（命令执行，控制台编码自适应）
          UpdateChecker（GitHub 更新，强制 SHA256 校验）
```

工具链（`tools/`）：`verify.ps1` 一键验证、`run-ui-probe.ps1` 全页面探针（截图 / 布局体检 / 绘制基准）、`make-release.ps1` 发版打包、`make-update-json.ps1` 生成 `update.json`。

---

## License

MIT——自由使用、修改与分发。
