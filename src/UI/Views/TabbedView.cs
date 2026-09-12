using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 合并页容器（主从式）：左侧竖排分类导航 + 右侧全高内容区。
    /// 相比旧顶部 chips 页签：子页获得完整高度与更宽的操作空间，分类导航常驻更好找。
    /// 子页在首次切到时才创建，保持懒加载特性。
    /// </summary>
    public sealed class TabbedView : ViewBase
    {
        private const int NavWidth = 176;
        private const int NavItemHeight = 42;

        private readonly string[] _labels;
        private readonly Func<ViewBase>[] _factories;
        private readonly ViewBase[] _pages;
        private readonly Panel _master;
        private readonly Panel _navPanel;
        private readonly Panel _host;
        private readonly List<NavItem> _navItems = new List<NavItem>();
        private int _index = -1;

        public TabbedView(string title, string subtitle, string[] labels, Func<ViewBase>[] factories)
            : base(title, subtitle)
        {
            _labels = labels;
            _factories = factories;
            _pages = new ViewBase[labels.Length];

            // 主容器：填满视口的一行
            _master = new Panel();
            _master.BackColor = Theme.WindowBg;
            _master.Tag = "stretch";
            _master.Margin = new Padding(0);

            // 左侧分类导航（手动布局：不用 Dock，规避停靠顺序坑）
            _navPanel = new Panel();
            _navPanel.BackColor = Theme.ChromeBg;
            _navPanel.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.BorderSoft))
                {
                    e.Graphics.DrawLine(p, NavWidth - 1, 0, NavWidth - 1, _navPanel.Height);
                }
            };
            _master.Controls.Add(_navPanel);

            for (int i = 0; i < labels.Length; i++)
            {
                NavItem item = new NavItem(labels[i]);
                item.Bounds = new Rectangle(0, i * NavItemHeight, NavWidth, NavItemHeight);
                int idx = i;
                item.Activate += delegate { SwitchTo(idx); };
                _navPanel.Controls.Add(item);
                _navItems.Add(item);
            }

            // 右侧内容区（手动布局）
            _host = new Panel();
            _host.BackColor = Theme.WindowBg;
            _master.Controls.Add(_host);

            _master.Resize += delegate { LayoutMaster(); };

            AddFull(_master, 600, 0);
            Body.Resize += delegate
            {
                // 主容器占满视口（扣除 Body 上下 padding），左导航随之满高
                _master.Height = Math.Max(400, ViewportHeight - 36);
            };

            SwitchTo(0);
        }

        /// <summary>主容器手动布局：左导航固定宽满高，内容区占剩余全部。</summary>
        private void LayoutMaster()
        {
            int h = Math.Max(1, _master.Height);
            _navPanel.SetBounds(0, 0, NavWidth, h);
            _host.SetBounds(NavWidth, 0, Math.Max(1, _master.Width - NavWidth), h);
        }

        private void SwitchTo(int idx)
        {
            if (_index == idx && _pages[idx] != null && _pages[idx].Visible) return;

            if (_pages[idx] == null)
            {
                ViewBase v = _factories[idx]();
                v.EmbedHeader = true;
                v.Dock = DockStyle.Fill;
                _pages[idx] = v;
            }

            for (int i = 0; i < _pages.Length; i++)
            {
                ViewBase p = _pages[i];
                if (p == null) continue;
                if (i == idx)
                {
                    if (p.Parent != _host) _host.Controls.Add(p);
                    p.Visible = true;
                    p.OnActivated();
                }
                else if (p.Visible)
                {
                    p.OnDeactivated();
                    p.Visible = false;
                }
            }
            _index = idx;

            for (int i = 0; i < _navItems.Count; i++)
            {
                _navItems[i].Selected = i == idx;
            }
            RefreshLayout();
        }

        public override void OnActivated()
        {
            ViewBase p = _pages[_index];
            if (p != null && p.Visible) p.OnActivated();
        }

        public override void OnDeactivated()
        {
            ViewBase p = _pages[_index];
            if (p != null) p.OnDeactivated();
        }

        public override bool IsBusy
        {
            get
            {
                ViewBase p = _pages[_index];
                try { return p != null && p.IsBusy; }
                catch { return false; }
            }
        }

        /// <summary>左列导航项：选中态左侧 accent 条 + 提亮底色。</summary>
        private sealed class NavItem : Control
        {
            private bool _hover;
            public bool Selected;

            public event EventHandler Activate;

            public NavItem(string text)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
                Text = text;
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent.BackColor);

                if (Selected)
                {
                    using (SolidBrush b = new SolidBrush(Theme.GridSelection))
                    {
                        g.FillRectangle(b, 0, 0, Width, Height);
                    }
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(b, 0, 6, 3, Height - 12);
                    }
                }
                else if (_hover)
                {
                    using (SolidBrush b = new SolidBrush(Theme.GridHover))
                    {
                        g.FillRectangle(b, 0, 0, Width, Height);
                    }
                }

                Color fc = Selected ? Color.White : (_hover ? Theme.TextPrimary : Theme.TextSecondary);
                using (SolidBrush b = new SolidBrush(fc))
                {
                    Gfx.DrawTextEllipsis(g, Text, Selected ? Theme.FontBodyBold : Theme.FontBody, fc,
                        new Rectangle(18, 0, Width - 30, Height));
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnClick(EventArgs e)
            {
                EventHandler h = Activate;
                if (h != null) h(this, EventArgs.Empty);
                base.OnClick(e);
            }
        }
    }
}
