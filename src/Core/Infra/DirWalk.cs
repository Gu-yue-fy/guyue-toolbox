using System;
using System.Collections.Generic;
using System.IO;

namespace GuyueBox.Core
{
    /// <summary>
    /// 目录安全遍历的公共实现。
    ///
    /// 为什么不用 <c>SearchOption.AllDirectories</c>：它遇到任何一个无权限 / 被占用的子目录
    /// 就会让整个枚举直接抛异常中断——表现为"目录体积被误报为 0""整个清理任务失败"。
    /// 这里改用显式栈 + 逐目录 try/catch：坏目录只跳过自己，其余照常处理。
    ///
    /// 原先这段逻辑散落在 RepairCenter / ProgramForcer 等多处各写一遍，现统一收在此处。
    /// </summary>
    public static class DirWalk
    {
        /// <summary>递归列出目录下的文件：逐目录容错，并跳过重解析点（联接 / 符号链接）防越界与死循环。</summary>
        /// <param name="dir">根目录；不存在时返回空列表</param>
        /// <param name="pattern">文件通配符（如 "*.lnk"）；传 null / 空表示全部文件</param>
        public static List<string> ListFiles(string dir, string pattern)
        {
            List<string> files = new List<string>();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return files;

            Stack<string> stack = new Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                string current = stack.Pop();

                try
                {
                    string[] fs = string.IsNullOrEmpty(pattern)
                        ? Directory.GetFiles(current)
                        : Directory.GetFiles(current, pattern);
                    for (int i = 0; i < fs.Length; i++) files.Add(fs[i]);
                }
                catch
                {
                    // 无权限 / 被占用：只跳过这一层的文件
                }

                string[] subs;
                try { subs = Directory.GetDirectories(current); }
                catch { continue; }

                for (int i = 0; i < subs.Length; i++)
                {
                    try
                    {
                        DirectoryInfo di = new DirectoryInfo(subs[i]);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch
                    {
                        continue;
                    }
                    stack.Push(subs[i]);
                }
            }

            return files;
        }
    }
}
