# 古月工具包（GuyueBox）

![License](https://img.shields.io/badge/license-MIT-green)
![Platform](https://img.shields.io/badge/platform-Windows%207--11-0078D6)
![Framework](https://img.shields.io/badge/framework-.NET%20Framework%204.x-512BD4)
![Language](https://img.shields.io/badge/language-C%23%20%2F%20WinForms-178600)
![Auto-update](https://img.shields.io/badge/auto--update-SHA256%20verified-orange)

一个面向 Windows 的原生系统优化与隐私工具，使用 **C# / WinForms（.NET Framework 4.x）** 编写。
不需要安装任何 SDK —— 用系统自带的 `csc.exe` 即可编译，产物是单个 `GuyueBox.exe`，绿色运行。

![GuyueBox 主界面](https://gu-yue-fy.github.io/guyue-toolbox-site/assets/img/screenshot-main.png)

- 深色主题、全自绘 UI（无图片 / 图标字体资源），6 种主题色可选
- 每一项优化**自动备份原值、可独立还原**，谨慎项启用前二次确认
- 硬件感知：N 卡 / A 卡 / Intel、Intel / AMD CPU 的专属优化自动适配，不适用的项自动失效
- 完全免费开源（MIT），无激活、无功能限制
- 内置自动更新，下载后**强制 SHA256 校验**

---

## 功能结构（6 分组 · 14 个功能页 · 28 个功能面板）

| 分组 | 页面 | 说明 |
| --- | --- | --- |
| **概览** | 系统概览 | 实时资源统计、一键体检评分、硬件信息、导出报告 |
| | 修复中心 | 「诊断 → 修复」闭环：健康评分环、10 项扫描、单项或一键修复（只做清缓存 / 刷新 DNS 等安全动作） |
| **性能** | 全部优化项 | 170+ 项开关式优化（10 大类）总表，页内按分类筛选；支持外部优化包扩展 |
| | 内存优化 | 一键回收待机列表与空闲工作集内存，占用排行与内存明细（即时工具，不改注册表） |
| | 性能测试（2 标签页） | 性能基准（9 项测试，保存历史）＋ 高精度计时器（寻优系统定时器精度，仅运行期有效、退出自动还原） |
| **网络** | 网络中心（2 标签页） | 网络诊断（刷新 DNS 缓存 / 续约 IP / Winsock·TCP-IP 重置 / DNS 预设快切）＋ Hosts 编辑 |
| **系统** | 电源计划 | 查看与切换系统电源计划，按硬件给出推荐方案 |
| | 系统配置（6 标签页） | 服务管理 / 计划任务 / 启动项管理 / 右键菜单 / 设备管理 / 系统还原点 |
| | 进程与核心（2 标签页） | 进程列表（结束进程 / 释放内存）＋ CPU 核心调度（进程亲和性与优先级，即时生效不写注册表） |
| | 已安装程序 | 列出已装软件，可打开安装位置或卸载 |
| | 系统信息 | 处理器（含大小核判定）/ 显卡 / 主板固件 / 系统版本 / 磁盘明细，可导出报告 |
| **清理** | 清理与磁盘（6 标签页） | 垃圾清理（10 类）/ 隐私清理 / 文件粉碎（1~7 次覆盖）/ 空间分析 / 重复文件（SHA256 去重）/ 磁盘健康（SMART） |
| **设置** | 软件设置（2 标签页） | 常规设置（主题色、动画、启动检查更新）＋ 优化包管理（外部优化包装载状态） |
| | 关于与更新 | 检查并应用 GitHub 自动更新 |

## 优化项（170+ 项 · 10 大类）

开关式注册表 / 服务优化，覆盖十类：性能优化、外观与体验、隐私与安全、系统服务、电源与启动、游戏优化、网络优化、系统精简、音频优化、极限性能。外部优化包（`packs\*.json`）可再动态扩展，不计入内置项。

安全机制：
- 每项修改前自动备份注册表原值，**关闭开关即还原**
- 谨慎项（Risky）应用前二次确认
- 厂商专属项（N 卡 / A 卡 / Intel）在无关硬件上**自动判定不适用**，拒绝写入无效键
- 「一键优化」会一次性应用所有标记为「推荐」且未启用的项，并在执行前列出清单请用户确认；仅高危单项在启用时自动创建系统还原点

## 编译与运行

```powershell
# 一键编译（使用系统 csc.exe，无需 SDK）
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 编译并直接运行
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Run
```

产物：`bin\GuyueBox.exe`（绿色单文件，建议以管理员身份运行以解锁全部功能）。

图标资源由 `tools\make-icon.ps1` 生成。

## 仓库布局

```
build.ps1              一键编译（系统 csc，无 SDK 依赖）
update.json            更新源（客户端轮询此文件）
src/                   源码（Program.cs / app.manifest / AssemblyInfo.cs + Core/ + UI/）
tools/                 工具链（图标生成）
bin/                   编译产物（gitignore）
```

## 更新机制

采用 GitHub Releases 发布：

1. 打 tag（如 `v1.2.0`）建 Release，上传 `GuyueBox.exe` 作为 asset
2. 更新仓库根 `update.json`（字段：`version` / `url` / `sha256` / `notes`）并提交
3. 老版本用户在「关于与更新」一键检查、下载（**自动 SHA256 校验**）、替换重启

## 系统要求

- Windows 7 / 8.1 / 10 / 11
- .NET Framework 4.x（系统自带，无需另行安装）
- 建议以管理员身份运行（部分优化与系统管理功能需要提权）

## License

MIT —— 自由使用、修改与分发。
