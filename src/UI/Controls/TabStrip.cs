using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    public sealed class TabStrip : Panel
    {
        /// <summary>页签栏高度：按钮 28 + 上下留白 8（默认隐藏，单页大功能不显示）。</summary>
        public const int StripHeight = 44;

        private const int ButtonHeight = 28;
        private const int Gap = 8;
        private const int TopPad = 8;   // 不能叫 Top：会隐藏 Control.Top（CS0108）

        private readonly List<AccentButton> _buttons = new List<AccentButton>();
        private readonly List<string> _keys = new List<string>();
        private string _current = "";

        /// <summary>点击页签：参数是页面 Key。</summary>
        public event Action<string> Selected;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.WindowBg;
            Height = StripHeight;
            Visible = false;
        }

        public string CurrentKey { get { return _current; } }

        public int TabCount { get { return _keys.Count; } }

        /// <summary>重建页签（切换大功能时调用，数量/文字变化才需要）。</summary>
        public void SetTabs(IList<string> keys, IList<string> texts)
        {
            bool same = keys != null && _keys.Count == keys.Count;
            if (same)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    if (_keys[i] != keys[i]) { same = false; break; }
                }
            }
            if (same) return;   // 同组内切页签：不重建，避免闪烁

            SuspendLayout();
            for (int i = 0; i < _buttons.Count; i++)
            {
                Controls.Remove(_buttons[i]);
                _buttons[i].Dispose();
            }
            _buttons.Clear();
            _keys.Clear();

            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    AccentButton b = new AccentButton();
                    b.Text = texts != null && i < texts.Count ? texts[i] : keys[i];
                    b.Variant = ButtonVariant.Ghost;
                    b.Height = ButtonHeight;
                    b.FitToText(76);
                    b.Top = TopPad;
                    string key = keys[i];
                    b.Click += delegate { if (Selected != null) Selected(key); };
                    _keys.Add(key);
                    _buttons.Add(b);
                    Controls.Add(b);
                }
            }
            ResumeLayout();
            LayoutTabs();
            Invalidate();
        }

        /// <summary>高亮当前页签（不重建）。</summary>
        public void SetCurrent(string key)
        {
            _current = key == null ? "" : key;
            for (int i = 0; i < _buttons.Count; i++)
            {
                ButtonVariant want = _keys[i] == _current ? ButtonVariant.Primary : ButtonVariant.Ghost;
                if (_buttons[i].Variant != want) _buttons[i].Variant = want;
                else _buttons[i].Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTabs();
        }

        private void LayoutTabs()
        {
            // 左缘与页面内容同一条线（ViewBase 内容区用 PagePadX，页签也从这里起排）
            int x = Theme.PagePadX;
            for (int i = 0; i < _buttons.Count; i++)
            {
                AccentButton b = _buttons[i];
                b.Left = x;
                x += b.Width + Gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Width <= 0 || Height <= 0) return;

            g.FillRectangle(GdiCache.Brush(Theme.WindowBg), ClientRectangle);
            // 与下方内容区的分隔线（页签栏隐藏时不画）
            g.DrawLine(GdiCache.Pen(Theme.BorderSoft, 1f), 0, Height - 1, Width, Height - 1);
        }
    }
}
