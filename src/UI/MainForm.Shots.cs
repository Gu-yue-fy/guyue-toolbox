﻿/* ============================================================
 * 文件说明：自动截图模式（--shots）：逐页离屏渲染保存 PNG，供 UI 回归比对。纯程序驱动，不模拟鼠标/键盘输入。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Views;

namespace GuyueBox.UI
{
    public sealed partial class MainForm
    {
        /// <summary>非空时启动即进入自动截图模式（--shots 参数注入，截图完成自动退出）。</summary>
        public static string AutoShotDir;

        /// <summary>
        /// 自动截图：遍历全部导航页，逐页 NavigateTo 后用 DrawToBitmap 离屏渲染保存 PNG。
        /// 全程纯程序驱动，不模拟任何鼠标/键盘输入；优化中心页额外截一张右栏展开图。
        /// </summary>
        public void RunAutoShots(string dir)
        {
            List<string> failures = new List<string>();
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                for (int i = 0; i < _entries.Count; i++)
                {
                    NavEntry e = _entries[i];
                    if (e == null || string.IsNullOrEmpty(e.Key)) continue;

                    // 每页独立兜底：某页抛异常时记下页名、继续拍后面的页，并在收尾用非 0 退出码回报。
                    // 原先整轮只包一个 try，任何一页出错都会静默跳出（少拍几张但退出码仍是 0），
                    // 排查时会被误判成"探针本身的问题"——教训：宁可失败得难看，也不要静默。
                    try
                    {
                        NavigateTo(e.Key);
                        Pump(400);   // 等懒加载起步
                        LayoutChrome();   // 强制按当前窗口尺寸重排内容区与视图（右栏等吸附元素随之归位）
                        PumpUntilIdle(e.View, 8000); // 等到页面不再忙碌，否则会截到「正在处理…」遮罩
                        SnapPage(e.Key, dir);

                        OptimizeView ov = e.View as OptimizeView;
                        if (ov != null)
                        {
                            ov.OpenFirstRail();
                            Pump(500);
                            SnapPage(e.Key + "_rail", dir);
                            ov.CloseRail();
                            Pump(300);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add(e.Key + " · " + ex.GetType().Name + "：" + ex.Message);
                        WriteShotFailures(dir, failures);
                    }
                }
                SetStatus("自动截图完成：" + dir);
            }
            catch
            {
            }
            finally
            {
                // 有失败页就以非 0 退出码收尾：ui-probe.ps1 会据此显式报错，而不是"看起来跑完了"
                if (failures.Count > 0) Environment.ExitCode = 2;
                Close();
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        /// <summary>
        /// 把当前窗口渲染为 PNG。用窗口级 PrintWindow（PW_RENDERFULLCONTENT）而非
        /// DrawToBitmap：本应用是全自绘控件树（Region 裁剪 + 双缓冲面板），
        /// 实测 DrawToBitmap 会输出陈旧画面，PrintWindow(2) 渲染结果与屏幕一致。
        /// </summary>
        private void SnapPage(string name, string dir)
        {
            try
            {
                RECT r;
                if (!GetWindowRect(Handle, out r)) return;
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return;
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                    bmp.Save(System.IO.Path.Combine(dir, name + ".png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 把截图失败的页写进 &lt;截图目录&gt;\_failures.txt：
        /// 少拍一张图肉眼很难发现，落盘一份清单便于直接定位是哪个页面、什么异常。
        /// </summary>
        private static void WriteShotFailures(string dir, List<string> failures)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "_failures.txt"),
                    string.Join("\r\n", failures.ToArray()), System.Text.Encoding.UTF8);
            }
            catch
            {
            }
        }

        /// <summary>泵消息循环 ms 毫秒，让布局与异步加载在截图前完成。</summary>
        private static void Pump(int ms)
        {
            int end = Environment.TickCount + ms;
            while (Environment.TickCount < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(30);
            }
        }

        /// <summary>
        /// 泵消息直到页面不再忙碌（最多等 timeoutMs）。
        /// 计划任务 / 进程列表 / 系统还原点等页要跑 schtasks、WMI 等外部命令，
        /// 固定等待时长会随机截到「正在处理…」遮罩，使截图对比结果不可信。
        /// </summary>
        private static void PumpUntilIdle(ViewBase view, int timeoutMs)
        {
            int end = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < end)
            {
                Application.DoEvents();
                bool busy = false;
                try { busy = view != null && !view.IsDisposed && view.IsBusy; }
                catch { }
                if (!busy) break;
                System.Threading.Thread.Sleep(40);
            }
            Pump(260); // 收尾缓冲：让最后一帧数据真正落到窗口上
        }
    }
}
