﻿/* ============================================================
 * 文件说明：优化中心：全部注册表优化的浏览/搜索/筛选/开关执行页；行列表按分类懒加载，过滤走可见性切换（零重建）。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    public partial class OptimizeView : ViewBase
    {
        private readonly List<ITweak> _tweaks = new List<ITweak>();
        private readonly List<TweakRow> _rows = new List<TweakRow>();
        /// <summary>设计模块页的圆环健康卡：同时表达已启用比例与风险分布，比扁平统计条信息量更大。</summary>
        private readonly ModuleHealthCard _health = new ModuleHealthCard();
        private readonly NoticeBar _notice = new NoticeBar();
        /// <summary>右侧详情栏（主从布局的「从」侧），构造函数中创建。</summary>
        private readonly TweakDetailPanel _rail;
        /// <summary>右栏宽度；列表展开时通过加宽 Body 右内边距为它让位。</summary>
        /// <summary>详情栏宽度：360 给四段说明留出可读行宽（300 时中文每行仅 8-10 字，竖条观感差）。</summary>
        private const int RailWidth = 360;

        /// <summary>右栏动画进度：0=完全收起 1=完全展开；点击优化项后由 0 滑到 1（滑动浮现）。</summary>
        private double _railAnim;
        private bool _railOpening;

        private readonly Dictionary<string, bool> _states = new Dictionary<string, bool>();
        private readonly Panel _toolbar = new Panel();
        private readonly FlowLayoutPanel _chips = new FlowLayoutPanel();
        private readonly List<AccentButton> _chipButtons = new List<AccentButton>();
        /// <summary>当前分类包含的底层分组集合（null = 全部）。</summary>
        private string[] _extraGroups;

        private bool _busy;
        private bool _loaded;
        private string _stateFilter;
        /// <summary>是否显示本机不适用的专属项：默认 false（隐藏），可在工具栏「显示不适用项」开关切换。</summary>
        private bool _showInapplicable;
        private readonly TextBox _search = new TextBox();
        private string _keyword = "";
        private readonly FlowLayoutPanel _filterRow = new FlowLayoutPanel();
        /// <summary>工具型控件行（搜索框 + 显示不适用项开关），挂在状态筛选行右侧。</summary>
        private readonly FlowLayoutPanel _toolsRow = new FlowLayoutPanel();
        private readonly List<AccentButton> _stateChips = new List<AccentButton>();
        private readonly FlowLayoutPanel _profileRow = new FlowLayoutPanel();
        private readonly FlowLayoutPanel _profileChips = new FlowLayoutPanel();
        private string _groupFilter;
        private bool _fixedGroup; // 构造时定一次：固定分组页只建本组，主页面建全部行（切分类仅切可见性）
        private readonly Dictionary<string, bool> _collapsedGroups = new Dictionary<string, bool>();

        /// <summary>外部（磁贴/快捷方式）希望进入页面时直接选中的分类，进入后消费一次。</summary>
        public static string PendingGroup;

        /// <summary>选中某个优化项：右栏滑动浮现显示详情、对应行加选中态（取代模态弹窗）。</summary>
        internal void ShowTweakDetail(ITweak t)
        {
            if (t == null) return;
            DetailInfo info;
            try
            {
                info = TweakDetails.Build(t);
            }
            catch (Exception ex)
            {
                // 详情推导失败记入程序内日志（可在「操作日志」查看）：
                // 原先这里往程序目录写 rail_diag.log，属调试残留——会在用户机器上留文件且无人看
                try { RegLog.Add("UI", "优化项详情推导失败", t.Id + " | " + ex.Message); } catch { }
                // 兜底：推导失败也必须给出非空内容，否则右栏会变成空白黑块（用户感知为"黑块没反应"）
                info = new DetailInfo();
                string grp = string.IsNullOrEmpty(t.Group) ? TweakPackProvider.GPack : t.Group;
                info.Title = t.Name + "（" + grp + "）";
                info.What = string.IsNullOrEmpty(t.Description) ? t.Name : t.Description;
                info.Why = "（详细推导暂时不可用，已回退为基础说明）";
                info.Risk = t.Risky ? "此项标记为「谨慎」，请先单项启用确认无异常。" : "此项属于普遍适用的安全调整。";
                info.Reversible = "可撤销：关闭开关即可还原到系统默认状态。";
            }
            _rail.Show(t, info);
            OpenRail();   // 点击即让右栏滑出（已展开时也安全：仅确保展开态）
            for (int i = 0; i < _rows.Count; i++) _rows[i].SetSelected(_rows[i].Tweak == t);
        }

        /// <summary>自动截图辅助：展开第一个可见优化项的右栏详情（--shots 模式调用）。</summary>
        internal void OpenFirstRail()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Visible && _rows[i].Tweak != null)
                {
                    ShowTweakDetail(_rows[i].Tweak);
                    // 泵 700ms 让滑入动画跑完（计时器依赖消息循环），右栏归位后再截图
                    int end = Environment.TickCount + 700;
                    while (Environment.TickCount < end)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(30);
                    }
                    return;
                }
            }
        }

        /// <summary>右栏按钮触发的开关：定位到对应行后复用行级 ToggleTweak（含危险确认与备份链）。</summary>
        internal void ToggleFromRail(ITweak t)
        {
            if (t == null) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Tweak == t)
                {
                    ToggleTweak(_rows[i]);
                    _rail.RefreshState();
                    return;
                }
            }
        }

        /// <summary>行状态变化后同步右栏按钮语义（右栏可能正显示该项）。</summary>
        internal void OnRowStateChanged(TweakRow row)
        {
            if (_rail != null && !_rail.IsEmpty && _rail.Current == row.Tweak) _rail.RefreshState();
        }

        public OptimizeView()
            : this("优化中心", "全部优化项：游戏 / 网络 / 性能 / 电源 / 隐私 / 精简 / 外观 / 服务，逐项开关随时还原", null)
        {
        }

        /// <summary>groupFilter 非空时只显示该分组的优化项（如「游戏优化」页）。</summary>
        public OptimizeView(string title, string subtitle, string groupFilter)
            : base(title, subtitle)
        {
            _groupFilter = groupFilter;
            // 固定分组模式：必须同时设 _extraGroups 才会真正过滤——FilterByGroups 只在
            // _extraGroups 非空时生效，只设 _groupFilter 的话该参数形同虚设。
            bool fixedGroup = !string.IsNullOrEmpty(groupFilter);
            _fixedGroup = fixedGroup;
            _extraGroups = fixedGroup ? new string[] { groupFilter } : null;

            _notice.NoticeIcon = "info";
            _notice.NoticeAccent = Theme.Warning;
            // 硬件感知提示：告诉用户本机 CPU/GPU，专属项只对本机硬件生效
            _notice.NoticeText = "每一项优化都会自动备份修改前的注册表内容，关闭开关即可还原到系统默认状态。" +
                BuildHardwareHint();

            AddAction("一键推荐优化", "bolt", ButtonVariant.Primary, OnRecommendedClick, 152);
            AddAction("全部还原", "undo", ButtonVariant.Danger, OnRestoreAllClick, 110);
            if (!fixedGroup)
            {
                // 方案库与方案文件都是跨分类功能：固定分组页里放它们会误导（保存出来的是残缺方案）
                AddAction("导出方案", "doc", ButtonVariant.Ghost, OnExportProfile, 110);
                AddAction("导入方案", "plus", ButtonVariant.Secondary, OnImportProfile, 110);
            }
            AddAction("刷新状态", "refresh", ButtonVariant.Secondary, delegate { Load(true); }, 110);
            _sortButton = AddAction("排序：推荐优先", "sort", ButtonVariant.Ghost, OnSortClick, 130);

            AddFull(_notice, 30, 8);

            // 健康卡单独一行；搜索框与「显示不适用项」下移到状态筛选行（见 BuildToolbar）。
            // 原先二者同一行：健康卡 76px 高会把那一行撑到 76px，28px 的搜索框贴在行顶部、
            // 下方空一大截 —— 就是用户反馈的"搜索框非要那么高"。
            BuildToolbar();
            FlowLayoutPanel row = MakeRow(0, 8);
            // 自然宽度行不拉宽子项。宽度给到能显示「安全 / 谨慎」图例
            //（ModuleHealthCard 要求 Width ≥ 环右缘 + 260，否则图例整块不画、右侧留白）
            _health.Width = 380;
            row.Controls.Add(_health);
            AddRow(row);

            // 固定分组页 = 设计式「模块页」：只保留状态筛选，不再出现主分类切换与方案库。
            // 三组切换（分类 / 状态 / 方案）各自独立成行、一律左起、尺寸与间距一致；
            // 原先状态 chip 挤在健康卡那半行里，两组切换分散在两处、观感不统一。
            if (!fixedGroup) AddFull(_toolbar, 30, 6);
            if (!fixedGroup) AddFull(_filterRow, 30, 6);
            if (!fixedGroup) AddFull(_profileRow, 30, 8);
            AddExtraControls();
            Relayout();

            // 主从布局：详情栏默认收起（列表满宽、不挡 UI），点击优化项后从右缘滑动浮现。
            // 展开时列表临时加宽右内边距让位，栏滑入填满预留区；收起后列表收回满宽。
            _rail = new TweakDetailPanel(this);
            _rail.StateReader = delegate (ITweak t)
            {
                bool applied = false;
                _states.TryGetValue(t.Id, out applied);
                return applied;
            };
            _rail.Visible = false;
            Controls.Add(_rail);
            // Z 序实证（本机实测）：先加入者在其中在上层，BringToFront 即提到最前。
            // _scroll 在基类构造中最先加入、位于 _rail 之上，若不显式置顶，
            // 详情栏会被整宽内容面板盖住——不可见也不可点，用户感知为"按了没反应"。
            _rail.BringToFront();
            Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop, Theme.PagePadX, Theme.PagePadBottom);
            // 右栏滑入/滑出由全局 AnimationClock 驱动（16ms 粒度）
            Resize += delegate { PositionRail(); };
            PositionRail();
        }

        /// <summary>把详情栏钉到视口右侧（页头之下、随窗口缩放跟随）。
        /// Z 序实证（本机实测）：先加入者在上层，BringToFront 即提到最前；
        /// _rail 已在构造中加入后显式置顶，这里无需再动 z 序。</summary>
        private void PositionRail()
        {
            int hh = EffectiveHeader;
            // 顶到页首工具行（通知条 / 健康卡+搜索行 / 分类条 / 方案库行）之下，不遮搜索框
            int y = hh + Theme.PagePadTop;
            // 取各工具行的底边（_toolsRow.Parent = 「健康卡 + 搜索」那一行）
            Control[] tops = new Control[] { _notice, _health.Parent, _toolbar, _filterRow, _profileRow };
            int maxBottom = 0;
            for (int i = 0; i < tops.Length; i++)
            {
                Control c = tops[i];
                if (c == null || c.Parent == null || c.Height <= 0) continue;
                int bottom = c.Top + c.Height + c.Margin.Bottom;
                if (bottom > maxBottom) maxBottom = bottom;
            }
            y += Math.Max(maxBottom, 82) + 4; // 82=通知条+搜索行的固定下限，防滚动后误上移
            int h = Math.Max(0, ClientSize.Height - y - Theme.PagePadBottom);
            int finalX = Math.Max(0, ClientSize.Width - Theme.PagePadX - RailWidth);
            // 收起进度越高越靠右推：_railAnim=0 时整栏在视口外，滑到 1 时归位
            int x = finalX + (int)((1 - _railAnim) * (RailWidth + 16));
            _rail.SetBounds(x, y, RailWidth, h);
        }

        /// <summary>点击优化项：列表让出右侧空间，详情栏从右缘滑动浮现（主从两栏交互）。</summary>
        private void OpenRail()
        {
            _rail.Visible = true;
            _railOpening = true;
            Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop,
                Theme.PagePadX + RailWidth + 10, Theme.PagePadBottom);
            // 列表让位变窄后筛选行可能换行变高，页首行底边随之下移——
            // 立即重算右栏顶部，否则 y 停在旧值会把换行后的搜索框盖住。
            PositionRail();
            AnimationClock.Instance.Subscribe(RailTick);
        }

        /// <summary>收起详情栏：滑出右缘后隐藏，列表收回满宽。</summary>
        internal void CloseRail()
        {
            _railOpening = false;
            if (_railAnim <= 0)
            {
                // 已收起：这里必须把"列表右侧让位"的宽度一起还原。
                // 原实现直接 return —— 若 OpenRail 刚加宽 Body 右内边距、滑动动画还没跑到第一帧
                // （_railAnim 仍为 0）就关闭，列表右侧会留下一条永不回收的深色空带，
                // 用户看到的就是"右边多出一个黑框挡住内容"。
                _rail.Visible = false;
                Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop, Theme.PagePadX, Theme.PagePadBottom);
                PositionRail();
                return;
            }
            AnimationClock.Instance.Subscribe(RailTick);
        }

        /// <summary>每帧推进右栏滑入/滑出动画，匀速逼近目标，手感顺滑。</summary>
        private void RailTick()
        {
            double step = Theme.Motion.RailStep;
            _railAnim += _railOpening ? step : -step;
            if (_railAnim >= 1) { _railAnim = 1; AnimationClock.Instance.Unsubscribe(RailTick); }
            if (_railAnim <= 0)
            {
                _railAnim = 0;
                AnimationClock.Instance.Unsubscribe(RailTick);
                _rail.Visible = false;
                Body.Padding = new Padding(Theme.PagePadX, Theme.PagePadTop, Theme.PagePadX, Theme.PagePadBottom);
                PositionRail();
                return;
            }
            PositionRail();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 右栏滑入动画随页面一起停掉：否则关页后仍在跑并持有已释放的行控件
                AnimationClock.Instance.Unsubscribe(RailTick);
                TweakRow.ShutdownAnimClock();
                _building = false;   // 分片构建的下一帧回调据此直接退出，不再访问已释放的 Body
            }
            base.Dispose(disposing);
        }

        /// <summary>扩展点：在列表上方追加本页专属控件。优化中心在这里挂「本机专属推荐」卡。</summary>
        protected virtual void AddExtraControls()
        {
            if (!string.IsNullOrEmpty(_groupFilter)) return; // 固定分组页（模块页）不显示
            BuildVendorCard();
        }

        private Label MakeLabel(string text, int width, bool bold)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = bold ? Theme.TextPrimary : Theme.TextMuted;
            l.Font = bold ? Theme.FontBodyBold : Theme.FontBody;
            l.TextAlign = ContentAlignment.MiddleLeft;
            if (width > 0) l.Size = new Size(width, 30);
            else l.AutoSize = true;
            l.Margin = new Padding(0, 0, 8, 0);
            return l;
        }

        /// <summary>本机专属推荐卡：检测 CPU/GPU 厂商，列出可用专属项，一键应用。
        /// v2.2 拆两行：第一行「标签+硬件摘要+按钮」，第二行「项名称」——
        /// 单行 FlowLayoutPanel 在 1180 最小窗口下总宽溢出，文字会与按钮叠在一起。</summary>
        private void BuildVendorCard()
        {
            List<ITweak> vendorTweaks = HardwareRecommender.CollectApplicable();
            string summary = HardwareRecommender.HardwareSummary();

            if (vendorTweaks.Count == 0)
            {
                FlowLayoutPanel row0 = MakeRow(0, 10);
                row0.Controls.Add(MakeLabel("专属优化", 84, true));
                Label none = MakeLabel("（未识别到本机专属优化项）", 260, false);
                none.Margin = new Padding(12, 8, 0, 0);
                row0.Controls.Add(none);
                AddRow(row0);
                return;
            }

            // 名称列表过长会溢出截断：最多展示 2 个名称 + 等计（完整清单在确认弹窗里看）
            const int Show = 2;
            string names = "";
            for (int i = 0; i < vendorTweaks.Count && i < Show; i++)
            {
                if (i > 0) names += " · ";
                names += vendorTweaks[i].Name;
            }
            if (vendorTweaks.Count > Show) names += " 等 " + vendorTweaks.Count + " 项";

            // 第一行：标签 + 硬件摘要 + 项数 + 应用按钮。
            // 不能用 FlowLayoutPanel：LayoutRowChildren 会把行内非 AutoSize 控件**等分**宽度，
            // 「专属优化」标签被撑到 1/3 行宽，摘要文字因此落到行的中间（用户反馈"非要在中间"）。
            // 这里用普通 Panel 手动摆位：标签固定宽 → 摘要紧跟其后 → 按钮贴右。
            Panel row1 = new Panel();
            row1.BackColor = Theme.WindowBg;

            Label tagLabel = MakeLabel("专属优化", 84, true);
            Label title = MakeLabel(summary + " · " + vendorTweaks.Count + " 项可用", 0, true);
            title.AutoSize = true;

            AccentButton apply = new AccentButton();
            apply.Text = "应用专属推荐";
            apply.IconKind = "bolt";
            apply.Variant = ButtonVariant.Primary;
            apply.Height = 30;
            apply.FitToText(120);
            apply.Click += delegate { ApplyVendorRec(vendorTweaks); };

            row1.Controls.Add(tagLabel);
            row1.Controls.Add(title);
            row1.Controls.Add(apply);
            // 标签右缘 → 摘要起点留 14px：原先只留 8px，「专属优化」与摘要挤在一起像连成一个词
            const int TagW = 84;
            const int TextX = TagW + 14;
            row1.Resize += delegate
            {
                int h = row1.ClientSize.Height;
                int ty = Math.Max(0, (h - 30) / 2);
                int right = row1.ClientSize.Width - apply.Width - 2;
                tagLabel.SetBounds(0, ty, TagW, 30);
                // 标题过长时截断让位：按钮永远钉在右缘以内。
                // 此前 bx 取 Max(title.Right + 12, right)，标题一长按钮就被推出窗口边缘裁掉。
                title.MaximumSize = new Size(Math.Max(40, right - TextX - 12), 30);
                title.SetBounds(TextX, ty, title.PreferredWidth, 30);
                apply.SetBounds(right, Math.Max(0, (h - apply.Height) / 2), apply.Width, apply.Height);
            };
            AddFull(row1, 32, 6);

            // 第二行：项名称（缩进对齐摘要，独占整行不再与按钮同排）
            FlowLayoutPanel row2 = MakeRow(0, 10);
            Label pad = MakeLabel("", 84, false);
            row2.Controls.Add(pad);
            Label list = MakeLabel(names, 0, false);
            list.MaximumSize = new Size(700, 64);   // 放宽：520 时中文名称常被折成两行
            // 缩进对齐第一行的摘要起点（TagW 84 + pad 的 8 + 这里 6 = 98）：
            // 原先 12 → 起点 104，比摘要右移 6px，两行的左缘看着是错的
            list.Margin = new Padding(6, 0, 0, 0);
            row2.Controls.Add(list);
            AddRow(row2);
        }

        private void ApplyVendorRec(List<ITweak> targets)
        {
            if (_busy) return;
            List<ITweak> todo = new List<ITweak>();
            for (int i = 0; i < targets.Count; i++)
            {
                bool applied = false;
                _states.TryGetValue(targets[i].Id, out applied);
                if (!applied) todo.Add(targets[i]);
            }
            if (todo.Count == 0)
            {
                Dialog.Info(this, "已应用", "本机专属推荐项都已启用，无需重复操作。");
                return;
            }

            string list = "";
            for (int i = 0; i < todo.Count; i++) list += "· " + todo[i].Name + "\r\n";
            if (!Dialog.Confirm(this, "应用专属优化",
                "将启用以下 " + todo.Count + " 项本机硬件专属优化：\r\n\r\n" + list +
                "\r\n所有改动都会被备份，可随时单独还原。是否继续？"))
                return;

            int okCount = 0, failCount = 0, adminNeeded = 0;
            for (int i = 0; i < todo.Count; i++)
            {
                ITweak t = todo[i];
                if (!TweakApplicability.IsApplicable(t)) continue; // 不适用本机硬件的专属项直接跳过
                if (t.AdminOnly && !Native.IsElevated()) { adminNeeded++; failCount++; continue; }
                if (TweakExecutor.Apply(t).IsOk) { okCount++; _states[t.Id] = true; }
                else { failCount++; }
            }
            SyncRows();
            UpdateSummary();
            SetSubtitle("专属优化完成：" + okCount + " 项成功。", failCount > 0 ? Theme.Warning : Theme.Success);
        }

        /// <summary>行首小标签（"分类" / "状态"）：三组切换一眼可辨，不再是一堆不知筛什么的 chip。</summary>
        private static Label RowTag(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Theme.TextMuted;
            l.Font = Theme.FontSmall;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Size = new Size(40, 30);
            l.Margin = new Padding(0, 0, 6, 0);
            return l;
        }

        private void BuildToolbar()
        {
            _toolbar.BackColor = Theme.WindowBg;
            _toolbar.Height = 34;

            // 工具行（搜索 + 不适用项开关）：跟在健康卡右侧，按自然宽度排布
            _toolsRow.FlowDirection = FlowDirection.LeftToRight;
            _toolsRow.WrapContents = false;
            _toolsRow.AutoSize = true;
            _toolsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _toolsRow.BackColor = Theme.WindowBg;
            _toolsRow.Margin = new Padding(12, 0, 0, 0);

            // 状态筛选行：独立成行（整宽），不再与健康卡分半行 —— 5 个 chip 放得下，也不会被裁
            _filterRow.FlowDirection = FlowDirection.LeftToRight;
            _filterRow.WrapContents = false;
            _filterRow.AutoSize = false;
            _filterRow.BackColor = Theme.WindowBg;
            _filterRow.Height = 30;
            _filterRow.Margin = new Padding(0);
            _filterRow.Controls.Add(RowTag("状态"));

            string[] stateChips = new string[] { "全部状态", "已启用", "未启用", "推荐", "谨慎" };
            string[] stateKeys = new string[] { null, "on", "off", "rec", "risky" };
            for (int i = 0; i < stateChips.Length; i++)
            {
                string key = stateKeys[i];
                AccentButton chip = new AccentButton();
                chip.Text = stateChips[i];
                chip.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                chip.Click += delegate
                {
                    _stateFilter = key;
                    for (int k = 0; k < _stateChips.Count; k++)
                    {
                        _stateChips[k].Variant = _stateChips[k] == chip
                            ? ButtonVariant.Primary : ButtonVariant.Ghost;
                    }
                    ApplyFilters(); // 只切可见性，不重建
                };
                _stateChips.Add(chip);
                _filterRow.Controls.Add(chip);
            }

            // 关键词搜索：242 个优化项光靠分类 / 状态筛选不够，
            // 按名称 / 说明 / 标识实时过滤（只切可见性，不重建行）
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Theme.FontBody;
            _search.Size = new Size(200, 28);
            Native.SetCue(_search, "搜索优化项…");
            _search.Margin = new Padding(0, 0, 0, 0);
            _search.TextChanged += delegate
            {
                _keyword = _search.Text.Trim();
                ApplyFilters();
            };
            _toolsRow.Controls.Add(ThemeInput.WrapSearch(_search));

            // 不适用项开关：默认隐藏本机不适用的专属项；开启后显示它们（开关仍 inert，应用会被拒绝）
            AccentButton showInapplicable = new AccentButton();
            showInapplicable.Text = "显示不适用项";
            showInapplicable.Variant = ButtonVariant.Ghost;
            showInapplicable.Height = 28;
            showInapplicable.FitToText(96);
            showInapplicable.Margin = new Padding(8, 0, 0, 0);   // 与搜索框之间留间距
            showInapplicable.Click += delegate
            {
                _showInapplicable = !_showInapplicable;
                showInapplicable.Variant = _showInapplicable ? ButtonVariant.Primary : ButtonVariant.Ghost;
                ApplyFilters();
                SetSubtitle(_showInapplicable ? "已显示本机不适用项（灰显，应用会被拒绝）" : "已隐藏本机不适用项",
                    Theme.TextSecondary);
            };
            _toolsRow.Controls.Add(showInapplicable);

            // 搜索 + 不适用项开关：挂在状态筛选行右侧（那一行只有 5 个 chip，右侧空着），
            // 行高 30px 而不是健康卡行的 76px —— 页头少占竖高，首屏多露一行优化项。
            _toolsRow.Margin = new Padding(18, 0, 0, 0);
            _filterRow.Controls.Add(_toolsRow);

            // 方案行：独立一行，避免与状态筛选挤在一行被裁剪
            _profileRow.FlowDirection = FlowDirection.LeftToRight;
            _profileRow.WrapContents = false;
            _profileRow.BackColor = Theme.WindowBg;
            _profileRow.Height = 34;
            _profileRow.Tag = "stretch";

            AccentButton saveProfile = new AccentButton();
            saveProfile.Text = "保存方案";
            saveProfile.IconKind = "plus";   // 原来用全角「＋」拼在文字里，与其它按钮的图标体系不一致
            saveProfile.Variant = ButtonVariant.Secondary;
            saveProfile.Height = 28;
            saveProfile.FitToText(72);
            saveProfile.Margin = new Padding(0, 0, 8, 0);
            saveProfile.Click += OnSaveProfileClick;
            _profileRow.Controls.Add(saveProfile);

            BuildProfileChips();
            _profileRow.Controls.Add(_profileChips);

            // 分类切换：独立一行，与状态 / 方案行同规格（左起、同高、同间距）
            _toolbar.Controls.Add(RowTag("分类"));
            _toolbar.Controls.Add(_chips);

            // 分类筛选条：收敛为 5 个主要分类（对应底层分组聚合）
            _chips.FlowDirection = FlowDirection.LeftToRight;
            _chips.WrapContents = false;
            _chips.BackColor = Theme.WindowBg;
            _chips.Height = 34;

            for (int i = 0; i < CategoryNames.Length; i++)
            {
                AccentButton chip = new AccentButton();
                chip.Text = CategoryNames[i];
                chip.Variant = i == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost;
                chip.Height = 28;
                chip.FitToText(72);
                chip.Margin = new Padding(0, 0, 8, 0);
                int idx = i; // 闭包按索引固定
                chip.Click += delegate { ApplyCategory(idx); };
                _chipButtons.Add(chip);
                _chips.Controls.Add(chip);
            }

            // 从行首标签之后开始排（46 = 标签宽 40 + 间距 6），否则 chip 条会盖住「分类」标签
            _toolbar.Resize += delegate
            {
                _chips.SetBounds(46, 0, Math.Max(60, _toolbar.Width - 46), 34);
            };
            _chips.SetBounds(46, 0, Math.Max(60, _toolbar.Width - 46), 34);
        }

        /// <summary>主分类名与对应的底层分组集合（索引 0 = 全部）。</summary>
        private static readonly string[] CategoryNames = new string[]
        {
            "全部", "游戏", "性能与电源", "网络", "隐私与安全", "系统服务", "精简与外观", "音频"
        };

        /// <summary>
        /// 分类聚合必须覆盖 TweakLibrary 的**全部分组**：
        /// 此前「极限性能」（17 项）不在任何分类里 —— 切到任一分类都看不到它们，只有"全部"才可见；
        /// 「外观与体验」也被塞进"隐私与精简"，名不副实。这里按名称语义重新归并（10 组 → 全部覆盖）。
        /// </summary>
        private static readonly string[][] CategoryGroups = new string[][]
        {
            null,
            new string[] { TweakLibrary.GGame },
            new string[] { TweakLibrary.GPerformance, TweakLibrary.GExtreme, TweakLibrary.GPower },
            new string[] { TweakLibrary.GNetwork },
            new string[] { TweakLibrary.GPrivacy },
            new string[] { TweakLibrary.GServices },
            new string[] { TweakLibrary.GSlim, TweakLibrary.GAppearance },
            new string[] { TweakLibrary.GAudio }
        };

        /// <summary>切换到指定主分类：行与状态探测已覆盖全部优化项（只建一次），
        /// 切分类只改变可见性集合（ApplyFilters 毫秒级），不再清空重建——这是消除切分类卡顿的关键。</summary>
        private void ApplyCategory(int index)
        {
            if (index < 0 || index >= CategoryNames.Length) return;
            _groupFilter = CategoryGroups[index] == null ? null : CategoryGroups[index][0];
            _extraGroups = CategoryGroups[index];
            for (int k = 0; k < _chipButtons.Count; k++)
            {
                _chipButtons[k].Variant = k == index
                    ? ButtonVariant.Primary : ButtonVariant.Ghost;
                _chipButtons[k].Invalidate();
            }
            // 注意：此处刻意不清空 _tweaks/_states/_rows，也不调用 Load。
            // 后台探测始终针对全部优化项并写 _states，切分类只是可见性重算，
            // 隐藏行也已被探测回填真实状态，切回即正确显示，无需重建。
            ApplyFilters();
            // 切换分类后让新露面的行依次落位：整屏内容换掉时给一点过渡，而不是"啪"地一下
            Post(delegate { CascadeVisibleRows(); });
            int vis = 0;
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Visible) vis++;
            SetSubtitle(CategoryNames[index] + "：" + vis + " 项优化" +
                (_loaded ? "" : "，正在读取状态…"), Theme.TextSecondary);
        }

        // 注意：本页刻意不 override IsBusy——状态探测期间行列表已即时渲染，
        // 若显示全屏遮罩反而挡住内容；进度通过页头副标题反馈。

        /// <summary>按当前分类的分组集合过滤（_extraGroups 为 null 表示全部）。</summary>
        private List<ITweak> FilterByGroups(List<ITweak> source)
        {
            if (_extraGroups == null) return new List<ITweak>(source);
            List<ITweak> result = new List<ITweak>();
            for (int i = 0; i < source.Count; i++)
            {
                for (int k = 0; k < _extraGroups.Length; k++)
                {
                    if (source[i].Group == _extraGroups[k])
                    {
                        result.Add(source[i]);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>本机 CPU/GPU 摘要，让用户清楚哪些厂商专属项适用于自己。</summary>
        private static string BuildHardwareHint()
        {
            string cpu = CpuVendor.IsIntel ? "Intel CPU" : (CpuVendor.IsAmd ? "AMD CPU" : "CPU");
            string gpu = GpuLatencyTweak.DetectVendor() == "N" ? "NVIDIA 显卡"
                : GpuLatencyTweak.DetectVendor() == "A" ? "AMD 显卡"
                : GpuLatencyTweak.DetectVendor() == "I" ? "Intel 核显" : "未知显卡";
            return "（本机：" + cpu + " + " + gpu + "，不适用本机硬件的专属项会自动隐藏，可用工具栏「显示不适用项」开关查看）";
        }

        /// <summary>让当前可见的前若干行做一次级联入场（展开分组 / 切换分类后的过渡）。</summary>
        private void CascadeVisibleRows()
        {
            if (!AppSettings.Animations || _rows.Count == 0) return;
            List<Control> list = new List<Control>();
            for (int i = 0; i < _rows.Count && list.Count < 10; i++)
            {
                if (_rows[i].Visible) list.Add(_rows[i]);
            }
            if (list.Count == 0) return;
            CascadeIn(list);
        }

        private bool _groupAnimBusy;
        /// <summary>正在做"展开动画"的分组名：BuildRows 时该组新行以 1px 起始高度建出。</summary>
        private string _expandAnimGroup;

        /// <summary>
        /// 折叠 / 展开分组：内容以高度插值收拢 / 展开（约 240ms），而不是瞬间切换。
        /// 行数超过 40 的组直接切换——每帧重排那么多行的代价大于观感收益（与整体"减少卡顿"的取舍一致）。
        /// 动画期间忽略重复点击（直接切换），避免两段动画互相打架。
        /// </summary>
        private void AnimateGroupToggle(string group, bool collapse)
        {
            if (!AppSettings.Animations || _groupAnimBusy)
            {
                _collapsedGroups[group] = collapse;
                BuildRows(true);   // 紧随其后要用行集合，必须同步建完
                return;
            }

            if (!collapse)
            {
                // 展开：先把行以 1px 建出来，再让它们长回正常高度
                _groupAnimBusy = true;
                _expandAnimGroup = group;
                _collapsedGroups[group] = false;
                BuildRows(true);   // 紧接着 CollectGroupRows 要拿这些新行做展开动画
                _expandAnimGroup = null;

                List<TweakRow> grow = CollectGroupRows(group);
                if (grow.Count == 0 || grow.Count > 40)
                {
                    _groupAnimBusy = false;
                    for (int i = 0; i < grow.Count; i++)
                    {
                        if (grow[i].IsDisposed) continue;
                        grow[i].SuppressAutoHeight = false;
                        grow[i].Height = grow[i].NormalHeight;
                    }
                    RefreshLayout();
                    return;
                }
                int[] target = new int[grow.Count];
                for (int i = 0; i < grow.Count; i++) target[i] = grow[i].NormalHeight;
                RunGroupAnim(grow, target, true, group);
                return;
            }

            List<TweakRow> rows = CollectGroupRows(group);
            if (rows.Count == 0 || rows.Count > 40)
            {
                _collapsedGroups[group] = true;
                BuildRows(true);
                return;
            }
            int[] cur = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++) cur[i] = rows[i].Height;
            _groupAnimBusy = true;
            RunGroupAnim(rows, cur, false, group);
        }

        private List<TweakRow> CollectGroupRows(string group)
        {
            List<TweakRow> list = new List<TweakRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                TweakRow r = _rows[i];
                if (r != null && !r.IsDisposed && r.Tweak != null && r.Tweak.Group == group && r.Visible) list.Add(r);
            }
            return list;
        }

        /// <summary>分组高度动画：grow=true 由 1px 长到 sizes[i]；false 由 sizes[i] 收到 1px。</summary>
        private void RunGroupAnim(List<TweakRow> rows, int[] sizes, bool grow, string group)
        {
            for (int i = 0; i < rows.Count; i++) rows[i].SuppressAutoHeight = true;

            float pos = 0f;
            Action tick = null;
            tick = delegate
            {
                pos += 0.12f;
                bool done = pos >= 1f;
                if (done) pos = 1f;
                float t = Theme.Ease.CubicOut(pos);
                try
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        TweakRow r = rows[i];
                        if (r.IsDisposed) continue;
                        int h = grow ? (int)(sizes[i] * t) : (int)(sizes[i] * (1 - t));
                        if (h < 1) h = 1;
                        if (r.Height != h) r.Height = h;
                    }
                    RefreshLayout();
                }
                catch
                {
                    done = true;
                }
                if (!done) return;

                AnimationClock.Instance.Unsubscribe(tick);
                _groupAnimBusy = false;
                try
                {
                    if (grow)
                    {
                        for (int i = 0; i < rows.Count; i++)
                        {
                            if (rows[i].IsDisposed) continue;
                            rows[i].SuppressAutoHeight = false;
                            rows[i].Height = sizes[i];
                        }
                        RefreshLayout();
                        Post(delegate { CascadeVisibleRows(); });
                    }
                    else
                    {
                        // 收拢完成：真正折叠并重建（行这才消失、分组计数更新）
                        _collapsedGroups[group] = true;
                        BuildRows(true);   // 折叠收尾后立即重建，避免与分片交叉导致状态不一致
                    }
                }
                catch
                {
                }
            };
            AnimationClock.Instance.Subscribe(tick);
        }

        private void Relayout()
        {
            // 健康卡高度固定 ⇒ 行高固定，不再随数据变化（也就不会在挂载后触发重排）
            Control row = _health.Parent;
            if (row != null) row.Height = _health.Height;
            RefreshLayout();
        }

        public override void OnActivated()
        {
            // 磁贴带入的分组（如「游戏优化」）：映射到对应主分类并选中，只消费一次
            if (!string.IsNullOrEmpty(PendingGroup) && _chipButtons.Count > 0)
            {
                int hit = -1;
                for (int i = 0; i < _chipButtons.Count; i++)
                {
                    if (_chipButtons[i].Text == PendingGroup) { hit = i; break; }
                }
                if (hit < 0)
                {
                    // 按主分类归属查找：直接复用 CategoryGroups（单一数据源），
                    // 避免手写 maps 与分类数量脱节导致索引越界崩溃
                    for (int i = 0; i < CategoryGroups.Length && hit < 0; i++)
                    {
                        string[] groups = CategoryGroups[i];
                        if (groups == null) continue;
                        for (int k = 0; k < groups.Length; k++)
                        {
                            if (groups[k] == PendingGroup) { hit = i; break; }
                        }
                    }
                }
                if (hit >= 0)
                {
                    ApplyCategory(hit);
                }
                PendingGroup = null;
            }
            // 主页面/固定页行只构建一次：首次进入 Load 建全部行；之后回到前台按当前分类重算可见性
            if (!_loaded) Load(false);
            else ApplyFilters();
        }

        public override void OnDeactivated()
        {
            if (_rows.Count > 0) CheckOverflow();
        }

        /// <summary>把超出内容区右侧的分组标题排好（分组标题宽度依赖内容区宽度）。</summary>
        private void CheckOverflow()
        {
            LayoutRows();
        }

        // --------------------------------------------------------------

        private int _loadGen;
        private int _sortMode;
        private AccentButton _sortButton;
        private static readonly string[] SortNames = new string[] { "推荐优先", "风险靠后", "名称" };

        private void Load(bool force)
        {
            // 强制重载（刷新按钮）同样可打断在途会话；普通 Load 在忙时跳过
            if (_busy && !force) return;
            _busy = false;
            _busy = true;
            _loadGen++;                // 会话令牌：期间发生的任何重载使旧探测结果作废
            int gen = _loadGen;
            SetSubtitle("正在读取当前优化状态…", Theme.Warning);

            if (force) ClearRows();

            // 关键体验修复：立即渲染完整列表（状态先用「未启用」占位），
            // 后台探测完成后逐行回填真实状态——点进去马上能看到全部优化项，
            // 不再是几秒空白、要切走切回才出现。
            // 主页面一次构建全部分组行（切分类/状态只切可见性，不再同步建 384 行卡顿）；
            // 固定分组页仍只构建本组；force（刷新按钮）强制重建。
            if (_tweaks.Count == 0 || force)
            {
                List<ITweak> all = _fixedGroup ? FilterByGroups(TweakLibrary.All()) : TweakLibrary.All();
                _tweaks.Clear();
                _tweaks.AddRange(all);
                SortTweaks();
                _states.Clear();
                for (int i = 0; i < _tweaks.Count; i++) _states[_tweaks[i].Id] = false;
                BuildRows();
                UpdateSummary();
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ITweak> all = null;
                Dictionary<string, bool> states = new Dictionary<string, bool>();
                string error = null;
                try
                {
                    all = FilterByGroups(TweakLibrary.All());
                    // 分批并行探测：全部项同时读注册表会引发 I/O 风暴导致界面卡顿，
                    // 改为每批 16 项并行，批间不等待——总时间相近但峰值 I/O 大幅降低
                    object sync = new object();
                    int batchSize = 16;
                    for (int batchStart = 0; batchStart < all.Count; batchStart += batchSize)
                    {
                        int end = Math.Min(batchStart + batchSize, all.Count);
                        System.Threading.Tasks.Parallel.For(batchStart, end, delegate(int i)
                        {
                            bool applied = false;
                            try { applied = all[i].IsApplied(); }
                            catch { }
                            lock (sync) { states[all[i].Id] = applied; }
                        });
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Post(delegate
                {
                    if (gen != _loadGen) return; // 已切换分类/重载：丢弃过期探测结果
                    _busy = false;
                    if (error != null)
                    {
                        SetSubtitle("读取失败：" + error, Theme.Danger);
                        return;
                    }

                    _states.Clear();
                    foreach (KeyValuePair<string, bool> kv in states) _states[kv.Key] = kv.Value;

                    // 逐行回填真实状态（不重建行，保持滚动位置）
                    for (int i = 0; i < _rows.Count; i++)
                    {
                        bool applied;
                        if (_states.TryGetValue(_rows[i].Tweak.Id, out applied)) _rows[i].SetState(applied);
                    }
                    UpdateSummary();
                    _loaded = true;
                    SetSubtitle("共 " + _tweaks.Count + " 项优化，已启用 " + CountApplied() + " 项。",
                        Theme.TextSecondary);
                });
            });
        }

        /// <summary>
        /// 按当前排序模式重排 _tweaks：保持「分组聚集」（组间顺序沿用列表首次出现顺序，不打散大功能），
        /// 仅组内排序。默认（推荐优先）：Recommended 在前、Risky 靠后、再按名称；风险靠后：Risky 靠后；名称：纯字母序。
        /// </summary>
        private void SortTweaks()
        {
            Dictionary<string, int> order = new Dictionary<string, int>();
            int gi = 0;
            for (int i = 0; i < _tweaks.Count; i++)
            {
                string g = _tweaks[i].Group;
                if (!order.ContainsKey(g)) { order[g] = gi; gi++; }
            }
            _tweaks.Sort(delegate(ITweak a, ITweak b)
            {
                int g = order[a.Group].CompareTo(order[b.Group]);
                if (g != 0) return g;
                if (_sortMode == 0)
                {
                    int r = (b.Recommended ? 1 : 0).CompareTo(a.Recommended ? 1 : 0);
                    if (r != 0) return r;
                    int k = (a.Risky ? 1 : 0).CompareTo(b.Risky ? 1 : 0);
                    if (k != 0) return k;
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                }
                else if (_sortMode == 1)
                {
                    int k = (a.Risky ? 1 : 0).CompareTo(b.Risky ? 1 : 0);
                    if (k != 0) return k;
                    int r = (b.Recommended ? 1 : 0).CompareTo(a.Recommended ? 1 : 0);
                    if (r != 0) return r;
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                }
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private void OnSortClick(object sender, EventArgs e)
        {
            _sortMode = (_sortMode + 1) % 3;
            if (_sortButton != null) _sortButton.Text = "排序：" + SortNames[_sortMode];
            SortTweaks();
            BuildRows(true);   // 同步重建：复用已探测的 _states，不重新读注册表
            for (int i = 0; i < _rows.Count; i++)
            {
                bool ap;
                if (_states.TryGetValue(_rows[i].Tweak.Id, out ap)) _rows[i].SetState(ap);
            }
            UpdateSummary();
        }

        private void ClearRows()
        {
            Body.SuspendLayout();
            List<Control> remove = new List<Control>();
            for (int i = 0; i < Body.Controls.Count; i++)
            {
                if (Body.Controls[i] is TweakRow || Body.Controls[i] is GroupHeader)
                    remove.Add(Body.Controls[i]);
            }
            for (int i = 0; i < remove.Count; i++)
            {
                Body.Controls.Remove(remove[i]);
                remove[i].Dispose();
            }
            _rows.Clear();
            Body.ResumeLayout(false);
        }

        // ---------------- 分片构建（时间切片） ----------------
        // 394 行控件在 UI 线程一次性创建会冻结界面约 0.5~1s（进页面、切分类后重建都走这条路）。
        // 改为每帧最多建 BuildSliceRows 行：首屏行先出现，其余在后续帧补齐；
        // 分片期间不重排（保持 SuspendRowLayout），只在全部建完后整体重排一次——
        // 总耗时几乎不变，但单帧占用从"一整块"降为"一小片"，界面不再卡住。
        private const int BuildSliceRows = 40;
        private int _buildIndex;
        private string _buildGroup;
        private int _buildGroupCount;
        private bool _buildCollapsed;
        private GroupHeader _buildHeader;
        private bool _building;

        private void BuildRows()
        {
            BuildRows(false);
        }

        /// <summary>
        /// 重建行列表。
        /// synchronous = true：一次建完。折叠 / 展开动画必须走这条——它们紧接着要枚举刚建出的行
        /// （分片会让它们拿到空集合，动画直接失效）。
        /// synchronous = false：按帧分片，首次加载 / 刷新走这条，首屏行先出现、不长时间占用 UI 线程。
        /// </summary>
        private void BuildRows(bool synchronous)
        {
            if (_building) FinishBuildRows();   // 上一轮分片先收尾（保证控件树一致，不半途重来）

            Body.SuspendLayout();
            ClearRows();
            // 批量挂载：抑制每行触发的全表重排（否则 N 行 = O(N²) 卡顿）
            SuspendRowLayout();

            _buildIndex = 0;
            _buildGroup = null;
            _buildGroupCount = 0;
            _buildCollapsed = false;
            _buildHeader = null;
            _building = true;

            if (synchronous)
            {
                while (_buildIndex < _tweaks.Count)
                {
                    BuildRowAt(_buildIndex);
                    _buildIndex++;
                }
                FinishBuildRows();
                return;
            }
            BuildRowsSlice();
        }

        /// <summary>构建一片（≤ BuildSliceRows 行）；未建完则排到下一帧继续。</summary>
        private void BuildRowsSlice()
        {
            if (!_building) return;   // 已收尾或被 Dispose 打断

            int built = 0;
            while (_buildIndex < _tweaks.Count && built < BuildSliceRows)
            {
                BuildRowAt(_buildIndex);
                _buildIndex++;
                built++;
            }

            if (_buildIndex < _tweaks.Count)
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    // 还有剩余：排到下一帧继续（首屏行本次已建出）
                    try { BeginInvoke((MethodInvoker)BuildRowsSlice); return; }
                    catch { }
                }

                // 没有可用句柄或投递失败：必须在这里同步建完。
                // 构造期 Load（启动即导航到本页）时窗口句柄尚未创建，BeginInvoke 无处投递，
                // 若就此返回则 _building 永久挂起 —— 列表停在空状态，且 ApplyFilters 会一直让路。
                while (_buildIndex < _tweaks.Count)
                {
                    BuildRowAt(_buildIndex);
                    _buildIndex++;
                }
            }
            FinishBuildRows();
        }

        /// <summary>分片收尾：组头计数、整体重排一次、应用筛选。</summary>
        private void FinishBuildRows()
        {
            if (!_building) return;
            _building = false;
            if (_buildHeader != null) _buildHeader.CountText = _buildGroupCount + " 项";
            _buildHeader = null;
            Body.ResumeLayout(false);
            ResumeRowLayout();
            ApplyFilters();
            RefreshLayout();
        }

        /// <summary>
        /// 构建单行（组头按需插入）。由 <see cref="BuildRowsSlice"/> 逐项调用，
        /// 构建进度保存在 _build* 字段里跨帧延续（原来是一个 for 循环一次建完）。
        /// </summary>
        private void BuildRowAt(int index)
        {
            ITweak t = _tweaks[index];
            if (t.Group != _buildGroup)
            {
                if (_buildHeader != null) _buildHeader.CountText = _buildGroupCount + " 项";
                _buildGroup = t.Group;
                _buildGroupCount = 0;
                _buildHeader = new GroupHeader(_buildGroup, GroupColor(_buildGroup));
                bool wasCollapsed;
                _collapsedGroups.TryGetValue(_buildGroup, out wasCollapsed);
                _buildHeader.Collapsed = wasCollapsed;
                GroupHeader thisHeader = _buildHeader; // 闭包按组固定，避免共享变量
                _buildHeader.CollapsedChanged += delegate
                {
                    // 折叠 / 展开走高度动画：内容逐行收拢或展开，而不是瞬间切换
                    AnimateGroupToggle(thisHeader.Text, thisHeader.Collapsed);
                };
                Body.Controls.Add(_buildHeader);
                _buildCollapsed = wasCollapsed;
            }

            _buildGroupCount++;
            if (_buildCollapsed) return; // 折叠：只计数量，不建行

            TweakRow row = new TweakRow(t, this);
            if (_expandAnimGroup != null && t.Group == _expandAnimGroup)
            {
                // 展开动画：新行以 1px 起始高度建出，随后由动画长回正常高度
                row.SuppressAutoHeight = true;
                row.Height = 1;
            }
            bool applied = false;
            _states.TryGetValue(t.Id, out applied);
            row.SetState(applied);
            _rows.Add(row);
            Body.Controls.Add(row);
        }

        /// <summary>
        /// 状态筛选应用：只切换行与组头的可见性，不重建任何控件（毫秒级）。
        /// 不可用「全量重建行」实现筛选：157 行的销毁重建是列表交互卡顿的主要来源；
        /// FlowLayoutPanel 会自动跳过隐藏行的占位。
        /// </summary>
        private void ApplyFilters()
        {
            // 分片构建进行中：Body 还处于 Suspend、_rows 也不完整。此处若继续，
            // 收尾的 ResumeRowLayout 会提前解除抑制 → 之后每挂一行都触发全表重排（O(N²) 回归）。
            // 构建收尾（FinishBuildRows）会统一调用一次，这里直接让路。
            if (_building) return;
            if (_rows.Count == 0 && Body.Controls.Count == 0) return;

            Dictionary<string, int> visibleCount = new Dictionary<string, int>();
            Body.SuspendLayout();
            SuspendRowLayout(); // 筛选只切可见性：抑制每行 VisibleChanged 钩子触发的全量重排，否则退化成 O(n²)
            try
            {
                for (int i = 0; i < _rows.Count; i++)
                {
                    TweakRow r = _rows[i];
                    bool vis = PassFilter(r.Tweak);
                    if (r.Visible != vis) r.Visible = vis;
                    if (vis)
                    {
                        int n;
                        visibleCount.TryGetValue(r.Tweak.Group, out n);
                        visibleCount[r.Tweak.Group] = n + 1;
                    }
                }

                // 每个分组在当前筛选下还剩多少项。
                // 折叠组没有行（visibleCount 里查不到），必须单独统计：否则搜索/筛选后
                // 已经没有匹配项的分组，标题仍会留在列表里，点开是空的
                //（用户感知为"优化项被自动收起来了"）。
                Dictionary<string, int> matched = new Dictionary<string, int>();
                for (int i = 0; i < _tweaks.Count; i++)
                {
                    ITweak t = _tweaks[i];
                    if (!PassFilter(t)) continue;
                    int m;
                    matched[t.Group] = matched.TryGetValue(t.Group, out m) ? m + 1 : 1;
                }

                for (int i = 0; i < Body.Controls.Count; i++)
                {
                    GroupHeader h = Body.Controls[i] as GroupHeader;
                    if (h == null) continue;
                    int n;
                    bool any = visibleCount.TryGetValue(h.Text, out n) && n > 0;
                    int m2;
                    bool hasMatched = matched.TryGetValue(h.Text, out m2) && m2 > 0;
                    // 折叠组：展开后确实还有匹配项才保留标题，并显示筛选后的项数
                    h.Visible = any || (h.Collapsed && hasMatched);
                    if (any) h.CountText = n + " 项";
                    else if (h.Collapsed && hasMatched) h.CountText = m2 + " 项";
                }
            }
            finally
            {
                ResumeRowLayout(); // 解抑制并做一次整体重排（替代逐行触发）
                Body.ResumeLayout(false);
            }
            RefreshLayout();
        }

        /// <summary>状态筛选判定（"只看已启用"等）+ 关键词搜索。</summary>
        private bool PassFilter(ITweak t)
        {
            // 硬件/系统不适用项：默认隐藏（可用工具栏「显示不适用项」开关查看；此类项应用会被拒绝）
            if (!_showInapplicable && !TweakApplicability.IsApplicable(t)) return false;

            // 关键词：名称 / 说明 / 标识 / 分组 任一命中即保留（不区分大小写）
            if (_keyword.Length > 0)
            {
                bool hit =
                    (t.Name != null && t.Name.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Description != null && t.Description.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Id != null && t.Id.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (t.Group != null && t.Group.IndexOf(_keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit) return false;
            }

            if (_stateFilter == "on")
            {
                bool a;
                if (!_states.TryGetValue(t.Id, out a) || !a) return false;
            }
            else if (_stateFilter == "off")
            {
                bool a;
                if (_states.TryGetValue(t.Id, out a) && a) return false;
            }
            else if (_stateFilter == "rec" && !t.Recommended) return false;
            else if (_stateFilter == "risky" && !t.Risky) return false;
            // 分类筛选：主页面行已全建，仅按当前分类切可见性（不重建）
            if (_extraGroups != null && Array.IndexOf(_extraGroups, t.Group) < 0) return false;
            return true;
        }

        private static Color GroupColor(string group)
        {
            if (group == TweakLibrary.GPerformance) return Theme.Accent;
            if (group == TweakLibrary.GAppearance) return Theme.Cyan;
            if (group == TweakLibrary.GPrivacy) return Theme.Purple;
            if (group == TweakLibrary.GServices) return Theme.Warning;
            if (group == TweakLibrary.GPower) return Theme.Success;
            if (group == TweakLibrary.GGame) return Theme.Cyan;
            if (group == TweakLibrary.GSlim) return Theme.Danger;
            if (group == TweakLibrary.GAudio) return Theme.Cyan;
            return Theme.Accent;
        }

        private int CountApplied()
        {
            int n = 0;
            foreach (KeyValuePair<string, bool> kv in _states)
            {
                if (kv.Value) n++;
            }
            return n;
        }

        private void UpdateSummary()
        {
            int applied = 0;
            int risky = 0;
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) applied++;
                if (_tweaks[i].Risky) risky++;
            }

            // 圆环健康卡：圆环进度 = 启用率，图例给出「安全 / 谨慎」两档项数
            _health.SetData(_tweaks.Count, applied, _tweaks.Count - risky, risky);
            RefreshChipCounts();
            Relayout();
            ApplyFilters(); // 状态变化后同步筛选可见性（幂等，隐藏行才重排）
        }

        /// <summary>
        /// 刷新筛选 chips 上的项数（设计 mod-filter-chip 的写法：标签右侧带数量）。
        /// 数量让用户在点之前就知道会看到多少项，避免"点进去是空的"。
        /// </summary>
        private void RefreshChipCounts()
        {
            if (_stateChips.Count != 5) return;

            int on = 0, rec = 0, rk = 0;
            for (int i = 0; i < _tweaks.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_tweaks[i].Id, out a);
                if (a) on++;
                if (_tweaks[i].Recommended) rec++;
                if (_tweaks[i].Risky) rk++;
            }

            string[] labels = new string[] { "全部状态", "已启用", "未启用", "推荐", "谨慎" };
            int[] counts = new int[] { _tweaks.Count, on, _tweaks.Count - on, rec, rk };

            for (int i = 0; i < _stateChips.Count; i++)
            {
                _stateChips[i].Text = labels[i] + " " + counts[i];
                _stateChips[i].FitToText(72);
            }
        }

        // --------------------------------------------------------------

        internal void ToggleTweak(TweakRow row)
        {
            if (row == null) return;
            if (_busy)
            {
                // 忙碌时拒绝操作，但开关已被用户点开——恢复到真实状态，避免视觉与实际脱节
                row.SetState(row.Tweak.IsApplied());
                SetSubtitle("正在执行其他操作，请稍候再试。", Theme.Warning);
                return;
            }
            ITweak t = row.Tweak;
            bool applied = row.IsApplied;

            // 本机不适用项：即便被「显示不适用项」开关显出，应用也会被拒绝——点开关即提示，不静默失败
            if (!TweakApplicability.IsApplicable(t))
            {
                Dialog.Info(this, "不适用", "本机硬件/系统不满足「" + t.Name + "」的生效条件，该项不适用，应用会被拒绝。");
                row.SetState(false);
                return;
            }

            if (!applied && t.AdminOnly && !Native.IsElevated())
            {
                bool go = Dialog.Confirm(this, "需要管理员权限",
                    "「" + t.Name + "」需要管理员权限才能修改。\r\n\r\n是否以管理员身份重新启动本程序？");
                row.SetState(false);
                if (go)
                {
                    if (Shell.RestartElevated(""))
                    {
                        Application.Exit();
                    }
                    else
                    {
                        Dialog.Error(this, "提权失败", "未能以管理员身份启动，请右键程序选择「以管理员身份运行」。");
                    }
                }
                return;
            }

            if (!applied && t.Risky)
            {
                bool go = Dialog.Confirm(this, "确认操作",
                    "「" + t.Name + "」属于高级选项。\r\n\r\n" + t.Description +
                    "\r\n\r\n应用前将自动创建系统还原点（若 24 小时内已创建过则跳过），是否继续？");
                if (!go)
                {
                    row.SetState(false);
                    return;
                }

                // 危险项闸门：先创建系统还原点（后台执行，24 小时内已有则跳过），再应用
                _busy = true;
                row.SetState(t.IsApplied());
                SetSubtitle("正在创建系统还原点…", Theme.Warning);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool hasRecent = false;
                    string rpNote = "";
                    try
                    {
                        List<RestorePoint> points = RestorePoints.List();
                        for (int i = 0; i < points.Count; i++)
                        {
                            if ((DateTime.Now - points[i].Created).TotalHours < 24) { hasRecent = true; break; }
                        }
                    }
                    catch { }
                    if (!hasRecent)
                    {
                        string err;
                        hasRecent = RestorePoints.Create("GuyueBox - 启用 " + t.Name + " 前", out err);
                        if (!hasRecent) rpNote = string.IsNullOrEmpty(err) ? "还原点创建失败" : err;
                        else rpNote = "已创建系统还原点";
                    }
                    else rpNote = "24 小时内已存在还原点，跳过创建";

                    // #22：谨慎项应用前必须确保有还原点兜底，创建失败则取消应用
                    if (!hasRecent)
                    {
                        Post(delegate
                        {
                            _busy = false;
                            row.SetState(applied); // 保持原状态（未应用）
                            Dialog.Warn(this, "还原点创建失败",
                                "谨慎项「" + t.Name + "」应用前必须确保有系统还原点兜底，但创建失败了：\r\n" + rpNote +
                                "\r\n\r\n本次应用已取消。请检查系统保护是否开启（系统属性 → 系统保护）后重试。");
                            SetSubtitle("还原点创建失败，已取消：" + t.Name, Theme.Danger);
                        });
                        return;
                    }

                    bool applyOk = false;
                    try { applyOk = TweakExecutor.Apply(t).IsOk; } catch { }
                    Post(delegate
                    {
                        _busy = false;
                        _states[t.Id] = applyOk;
                        row.SetState(applyOk);
                        UpdateSummary();
                        if (!applyOk)
                        {
                            Dialog.Error(this, "应用失败", "未能应用「" + t.Name + "」。");
                            SetSubtitle("应用失败：" + t.Name, Theme.Danger);
                            return;
                        }
                        SetSubtitle("已启用：" + t.Name +
                            (rpNote.Length > 0 ? "（" + rpNote + "）" : ""), Theme.Success);
                    });
                });
                return;
            }

            if (applied)
            {
                // 还原本身是安全操作（自动回到系统默认），高频操作不打断——直接执行。
                // 「谨慎项」例外：关闭也确认一次。
                if (t.Risky)
                {
                    bool go = Dialog.Confirm(this, "还原谨慎项",
                        "「" + t.Name + "」属于高级选项，确定要还原为系统默认状态吗？");
                    if (!go)
                    {
                        row.SetState(true);
                        return;
                    }
                }
            }

            // 应用/还原可能执行外部命令（powercfg/powershell/sc），放到后台线程避免界面卡顿，
            // 结果回主线程更新状态。UI 线程只负责确认弹窗，不再被命令阻塞。
            bool wasApplied = applied;
            ITweak tw = t;
            TweakRow r = row;
            SetSubtitle(wasApplied ? "正在还原…" : "正在应用…", Theme.Warning);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = wasApplied ? TweakExecutor.Revert(tw).IsOk : TweakExecutor.Apply(tw).IsOk; }
                catch { ok = false; }
                Post(delegate
                {
                    if (!ok)
                    {
                        r.SetState(wasApplied);
                        Dialog.Error(this, wasApplied ? "还原失败" : "应用失败",
                            "未能" + (wasApplied ? "还原" : "应用") + "「" + tw.Name + "」。\r\n\r\n" +
                            "请确认程序以管理员身份运行，并且目标服务或注册表项存在。");
                        return;
                    }
                    bool nowApplied = !wasApplied;
                    _states[tw.Id] = nowApplied;
                    r.SetState(nowApplied);
                    UpdateSummary();
                    string suffix = (tw.Id == "hibernate_off" || tw.Id == "ntfs_lastaccess" ||
                                     tw.Id == "win11_classic_menu") ? "（部分设置需重启后生效）" : "";
                    SetSubtitle("已" + (nowApplied ? "启用" : "还原") + "：" + tw.Name + suffix, Theme.Success);
                });
            });
        }
        private void SyncRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                bool a = false;
                _states.TryGetValue(_rows[i].Tweak.Id, out a);
                _rows[i].SetState(a);
            }
        }
    }
}
