using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 全局配色与字体。支持深色 / 浅色两套方案，主题色可切换。
    /// 所有颜色字段均为可变静态量，统一由 <see cref="ApplyScheme"/> 赋值；
    /// 界面各处只引用 Theme.*，因此新增配色方案无需改动任何控件代码。
    ///
    /// ── 全工程设计纪律（改界面前先读这一节；每条都对应一次真实缺陷）──
    /// 1. 颜色语义固定，不做装饰：蓝=主操作、绿=成功/安全、琥珀=建议或"可撤销的破坏性动作"、
    ///    红=危险/不可撤销、紫=仅用于上传类语义。红色不可滥用，否则失去警示力。
    /// 2. 半透明色不能当实色用：玻璃色是"白 + 低 alpha"，直接把 alpha 改成不透明会得到纯白底
    ///    （曾造成主题色块全白、切浅色后白底白字、Toast 白底白字）。跨控件绘制要自己合成实色。
    /// 3. 破坏性操作必须有确认，且说清"后果 / 是否可撤销 / 影响范围"三件事；
    ///    确认键写动作名（"开始粉碎"）而不是"确定"，并用 Dialog.ConfirmDanger。
    /// 4. 不放假按钮：不能自动修的项（如驱动异常）只做说明，不做"点了也没用"的按钮。
    /// 5. 长任务显示分阶段进度并尽量可取消；取消后保留已完成结果并说明下一步（不是"失败"）。
    /// 6. 瞬时反馈用 Toast 浮层（不抢焦点、自动消失）；页头副标题只承担"当前状态"。
    /// 7. 空状态三件套：说明原因 + 给出下一步入口，不留一屏死白（InfoList.EmptyActionText）。
    /// 8. 行高必须在挂载前定稿：挂载后再改高会与其后区块错位（见 ViewBase.RefreshLayout 注释）。
    /// 9. 悬停与动画只改颜色/位置，不在每帧路径上新建 GDI 对象（一律走 GdiCache）。
    /// 10. 任何"看起来没用的字段"先核实再删：Win32 结构体字段、接口实现删了会崩。
    /// </summary>
    public static class Theme
    {
        // ---------------- 当前方案 ----------------

        private static bool _light;

        /// <summary>当前是否为浅色方案。</summary>
        public static bool IsLight { get { return _light; } }

        // ---------------- 背景层次 ----------------

        public static Color ChromeBg;      // 标题栏 / 侧栏
        public static Color WindowBg;      // 页面背景
        public static Color CardBg;        // 卡片
        public static Color CardBgAlt;     // 卡片内次级区域
        public static Color CardHover;
        public static Color SidebarBg;

        // ---------------- 描边 ----------------

        public static Color BorderSoft;
        public static Color Border;
        public static Color BorderStrong;

        // ---------------- 文本 ----------------

        public static Color TextPrimary;
        public static Color TextSecondary;
        public static Color TextMuted;

        // ---------------- 强调色（由主题色索引派生） ----------------

        public static Color Accent;
        public static Color AccentHover;
        public static Color AccentPress;
        public static Color AccentSoft;

        // ---------------- 语义色 ----------------

        public static Color Success;
        public static Color Warning;
        public static Color Danger;
        public static Color DangerHover;
        public static Color Purple;
        public static Color Cyan;

        /// <summary>冷青强调色：仅允许 inset 内向发光，禁止外溢与粉色。</summary>
        public static Color Prism;

        // ---------------- 表格 ----------------

        public static Color GridHeader;
        public static Color GridRow;
        public static Color GridRowAlt;
        public static Color GridSelection;
        public static Color GridHover;

        // ---------------- 设计令牌 ----------------

        /// <summary>菜单项 / 选中态圆角 8px。</summary>
        public const int RadiusItem = 8;

        /// <summary>导航项圆角（设计 8）。</summary>
        public const int RadiusNav = 8;

        /// <summary>按钮圆角（设计 Token.Radius.Button = 4）。</summary>
        public const int RadiusButton = 4;

        /// <summary>菜单项高度 36px（设计 Token.Size.ButtonHeight = 36 一致）。</summary>
        public const int ItemHeight = 36;

        /// <summary>选中态时长 240ms。</summary>
        public const int MotionActiveMs = 240;

        // ============================================================
        // 动效令牌：数值对齐设计引擎（DesignTokens 时长档 + 各控件实测动画取值）
        // 集中在这里，避免各控件各写一份 magic number 导致节奏不一致
        // ============================================================

        /// <summary>动效时长阶梯（ms），从「按下」到「数值滚动」覆盖全部场景。</summary>
        public static class Motion
        {
            public const int Press = 100;        // 按下反馈
            public const int HoverIn = 120;      // 悬停进入
            public const int State = 150;        // 颜色 / 开关等状态切换默认档
            public const int HoverOut = 160;     // 悬停退出（比进入慢，收得更稳）
            public const int Enter = 240;        // 入场淡入
            public const int Move = 320;         // 入场位移（比淡入长 → 「先看见再落位」）
            public const int Progress = 300;     // 进度 / 指针 / 图标旋转
            public const int Number = 500;       // 数值滚动
            public const int LoopSpin = 1000;    // 环形加载旋转周期
            public const int LoopShimmer = 1500; // 骨架屏微光周期
        }

        /// <summary>缓动函数：入参 t∈[0,1]，返回插值后的进度（均为标准曲线）。</summary>
        public static class Ease
        {
            /// <summary>三次缓出：出现频率最高（入场位移、按钮状态过渡）。</summary>
            public static float CubicOut(float t)
            {
                t = Clamp01(t);
                float u = 1f - t;
                return 1f - u * u * u;
            }

            public static float QuadOut(float t)
            {
                t = Clamp01(t);
                float u = 1f - t;
                return 1f - u * u;
            }

            public static float QuadInOut(float t)
            {
                t = Clamp01(t);
                return t < 0.5f
                    ? 2f * t * t
                    : 1f - (2f * t - 2f) * (2f * t - 2f) / 2f;
            }

            /// <summary>正弦缓入缓出：循环呼吸 / 脉冲类动效专用。</summary>
            public static float SineInOut(float t)
            {
                t = Clamp01(t);
                return -(float)(Math.Cos(Math.PI * t) - 1.0) / 2f;
            }

            /// <summary>回弹缓出（a=0.3 约 10% 过冲）：释放 / 弹出类动效。</summary>
            public static float BackOut(float t, float amplitude)
            {
                t = Clamp01(t);
                float u = t - 1f;
                return 1f + (amplitude + 1f) * u * u * u + amplitude * u * u;
            }

            private static float Clamp01(float t)
            {
                if (t < 0f) return 0f;
                if (t > 1f) return 1f;
                return t;
            }
        }

        /// <summary>滚动条滑块（深色方案白色、浅色方案深灰，同为 18% 不透明度）。</summary>
        public static Color ScrollThumb;

        /// <summary>滚动条滑块悬停（32% 不透明度）。</summary>
        public static Color ScrollThumbHover;

        /// <summary>开关关闭态滑块颜色。</summary>
        public static Color KnobOff;

        /// <summary>侧栏选中态底色强度（%）：浅色方案需要更实的底色才看得清。</summary>
        public static int SelFillPercent;

        /// <summary>卡片顶部 1px 高光（深色用白、浅色用极淡黑）。</summary>
        public static Color CardHighlight;

        // ---------------- 设计设计语言扩展令牌 ----------------
        // 取自 设计规范 的 DarkTheme.xaml / DesignTokens.xaml，保持语义命名一致。

        /// <summary>外壳底色（设计 ShellBackground #050508）。</summary>
        public static Color ShellBg;

        /// <summary>应用背景渐变上下端（设计 AppBackground：#050508 → #0C1220）。</summary>
        public static Color AppBgTop;
        public static Color AppBgBottom;

        /// <summary>侧栏背景渐变上下端（设计 SidebarBackground：#050A18 → #070E21）。</summary>
        public static Color SidebarBgTop;
        public static Color SidebarBgBottom;

        /// <summary>导航项悬停底（设计 NavItemHover：#0CFFFFFF）。</summary>
        public static Color NavHover;

        /// <summary>导航项按下底（设计 NavItemPressed：#172235）。</summary>
        public static Color NavPressed;

        /// <summary>导航选中底渐变上下端（设计 NavItemSelected：#243B82F6 → #123B82F6）。</summary>
        public static Color NavSelTop;
        public static Color NavSelBottom;

        /// <summary>导航选中描边渐变上下端（设计 NavItemSelectedBorder：#25FFFFFF → #10FFFFFF）。</summary>
        public static Color NavSelBorderTop;
        public static Color NavSelBorderBottom;

        /// <summary>导航选中态图标底（设计 NavItemSelectedIconBackground：#203B82F6）。</summary>
        public static Color NavSelIconBg;

        /// <summary>导航分组标题文字（设计 11px SemiBold）。</summary>
        public static Color NavGroupText;

        /// <summary>玻璃卡片底/悬停底/描边（设计 Card：12%/21% 白叠加 + 玻璃边框）。</summary>
        public static Color GlassCardBg;
        public static Color GlassCardHover;
        public static Color GlassBorder;

        /// <summary>窗框渐变三色（设计 Shell.Frame：上 #52647D、侧 #334155、下 #1E293B）。</summary>
        public static Color FrameTop;
        public static Color FrameSide;
        public static Color FrameBottom;

        /// <summary>窗内 1px 高光（设计 #14FFFFFF）。</summary>
        public static Color FrameInnerHighlight;

        /// <summary>顶栏环境光晕（设计 HeroGlow #2563EB，绘制时叠低 alpha）。</summary>
        public static Color HeroGlow;

        /// <summary>侧栏右缘光带（设计：垂直渐变 透明 → #15FFFFFF → 透明）。</summary>
        public static Color LightBeam;

        /// <summary>滚动轨底色（设计 Scrollbar.Track #2A3850）。</summary>
        public static Color ScrollTrack;

        /// <summary>表格行分隔线（设计 Theme.Row.Border #334155）。</summary>
        public static Color RowBorder;

        /// <summary>强调色上的文字色（设计 TextOnAccent #0F172A 深墨，而非白色）。</summary>
        public static Color TextOnAccent;

        /// <summary>可选主题色（索引对应设置页色块）。数组实例固定，切换方案时就地覆盖。</summary>
        public static readonly Color[] Palette = new Color[6];

        // 主题色板：以设计的强调色 #3B82F6 为首，其余取自同一套语义色家族
        // （Success #34D399 / Warning #FBBF24 / Error #F87171），保持"无彩虹"的克制感。
        private static readonly Color[] DarkPalette = new Color[]
        {
            Color.FromArgb(59, 130, 246),   // 蓝 #3B82F6（默认，设计强调色）
            Color.FromArgb(34, 211, 238),   // 青 #22D3EE
            Color.FromArgb(167, 139, 250),  // 紫 #A78BFA
            Color.FromArgb(52, 211, 153),   // 绿 #34D399（设计 Success）
            Color.FromArgb(251, 191, 36),   // 琥珀 #FBBF24（设计 Warning）
            Color.FromArgb(248, 113, 113)   // 红 #F87171（设计 Error）
        };

        private static readonly Color[] LightPalette = new Color[]
        {
            Color.FromArgb(37, 99, 235),    // 蓝 #2563EB（默认）
            Color.FromArgb(8, 145, 178),    // 青 #0891B2
            Color.FromArgb(124, 58, 237),   // 紫 #7C3AED
            Color.FromArgb(5, 150, 105),    // 绿 #059669
            Color.FromArgb(217, 119, 6),    // 琥珀 #D97706
            Color.FromArgb(220, 38, 38)     // 红 #DC2626
        };

        static Theme()
        {
            // 默认深色方案；启动时由 Program 用持久化的设置重新套用
            ApplyScheme(0, 0);
        }

        /// <summary>应用配色方案：scheme 0=深色 1=浅色。</summary>
        public static void ApplyScheme(int scheme, int accentIndex)
        {
            _light = scheme == 1;

            if (_light)
            {
                // ===== 设计浅色：同一套语义角色，暖白底 + 精密描边（保持设计语言一致）=====
                ShellBg = Color.FromArgb(242, 244, 247);
                AppBgTop = Color.FromArgb(255, 255, 255);
                AppBgBottom = Color.FromArgb(238, 242, 247);
                SidebarBgTop = Color.FromArgb(247, 249, 252);
                SidebarBgBottom = Color.FromArgb(238, 242, 248);

                ChromeBg = ShellBg;
                SidebarBg = SidebarBgTop;
                WindowBg = Color.FromArgb(245, 247, 250);
                CardBg = Color.FromArgb(255, 255, 255);
                CardBgAlt = Color.FromArgb(242, 245, 249);
                CardHover = Color.FromArgb(233, 238, 245);

                BorderSoft = Color.FromArgb(230, 234, 240);
                Border = Color.FromArgb(216, 222, 231);
                BorderStrong = Color.FromArgb(185, 194, 207);

                TextPrimary = Color.FromArgb(15, 23, 42);     // #0F172A
                TextSecondary = Color.FromArgb(71, 85, 105);  // #475569
                TextMuted = Color.FromArgb(100, 116, 139);    // #64748B

                Success = Color.FromArgb(5, 150, 105);
                Warning = Color.FromArgb(180, 83, 9);
                Danger = Color.FromArgb(220, 38, 38);
                DangerHover = Color.FromArgb(239, 68, 68);
                Purple = Color.FromArgb(124, 58, 237);
                Cyan = Color.FromArgb(2, 132, 199);
                Prism = Color.FromArgb(14, 165, 233);

                GridHeader = Color.FromArgb(237, 241, 246);
                GridRow = Color.FromArgb(255, 255, 255);
                GridRowAlt = Color.FromArgb(247, 249, 252);
                GridSelection = Color.FromArgb(37, 99, 235);
                GridHover = Color.FromArgb(239, 243, 249);

                ScrollTrack = Color.FromArgb(30, 15, 23, 42);
                RowBorder = Color.FromArgb(226, 232, 240);
                ScrollThumb = Color.FromArgb(56, 15, 23, 42);
                ScrollThumbHover = Color.FromArgb(96, 15, 23, 42);
                KnobOff = Color.FromArgb(255, 255, 255);
                CardHighlight = Color.FromArgb(14, 0, 0, 0);
                SelFillPercent = 90;

                NavHover = Color.FromArgb(12, 15, 23, 42);
                NavPressed = Color.FromArgb(226, 232, 240);
                NavSelTop = Color.FromArgb(38, 37, 99, 235);
                NavSelBottom = Color.FromArgb(18, 37, 99, 235);
                NavSelBorderTop = Color.FromArgb(46, 37, 99, 235);
                NavSelBorderBottom = Color.FromArgb(20, 37, 99, 235);
                NavSelIconBg = Color.FromArgb(34, 37, 99, 235);
                NavGroupText = TextMuted;

                GlassCardBg = Color.FromArgb(255, 255, 255);
                GlassCardHover = Color.FromArgb(242, 245, 249);
                GlassBorder = BorderSoft;

                FrameTop = Color.FromArgb(195, 203, 216);
                FrameSide = Color.FromArgb(216, 222, 231);
                FrameBottom = Color.FromArgb(230, 234, 240);
                FrameInnerHighlight = Color.FromArgb(90, 255, 255, 255);
                HeroGlow = Color.FromArgb(37, 99, 235);
                LightBeam = Color.FromArgb(30, 15, 23, 42);
                TextOnAccent = Color.FromArgb(255, 255, 255);

                Array.Copy(LightPalette, Palette, Palette.Length);
            }
            else
            {
                // ===== 设计深色：深空灰玻璃 + 精密描边（DarkTheme.xaml）=====
                ShellBg = Color.FromArgb(5, 5, 8);          // #050508
                AppBgTop = Color.FromArgb(5, 5, 8);         // #050508
                AppBgBottom = Color.FromArgb(12, 18, 32);   // #0C1220
                SidebarBgTop = Color.FromArgb(5, 10, 24);   // #050A18
                SidebarBgBottom = Color.FromArgb(7, 14, 33); // #070E21

                ChromeBg = ShellBg;
                SidebarBg = SidebarBgBottom;
                WindowBg = Color.FromArgb(9, 13, 22);       // 工作区底板（AppBackground 中段）
                CardBg = Color.FromArgb(15, 21, 37);        // Theme.Background.Card #0F1525
                CardBgAlt = Color.FromArgb(21, 28, 47);     // CardAlt #151C2F
                CardHover = Color.FromArgb(27, 35, 53);     // Surface #1B2335

                BorderSoft = Color.FromArgb(31, 31, 35);    // #1F1F23
                Border = Color.FromArgb(39, 39, 42);        // #27272A
                BorderStrong = Color.FromArgb(63, 63, 70);  // #3F3F46

                TextPrimary = Color.FromArgb(248, 250, 252);  // #F8FAFC
                TextSecondary = Color.FromArgb(203, 213, 225); // #CBD5E1
                TextMuted = Color.FromArgb(174, 184, 199);     // #AEB8C7

                Success = Color.FromArgb(52, 211, 153);     // #34D399
                Warning = Color.FromArgb(251, 191, 36);     // #FBBF24
                Danger = Color.FromArgb(248, 113, 113);     // #F87171
                DangerHover = Color.FromArgb(252, 165, 165);
                // 设计刻意「无彩虹」：紫/青收敛为强调色的邻近色，只承担层次不承担装饰
                Purple = Color.FromArgb(139, 92, 246);
                Cyan = Color.FromArgb(96, 165, 250);        // Info #60A5FA
                Prism = Color.FromArgb(96, 165, 250);

                GridHeader = Color.FromArgb(12, 18, 32);
                GridRow = Color.FromArgb(15, 21, 37);
                GridRowAlt = Color.FromArgb(20, 28, 43);    // Row.Background #141C2B
                GridSelection = Color.FromArgb(30, 58, 95);  // PrimaryMuted #1E3A5F
                // 悬停行必须与斑马纹（#141C2B）可区分，否则深色下 hover 看不出来
                GridHover = Color.FromArgb(26, 35, 52);

                ScrollTrack = Color.FromArgb(42, 56, 80);   // #2A3850
                RowBorder = Color.FromArgb(51, 65, 85);     // #334155
                ScrollThumb = Color.FromArgb(82, 100, 125);   // #52647D
                ScrollThumbHover = Color.FromArgb(120, 144, 174); // #7890AE
                KnobOff = Color.FromArgb(82, 100, 125);
                CardHighlight = Color.FromArgb(20, 255, 255, 255); // #14FFFFFF
                SelFillPercent = 100;

                // 导航状态（GlassPillNavItem）
                NavHover = Color.FromArgb(12, 255, 255, 255);      // #0CFFFFFF
                NavPressed = Color.FromArgb(23, 34, 53);           // #172235
                NavSelTop = Color.FromArgb(36, 59, 130, 246);      // #243B82F6
                NavSelBottom = Color.FromArgb(18, 59, 130, 246);   // #123B82F6
                NavSelBorderTop = Color.FromArgb(37, 255, 255, 255);  // #25FFFFFF
                NavSelBorderBottom = Color.FromArgb(16, 255, 255, 255); // #10FFFFFF
                NavSelIconBg = Color.FromArgb(32, 59, 130, 246);   // #203B82F6
                NavGroupText = TextSecondary;

                // 玻璃承载层
                GlassCardBg = Color.FromArgb(12, 255, 255, 255);
                GlassCardHover = Color.FromArgb(21, 255, 255, 255);
                GlassBorder = Color.FromArgb(21, 255, 255, 255);

                // 外壳窗框
                FrameTop = Color.FromArgb(82, 100, 125);      // #52647D
                FrameSide = Color.FromArgb(51, 65, 85);       // #334155
                FrameBottom = Color.FromArgb(30, 41, 59);     // #1E293B
                FrameInnerHighlight = Color.FromArgb(20, 255, 255, 255);
                HeroGlow = Color.FromArgb(37, 99, 235);       // #2563EB
                LightBeam = Color.FromArgb(21, 255, 255, 255);
                TextOnAccent = Color.FromArgb(15, 23, 42);    // #0F172A 深墨

                Array.Copy(DarkPalette, Palette, Palette.Length);
            }

            ApplyAccent(accentIndex);
        }

        /// <summary>应用主题色索引 0-5，派生出悬停/按压/柔和高亮色（须在 ApplyScheme 之后调用）。</summary>
        public static void ApplyAccent(int index)
        {
            if (index < 0 || index >= Palette.Length) index = 0;
            Color c = Palette[index];
            Accent = c;
            // 深色底盘：悬停更亮；浅色底盘：悬停更暗
            AccentHover = _light ? Darken(c, 0.12f) : Lighten(c, 0.18f);
            AccentPress = Darken(c, 0.14f);
            Color mixed = Mix(c, WindowBg, 0.45f);
            AccentSoft = Color.FromArgb(_light ? 46 : 38, mixed.R, mixed.G, mixed.B);
        }

        /// <summary>
        /// 取「随方案变化」的颜色快照（顺序固定）。用于切换方案后把控件树里
        /// 由 Theme 赋值的 BackColor 从旧色重映射到新色（见 ThemeSkin.Reload）。
        /// </summary>
        internal static Color[] SchemeSnapshot()
        {
            return new Color[]
            {
                ChromeBg, WindowBg, CardBg, CardBgAlt, CardHover, SidebarBg,
                BorderSoft, Border, BorderStrong,
                TextPrimary, TextSecondary, TextMuted,
                GridHeader, GridRow, GridRowAlt, GridSelection, GridHover,
                ScrollThumb, ScrollThumbHover, KnobOff, CardHighlight,
                Accent, AccentSoft
            };
        }

        /// <summary>
        /// 取「随方案变化的前景色」快照（顺序固定），供切换方案时重映射 ForeColor。
        /// 只含文字色与语义色——刻意排除 CardBg / GridRow 等「浅色方案下为纯白」的
        /// 表面色，否则会把强调色底上的白字误改成深色。
        /// </summary>
        internal static Color[] ForeSnapshot()
        {
            return new Color[]
            {
                TextPrimary, TextSecondary, TextMuted,
                Success, Warning, Danger, DangerHover, Purple, Cyan, Prism,
                Accent
            };
        }

        private static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(
                c.A,
                c.R + (int)((255 - c.R) * amount),
                c.G + (int)((255 - c.G) * amount),
                c.B + (int)((255 - c.B) * amount));
        }

        private static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static Color Mix(Color c, Color bg, float amount)
        {
            return Color.FromArgb(
                (int)(c.R * (1 - amount) + bg.R * amount),
                (int)(c.G * (1 - amount) + bg.G * amount),
                (int)(c.B * (1 - amount) + bg.B * amount));
        }

        /// <summary>
        /// 弹簧缓动 cubic-bezier(0.34, 1.3, 0.64, 1)：先过冲后回落
        /// （y1 &gt; 1 产生过冲）。t 为 0..1 的归一化时间，返回值可略大于 1。
        /// </summary>
        public static float Spring(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float u = SolveBezierX(t, 0.34f, 0.64f);
            return BezierAxis(u, 1.3f, 1f);
        }

        /// <summary>二分求解 x(u) = t，得到参数 u。</summary>
        private static float SolveBezierX(float t, float x1, float x2)
        {
            float lo = 0f, hi = 1f, u = t;
            for (int i = 0; i < 24; i++)
            {
                u = (lo + hi) * 0.5f;
                if (BezierAxis(u, x1, x2) < t) lo = u; else hi = u;
            }
            return u;
        }

        /// <summary>三次贝塞尔单轴取值（P0=0, P1=p1, P2=p2, P3=1）。</summary>
        private static float BezierAxis(float u, float p1, float p2)
        {
            float mu = 1f - u;
            return 3f * mu * mu * u * p1 + 3f * mu * u * u * p2 + u * u * u;
        }

        // ---------------- 布局令牌 ----------------
        // 界面尺寸的唯一事实来源：改这里即可全局生效，避免各页面各写一套魔数。

        /// <summary>卡片 / 大容器圆角（设计 CardLarge = 8：精密而非圆润）。</summary>
        public const int RadiusCard = 8;

        /// <summary>小徽标 / 图标底圆角（设计 Token.Radius.Icon = 4，小元素不做过度圆角）。</summary>
        public const int RadiusChip = 4;

        /// <summary>页面内容区左右内边距。</summary>
        public const int PagePadX = 30;

        /// <summary>页面内容区上内边距。</summary>
        public const int PagePadTop = 20;

        /// <summary>页面内容区下内边距（略小于上边距，视觉重心略上更稳）。</summary>
        public const int PagePadBottom = 18;

        /// <summary>区块（行与行）之间的垂直间距。</summary>
        public const int GapSection = 14;

        /// <summary>同一行内控件之间的水平间距。</summary>
        public const int GapInline = 16;

        /// <summary>紧凑间距（标签与控件之间、按钮之间）。</summary>
        public const int GapTight = 10;

        /// <summary>页头高度。</summary>
        public const int HeaderHeight = 92;

        /// <summary>嵌入模式（合并页子页）操作条高度。</summary>
        public const int ActionBarHeight = 46;

        /// <summary>页头操作按钮高度（设计 Token.Size.ButtonHeight = 36）。</summary>
        public const int ActionButtonHeight = 36;

        /// <summary>紧凑按钮高度（设计 Token.Size.ButtonHeightSm = 28，筛选用 chips 用）。</summary>
        public const int ButtonHeightSm = 28;

        /// <summary>侧栏宽度（设计 220）。</summary>
        public const int SidebarWidth = 220;

        /// <summary>顶栏 / 标题栏高度（设计 WindowChrome CaptionHeight = 40）。</summary>
        public const int TopBarHeight = 40;

        /// <summary>状态栏高度。</summary>
        public const int StatusBarHeight = 30;

        /// <summary>侧栏底部信息块高度（设计深玻璃卡片：图标 + 状态 + 版本）。</summary>
        public const int SidebarFooterHeight = 64;

        // ---------------- 字体 ----------------

        private static readonly string Family = PickFamily();

        private static string PickFamily()
        {
            string[] candidates = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "Segoe UI" };
            foreach (string c in candidates)
            {
                try
                {
                    using (Font probe = new Font(c, 9F))
                    {
                        if (string.Equals(probe.Name, c, StringComparison.OrdinalIgnoreCase)) return c;
                    }
                }
                catch
                {
                }
            }
            return "Segoe UI";
        }

        /// <summary>等宽字体探测（设计 Token.FontFamily.Mono：Cascadia Mono → Consolas → Courier New）。</summary>
        private static Font PickMono()
        {
            string[] candidates = new string[] { "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New" };
            foreach (string c in candidates)
            {
                try
                {
                    using (Font probe = new Font(c, 9F))
                    {
                        // 注意：probe 只是探测用，必须新建实例返回——返回已释放的字体是隐性崩溃源
                        if (string.Equals(probe.Name, c, StringComparison.OrdinalIgnoreCase))
                        {
                            return new Font(c, 9F);
                        }
                    }
                }
                catch
                {
                }
            }
            return new Font(FontFamily.GenericMonospace, 9F);
        }

        // 字体只创建一次并长期复用，避免频繁绘制产生 GDI 句柄泄漏。
        // 字号按设计 Token.FontSize 层级换算（px → pt）：Xs11≈8.25 / Sm12≈9 / Base14≈10.5 /
        // Lg16≈12 / Xl18≈13.5 / 2xl22≈16.5 / 3xl28≈21。
        public static readonly Font FontMicro = new Font(Family, 8.25F);
        public static readonly Font FontSmall = new Font(Family, 9F);
        public static readonly Font FontBody = new Font(Family, 10F);
        public static readonly Font FontBodyBold = new Font(Family, 10F, FontStyle.Bold);
        public static readonly Font FontNav = new Font(Family, 10F);
        public static readonly Font FontSubTitle = new Font(Family, 11F, FontStyle.Bold);
        public static readonly Font FontTitle = new Font(Family, 16F, FontStyle.Bold);
        public static readonly Font FontMetric = new Font(Family, 21F, FontStyle.Bold);

        /// <summary>等宽字体：设计用 Cascadia Mono → Consolas → Courier New 兜底。</summary>
        public static readonly Font FontMono = PickMono();

        public static string FontFamilyName
        {
            get { return Family; }
        }
    }

    /// <summary>
    /// 配色方案切换后的「皮肤重刷」：递归遍历控件树，把那些 BackColor 恰好等于
    /// 旧方案颜色的控件改写成新方案对应颜色。
    /// 只做旧的成对色映射（不碰业务色，如按钮填充、状态色），因此不会误伤自绘逻辑。
    /// </summary>
    internal static class ThemeSkin
    {
        public static void Reload(Control root, Color[] backFrom, Color[] backTo,
            Color[] foreFrom, Color[] foreTo)
        {
            if (root == null) return;

            Remap(root, backFrom, backTo, true);
            Remap(root, foreFrom, foreTo, false);

            // DataGridView 的主题色是写进样式对象的（不是 BackColor），需要显式重刷；
            // 逐行逐格显式设过的样式还要额外做一次颜色映射
            DarkGrid grid = root as DarkGrid;
            if (grid != null)
            {
                grid.ApplyTheme();
                grid.RemapRowStyles(backFrom, backTo, foreFrom, foreTo);
            }

            // StatStrip 的数值色 / 标题色是「加载期快照」进字段的，同样需要显式重刷
            StatStrip strip = root as StatStrip;
            if (strip != null) strip.RemapThemeColors(foreFrom, foreTo);

            Control.ControlCollection kids = root.Controls;
            for (int i = 0; i < kids.Count; i++) Reload(kids[i], backFrom, backTo, foreFrom, foreTo);
        }

        private static void Remap(Control c, Color[] from, Color[] to, bool asBack)
        {
            if (from == null || to == null || from.Length != to.Length) return;
            try
            {
                int cur = (asBack ? c.BackColor : c.ForeColor).ToArgb();
                for (int i = 0; i < from.Length; i++)
                {
                    if (cur != from[i].ToArgb()) continue;
                    if (asBack) c.BackColor = to[i]; else c.ForeColor = to[i];
                    break;
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>绘制辅助。</summary>
    public static class Gfx
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)));
                return path;
            }

            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>填充圆角矩形。路径与画刷均走缓存，避免每帧创建 GDI 对象。</summary>
        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.FillPath(GdiCache.Brush(color), path);
            g.Restore(st);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.DrawPath(GdiCache.Pen(color, width), path);
            g.Restore(st);
        }

        /// <summary>卡片风格：填充 + 描边 + 顶部 1px 高光，营造轻微立体感。</summary>
        public static void DrawCard(Graphics g, Rectangle r, int radius, Color fill, Color border, bool highlight)
        {
            if (r.Width <= 0 || r.Height <= 0) return;

            GraphicsPath path = GdiCache.RoundRect(r, radius);
            GraphicsState st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            try
            {
                g.FillPath(GdiCache.Brush(fill), path);
                if (highlight)
                {
                    // 设计内高光：上缘通亮、向下渐隐（渐变描边），而不是整圈等亮。
                    // 路径是 (0,0) 基准的局部坐标，故渐变矩形同样用局部坐标。
                    using (LinearGradientBrush hb = new LinearGradientBrush(
                        new Rectangle(0, 0, 1, Math.Max(1, r.Height)),
                        Theme.CardHighlight, Color.Transparent, 90f))
                    using (Pen hp = new Pen(hb, 1f))
                    {
                        g.DrawPath(hp, path);
                    }
                }
                g.DrawPath(GdiCache.Pen(border, 1f), path);
            }
            finally
            {
                g.Restore(st);
            }
        }

        public static void EnableSmoothing(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>把颜色按比例调亮 / 调暗。</summary>
        public static Color Shade(Color c, double factor)
        {
            int r = Clamp(c.R * factor);
            int g = Clamp(c.G * factor);
            int b = Clamp(c.B * factor);
            return Color.FromArgb(c.A, r, g, b);
        }

        private static int Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (int)v;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// 带 alpha 的颜色插值。<see cref="Blend"/> 会把结果 alpha 固定为 255，
        /// 透明 / 半透明色参与状态过渡时会被拉成不透明（例如 Ghost 按钮凭空出现实心底），
        /// 因此凡是有透明度参与的混合都必须用这个重载。
        /// </summary>
        public static Color BlendArgb(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>带透明度的颜色。</summary>
        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>根据占用率返回语义化颜色。</summary>
        public static Color LoadColor(double percent)
        {
            if (percent < 0) return Theme.Accent;
            if (percent >= 90) return Theme.Danger;
            if (percent >= 70) return Theme.Warning;
            return Theme.Success;
        }

        /// <summary>绘制单行文本，过长时自动省略号。</summary>
        public static void DrawTextEllipsis(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            g.DrawString(text, font, GdiCache.Brush(color), bounds, GdiCache.Ellipsis);
        }

        /// <summary>在指定矩形内居中绘制文本。</summary>
        public static void DrawTextCenter(Graphics g, string text, Font font, Color color, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            g.DrawString(text, font, GdiCache.Brush(color), bounds, GdiCache.EllipsisCenter);
        }
    }
}
