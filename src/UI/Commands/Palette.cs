/* 文件说明：命令面板（Ctrl+K）：页面导航 + 快捷动作的浮层列表。 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Commands
{
    /// <summary>
    /// 命令面板浮层：盖满整个客户区，中央偏上画一张卡片。
    /// 内容分两段——「页面」（PageCatalog 全量、输入过滤）+「快捷动作」（电竞模式等 4 项固定）。
    /// ↑↓ 选择、Enter 执行、Esc 关闭；点击遮罩或失焦外区域关闭。
    /// </summary>
    public sealed class Palette : Panel
    {
        // 列表项：页面与快捷动作共用
        private sealed class Item
        {
            public string Section;   // "页面" / "快捷动作"
            public string Title;
            public string Icon;
            public string Hint;      // 副文本（页面分组 / 动作说明）
            public Action Run;
        }

        // 视觉行：分组标题头 / 可执行列表项（ContentY 为相对内容顶部的偏移，便于滚动裁剪）
        private sealed class VisualRow
        {
            public bool Header;
            public int ItemIndex;    // -1 表示分组标题头；否则为 _filtered 下标
            public string Text;
            public string Icon;
            public string Hint;
            public int ContentY;
            public int Height;
        }

        private const int CardWidth = 560;
        private const int Pad = 10;
        private const int SearchH = 38;
        private const int SearchGap = 8;
        private const int FooterH = 16;
        private const int FooterGap = 4;
        private const int ItemH = 38;
        private const int SectionH = 28;

        private readonly List<Item> _all = new List<Item>();
        private readonly List<Item> _filtered = new List<Item>();
        private readonly TextBox _input = new TextBox();
        private int _selected;
        private int _scrollY;

        public Palette()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Visible = false;
            DoubleBuffered = true;

            BuildItems();

            _input.BorderStyle = BorderStyle.None;
            _input.Font = Theme.FontBody;
            _input.TextChanged += delegate { ApplyFilter(); };
            Controls.Add(_input);
        }

        // ---------------- 条目装配 ----------------

        private void BuildItems()
        {
            for (int i = 0; i < PageCatalog.All.Count; i++)
            {
                PageModule m = PageCatalog.All[i];
                string key = m.Key;
                Item it = new Item();
                it.Section = "页面";
                it.Title = m.Name;
                it.Icon = m.Icon == null ? "info" : m.Icon;
                it.Hint = m.Group;
                it.Run = delegate { Navigate(key); };
                _all.Add(it);
            }

            _all.Add(MakeAction("快捷动作", "进入电竞模式", "bolt", "应用低延迟预设（谨慎项需确认）", delegate { ApplyEsports(); }));
            _all.Add(MakeAction("快捷动作", "退出电竞模式", "undo", "一键还原电竞模式修改", delegate { RevertEsports(); }));
            _all.Add(MakeAction("快捷动作", "打开修复中心", "shield", "扫描并一键修复系统问题", delegate { Navigate("repair"); }));
            _all.Add(MakeAction("快捷动作", "检查更新", "refresh", "联网比对最新版本", delegate { CheckUpdate(); }));

            ApplyFilter();
        }

        private static Item MakeAction(string section, string title, string icon, string hint, Action run)
        {
            Item it = new Item();
            it.Section = section;
            it.Title = title;
            it.Icon = icon == null ? "info" : icon;
            it.Hint = hint;
            it.Run = run;
            return it;
        }

        // ---------------- 打开 / 关闭 ----------------

        public void Open()
        {
            _selected = 0;
            _scrollY = 0;
            _input.Text = "";
            ApplyFilter();
            _input.BackColor = Theme.CardBgAlt;
            _input.ForeColor = Theme.TextPrimary;
            LayoutInput();
            Visible = true;
            BringToFront();
            Invalidate();
            try { _input.Focus(); }
            catch { }
        }

        public void Close()
        {
            Visible = false;
        }

        // ---------------- 过滤与导航 ----------------

        private void ApplyFilter()
        {
            string q = _input.Text == null ? "" : _input.Text;
            q = q.Trim();
            _filtered.Clear();
            for (int i = 0; i < _all.Count; i++)
            {
                Item it = _all[i];
                if (it.Section == "快捷动作")
                {
                    _filtered.Add(it); // 快捷动作固定显示，不参与输入过滤
                    continue;
                }
                if (q.Length == 0 || it.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filtered.Add(it);
                }
            }

            if (_selected >= _filtered.Count) _selected = _filtered.Count - 1;
            if (_selected < 0) _selected = 0;
            ClampScroll();
            LayoutInput();
            Invalidate();
        }

        private void Navigate(string key)
        {
            Close();
            MainForm form = MainForm.Current;
            if (form != null) form.NavigateTo(key);
        }

        private void MoveSelection(int delta)
        {
            if (_filtered.Count == 0) { _selected = -1; return; }
            _selected += delta;
            if (_selected < 0) _selected = 0;
            if (_selected >= _filtered.Count) _selected = _filtered.Count - 1;
            EnsureSelectionVisible();
            Invalidate();
        }

        private void ExecuteSelected()
        {
            if (_selected >= 0 && _selected < _filtered.Count)
            {
                Item it = _filtered[_selected];
                if (it.Run != null) it.Run();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Up) { MoveSelection(-1); return true; }
            if (keyData == Keys.Down) { MoveSelection(1); return true; }
            if (keyData == Keys.PageUp) { MoveSelection(-8); return true; }
            if (keyData == Keys.PageDown) { MoveSelection(8); return true; }
            if (keyData == Keys.Enter) { ExecuteSelected(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- 快捷动作 ----------------

        private void ApplyEsports()
        {
            List<string> risky = TweakLibrary.RiskyInPreset(TweakLibrary.EsportsIds);
            if (risky != null && risky.Count > 0)
            {
                MainForm form = MainForm.Current;
                if (form != null)
                {
                    bool ok = Dialog.ConfirmDanger(form, "进入电竞模式",
                        "将启用以下「谨慎」优化项：" + RiskyNames(risky),
                        "全部改动均可撤销，之后可用「退出电竞模式」一键还原。",
                        "影响系统调度、网络与电源策略，建议对战前临时启用。",
                        "进入电竞模式", false);
                    if (!ok) return;
                }
            }
            Close();
            RunPreset(true, TweakLibrary.EsportsIds);
        }

        private void RevertEsports()
        {
            Close();
            RunPreset(false, TweakLibrary.DailyIds);
        }

        private static string RiskyNames(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "";
            List<ITweak> all = TweakLibrary.All();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                string name = ids[i];
                for (int j = 0; j < all.Count; j++)
                {
                    if (string.Equals(all[j].Id, ids[i], StringComparison.OrdinalIgnoreCase))
                    {
                        name = all[j].Name;
                        break;
                    }
                }
                if (i > 0) sb.Append("、");
                sb.Append(name);
            }
            return sb.ToString();
        }

        private void RunPreset(bool apply, string[] ids)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<string> report = new List<string>();
                    int[] result = apply
                        ? TweakLibrary.ApplyPreset(ids, report)
                        : TweakLibrary.RevertPreset(ids, report);
                    PostPresetToast(apply, result);
                }
                catch
                {
                }
            });
        }

        private static void PostPresetToast(bool apply, int[] result)
        {
            MainForm form = MainForm.Current;
            if (form == null || result == null) return;
            form.BeginInvoke((MethodInvoker)delegate
            {
                try
                {
                    if (form.IsDisposed) return;
                    if (apply)
                    {
                        form.ShowToast("已进入电竞模式",
                            "成功 " + result[0] + " 项 · 跳过 " + result[1] + " 项 · 失败 " + result[2] + " 项",
                            result[2] == 0 ? ToastKind.Success : ToastKind.Warning);
                    }
                    else
                    {
                        form.ShowToast("已退出电竞模式",
                            "还原 " + result[0] + " 项 · 未还原 " + result[1] + " 项",
                            result[1] == 0 ? ToastKind.Success : ToastKind.Warning);
                    }
                }
                catch
                {
                }
            });
        }

        private void CheckUpdate()
        {
            Close();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    UpdateInfo info = UpdateChecker.Check();
                    MainForm form = MainForm.Current;
                    if (form == null) return;
                    form.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (form.IsDisposed) return;
                            if (info == null || !info.Ok)
                            {
                                form.ShowToast("检查更新失败",
                                    info == null ? "网络异常" : info.Error, ToastKind.Warning);
                            }
                            else if (info.IsNewerThan(MainForm.AppVersion))
                            {
                                form.ShowToast("发现新版本 v" + info.Version,
                                    "可前往「关于与更新」安装", ToastKind.Success);
                            }
                            else
                            {
                                form.ShowToast("已是最新版本",
                                    "当前 v" + MainForm.AppVersion, ToastKind.Success);
                            }
                        }
                        catch
                        {
                        }
                    });
                }
                catch
                {
                }
            });
        }

        // ---------------- 布局计算 ----------------

        private int BodyTopOffset()
        {
            return Pad + SearchH + SearchGap;
        }

        private List<VisualRow> ComputeContent(out int contentH)
        {
            List<VisualRow> rows = new List<VisualRow>();
            string last = null;
            int y = 0;
            for (int i = 0; i < _filtered.Count; i++)
            {
                Item it = _filtered[i];
                if (it.Section != last)
                {
                    VisualRow h = new VisualRow();
                    h.Header = true;
                    h.ItemIndex = -1;
                    h.Text = it.Section;
                    h.ContentY = y;
                    h.Height = SectionH;
                    rows.Add(h);
                    last = it.Section;
                    y += SectionH;
                }
                VisualRow r = new VisualRow();
                r.Header = false;
                r.ItemIndex = i;
                r.Text = it.Title;
                r.Icon = it.Icon;
                r.Hint = it.Hint;
                r.ContentY = y;
                r.Height = ItemH;
                rows.Add(r);
                y += ItemH;
            }
            contentH = y;
            return rows;
        }

        private Rectangle CardRect()
        {
            int cardW = Math.Min(CardWidth, Math.Max(320, ClientSize.Width - 48));
            int cardX = (ClientSize.Width - cardW) / 2;
            int cardTop = Math.Max(60, ClientSize.Height / 6);
            int maxCardH = Math.Max(176, ClientSize.Height - cardTop - 32);

            int contentH;
            ComputeContent(out contentH);
            int fixedH = Pad + SearchH + SearchGap + FooterGap + FooterH + Pad;
            int maxBodyH = maxCardH - fixedH;
            if (maxBodyH < 80) maxBodyH = 80;
            int bodyH = Math.Min(contentH, maxBodyH);

            return new Rectangle(cardX, cardTop, cardW, fixedH + bodyH);
        }

        private Rectangle BodyViewport()
        {
            Rectangle card = CardRect();
            int top = card.Y + BodyTopOffset();
            int h = card.Height - Pad - BodyTopOffset() - FooterGap - FooterH - Pad;
            return new Rectangle(card.X + Pad, top, card.Width - Pad * 2, Math.Max(0, h));
        }

        private int MaxScroll()
        {
            int contentH;
            ComputeContent(out contentH);
            int max = contentH - BodyViewport().Height;
            return max < 0 ? 0 : max;
        }

        private void ClampScroll()
        {
            int max = MaxScroll();
            if (_scrollY < 0) _scrollY = 0;
            else if (_scrollY > max) _scrollY = max;
        }

        private void EnsureSelectionVisible()
        {
            int contentH;
            List<VisualRow> rows = ComputeContent(out contentH);
            Rectangle vp = BodyViewport();
            for (int i = 0; i < rows.Count; i++)
            {
                VisualRow r = rows[i];
                if (r.Header || r.ItemIndex != _selected) continue;
                int top = r.ContentY;
                int bottom = r.ContentY + r.Height;
                if (top < _scrollY) _scrollY = top;
                else if (bottom > _scrollY + vp.Height) _scrollY = bottom - vp.Height;
                break;
            }
            ClampScroll();
        }

        private void LayoutInput()
        {
            Rectangle card = CardRect();
            _input.SetBounds(card.X + Pad, card.Y + Pad, card.Width - Pad * 2, SearchH);
            _input.BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInput();
            ClampScroll();
            Invalidate();
        }

        // ---------------- 绘制与鼠标 ----------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Rectangle card = CardRect();
            if (!card.Contains(e.Location))
            {
                Close(); // 点击遮罩关闭
                return;
            }

            Rectangle vp = BodyViewport();
            if (vp.Contains(e.Location))
            {
                int contentH;
                List<VisualRow> rows = ComputeContent(out contentH);
                int cy = (e.Y - vp.Top) + _scrollY;
                for (int i = 0; i < rows.Count; i++)
                {
                    VisualRow r = rows[i];
                    if (r.Header) continue;
                    if (cy >= r.ContentY && cy < r.ContentY + r.Height)
                    {
                        _selected = r.ItemIndex;
                        Invalidate();
                        ExecuteSelected();
                        return;
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Gfx.EnableSmoothing(g);

            // 半透明遮罩，压暗下方内容
            g.FillRectangle(GdiCache.Brush(Gfx.Alpha(Color.Black, Theme.IsLight ? 96 : 150)), ClientRectangle);

            Rectangle card = CardRect();
            Gfx.FillRound(g, card, Theme.RadiusCard, Theme.CardBg);
            Gfx.StrokeRound(g, card, Theme.RadiusCard, Theme.BorderStrong, 1f);

            Rectangle vp = BodyViewport();
            Region oldClip = g.Clip;
            g.SetClip(vp);
            try
            {
                int contentH;
                List<VisualRow> rows = ComputeContent(out contentH);
                for (int i = 0; i < rows.Count; i++)
                {
                    VisualRow r = rows[i];
                    int sy = vp.Top - _scrollY + r.ContentY;
                    Rectangle rr = new Rectangle(vp.X, sy, vp.Width, r.Height);
                    if (rr.Bottom < vp.Top || rr.Top > vp.Bottom) continue;

                    if (r.Header)
                    {
                        Gfx.DrawTextEllipsis(g, r.Text, Theme.FontMicro, Theme.NavGroupText,
                            new Rectangle(rr.X + 8, rr.Y, Math.Max(20, rr.Width - 16), rr.Height));
                    }
                    else
                    {
                        bool sel = r.ItemIndex == _selected;
                        if (sel)
                        {
                            Gfx.FillRound(g, new Rectangle(rr.X + 2, rr.Y + 1, rr.Width - 4, rr.Height - 2),
                                Theme.RadiusChip, Theme.AccentSoft);
                        }

                        Rectangle icon = new Rectangle(rr.X + 10, rr.Y + (rr.Height - 16) / 2, 16, 16);
                        IconPainter.Draw(g, r.Icon == null ? "info" : r.Icon, icon,
                            sel ? Theme.Accent : Theme.TextSecondary);

                        Color tc = sel ? Theme.TextPrimary : Theme.TextSecondary;
                        int textRight = rr.Right - 10;
                        Gfx.DrawTextEllipsis(g, r.Text, Theme.FontBody, tc,
                            new Rectangle(icon.Right + 8, rr.Y, Math.Max(40, textRight - icon.Right - 140), rr.Height));
                        if (!string.IsNullOrEmpty(r.Hint))
                        {
                            Gfx.DrawTextEllipsis(g, r.Hint, Theme.FontMicro, Theme.TextMuted,
                                new Rectangle(rr.Right - 138, rr.Y, 128, rr.Height));
                        }
                    }
                }
            }
            finally
            {
                g.Clip = oldClip;
            }

            // 底部操作提示
            Gfx.DrawTextEllipsis(g, "↑↓ 选择 · Enter 进入 · Esc 关闭", Theme.FontMicro, Theme.TextMuted,
                new Rectangle(card.X + Pad, card.Bottom - FooterH - Pad + 6, card.Width - Pad * 2, FooterH));
        }
    }
}