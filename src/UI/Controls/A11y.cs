using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 无障碍（可访问性）辅助：焦点可视、读屏角色、键盘激活。
    ///
    /// 本工程的交互控件都是自绘 <see cref="Control"/> 派生类，系统不会替它们提供这三项能力，
    /// 因此统一由本类实现，各交互控件在构造函数中调用一次 <see cref="MakeFocusable"/> 即可。
    /// </summary>
    internal static class A11y
    {
        /// <summary>绘制焦点环（在控件自身矩形内，2px）。取系统高亮色，以便高对比度主题可替换。</summary>
        public static void DrawFocusRing(Graphics g, Rectangle bounds, int radius)
        {
            if (g == null) return;
            if (bounds.Width <= 3 || bounds.Height <= 3) return;
            Gfx.StrokeRound(g, bounds, radius, SystemColors.Highlight, 2f);
        }

        /// <summary>
        /// 让控件可被 Tab 聚焦、声明读屏角色，并接入统一键盘激活。
        ///
        /// 注意：<c>ControlStyles.Selectable</c> 只能由控件**自身**在 SetStyle 中声明
        /// （SetStyle 是 protected，外部辅助类调不到），因此各交互控件的构造函数里
        /// 都必须显式包含它，否则本方法挂上的按键事件永远不会触发。
        /// </summary>
        public static void MakeFocusable(Control c, AccessibleRole role)
        {
            if (c == null) return;
            c.TabStop = true;
            c.AccessibleRole = role;

            // 控件若自行实现了按键处理，说明它有自己的按键语义，不再重复接管
            if (!HandlesKeysItself(c)) HookActivation(c);
        }

        // ==============================================================
        // 键盘激活
        // ==============================================================

        /// <summary>
        /// 控件是否已自行实现 OnKeyDown / OnKeyUp。
        /// 例如 <see cref="ToggleSwitch"/> 规定「空格在抬起时切换」，属于它自身的按键语义；
        /// 若这里再接管一次，同一次按键会被处理两遍（切换两次等于没切换）。
        /// </summary>
        private static bool HandlesKeysItself(Control c)
        {
            Type t = c.GetType();
            MethodInfo down = t.GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo up = t.GetMethod("OnKeyUp", BindingFlags.NonPublic | BindingFlags.Instance);
            return (down != null && down.DeclaringType != typeof(Control))
                || (up != null && up.DeclaringType != typeof(Control));
        }

        /// <summary>
        /// 统一键盘激活语义（与系统按钮一致）：Enter 在按下时激活，空格在抬起时激活。
        /// 自绘控件只挂了 Click 事件，因此触发 Click 即等价于一次鼠标点击。
        /// </summary>
        private static void HookActivation(Control c)
        {
            c.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!IsActivateKey(e)) return;
                e.Handled = true;
                e.SuppressKeyPress = true;   // 吞掉按键，避免系统提示音与父容器滚动
                if (e.KeyCode == Keys.Enter) RaiseClick(c);
            };

            c.KeyUp += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space) return;
                RaiseClick(c);
            };
        }

        /// <summary>是否为「激活键」（Enter 或 空格）。</summary>
        public static bool IsActivateKey(KeyEventArgs e)
        {
            return e != null && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space);
        }

        /// <summary>
        /// 触发控件的 Click 事件。<c>Control.OnClick</c> 是 protected，外部类无法直接调用，
        /// 故按控件类型缓存 MethodInfo 后反射调用（每个类型只解析一次，运行期开销可忽略）。
        /// </summary>
        private static readonly Dictionary<Type, MethodInfo> _clickCache = new Dictionary<Type, MethodInfo>();

        private static void RaiseClick(Control c)
        {
            if (c == null || !c.Enabled) return;

            Type t = c.GetType();
            MethodInfo mi;
            if (!_clickCache.TryGetValue(t, out mi))
            {
                mi = t.GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance);
                _clickCache[t] = mi;
            }
            if (mi == null) return;

            try { mi.Invoke(c, new object[] { EventArgs.Empty }); }
            catch { }
        }
    }
}
