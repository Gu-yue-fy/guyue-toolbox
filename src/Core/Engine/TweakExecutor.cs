using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>统一执行管道：集中执行优化项的启用/还原，分类结果并留痕。</summary>
    public static class TweakExecutor
    {
        /// <summary>执行结果分类：已应用 / 已还原 / 失败 / 本机不适用。</summary>
        public enum Outcome { Applied, Reverted, Failed, NotApplicable }

        /// <summary>单次执行的结构化结果。</summary>
        public sealed class Result
        {
            public string Id;
            public string Name;
            public Outcome Outcome;
            public string Message;
            public DateTime At;
            /// <summary>本次执行对应的优化项（供操作日志界面按项撤销）。</summary>
            public ITweak Source;
            /// <summary>外部命令类改动的命令全文；tweak 项为空。</summary>
            public string Command;
            /// <summary>来源类别："tweak" 注册表优化项 / "shell" 外部命令与系统操作（含还原点）。</summary>
            public string Kind = "tweak";
            /// <summary>是否可视为成功（已应用或已还原）。</summary>
            public bool IsOk { get { return Outcome == Outcome.Applied || Outcome == Outcome.Reverted; } }
        }

        private static readonly List<Result> _recent = new List<Result>();
        private static readonly object _gate = new object();

        // 优化项 Apply/Revert 执行期间置位：抑制其内部 Shell 调用的重复审计
        // （该改动已由本类的 tweak 结果代表，避免预设批量应用时刷屏操作日志）。
        [ThreadStatic]
        private static bool _inTweak;

        /// <summary>当前线程是否正处于优化项 Apply/Revert 内部。</summary>
        public static bool InTweakExecution { get { return _inTweak; } }

        /// <summary>启用一项：经条件判定后调用 tweak.Apply()，记录结果并返回。</summary>
        public static Result Apply(ITweak t)
        {
            Result r = new Result { Id = t.Id, Name = t.Name, At = DateTime.Now };
            r.Source = t;
            try
            {
                if (!TweakApplicability.IsApplicable(t))
                {
                    r.Outcome = Outcome.NotApplicable;
                    r.Message = "本机不适用";
                }
                else
                {
                    bool ok = false;
                    _inTweak = true;
                    try { ok = t.Apply(); }
                    finally { _inTweak = false; }
                    if (ok)
                    {
                        r.Outcome = Outcome.Applied;
                        r.Message = "已应用";
                    }
                    else
                    {
                        r.Outcome = Outcome.Failed;
                        r.Message = "应用失败（权限不足或键不存在）";
                    }
                }
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.Failed;
                r.Message = ex.Message;
            }
            Record(r);
            return r;
        }

        /// <summary>还原一项：调用 tweak.Revert()，记录结果并返回。</summary>
        public static Result Revert(ITweak t)
        {
            Result r = new Result { Id = t.Id, Name = t.Name, At = DateTime.Now };
            r.Source = t;
            try
            {
                bool ok = false;
                _inTweak = true;
                try { ok = t.Revert(); }
                finally { _inTweak = false; }
                if (ok)
                {
                    r.Outcome = Outcome.Reverted;
                    r.Message = "已还原";
                }
                else
                {
                    r.Outcome = Outcome.Failed;
                    r.Message = "还原失败";
                }
            }
            catch (Exception ex)
            {
                r.Outcome = Outcome.Failed;
                r.Message = ex.Message;
            }
            Record(r);
            return r;
        }

        private static void Record(Result r)
        {
            try
            {
                lock (_gate)
                {
                    _recent.Add(r);
                    if (_recent.Count > 50) _recent.RemoveAt(0);
                }
                RegLog.Add(r.Kind == "shell" ? "Shell" : "Tweak", r.Outcome.ToString(), (r.Id ?? "") + " | " + r.Message);
            }
            catch { }
        }

        /// <summary>记录一条外部命令（Shell 类）或系统还原点等"非注册表优化项"的变更，
        /// 使其与注册表优化项同处于操作日志（TweakExecutor.Recent()）。</summary>
        public static void RecordShell(string name, string command, bool ok, string message)
        {
            Result r = new Result
            {
                Name = name ?? "",
                At = DateTime.Now,
                Kind = "shell",
                Command = command ?? "",
                Outcome = ok ? Outcome.Applied : Outcome.Failed,
                Message = message ?? ""
            };
            Record(r);
        }

        /// <summary>最近若干次执行记录（最多 50 条），供"操作日志"等界面展示。</summary>
        public static IList<Result> Recent()
        {
            lock (_gate) return new List<Result>(_recent);
        }
    }
}
