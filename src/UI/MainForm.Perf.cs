using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>轻量打点器：相对启动的毫秒时间戳 + 标签。</summary>
    internal static class Perf
    {
        public static bool Enabled;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<string> Lines = new List<string>();
        private static readonly object Gate = new object();

        /// <summary>相对打点器启动的毫秒数（未启用时也可安全调用）。</summary>
        public static long Now { get { return Clock.ElapsedMilliseconds; } }

        public static void Mark(string label)
        {
            if (!Enabled) return;
            lock (Gate) Lines.Add(Clock.ElapsedMilliseconds.ToString() + "\t" + label);
        }

        public static void Mark(string label, long ms)
        {
            if (!Enabled) return;
            lock (Gate) Lines.Add(Clock.ElapsedMilliseconds.ToString() + "\t" + label + "\t" + ms + "ms");
        }

        public static void Dump(string path)
        {
            if (!Enabled || string.IsNullOrEmpty(path)) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("古月工具箱 · 性能巡检（" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）");
                sb.AppendLine("格式：相对启动毫秒 <TAB> 标签 <TAB> 耗时");
                lock (Gate)
                {
                    for (int i = 0; i < Lines.Count; i++) sb.AppendLine(Lines[i]);
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }

    public sealed partial class MainForm
    {
        /// <summary>非空时启动即进入性能巡检模式（--perf-tour 注入），巡检完自动退出。</summary>
        public static string PerfTourFile;

        /// <summary>
        /// 逐页巡检：每页记录「入口切换耗时」与「静置耗时」，首次访问还额外记录
        /// EnsureView 的构建耗时（见 MainForm.Nav 的 hook）。全程纯程序驱动，不模拟输入。
        /// </summary>
        public void RunPerfTour(string file)
        {
            Perf.Enabled = true;
            Perf.Mark("tour.start");
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    NavEntry e = _entries[i];
                    if (e == null || string.IsNullOrEmpty(e.Key)) continue;

                    bool firstVisit = e.View == null;
                    long t0 = Perf.Now;
                    NavigateTo(e.Key);
                    long t1 = Perf.Now;
                    Pump(150);              // 让布局与异步加载起步（与截图模式同一套泵消息机制）
                    long t2 = Perf.Now;

                    Perf.Mark("page " + e.Key + (firstVisit ? " [first]" : ""), t1 - t0);
                    Perf.Mark("page " + e.Key + " settle", t2 - t1);
                }
                Perf.Mark("tour.end");
            }
            catch
            {
            }
            finally
            {
                Perf.Dump(file);
                Close();
            }
        }
    }
}
