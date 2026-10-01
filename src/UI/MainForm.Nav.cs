﻿/* ============================================================
 * 文件说明：导航与侧栏：页面注册、侧栏构建、切页路由与忙碌指示。
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
        // ==============================================================
        // 页面（懒加载）
        // ==============================================================

        private void RegisterViews()
        {
            // 数据驱动：页面模块统一在 PageCatalog 注册，这里只负责装配
            for (int i = 0; i < PageCatalog.All.Count; i++)
            {
                PageModule m = PageCatalog.All[i];
                AddEntry(m.Group, m.Key, m.Name, m.Icon, m.Factory);
            }

            BuildSidebarItems();
        }

        /// <summary>全部已注册页面模块（供 UI 探针与命令面板动态枚举）。</summary>
        public IList<PageModule> Modules
        {
            get { return PageCatalog.All; }
        }

        private void AddEntry(string group, string key, string text, string icon, Func<ViewBase> factory)
        {
            NavEntry entry = new NavEntry();
            entry.Key = key;
            entry.Text = text;
            entry.Icon = icon;
            entry.Group = group;
            entry.Factory = factory;
            _entries.Add(entry);
        }

        /// <summary>
        /// 侧栏：设计式深空蓝竖向渐变 + 右缘 1px 光带 + 底部深玻璃状态卡片。
        /// 导航项自身内缩 8px 绘制胶囊，故容器只留纵向呼吸。
        /// </summary>
        private void BuildSidebar()
        {
            _sidebar.BackColor = Theme.SidebarBg;
            _sidebar.Surface = PaintSidebarSurface;   // 子项铺底：重画渐变与右缘光带，不留平色方块
            _sidebar.Paint += delegate (object s, PaintEventArgs e)
            {
                PaintSidebarSurface(e.Graphics);
            };

            _navFlow.FlowDirection = FlowDirection.TopDown;

            _navFlow.WrapContents = false;
            _navFlow.AutoScroll = false;
            // （侧栏画刷缓存字段与 SidebarBrush 见本文件末尾）
            _navFlow.BackColor = Theme.SidebarBg;
            _navFlow.Padding = new Padding(0, 8, 0, 10);
            _navFlow.SetBounds(0, 0, SidebarWidth, 600);

            // 侧栏顶部：LOGO 区（品牌名 + 副标题）
            _sidebarLogo.BackColor = Theme.SidebarBg;
            _sidebarLogo.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                Rectangle rc = _sidebarLogo.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;
                g.FillRectangle(GdiCache.Brush(Theme.SidebarBg), rc);

                // 标记：强调色→青渐变圆角方块 + 调校图标
                Rectangle mark = new Rectangle(14, (rc.Height - 30) / 2, 30, 30);
                using (System.Drawing.Drawing2D.LinearGradientBrush lb =
                    new System.Drawing.Drawing2D.LinearGradientBrush(mark, Theme.Accent, Theme.Cyan, 45f))
                using (System.Drawing.Drawing2D.GraphicsPath p = Gfx.RoundRect(mark, Theme.RadiusChip))
                {
                    g.FillPath(lb, p);
                }
                IconPainter.Draw(g, "tune", new Rectangle(mark.X + 7, mark.Y + 7, 16, 16), Color.White);

                int tx = mark.Right + 10;
                Gfx.DrawTextEllipsis(g, "古月工具箱", Theme.FontSubTitle, Theme.TextPrimary,
                    new Rectangle(tx, 12, Math.Max(10, rc.Width - tx - 12), 20));
                // 副标题不再写第二个"品牌名"（同屏出现两个产品名会让人以为是两个软件），改成功能定位
                Gfx.DrawTextEllipsis(g, "系统优化与维护", Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(tx, 35, Math.Max(10, rc.Width - tx - 12), 16));
            };
            _sidebar.Controls.Add(_sidebarLogo);

            // 滚动容器同色：不设的话默认内容区底色，导航项结束后会露出一段异色带
            _sidebarScroll.BackColor = Theme.SidebarBg;
            _sidebarScroll.SetContent(_navFlow);
            _sidebar.Controls.Add(_sidebarScroll);

            // 侧栏底部：设计「深玻璃状态卡片」——盾牌图标 + 状态点(带同色光晕) + 运行模式 + 版本 + 齿轮
            _sidebarFooter.BackColor = Theme.SidebarBg;
            _sidebarFooter.Height = SidebarFooterHeight;
            _sidebarFooter.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Gfx.EnableSmoothing(g);
                Rectangle rc = _sidebarFooter.ClientRectangle;
                if (rc.Width <= 0 || rc.Height <= 0) return;

                g.FillRectangle(GdiCache.Brush(Theme.SidebarBg), new Rectangle(0, 0, Math.Max(1, rc.Width - 1), rc.Height));
                g.DrawLine(GdiCache.Pen(Gfx.Alpha(Theme.LightBeam, Theme.LightBeam.A / 2), 1f),
                    rc.Width - 1, 0, rc.Width - 1, rc.Height);

                Rectangle card = new Rectangle(10, 8, Math.Max(40, rc.Width - 20), Math.Max(20, rc.Height - 16));
                Gfx.FillRound(g, card, Theme.RadiusCard, Theme.GlassCardBg);
                Gfx.StrokeRound(g, card, Theme.RadiusCard, Theme.GlassBorder, 1f);

                Rectangle shield = new Rectangle(card.X + 10, card.Y + (card.Height - 32) / 2, 32, 32);
                Gfx.FillRound(g, shield, Theme.RadiusChip, Theme.CardBgAlt);
                IconPainter.Draw(g, "admin", new Rectangle(shield.X + 8, shield.Y + 8, 16, 16), Theme.TextMuted);

                bool admin = Native.IsElevatedCached;
                Color dot = admin ? Theme.Success : Theme.Warning;
                SolidBrush glow = GdiCache.Brush(Gfx.Alpha(dot, 60));
                {
                    g.FillEllipse(glow, shield.Right - 7, shield.Bottom - 7, 12, 12);
                }
                g.FillEllipse(GdiCache.Brush(dot), shield.Right - 5, shield.Bottom - 5, 8, 8);

                int textX = shield.Right + 10;
                int textW = Math.Max(20, card.Right - textX - 30);
                Gfx.DrawTextEllipsis(g, admin ? "管理员模式运行中" : "普通模式运行中",
                    Theme.FontSmall, Theme.TextPrimary, new Rectangle(textX, card.Y + 14, textW, 18));
                Gfx.DrawTextEllipsis(g, "v" + AppVersion, Theme.FontMicro, Theme.TextMuted,
                    new Rectangle(textX, card.Y + 32, textW, 16));

                // 点这张卡进「设置」，图标就用齿轮（原先用 feature，与「可选功能」页图标撞语义）
                IconPainter.Draw(g, "gear",
                    new Rectangle(card.Right - 28, card.Y + (card.Height - 18) / 2, 18, 18), Theme.TextMuted);
            };
            _sidebarFooter.Cursor = Cursors.Hand;
            _sidebarFooter.Click += delegate { NavigateTo("settings"); };
            _sidebar.Controls.Add(_sidebarFooter);
        }

        /// <summary>
        /// 按「大功能」重建侧栏项（RegisterViews 末尾调用一次）。
        /// 同组的所有页面共用一个侧栏项：小功能是内容区顶部的页签，不再各占一行；
        /// 点击侧栏项跳到该大功能的第一个页面，再由页签切到别的小功能。
        /// </summary>
        private void BuildSidebarItems()
        {
            string currentGroup = null;
            NavSideItem groupItem = null;
            foreach (NavEntry e in _entries)
            {
                if (e.Group != currentGroup)
                {
                    currentGroup = e.Group;
                    // 大功能之间留 2px 呼吸（胶囊本身内缩，加 1px 上下边距后行距与设计节奏一致）
                    groupItem = new NavSideItem(currentGroup, e.Icon);
                    groupItem.Width = SidebarWidth - 22;
                    groupItem.Margin = new Padding(0, 1, 0, 1);
                    string firstKey = e.Key;
                    groupItem.Click += delegate { NavigateTo(firstKey); };
                    _navFlow.Controls.Add(groupItem);
                }
                e.Button = groupItem;
            }
            int navH = 8;
            foreach (Control c in _navFlow.Controls) navH += c.Height;
            _navFlow.Height = navH;
        }

        private NavEntry FindEntry(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == key) return _entries[i];
            }
            return null;
        }

        private ViewBase EnsureView(NavEntry entry)
        {
            if (entry.View != null) return entry.View;

            long buildStart = Perf.Now;
            ViewBase view = entry.Factory();
            Perf.Mark("build " + entry.Key, Perf.Now - buildStart);
            entry.View = view;
            view.Visible = false;
            view.SetBounds(0, 0, Math.Max(1, _content.Width), Math.Max(1, _content.Height));
            _content.Controls.Add(view);
            return view;
        }

        public void NavigateTo(string key)
        {
            if (key == _currentKey)
            {
                NavEntry curEntry = FindEntry(key);
                if (curEntry == null) return;
                EnsureView(curEntry).BringToFront();
                return;
            }

            NavEntry target = FindEntry(key);
            if (target == null) return;

            Control oldView = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                NavEntry e = _entries[i];
                // 侧栏高亮按「大功能」整组：同组页面共用一个侧栏项实例
                if (e.Button != null) e.Button.Selected = e.Group == target.Group;
                if (e.View != null && e.View.Visible)
                {
                    e.View.OnDeactivated();
                    if (e.View != target.View) oldView = e.View; // 旧页先记下，新页就位后再隐藏
                }
            }

            ViewBase view = EnsureView(target);
            _currentKey = key;
            if (!TestMode) AppSettings.LastPage = key; // 记录所在页面，供「启动时回到上次页面」使用（测试模式不写）
            view.Visible = true;
            view.BringToFront();
            long actStart = Perf.Now;
            view.OnActivated();
            view.ForceRefresh();
            Perf.Mark("activate " + key, Perf.Now - actStart);
            SwapViews(view, oldView);
            AnimateSwitchIn(view);
            SetStatus(target.Text);
            _pageTitle = target.Text;
            PlaySwitchCue();   // 顶栏标题 + 状态文字淡入（切页过渡）
            _topBar.Invalidate();
            SyncTabStrip(target);
        }

        /// <summary>
        /// 刷新内容区顶部的大功能页签栏：只列当前大功能下的小功能；
        /// 大功能下只有一页时不显示页签栏（把高度让给内容）。
        /// </summary>
        private void SyncTabStrip(NavEntry target)
        {
            List<string> keys = new List<string>();
            List<string> texts = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                NavEntry e = _entries[i];
                if (e.Group != target.Group) continue;
                keys.Add(e.Key);
                texts.Add(e.Text);
            }

            _tabStrip.SetTabs(keys, texts);
            _tabStrip.SetCurrent(target.Key);

            bool show = keys.Count > 1;
            if (_tabStrip.Visible != show)
            {
                _tabStrip.Visible = show;
                LayoutChrome();   // 让出 / 收回内容区顶部高度
            }
        }

        /// <summary>
        /// 切页收尾：旧页隐藏、新页归位到 (0,0)。
        /// 刻意不做视差滑动——两页同时位移会与快速连点竞态（重叠 / 残影 / 内容加载被打断），
        /// 对低配机也是纯负担；落位感交给下面的 AnimateSwitchIn（只动新页）。
        /// </summary>
        private static void SwapViews(Control view, Control oldView)
        {
            if (oldView != null) oldView.Visible = false;
            view.Location = new Point(0, 0);
        }

        // --------------------------------------------------------------
        // 切页过渡：新页从下方 8px 缓入
        // 约束（都是当年视差动画踩过的坑）：
        //   · 只动新页，旧页立即隐藏 —— 两页同时在动必然出现重叠与残影；
        //   · 位移只有 8px、不触发子布局 —— 页面内容不会重排，低配机也无压力；
        //   · 每次切页先 Finish —— 连点时不会被上一个 Timer 抢同一个 Top；
        //   · 「软件设置」里关掉动画时直接落位。
        // --------------------------------------------------------------

        // 上浮幅度与步数：14px / 9 步（约 150ms）。原来的 8px / 6 步到位太快，
        // 观感接近"硬切"；配合页面内容级联入场（PlayEnterAnimation），形成"整页上浮 + 内容依次落位"
        private const int SwitchShift = 14;
        private const int SwitchSteps = 9;

        private Control _switchView;
        private int _switchStep;

        /// <summary>立即结束切页过渡并归位（连点切页、关窗前调用）。</summary>
        private void FinishSwitchAnim()
        {
            AnimationClock.Instance.Unsubscribe(SwitchTick);
            if (_switchView != null)
            {
                if (!_switchView.IsDisposed) _switchView.Top = 0;
                _switchView = null;
            }
            _switchStep = 0;
        }

        private void AnimateSwitchIn(Control view)
        {
            FinishSwitchAnim();
            if (view == null || view.IsDisposed) return;
            if (!AppSettings.Animations)
            {
                view.Top = 0;
                return;
            }

            _switchView = view;
            _switchStep = 0;
            view.Top = SwitchShift;

            // 内容级联入场：页头 / 工具行 / 前几块内容依次落位，
            // 与整页上浮叠加成"淡入上浮"的观感（控件无透明度，用错峰位移近似）
            ViewBase vb = view as ViewBase;
            if (vb != null) vb.PlayEnterAnimation();

            AnimationClock.Instance.Subscribe(SwitchTick);
        }

        private void SwitchTick()
        {
            Control v = _switchView;
            // 页面在动画中途被隐藏 / 销毁（连点切页、关窗）时收尾退出
            if (v == null || v.IsDisposed || !v.Visible)
            {
                FinishSwitchAnim();
                return;
            }

            // LayoutChrome（窗口缩放 / 截图探针重排）会把所有页面 Top 归零：
            // 那是外部重排，不再抢位置，直接收尾
            if (v.Top == 0) { FinishSwitchAnim(); return; }

            _switchStep++;
            if (_switchStep >= SwitchSteps)
            {
                v.Top = 0;
                FinishSwitchAnim();
                return;
            }

            // 二次缓出：位移先快后慢，落位不生硬。
            // 过程中不落到 0（最小 1px），这样上面"Top==0 即外部重排"的判断才成立
            double t = (double)_switchStep / SwitchSteps;
            int top = (int)Math.Round(SwitchShift * (1 - t) * (1 - t));
            v.Top = top < 1 ? 1 : top;
        }

        private void RefreshCurrent()
        {
            NavEntry e = FindEntry(_currentKey);
            if (e == null) return;
            ViewBase view = EnsureView(e);
            view.OnDeactivated();
            view.OnActivated();
        }

        /// <summary>Ctrl+Tab / Ctrl+Shift+Tab：当前大功能内循环切页签（该大功能只有一页时按全表循环）。</summary>
        private void NextPage(int dir)
        {
            int idx = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Key == _currentKey) { idx = i; break; }
            }
            if (idx < 0) return;

            string group = _entries[idx].Group;
            List<int> same = new List<int>();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Group == group) same.Add(i);
            }
            if (same.Count <= 1)
            {
                NavigateTo(_entries[(idx + dir + _entries.Count) % _entries.Count].Key);
                return;
            }

            int pos = same.IndexOf(idx);
            NavigateTo(_entries[same[(pos + dir + same.Count) % same.Count]].Key);
        }

        private void UpdateBusyIndicator()
        {
            bool busy = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                NavEntry e = _entries[i];
                if (e.View != null && e.View.Visible && e.View.IsBusy) { busy = true; break; }
            }

            if (busy)
            {
                if (!_spinner.Visible)
                {
                    _spinner.Visible = true;
                    _spinner.StartSpin();
                }
            }
            else if (_spinner.Visible)
            {
                _spinner.StopSpin();
                _spinner.Visible = false;
            }
        }

        /// <summary>
        /// 侧栏背景：竖向渐变 + 右缘光带。
        /// 抽成方法后既是侧栏自身的绘制，也是子项（导航项 / 底部卡片）的铺底来源——
        /// 子项原先只铺 Parent.BackColor，会把渐变与右缘光带盖成平色块。
        /// </summary>
        private void PaintSidebarSurface(Graphics g)
        {
            if (g == null) return;
            Rectangle rc = _sidebar.ClientRectangle;
            if (rc.Width <= 0 || rc.Height <= 0) return;

            // 设计 SidebarBackground：#050A18 → #070E21（竖向）
            // 画刷走缓存：原先每帧都在 new LinearGradientBrush（含 ColorBlend），
            // 侧栏每次切页 / 悬停重绘都要重新分配一组 GDI 对象。
            g.FillRectangle(SidebarBrush(ref _sideBgKey, ref _sideBgBrush, rc.Height,
                Theme.SidebarBgTop, Theme.SidebarBgBottom, null), rc);

            // 右缘光带：垂直渐变 透明 → 15% 白 → 透明（设计 Sidebar Light Beam Divider）
            if (rc.Width > 2)
            {
                System.Drawing.Drawing2D.ColorBlend cb = new System.Drawing.Drawing2D.ColorBlend(3);
                cb.Colors = new Color[] { Color.Transparent, Theme.LightBeam, Color.Transparent };
                cb.Positions = new float[] { 0f, 0.5f, 1f };
                g.FillRectangle(SidebarBrush(ref _sideBeamKey, ref _sideBeamBrush, rc.Height,
                    Color.Transparent, Color.Transparent, cb), rc.Width - 1, 0, 1, rc.Height);
            }
        }

        // ------------------------------------------------------------------
        // 侧栏渐变画刷缓存
        // ------------------------------------------------------------------
        private System.Drawing.Drawing2D.LinearGradientBrush _sideBgBrush;
        private int _sideBgKey;
        private System.Drawing.Drawing2D.LinearGradientBrush _sideBeamBrush;
        private int _sideBeamKey;

        /// <summary>
        /// 取侧栏用的纵向渐变画刷（带缓存）。
        /// 缓存键 = 高度 + 颜色；配色切换或尺寸变化时自动重建，其余情况直接复用。
        /// 此前每次重绘都新建 LinearGradientBrush + ColorBlend，切页与悬停都在重复分配 GDI 对象。
        /// </summary>
        private static System.Drawing.Drawing2D.LinearGradientBrush SidebarBrush(
            ref int key, ref System.Drawing.Drawing2D.LinearGradientBrush brush, int height,
            Color top, Color bottom, System.Drawing.Drawing2D.ColorBlend blend)
        {
            int k = (Math.Max(1, height) * 397) ^ top.ToArgb() ^ (bottom.ToArgb() * 31)
                ^ (blend == null || blend.Colors.Length < 2 ? 0 : blend.Colors[1].ToArgb());
            if (brush != null && k == key) return brush;

            if (brush != null) { try { brush.Dispose(); } catch { } }
            System.Drawing.Drawing2D.LinearGradientBrush nb =
                new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, 1, Math.Max(1, height)), top, bottom, 90f);
            if (blend != null) nb.InterpolationColors = blend;
            brush = nb;
            key = k;
            return brush;
        }
    }
}

