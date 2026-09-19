using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>
    /// 注册表操作日志（内存环形缓冲，容量 400 条）。
    /// 记录本工具对注册表的每次写入/删除/还原（含成败与所属备份组），
    /// 供「软件设置 → 操作日志」查看与按组回滚——写入失败不再完全静默。
    /// </summary>
    public static class RegLog
    {
        private const int Cap = 400;
        private static readonly object _lock = new object();
        private static readonly List<string> _lines = new List<string>();
        private static readonly List<string> _backupIds = new List<string>();

        /// <summary>记录一条日志。backupId 为所属备份组（可空），供按组回滚。</summary>
        public static void Add(string backupId, string action, string detail)
        {
            lock (_lock)
            {
                _lines.Add(DateTime.Now.ToString("MM-dd HH:mm:ss") + "  " +
                    (action ?? "").PadRight(8) + "  " + (detail ?? ""));
                _backupIds.Add(backupId ?? "");
                if (_lines.Count > Cap)
                {
                    _lines.RemoveAt(0);
                    _backupIds.RemoveAt(0);
                }
            }
        }

        /// <summary>全部日志行（时间 动作 详情）。</summary>
        public static List<string> Lines()
        {
            lock (_lock) return new List<string>(_lines);
        }

        /// <summary>第 index 行所属的备份组 Id（越界返回空）。</summary>
        public static string BackupIdAt(int index)
        {
            lock (_lock)
            {
                if (index < 0 || index >= _backupIds.Count) return "";
                return _backupIds[index];
            }
        }

        /// <summary>清空日志（还原操作会自然刷新列表时也可调用）。</summary>
        public static void Clear()
        {
            lock (_lock) { _lines.Clear(); _backupIds.Clear(); }
        }
    }
}
