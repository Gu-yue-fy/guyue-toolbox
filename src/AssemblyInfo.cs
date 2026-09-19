/* ============================================================
 * 文件说明：程序集属性（名称、版本、说明、版权）。
 *
 * 版本不在这里写死：从 <see cref="GuyueBox.AppInfo.Version"/> 常量拼接，
 * 保证"exe 属性里看到的版本"与"程序界面上显示的版本"永远是同一个值。
 * 改版本请改 src\AppInfo.cs。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// 三段版本号补上修订位（1.2.0 → 1.2.0.0）：AssemblyVersion 要求四段
[assembly: AssemblyVersion(GuyueBox.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(GuyueBox.AppInfo.Version + ".0")]

[assembly: AssemblyTitle("古月工具包")]
[assembly: AssemblyProduct("GuyueBox")]
[assembly: AssemblyCompany("GuyueBox")]
[assembly: AssemblyDescription("Windows 优化与系统维护工具集：注册表优化、服务与启动项管理、清理与修复，全部改动可一键还原。")]
[assembly: AssemblyCopyright("Copyright © GuyueBox")]

// 纯托管程序，不对外暴露 COM 类型：显式关闭可避免被误做 COM 注册
[assembly: ComVisible(false)]

// 界面与提示全为中文：声明中性资源语言，省掉每个资源查找的最后一次回退尝试
[assembly: NeutralResourcesLanguage("zh-CN")]
