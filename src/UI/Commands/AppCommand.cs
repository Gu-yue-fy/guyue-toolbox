using System;
using System.Collections.Generic;
using System.Diagnostics;
using SysToolbox.UI.Views;

namespace SysToolbox.UI.Commands
{
    /// <summary>
    /// 应用命令：一个用户操作（按钮/快捷键/面板入口）的独立负责单元。
    /// 约定：
    ///  - Id 全库唯一，命名规范「页面key.动作」，如 dashboard.health_check；
    ///  - 每个页面的命令集中定义在该页配套的 Commands 文件里，View 只负责绑定按钮；
    ///  - 执行统一经 <see cref="CommandHub.Run"/>，异常兜底、状态提示不再散落各处。
    /// </summary>
    public sealed class AppCommand
    {
        /// <summary>唯一命令号（页面key.动作）。</summary>
        public string Id;

        /// <summary>显示名称（与按钮文案一致）。</summary>
        public string Title;

        /// <summary>执行体。owner 为触发命令的页面，可调用其 SetSubtitle 等成员。</summary>
        public Action<ViewBase> Execute;
    }

    /// <summary>
    /// 命令中枢：全库命令的注册表与统一执行入口。
    /// </summary>
    public static class CommandHub
    {
        private static readonly Dictionary<string, AppCommand> _map =
            new Dictionary<string, AppCommand>(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册命令。命令号重复时忽略（页面可能重复构造），命令缺少执行体则拒绝并记录。</summary>
        public static void Register(AppCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.Id)) return;
            if (command.Execute == null)
            {
                Debug.WriteLine("命令缺少执行体，已拒绝注册: " + command.Id);
                return;
            }
            if (_map.ContainsKey(command.Id))
            {
                return; // 同 Id 重复注册：忽略，保持首个（幂等）
            }
            _map.Add(command.Id, command);
        }

        /// <summary>批量注册。</summary>
        public static void RegisterAll(IEnumerable<AppCommand> commands)
        {
            foreach (AppCommand c in commands)
            {
                Register(c);
            }
        }

        public static AppCommand Find(string id)
        {
            AppCommand c;
            return _map.TryGetValue(id, out c) ? c : null;
        }

        /// <summary>已注册命令总数（探针/自检用）。</summary>
        public static int Count
        {
            get { return _map.Count; }
        }

        /// <summary>
        /// 统一执行入口：执行体抛出的任何异常都在这里兜底（状态栏提示 + 不崩溃），
        /// 各命令内部无需重复 try-catch。
        /// </summary>
        public static void Run(ViewBase owner, AppCommand command)
        {
            if (command == null || command.Execute == null) return;
            try
            {
                command.Execute(owner);
            }
            catch (Exception ex)
            {
                string msg = "操作「" + command.Title + "」执行失败：" + ex.Message;
                if (owner != null)
                {
                    owner.Notify(msg, Theme.Danger);
                }
                Debug.WriteLine("命令异常 " + command.Id + ": " + ex);
            }
        }
    }
}
