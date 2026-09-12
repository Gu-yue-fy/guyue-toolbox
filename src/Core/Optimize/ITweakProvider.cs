using System.Collections.Generic;

namespace SysToolbox.Core
{
    /// <summary>
    /// 优化项 Provider：一组优化项的独立装配单元。
    /// 内置分组（游戏/网络/性能…）由 TweakLibrary 自带；扩展新的优化组时
    /// 实现 ITweakProvider 并调用 TweakLibrary.RegisterProvider 注册即可，
    /// 优化中心、一键推荐与方案库自动接入，无需改动既有代码。
    /// </summary>
    public interface ITweakProvider
    {
        /// <summary>返回本 Provider 提供的全部优化项（每次调用返回新列表；Id 必须全库唯一）。</summary>
        IEnumerable<ITweak> Provide();
    }
}
