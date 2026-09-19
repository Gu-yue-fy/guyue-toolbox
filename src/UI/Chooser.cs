using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>主题化的「多选一」对话框，替代原生下拉与 MessageBox。</summary>
    public static class Chooser
    {
        /// <summary>显示选项列表，返回选中的项；取消返回 null。</summary>
        public static string ChooseOne(IWin32Window owner, string title, List<string> options)
        {
            if (options == null || options.Count == 0) return null;

            using (Form f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                f.BackColor = Theme.CardBg;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(420, 122 + options.Count * 42);
                f.Font = Theme.FontBody;
                f.MinimizeBox = false;
                f.MaximizeBox = false;

                Label hint = new Label();
                hint.Text = "请选择要执行的操作：";
                hint.ForeColor = Theme.TextSecondary;
                hint.BackColor = Color.Transparent;
                hint.SetBounds(24, 52, 372, 20);
                f.Controls.Add(hint);

                string result = null;

                for (int i = 0; i < options.Count; i++)
                {
                    AccentButton b = new AccentButton();
                    b.Text = options[i];
                    b.Variant = ButtonVariant.Secondary;
                    b.SetBounds(24, 80 + i * 42, 420 - 48, 34);
                    string captured = options[i];
                    b.Click += delegate
                    {
                        result = captured;
                        f.DialogResult = DialogResult.OK;
                        f.Close();
                    };
                    f.Controls.Add(b);
                }

                AccentButton cancel = new AccentButton();
                cancel.Text = "取消";
                cancel.Variant = ButtonVariant.Ghost;
                cancel.SetBounds(420 - 48 - 104, 80 + options.Count * 42, 104, 34);
                cancel.Click += delegate
                {
                    f.DialogResult = DialogResult.Cancel;
                    f.Close();
                };
                f.Controls.Add(cancel);

                CaptionButton x = new CaptionButton("close");
                x.HoverColor = Theme.Danger;
                x.SetBounds(420 - 46, 0, 46, 32);
                x.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
                f.Controls.Add(x);

                string dialogTitle = title;
                f.Paint += delegate (object s, PaintEventArgs e)
                {
                    Graphics g = e.Graphics;
                    Gfx.EnableSmoothing(g);

                    using (SolidBrush back = new SolidBrush(Theme.CardBg))
                    {
                        g.FillRectangle(back, f.ClientRectangle);
                    }
                    using (Pen p = new Pen(Theme.BorderStrong, 1f))
                    {
                        g.DrawRectangle(p, 0, 0, f.Width - 1, f.Height - 1);
                    }
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                    {
                        g.FillRectangle(b, 0, 0, f.Width, 3);
                    }

                    Rectangle box = new Rectangle(24, 18, 22, 22);
                    Gfx.FillRound(g, box, Theme.RadiusChip, Gfx.Alpha(Theme.Accent, 32));
                    IconPainter.Draw(g, "list", new Rectangle(28, 22, 14, 14), Theme.Accent);

                    using (SolidBrush b = new SolidBrush(Theme.TextPrimary))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.LineAlignment = StringAlignment.Center;
                        sf.FormatFlags = StringFormatFlags.NoWrap;
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        g.DrawString(dialogTitle, Theme.FontSubTitle, b, new Rectangle(54, 16, 320, 26), sf);
                    }
                    using (Pen p = new Pen(Theme.Border))
                    {
                        g.DrawLine(p, 24, 74, f.Width - 24, 74);
                    }
                };

                return f.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }
    }
}
