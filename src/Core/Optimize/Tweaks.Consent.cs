/* ============================================================
 * 文件说明：优化项库「隐私授权」批量项。
 *           14 个 ConsentStore 子键同父键同值，合并为一个批量开关
 *           （一个 RegTweak + 14 条写入），备份/还原按值逐条生效。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System.Collections.Generic;
using Microsoft.Win32;

namespace GuyueBox.Core
{
    public static partial class TweakLibrary
    {
        private const string ConsentRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        /// <summary>ConsentStore 的 14 个隐私类别。</summary>
        private static readonly string[] ConsentKeys = new string[]
        {
            "radios",                    // 无线电（蓝牙/Wi-Fi 开关控制）
            "chat",                      // 短信 / 聊天
            "email",                     // 邮箱
            "appointments",              // 日历
            "contacts",                  // 联系人
            "webcam",                    // 相机
            "phoneCall",                 // 拨号
            "phoneCallHistory",          // 通话记录
            "trustedDevices",            // 受信任设备
            "activity",                  // 运动传感器
            "userDataTasks",             // 任务
            "userNotificationListener",  // 通知访问
            "userAccountInformation",    // 账户信息
            "appDiagnostics"             // 诊断数据
        };

        /// <summary>隐私授权批量项：拒绝应用访问隐私设备与数据。</summary>
        private static IEnumerable<ITweak> Consent()
        {
            List<ITweak> list = new List<ITweak>();

            RegTweak deny = new RegTweak();
            deny.IdValue = "consent_store_deny";
            deny.GroupValue = GPrivacy;
            deny.NameValue = "拒绝应用访问隐私设备与数据（14 类）";
            deny.DescriptionValue =
                "把「隐私和安全性 → 应用权限」中的 14 类权限统一设为「拒绝」：" +
                "无线电、聊天、邮箱、日历、联系人、相机、拨号、通话记录、受信任设备、" +
                "运动传感器、任务、通知访问、账户信息、诊断数据。\r\n" +
                "注意：这会影响依赖这些权限的商店应用（如相机 App、邮件、日历无法读取相应数据），" +
                "但不影响桌面程序与设备驱动。关闭本项即按备份逐条还原。";
            deny.RiskyValue = true;

            for (int i = 0; i < ConsentKeys.Length; i++)
            {
                deny.Enable.Add(RegWrite.Str(RegistryHive.CurrentUser,
                    ConsentRoot + "\\" + ConsentKeys[i], "Value", "Deny"));
            }

            list.Add(deny);
            return list;
        }
    }
}
