using System;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI
{
    /// <summary>空态/失败态卡片：标题 + 说明 + 可选动作按钮。</summary>
    public sealed class EmptyState : RoundPanel
    {
        private readonly AccentButton _action = new AccentButton();
        private string _title = "";
        private string _sub = "";
        private EventHandler _handler;

        public EmptyState()
        {
            BackColor = Theme.CardBg;
            Radius = Theme.RadiusItem;
            CornerColor = Theme.WindowBg;
            BorderColor = Theme.GlassBorder;
            Highlight = false;

            _action.Variant = ButtonVariant.Secondary;
            _action.IconKind = "refresh";
            _action.Height = 30;
            _action.Visible = false;
            Controls.Add(_action);
        }

        /// <summary>
        /// 设置空态内容。
        /// actionText 为空则不显示动作按钮；actionIcon 为空时回退为「refresh」图标。
        /// </summary>
        public void SetState(string title, string sub, string actionText, string actionIcon, EventHandler onAction)
        {
            _title = title ?? "";
            _sub = sub ?? "";

            if (_handler != null) _action.Click -= _handler;
            _handler = onAction;
            if (_handler != null) _action.Click += _handler;

            _action.Text = actionText ?? "";
            _action.IconKind = string.IsNullOrEmpty(actionIcon) ? "refresh" : actionIcon;
            _action.Visible = !string.IsNullOrEmpty(actionText);
            LayoutAction();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutAction();
            Invalidate();
        }

        /// <summary>动作按钮在卡片内水平居中、位于两行文字之下。</summary>
        private void LayoutAction()
        {
            if (!_action.Visible) return;
            if (_action.Width <= 0) _action.Width = 150;
            _action.Left = Math.Max(12, (Width - _action.Width) / 2);
            _action.Top = Math.Max(12, Height / 2 + 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int w = Width, h = Height;
            if (w <= 0 || h <= 0) return;

            // 文字块整体落在卡片中线略偏上，动作按钮在其下——空态视觉重心稳定
            int blockTop = Math.Max(12, h / 2 - 56);
            Rectangle titleRect = new Rectangle(24, blockTop, Math.Max(10, w - 48), 26);
            Rectangle subRect = new Rectangle(24, blockTop + 30, Math.Max(10, w - 48), 44);

            TextRenderer.DrawText(g, _title, Theme.FontBody, titleRect, Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _sub, Theme.FontSmall, subRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                TextFormatFlags.NoPrefix);
        }
    }
}
