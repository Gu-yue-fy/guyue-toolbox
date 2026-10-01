# 古月工具箱（GuyueBox）

面向 Windows 的原生系统优化工具箱，**C# / WinForms（.NET Framework 4.x）自绘界面**。
编译产物是绿色单文件 `GuyueBox.exe`，不依赖任何运行时安装包；仓库自带 Roslyn 编译器（`tools\roslyn\`，
由 `tools\install-roslyn.ps1` 按需下载），没有它也会回退系统自带 `csc.exe`（C# 5）——源码统一按 **C# 5 语法**
书写，两条编译路径都能通过。

- 深空灰磨砂深色主题（可切浅色，6 种主题色，即时生效）
- **390 项优化**：每一项自动备份原值、可独立还原；硬件感知（N 卡 / A 卡 / Intel、大小核 CPU）
- **C# + PowerShell 协作**：批量系统操作（UWP 应用精简）由内置 PS 脚本承担，其余外部调用统一走受控命令边界
- 完全免费开源（MIT），无激活、无广告、无功能限制、不上传任何数据

## 免责声明（先读这段）

本工具会修改系统注册表、服务、计划任务与电源设置。我们做了多层保护（自动备份、写入校验、
谨慎项强制系统还原点、全部改动可一键还原），但**任何系统级修改都有风险**：

- 不了解的项不要开；谨慎项（橙色标记）应用前请阅读详情栏的「风险与恢复」
- 重要操作前工具会自动创建系统还原点，也建议你自行保留一份
- 请在自己的机器上自行承担使用结果；发现问题欢迎提 Issue

## 快速开始

到 [Releases](../../releases) 下载 `GuyueBox.exe`，右键**以管理员身份运行**即可（绿色单文件，
配置保存在注册表 `HKCU\Software\GuyueBox`，卸载即删）。

从源码构建：

```powershell
# 一键编译（构建后自动跑优化项目录自检）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 编译后直接启动
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Run

# 提交前全量校验：编译 + 自检 + 外部进程边界 + 36 页截图回归 + 性能巡检
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Verify

# 发版打包：编译 → SHA256 → 生成 bin\release\update.json / SHA256.txt
powershell -ExecutionPolicy Bypass -File .\tools\make-release.ps1 -Version 2.1.0 -Notes "..."
```

## 功能导览（14 个大功能 · 35 页）

侧栏只列 14 个**大功能**；同一大功能下的小功能是**内容区顶部的页签栏**（点页签即切换，`Ctrl+Tab`
在当前大功能内循环）。页面在 `src\UI\PageCatalog.cs` 统一注册——新增页面只加一行。

| 大功能 | 页签 |
| --- | --- |
| **概览** | 系统概览（体检评分 · **电竞模式** · 硬件/磁盘实时信息）· 修复中心（13 项检查 → 一键修复闭环） |
| **优化** | 全部优化项（390 项：分类 / 搜索 / 风险筛选 + 完整详情栏） |
| **磁盘工具** | 清理（垃圾 + 使用痕迹）· 文件粉碎 · 空间分析 · 重复文件 · 磁盘健康 · 解除占用（查出占用文件的进程并结束） |
| **电源计划** | 电源计划（计划一览与切换）· 高级电源设置（处理器 / 硬盘 / PCIe / 睡眠 / 显示 / 电池等子组，共 19 项隐藏电源项） |
| **内存优化** | 内存优化（实时占用 · 明细拆分 · 一键与定时释放 · 占用排行） |
| **计时器分辨率** | 计时器分辨率（实测寻优 + 手动指定，降低帧时间抖动） |
| **性能跑分** | 性能跑分（标准化分数 · 性能等级 · 机型对比 · 前后对比） |
| **N卡设置** | N 卡设置（3 套预设 + 14 项逐项可调）· 显卡伪装（型号伪装与一键还原） |
| **进程管理** | 进程管理（点列头排序 / 结束进程树 / 绑核）· CPU 核心调度（亲和性预设，即时生效） |
| **系统** | 系统信息（报告导出）· 系统还原点 |
| **启动与后台** | 启动项管理 · 服务管理（一键提速 + 备份恢复）· 计划任务 · 设备管理 · 驱动一览 · 可选功能 · 右键菜单 |
| **网络** | 网络诊断（端口占用 / 修复动作）· **DNS 切换**（120 条公共与运营商库，测速选优）· **MTU 优化** |
| **应用** | 已安装程序（含 winget 安装 / 强制卸载）· 应用精简（UWP） |
| **设置** | 常规设置 · 优化包管理 · 关于与更新 |

**Ctrl + K** 任意页面呼出命令面板：直达任意页面、进入/退出电竞模式、检查更新。
所有表格支持**点击列头排序**。

## 优化中心（390 项）

开关式注册表 / 服务 / 计划任务 / 电源设置优化，覆盖十类：游戏优化、性能优化、网络优化、
电源与启动、隐私与安全、系统精简、外观与体验、系统服务、极限性能、音频优化。
点击任意项在右侧查看完整详情：作用 / 原理 / 风险与恢复 / 将写入的注册表清单。

安全机制：

- 每项修改前自动备份原值，**关闭开关即还原**；写入后读回校验，失败自动回滚
- 谨慎项（Risky）应用前二次确认，并**强制创建系统还原点**（24 小时内已有则跳过，创建失败即取消）
- 硬件专属项在无关硬件上自动判定不适用、直接隐藏；只写已存在的键，不凭空造键
- 一键推荐集**永远不包含**谨慎项；电竞预设已剔除经证实的伪优化 / 负优化项

**本机专属推荐**：自动检测本机 CPU（Intel/AMD）与显卡（NVIDIA/AMD/Intel）组合，
列出仅适用于本机硬件的专属优化项，一键应用。

**方案管理**：保存当前优化组合为方案一键应用 / 同步；方案可导出为 `.stprofile` 文件分享，
也可在别的机器导入。

## 扩展包（不改代码给工具加功能）

把 JSON 清单放进 `packs\`（或 `%LOCALAPPDATA%\GuyueBox\packs\`）即可在优化中心出现新条目：
不装插件、不编译、不能执行任何命令（只能声明注册表写入），扩展面与风险都受控。

清单格式 v2 支持：**适用条件**（`when`：显卡厂商 / CPU 厂商 / 系统版本 / 需管理员）、
**显式还原**（`revert` 数组或每条写入的 `revertValue`）、**共享备份组**（`backup`）。
完整格式说明与示例见 [`packs\example-pack.json`](packs/example-pack.json)，
「设置 → 优化包管理」页可查看装载结果、重新扫描、导入导出。

## 命令行

```powershell
# 优化项目录自检（构建脚本自动跑；未提权时可手动运行）
GuyueBox.exe --selftest

# 无人值守应用优化方案：不带 --yes 只打印计划，不改系统
GuyueBox.exe --apply 我的方案.stprofile
GuyueBox.exe --apply 我的方案.stprofile --yes

# 性能巡检（每页构建 / 激活耗时）
GuyueBox.exe --perf-tour perf.log
```

`--apply` 只启用方案里列出的项（不碰方案外已启用的项）；含谨慎项时强制过还原点闸门，
创建失败即整体取消、一个键都不写。结果写入程序目录 `apply.log`，退出码 0 / 1 / 2。

## 回归验证（改 UI 后必跑）

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Verify   # 一键全量校验
# 或分步：
powershell -ExecutionPolicy Bypass -File .\ui-probe.ps1 -OutDir .\shots\candidate   # 全页自动截图
powershell -ExecutionPolicy Bypass -File .\tools\shot-diff.ps1 -A .\shots\baseline -B .\shots\candidate
bin\GuyueBox.exe --perf-tour bin\perf.log
```

截图基线**不入库**（截图包含本机的程序清单、硬件与网络信息，属个人隐私）：
首次在本机跑 `ui-probe.ps1 -OutDir shots\baseline` 生成即可；之后确认改动符合预期时，
把 `candidate` 覆盖成新的 `baseline`。`tools\shot-diff.ps1` 对展示实时数据的页面
（服务 / 进程 / 网络 / 启动项等）有单独的放宽阈值，表头注明了每页实际使用的阈值。

## 仓库布局

```
build.ps1            一键编译（+ 自检 + 脚本复制；-Verify 全量校验）
ui-probe.ps1         UI 自动截图探针
update.json          更新源（客户端轮询此文件，SHA256 校验）
LICENSE              MIT
src/                 Program.cs / app.manifest / Assets + Core/（引擎 + 业务域）+ UI/ + Scripts/
  Core/              Clean / Engine / Infra / Net / Optimize / System / Tool
  UI/                MainForm.* / PageCatalog / Theme / Gfx + Controls/ + Views/（35 页）
  Scripts/           随程序分发的 PowerShell（如 appx.ps1）
tools/               发版 / 回归工具链（roslyn\ 由 install-roslyn.ps1 下载，不入库）
packs/               外部优化包示例（编译时同步到 bin\packs）
shots/               截图基线（本地生成，含个人信息，不入库）
bin/                 编译产物（不入库）
```

## License

[MIT](LICENSE)——自由使用、修改与分发。
