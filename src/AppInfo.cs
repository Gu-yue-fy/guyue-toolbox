/* ============================================================
 * 文件说明：版本号的唯一来源。
 *
 * 为什么单独一个文件：版本号此前在四处各写一遍（程序集属性、MainForm 常量、
 * update.json、发版脚本的命令行参数），漏改一处就会造成"程序内显示旧版本"
 * 或"更新检查把新包判成旧包"。现在只有这里写版本，其余全部由它派生：
 *   - 程序集版本：src\AssemblyInfo.cs（编译期常量拼接）
 *   - 界面显示：MainForm.AppVersion（const 转发，调用点无需改动）
 *   - 发版清单：tools\make-update-json.ps1 由命令行传入，build.ps1 会校验两者一致
 *
 * 改版本时只需改这里的 Version，然后跑 tools\make-update-json.ps1。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

namespace GuyueBox
{
    internal static class AppInfo
    {
        /// <summary>语义化版本号（不含末尾的 .0 修订位；程序集版本由它补全为四段）。</summary>
        public const string Version = "1.2.0";
    }
}
