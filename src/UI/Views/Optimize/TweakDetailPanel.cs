/* UI/Views/Optimize/TweakDetailPanel.cs — 优化中心右栏：项名 + 四色风险徽章 + 图标化四段详情（作用/原理/风险与恢复/将写入的项）。
 * v2.2 视觉重做：段标题带彩色图标与细线（tune/info/shield/list 四色四图标），
 * 行距与段距放宽，底部按钮占满栏宽。 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using GuyueBox.Core;
using GuyueBox.UI.Controls;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 优化详情右栏（主从布局的「从」侧）：
    /// 点击左侧任意优化项后持续显示它的四段式说明与启用/停用操作，
    /// 取代「每点一次弹一次模态框」的查看方式——对比多行时不必反复开关弹窗。
    /// </summary>
    internal sealed class TweakDetailPanel : RoundPanel
    {
        private ITweak _tweak;
        private DetailInfo _info;
        private readonly AccentButton _action = new AccentButton();
        private readonly AccentButton _close = new AccentButton();
        private readonly OptimizeView _owner;
        // v2.2：滚动从「Panel AutoScroll（系统滚动条，灰框无平滑）」换成全站统一的
        // ScrollHost：自绘细滚动条 + 平滑滚轮 + 上下渐隐，与页面滚动体验一致。
        private readonly ScrollHost _scrollHost = new ScrollHost();
        private readonly Panel _body = new Panel();
        private readonly Label _title = new Label();
        private readonly BadgeLabel _risk = new BadgeLabel();
        private readonly Label _meta = new Label();
        private readonly Panel _divider = new Panel();
        private readonly Label _empty = new Label();

        // 四段：0=作用 1=原理 2=风险与恢复 3=将写入的项（图标 + 色调随段固定）
        private readonly SectionHead[] _heads = new SectionHead[4];
        private readonly WrapTextLabel[] _bodies = new WrapTextLabel[3];
        private readonly Label _list = new Label();
        private readonly List<string> _writeLines = new List<string>();

        /// <summary>
        /// 正文限长：说明框宽度有限，段落过长会变成一堵字墙（读起来累、也不好看）。
        /// 超出部分按句号截断，尽量不在词中间切断。
        /// </summary>
        private static string Clamp(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            string t = s.Substring(0, max);
            int p = t.LastIndexOfAny(new char[] { '。', '；', '，', '、' });
            if (p >= max / 2) t = t.Substring(0, p);
            return t.TrimEnd() + "…";
        }

        private const int PadX = 22;
        private const int PadTop = 48;

        /// <summary>由页面注入：读取优化项当前是否已启用，保证右栏与左侧开关一致。</summary>
        public Func<ITweak, bool> StateReader;

        public TweakDetailPanel(OptimizeView owner)
        {
            _owner = owner;
            Radius = Theme.RadiusCard;

            // 用户反馈：这一栏像"挡住右侧约四分之一视野的黑框"。
            // 去框化：底色与页面同色、不描边、不加内高光——说明作为页面的一部分呈现，
            // 而不是一张浮在右侧的卡片；层次感改由文字层级与段标题条承担。
            BackColor = Theme.WindowBg;
            ShowBorder = false;

            // 滚动容器：ScrollHost 包着内容面板，过长可平滑滚动（滚轮经全局 WheelRouter 路由）
            _body.BackColor = Theme.WindowBg;
            // 左右留白一致（此前右侧只有 16px，正文贴边）
            _body.Padding = new Padding(PadX, PadTop, PadX, 66);
            _scrollHost.Dock = DockStyle.Fill;
            _scrollHost.BackColor = Theme.WindowBg;
            _scrollHost.SetContent(_body);
            _scrollHost.Resize += delegate { LayoutContent(); };
            Controls.Add(_scrollHost);

            _title.AutoSize = true;
            _title.Font = Theme.FontSubTitle;          // 11pt bold：项名是整栏最醒目的一层
            _title.ForeColor = Theme.TextPrimary;
            _title.BackColor = Theme.WindowBg;
            _body.Controls.Add(_title);

            // 风险徽章：四档四色（与行内 TweakRow 风险标签共用同一判定）
            _risk.Size = new Size(58, 20);
            _risk.Filled = false;
            _body.Controls.Add(_risk);

            _meta.AutoSize = true;
            _meta.Font = Theme.FontSmall;
            _meta.ForeColor = Theme.TextMuted;
            _meta.BackColor = Theme.WindowBg;
            _body.Controls.Add(_meta);

            _divider.Height = 1;
            _divider.BackColor = Theme.BorderSoft;
            _body.Controls.Add(_divider);

            // 段标题：图标 + 彩色标题 + 延伸细线（把灰字小标题升级成有层次的分段锚点）
            _heads[0] = new SectionHead("作用", "tune", Theme.Accent);
            _heads[1] = new SectionHead("原理", "info", Theme.Cyan);
            _heads[2] = new SectionHead("风险与恢复", "shield", Theme.Success);
            _heads[3] = new SectionHead("将写入的项", "list", Theme.TextSecondary);
            for (int i = 0; i < 4; i++) _body.Controls.Add(_heads[i]);

            for (int i = 0; i < 3; i++)
            {
                WrapTextLabel b = new WrapTextLabel();
                b.Font = Theme.FontSmall;
                b.ForeColor = Theme.TextSecondary;
                _body.Controls.Add(b);
                _bodies[i] = b;
            }

            _list.AutoSize = true;
            // 此前用等宽字体：同样的字号下等宽看起来更大更挤，是"字太大"观感的主要来源
            _list.Font = Theme.FontSmall;
            _list.ForeColor = Theme.TextMuted;
            _list.BackColor = Theme.WindowBg;
            _list.Padding = new Padding(8, 6, 0, 6);
            _body.Controls.Add(_list);

            _empty.Text = "点击左侧任意优化项\n它的完整说明会显示在这里";
            _empty.TextAlign = ContentAlignment.MiddleCenter;
            _empty.Dock = DockStyle.Fill;
            _empty.ForeColor = Theme.TextMuted;
            _empty.Font = Theme.FontBody;
            _empty.BackColor = Color.Transparent;
            Controls.Add(_empty);

            _action.Height = 34;
            _action.Click += delegate
            {
                if (_tweak != null && _owner != null) _owner.ToggleFromRail(_tweak);
            };
            Controls.Add(_action);

            // 收起按钮（右上角 ×）：让详情栏可主动关闭，列表收回满宽，不再一直占着右侧
            _close.Text = "×";
            _close.Variant = ButtonVariant.Ghost;
            _close.Size = new Size(28, 28);
            _close.Click += delegate { if (_owner != null) _owner.CloseRail(); };
            Controls.Add(_close);

            // Z 序实证（本机实测）：先加入者在上层。_scrollHost 为 Dock=Fill 且最先加入、
            // 盖住整栏，后加入的 _action/_close 会被压在其下——启用/停用与 × 按钮
            // 不可见也不可点（"详情栏按键没反应"的直接原因），必须显式置顶。
            _close.BringToFront();
            _action.BringToFront();

            Resize += delegate { LayoutRailCtrls(); UpdateRegion(); };
            // 位置变化也要重排：PositionRail 多数时候只改 X/Y（滑动动画、高度不变），
            // 此时 Resize 不触发，启用/停用按钮会停在旧高度上被圆角裁剪区挡掉。
            LocationChanged += delegate { LayoutRailCtrls(); };
            LayoutRailCtrls();
            UpdateRegion();
        }

        /// <summary>重排栏内浮层控件（关闭按钮固定右上角，主按钮占满栏宽）。</summary>
        private void LayoutRailCtrls()
        {
            _close.Location = new Point(Width - 34, 10);
            if (_action != null)
            {
                int bw = Math.Max(96, Width - PadX - 16);
                _action.SetBounds(PadX, Math.Max(8, Height - 50), bw, 34);
            }
        }

        /// <summary>用圆角 Region 裁剪整栏，确保内容被裁成圆角卡片（控件本身不透明，渲染可靠）。
        /// GraphicsPath 构造 Region 后必须释放：此处随 resize 反复执行，
        /// 不释放则每次窗口尺寸变化都泄漏一个 Path（诊断报告 #2）。</summary>
        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Gfx.RoundRect(new Rectangle(0, 0, Width, Height), Theme.RadiusCard))
            {
                Region old = this.Region;
                this.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        /// <summary>当前展示的优化项（null = 空态）。</summary>
        public ITweak Current { get { return _tweak; } }

        public bool IsEmpty { get { return _tweak == null; } }

        public void Show(ITweak t, DetailInfo info)
        {
            _tweak = t;
            _info = info;
            if (t == null || info == null)
            {
                _empty.Visible = true;
                _scrollHost.Visible = false;
                _action.Visible = false;
                return;
            }
            _empty.Visible = false;
            _scrollHost.Visible = true;

            DetailInfo d = info;
            _title.Text = t.Name == null ? "" : t.Name;
            _risk.Text = TweakRiskBadge.Label(t);
            _risk.BadgeColor = TweakRiskBadge.Color(t);

            bool applied = StateReader != null && StateReader(t);
            _meta.Text = (applied ? "已启用" : "未启用")
                + (string.IsNullOrEmpty(t.Group) ? "" : " · " + t.Group)
                + (t.AdminOnly ? " · 需管理员" : "");
            _meta.ForeColor = applied ? Theme.Success : Theme.TextMuted;

            // 风险段色调：谨慎=橙、安全=绿（图标与标题跟着段语义变色）
            Color riskTone = t.Risky ? Theme.Warning : Theme.Success;
            _heads[2].Tint = riskTone;

            // 四段：作用=正文；原理/风险与恢复=标题+正文（空则自动隐藏）；将写入的项=紧凑列表
            // 说明只留"作用 + 风险与恢复"两段：原来还铺一整段"原理"，信息密度低、
            // 把右栏拉得很长（用户反馈说明文字太多）。原理仍可在优化项描述里看到。
            // 正文限长：说明栏只有一栏宽，整段铺开就是一堵字墙（读起来累、也不好看）。
            // 作用给两行左右，原理与风险各留一句，完整表述仍可在优化项描述里看到。
            _bodies[0].Text = Clamp(d.What, 130);
            _bodies[1].Text = Clamp(d.Why, 88);
            _bodies[2].Text = Clamp(CombineRisk(d), 110);
            BuildWriteLines(t);

            _heads[0].Visible = _bodies[0].Visible = !string.IsNullOrEmpty(d.What);
            _heads[1].Visible = _bodies[1].Visible = !string.IsNullOrEmpty(d.Why);
            bool hasRisk = !string.IsNullOrEmpty(_bodies[2].Text);
            _heads[2].Visible = _bodies[2].Visible = hasRisk;
            bool hasList = _writeLines.Count > 0;
            _heads[3].Visible = _list.Visible = hasList;

            SyncAction();
            LayoutContent();
        }

        /// <summary>状态可能被行内开关、一键推荐等路径改变，同步一次按钮语义与文案。</summary>
        public void RefreshState()
        {
            if (_tweak != null && _info != null) Show(_tweak, _info);
        }

        private void SyncAction()
        {
            bool has = _tweak != null;
            // 注意：不能用 _scrollHost.Visible 判断——它是"含父链"的有效可见性，
            // 首次打开时 OpenRail() 在 Show() 之后才把整栏设为可见，
            // 此处会读到 false 并把按钮永久隐藏（首次打开无启用/停用按钮的根因）。
            // 整栏隐藏时子控件自然不可见，这里只看语义即可。
            _action.Visible = has;
            if (!has) return;
            bool applied = StateReader != null && StateReader(_tweak);
            _action.Text = applied ? "停用此项" : "启用此项";
            _action.Variant = applied ? ButtonVariant.Ghost : ButtonVariant.Primary;
            LayoutRailCtrls();
        }

        /// <summary>「风险与恢复」段：风险 + 可撤销说明，两段合一（均空则整段隐藏）。</summary>
        private static string CombineRisk(DetailInfo d)
        {
            string risk = string.IsNullOrEmpty(d.Risk) ? "" : d.Risk;
            string rev = string.IsNullOrEmpty(d.Reversible) ? "" : d.Reversible;
            if (risk.Length == 0 && rev.Length == 0) return "";
            if (risk.Length == 0) return rev;
            if (rev.Length == 0) return risk;
            return risk + "\n" + rev;
        }

        /// <summary>把优化项写入清单转成显示的紧凑文本（非 RegTweak 无清单，整段隐藏）。</summary>
        private void BuildWriteLines(ITweak t)
        {
            _writeLines.Clear();
            RegTweak reg = t as RegTweak;
            if (reg == null || reg.Enable == null || reg.Enable.Count == 0) return;
            int max = 12;
            int shown = reg.Enable.Count < max ? reg.Enable.Count : max;
            for (int i = 0; i < shown; i++)
            {
                RegWrite r = reg.Enable[i];
                if (r == null) continue;
                string line = HiveShort(r.Hive) + "\\" + r.Path;
                line += string.IsNullOrEmpty(r.Name) ? " （默认值）" : "\\" + r.Name;
                line += " = " + ValueText(r);
                _writeLines.Add(line);
            }
            if (reg.Enable.Count > shown) _writeLines.Add("…共 " + reg.Enable.Count + " 条");
        }

        private static string HiveShort(Microsoft.Win32.RegistryHive hive)
        {
            switch (hive)
            {
                case Microsoft.Win32.RegistryHive.LocalMachine: return "HKLM";
                case Microsoft.Win32.RegistryHive.CurrentUser: return "HKCU";
                case Microsoft.Win32.RegistryHive.ClassesRoot: return "HKCR";
                case Microsoft.Win32.RegistryHive.Users: return "HKU";
                default: return hive.ToString();
            }
        }

        private static string ValueText(RegWrite r)
        {
            if (r.Delete) return "删除该值";
            byte[] bin = r.Value as byte[];
            if (bin != null) return "二进制 " + bin.Length + " 字节";
            return r.Value == null ? "" : Convert.ToString(r.Value);
        }

        /// <summary>把长注册表路径按像素折行（路径无空格须按字符折；优先在 \、=、空格之后断）。</summary>
        private static string WrapWriteLine(string line, Font f, int maxPx)
        {
            if (string.IsNullOrEmpty(line)) return "";
            StringBuilder sb = new StringBuilder();
            int len = line.Length;
            int start = 0;
            while (start < len)
            {
                int fit = len;      // 默认剩余全部放下
                int bestBreak = -1; // 断行点（\、=、空格之后）
                int end = start + 1;
                while (end <= len)
                {
                    string seg = line.Substring(start, end - start);
                    if (TextRenderer.MeasureText(seg, f).Width > maxPx)
                    {
                        fit = end - 1;
                        break;
                    }
                    char c = line[end - 1];
                    if (c == '\\' || c == '=' || c == ' ') bestBreak = end;
                    end++;
                }
                int cut;
                if (fit == len) cut = len;
                else if (bestBreak > start) cut = bestBreak;
                else cut = fit;
                if (cut <= start) cut = Math.Min(start + 1, len);
                sb.Append(line.Substring(start, cut - start));
                start = cut;
                if (start < len) sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>在滚动容器内自上而下排布标题+徽章 / 元信息 / 分隔线 / 四个分段。
        /// 排布完成后通知 ScrollHost 重测内容高度（子项高度变了它不会自动知道）。</summary>
        private void LayoutContent()
        {
            // ScrollHost 的内容面板宽度 = 客户区宽 - 自绘滚动条(8+3)；Padding 落在 _body 上
            int w = _body.ClientSize.Width - _body.Padding.Left - _body.Padding.Right;
            if (w < 60) w = 60;
            int x = 0;
            int y = 0;

            // 标题行：项名（左）+ 四色风险徽章（右对齐）
            // 标题独占整行；风险徽章移到元信息行右侧——两行长标题不再被徽章压住文字
            _title.MaximumSize = new Size(Math.Max(40, w), 0);
            _title.Location = new Point(x, y);
            y += _title.Height + 6;

            int chipW = 58;
            _meta.MaximumSize = new Size(Math.Max(40, w - chipW - 8), 0);
            _meta.Location = new Point(x, y);
            _risk.Location = new Point(x + w - chipW, y - 1);
            y += Math.Max(_meta.Height, _risk.Height) + 10;

            _divider.SetBounds(x, y, w, 1);
            y += 20;

            // 段头固定高，延线由 SectionHead 自绘撑满
            for (int i = 0; i < 3; i++)
            {
                if (!_heads[i].Visible) continue;
                _heads[i].SetBounds(x, y, w, SectionHead.HeadH);
                y += SectionHead.HeadH + 6;
                _bodies[i].Width = w;
                _bodies[i].Recompute();
                _bodies[i].Location = new Point(x, y);
                y += _bodies[i].Height + 22;
            }

            if (_heads[3].Visible)
            {
                _heads[3].SetBounds(x, y, w, SectionHead.HeadH);
                y += SectionHead.HeadH + 8;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _writeLines.Count; i++)
                {
                    if (i > 0) sb.Append("\r\n");
                    sb.Append(WrapWriteLine(_writeLines[i], Theme.FontMono, w));
                }
                _list.MaximumSize = new Size(w, 0);
                _list.Text = sb.ToString();
                _list.Location = new Point(x, y);
                y += _list.Height + 18;
            }

            // 子项高度已定稿：通知 ScrollHost 按新内容高度重测与裁剪
            _scrollHost.Relayout();
        }

        // --------------------------------------------------------------
        // 段标题：图标 + 彩色粗体标题 + 延伸细线
        // --------------------------------------------------------------

        private sealed class SectionHead : Control
        {
            public const int HeadH = 26;

            private string _icon;
            private Color _tint;
            private readonly string _text;
            private readonly Font _font;

            public SectionHead(string text, string icon, Color tint)
            {
                _text = text;
                _icon = icon;
                _tint = tint;
                _font = Theme.FontSmallBold;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                // 底色与内容面板同色：段标题条本身由 OnPaint 画成「半透明圆角条」，
                // 不再用 CardBgAlt 不透明直角平铺——四段说明就是四条黑色方框，又硬又抢眼。
                BackColor = Theme.CardBg;
                Height = HeadH;
            }

            public string IconKind
            {
                get { return _icon; }
                set { _icon = value; Invalidate(); }
            }

            /// <summary>段色调（风险段随 安全/谨慎 变色）。</summary>
            public Color Tint
            {
                get { return _tint; }
                set { _tint = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // 半透明圆角底条：圆角与全站小元素一致，填充带透明度——
                // 抬升层色以约 43% 叠在内容面板底色上，看起来是"浮着的一层"而不是贴上去的黑框
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                Gfx.FillRound(g, r, 7, Gfx.Alpha(Theme.CardBgAlt, 110));

                // 图标（内收 8px：圆角条内不再贴边）
                IconPainter.Draw(g, _icon, new Rectangle(8, 5, 16, 16), _tint);

                // 彩色粗体标题
                Size ts = TextRenderer.MeasureText(_text, _font);
                TextRenderer.DrawText(g, _text, _font, new Rectangle(30, 3, ts.Width + 8, 20),
                    _tint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                // 标题右侧延伸细线，弱色淡出（收在圆角条内，不再顶到边界）
                int lineY = HeadH / 2;
                int lx = 30 + ts.Width + 8;
                if (Width > lx + 14)
                {
                    using (Pen p = new Pen(Color.FromArgb(60, _tint), 1f))
                    {
                        g.DrawLine(p, lx, lineY, Width - 10, lineY);
                    }
                }
            }
        }
    }
}