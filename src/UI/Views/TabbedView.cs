using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 合并页容器（顶部页签式）：页头下方一排横向 tab（与优化中心分类条同款风格），
    /// 点击切换下方全宽内容区。子页在首次切到时才创建，保持懒加载特性。
    /// </summary>
    public sealed class TabbedView : ViewBase
    {
        private const int TabRowHeight = 42;

        private readonly string[] _labels;
        private readonly Func<ViewBase>[] _factories;
        private readonly ViewBase[] _pages;
        private readonly FlowLayoutPanel _tabRow;
        private readonly List<AccentButton> _tabs = new List<AccentButton>();
        private readonly Panel _host;
        private int _index = -1;

        public TabbedView(string title, string subtitle, string[] labels, Func<ViewBase>[] factories)
            : base(title, subtitle)
        {
            _labels = labels;
            _factories = factories;
            _pages = new ViewBase[labels.Length];

            // 顶部页签行：横向一排 chips，选中=Primary 高亮，其余 Ghost——与优化中心分类条一致
            _tabRow = new FlowLayoutPanel();
            _tabRow.FlowDirection = FlowDirection.LeftToRight;
            _tabRow.WrapContents = false;
            _tabRow.BackColor = Theme.WindowBg;
            _tabRow.Height = TabRowHeight;
            // 不可用 "row"/"rowfixed" 标签：那会让 LayoutRowChildren 把页签等分拉宽成半行
            // （"性能基准"页签曾因此占 415px、Ghost 页签文字飘到右侧）。"stretch" 只撑满行宽。
            _tabRow.Tag = "stretch";
            _tabRow.Margin = new Padding(0, 0, 0, 14);

            for (int i = 0; i < labels.Length; i++)
            {
                AccentButton tab = new AccentButton();
                tab.Text = labels[i];
                tab.IconKind = null;
                tab.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                tab.Height = 32;
                tab.FitToText(96);
                tab.Margin = new Padding(0, 0, 10, 0);
                int idx = i;
                tab.Click += delegate { SwitchTo(idx); };
                _tabs.Add(tab);
                _tabRow.Controls.Add(tab);
            }
            AddRow(_tabRow);

            // 内容区：占 tab 行以下全部空间，子页填满
            _host = new Panel();
            _host.BackColor = Theme.WindowBg;
            _host.Tag = "stretch";
            _host.Margin = new Padding(0);
            _host.Height = 500;
            Body.Controls.Add(_host);

            Body.Resize += delegate
            {
                // 内容区高度 = 视口 - 页签行 - 间距（保证子页满高可用）
                _host.Height = Math.Max(300, ViewportHeight - TabRowHeight - 14 - 36);
            };

            SwitchTo(0);
        }

        /// <summary>切换到指定子页（探针等自动化测试也用它遍历子页截图）。</summary>
        public void SwitchTo(int idx)
        {
            if (idx < 0 || idx >= _pages.Length) return;
            if (_index == idx && _pages[idx] != null && _pages[idx].Visible) return;

            // 内容区高度显式重算：Body 尺寸未变化时 Resize 事件不触发，
            // _host 会停在初始 500 高度导致子页拉伸错误
            _host.Height = Math.Max(300, ViewportHeight - TabRowHeight - 14 - 36);

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

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Variant = i == idx ? ButtonVariant.Primary : ButtonVariant.Ghost;
                _tabs[i].Invalidate();
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
    }
}
