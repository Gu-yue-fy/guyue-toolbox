/* 文件说明：全局配色、字体与绘制工具（Gfx）——深空灰磨砂 + 克制精密。 */

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GuyueBox.UI
{
    /// <summary>
    /// 全局配色与字体。支持深色 / 浅色两套方案，主题色可切换；所有颜色统一由 ApplyScheme 赋值。
    /// 工程纪律（每条对应一次真实缺陷）：
    /// 1. 颜色语义固定：蓝=主操作、绿=成功/安全、琥珀=可撤销的破坏性动作、红=不可撤销/危险。
    /// 2. 半透明色不能当实色用（玻璃色 = 白 + 低 alpha），跨控件绘制须自行合成实色。
    /// 3. 破坏性操作必须用 Dialog.ConfirmDanger 说清后果 / 是否可撤销 / 影响范围三件事。
    /// 4. 瞬时反馈走 Toast；空状态三件套；行高挂载前定稿；GDI 对象一律走 GdiCache。
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
        public const int RadiusItem = 10;

        /// <summary>导航项圆角（设计 8）。</summary>
        public const int RadiusNav = 10;

        /// <summary>
        /// 按钮圆角。原为 4（设计 Token.Radius.Button），视觉上偏"方方正正"；
        /// 按反馈调圆润到 8 —— 与导航项 / 菜单项同一圆角档，按钮高度 30~36px 时不会显得笨。
        /// </summary>
        public const int RadiusButton = 10;

        /// <summary>菜单项高度 36px（设计 Token.Size.ButtonHeight = 36 一致）。</summary>
        public const int ItemHeight = 36;

        /// <summary>侧栏导航项高度 34px（行高紧凑但不局促；配合零边距让全部分组一屏可见）。</summary>
        public const int NavItemHeight = 34;

        /// <summary>侧栏分组标题高度 28px。</summary>
        public const int NavGroupHeight = 28;

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

            // ---- 每帧进度步长（线性逼近 0→1，配合 AnimationClock 16ms 粒度）----
            public const float CascStep = 0.13f;   // 页面元素级联入场
            public const float RailStep = 0.16f;   // 优化项详情栏滑入/滑出

            // ---- 指数插值系数（lerp factor，越小越柔、越大越快收敛）----
            public const float ScoreLerp  = 0.22f;  // 健康卡得分滚动
            public const float FadeLerp   = 0.30f;  // 加载遮罩淡入淡出
            public const float GridLerp   = 0.35f;  // 数据网格平滑滚动
            public const float ScrollLerp = 0.45f;  // 页面自绘滚动条平滑滚动

            // ---- 平滑滚动收敛阈值（行索引/像素差低于此即吸附到目标，停止动画）----
            public const double ScrollSnap = 0.06;  // 数据网格：行索引差阈值

            // ---- 旋转角速度（度/帧，AnimationClock 16ms 粒度）----
            public const float SpinDegPerFrame = 8f;    // 状态 Spinner（0.5°/ms）
            public const float ScanDegPerFrame = 9f;    // 体检扫描环
            public const float BreathStep      = 0.09f; // 加载遮罩呼吸相位
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

        /// <summary>卡片顶部 1px 高光（深色用白、浅色用极淡黑）。</summary>
        public static Color CardHighlight;

        // ---------------- 设计语言扩展令牌 ----------------
        // 语义化设计令牌命名，保持全局一致。

        /// <summary>外壳底色。</summary>
        public static Color ShellBg;

        /// <summary>侧栏背景渐变上下端。</summary>
        public static Color SidebarBgTop;
        public static Color SidebarBgBottom;

        /// <summary>导航项悬停底（设计 NavItemHover：#0CFFFFFF）。</summary>
        public static Color NavHover;

        /// <summary>导航项按下底（设计 NavItemPressed：#172235）。</summary>
        public static Color NavPressed;

        /// <summary>导航选中底渐变上下端（设计 NavItemSelected：#243B82F6 → #123B82F6）。</summary>
        public static Color NavSelTop;
        public static Color NavSelBottom;

        /// <summary>导航选中描边上端（设计 NavItemSelectedBorder：#25FFFFFF）。</summary>
        public static Color NavSelBorderTop;

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

        /// <summary>表格行分隔线。</summary>
        public static Color RowBorder;

        /// <summary>强调色上的文字色（设计 TextOnAccent #0F172A 深墨，而非白色）。</summary>
        public static Color TextOnAccent;

        /// <summary>可选主题色（索引对应设置页色块）。数组实例固定，切换方案时就地覆盖。</summary>
        public static readonly Color[] Palette = new Color[6];

        // 主题色板：蓝 #3B82F6 为首，青/紫/绿/琥珀/红取自同族语义色，克制无彩虹。
        private static readonly Color[] DarkPalette = new Color[]
        {
            Color.FromArgb(59, 130, 246),   // 蓝 #3B82F6（默认强调色）
            Color.FromArgb(90, 200, 250),   // 青 #5AC8FA
            Color.FromArgb(167, 139, 250),  // 紫 #A78BFA
            Color.FromArgb(52, 199, 89),    // 绿 #34C759
            Color.FromArgb(255, 149, 0),    // 琥珀 #FF9500
            Color.FromArgb(255, 59, 48)     // 红 #FF3B30
        };

        private static readonly Color[] LightPalette = new Color[]
        {
            Color.FromArgb(37, 99, 235),    // 蓝 #2563EB（默认）
            Color.FromArgb(2, 132, 199),    // 青 #0284C7
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
                // ===== 浅色：暖白底 + 精密描边（同一套语义角色）=====
                ShellBg = Color.FromArgb(245, 246, 248);        // #F5F6F8
                SidebarBgTop = Color.FromArgb(240, 241, 245);
                SidebarBgBottom = Color.FromArgb(233, 234, 240);

                ChromeBg = ShellBg;
                SidebarBg = SidebarBgTop;
                WindowBg = Color.FromArgb(237, 238, 243);
                CardBg = Color.FromArgb(255, 255, 255);        // #FFFFFF
                CardBgAlt = Color.FromArgb(244, 245, 249);
                CardHover = Color.FromArgb(236, 238, 244);

                BorderSoft = Color.FromArgb(230, 232, 238);
                Border = Color.FromArgb(215, 218, 226);
                BorderStrong = Color.FromArgb(193, 197, 208);

                TextPrimary = Color.FromArgb(23, 24, 28);      // #17181C
                TextSecondary = Color.FromArgb(90, 93, 103);
                // 浅色主题同理：原 #8B8E98 在白底上偏淡，压深到 #6E7280
                TextMuted = Color.FromArgb(110, 114, 128);   // #6E7280

                Success = Color.FromArgb(5, 150, 105);         // #059669
                Warning = Color.FromArgb(217, 119, 6);         // #D97706
                Danger = Color.FromArgb(220, 38, 38);          // #DC2626
                DangerHover = Color.FromArgb(239, 68, 68);
                Purple = Color.FromArgb(124, 58, 237);         // #7C3AED
                Cyan = Color.FromArgb(2, 132, 199);            // #0284C7
                Prism = Cyan;

                GridHeader = Color.FromArgb(240, 242, 246);
                GridRow = Color.FromArgb(255, 255, 255);
                GridRowAlt = Color.FromArgb(247, 248, 251);
                GridSelection = Color.FromArgb(37, 99, 235);   // #2563EB（白字选中）
                GridHover = Color.FromArgb(236, 239, 245);

                RowBorder = Color.FromArgb(228, 231, 237);
                ScrollThumb = Color.FromArgb(56, 15, 23, 42);
                ScrollThumbHover = Color.FromArgb(96, 15, 23, 42);
                KnobOff = Color.FromArgb(255, 255, 255);
                CardHighlight = Color.FromArgb(14, 0, 0, 0);

                NavHover = Color.FromArgb(12, 15, 23, 42);
                NavPressed = Color.FromArgb(226, 232, 240);
                NavSelTop = Color.FromArgb(38, 37, 99, 235);
                NavSelBottom = Color.FromArgb(18, 37, 99, 235);
                NavSelBorderTop = Color.FromArgb(46, 37, 99, 235);
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
                // ===== 深色：深空灰磨砂 + 精密描边（克制精密）=====
                ShellBg = Color.FromArgb(15, 16, 16);         // #0F0F10
                SidebarBgTop = Color.FromArgb(11, 11, 13);    // #0B0B0D
                SidebarBgBottom = Color.FromArgb(8, 8, 10);   // #08080A

                ChromeBg = ShellBg;
                SidebarBg = SidebarBgTop;
                WindowBg = Color.FromArgb(19, 19, 22);        // #131316
                CardBg = Color.FromArgb(26, 26, 28);          // #1A1A1C
                CardBgAlt = Color.FromArgb(32, 32, 36);       // #202024
                CardHover = Color.FromArgb(38, 38, 43);       // #26262B

                BorderSoft = Color.FromArgb(42, 42, 46);      // #2A2A2E
                Border = Color.FromArgb(56, 56, 62);          // #38383E
                BorderStrong = Color.FromArgb(74, 74, 82);    // #4A4A52

                TextPrimary = Color.FromArgb(244, 244, 246);  // #F4F4F6
                TextSecondary = Color.FromArgb(200, 200, 207);// #C8C8CF
                // 次要文字提亮：原 #8F8F99 在深色卡片上对比偏低（说明、状态、计数都读得吃力），
                // 提到 #A6A6B2 仍明显弱于次级文字 #C8C8CF，层级不变但可读性达标。
                TextMuted = Color.FromArgb(166, 166, 178);    // #A6A6B2

                Success = Color.FromArgb(52, 199, 89);        // #34C759
                Warning = Color.FromArgb(255, 149, 0);        // #FF9500
                Danger = Color.FromArgb(255, 59, 48);         // #FF3B30
                DangerHover = Color.FromArgb(255, 107, 97);   // #FF6B61
                Purple = Color.FromArgb(167, 139, 250);       // #A78BFA
                Cyan = Color.FromArgb(90, 200, 250);          // #5AC8FA
                Prism = Color.FromArgb(90, 200, 250);         // #5AC8FA

                GridHeader = Color.FromArgb(22, 22, 25);      // #161619
                GridRow = Color.FromArgb(26, 26, 28);         // #1A1A1C
                GridRowAlt = Color.FromArgb(30, 30, 34);      // #1E1E22
                GridSelection = Color.FromArgb(30, 58, 95);   // #1E3A5F
                GridHover = Color.FromArgb(35, 35, 39);       // #232327

                RowBorder = Color.FromArgb(42, 42, 46);       // #2A2A2E
                ScrollThumb = Color.FromArgb(58, 58, 64);     // #3A3A40
                ScrollThumbHover = Color.FromArgb(74, 74, 82);// #4A4A52
                KnobOff = Color.FromArgb(58, 58, 64);         // #3A3A40
                CardHighlight = Color.FromArgb(20, 255, 255, 255); // #14FFFFFF

                // 导航状态
                NavHover = Color.FromArgb(12, 255, 255, 255);      // #0CFFFFFF
                NavPressed = Color.FromArgb(35, 35, 39);           // #232327
                NavSelTop = Color.FromArgb(36, 59, 130, 246);      // #243B82F6
                NavSelBottom = Color.FromArgb(18, 59, 130, 246);   // #123B82F6
                NavSelBorderTop = Color.FromArgb(37, 255, 255, 255);  // #25FFFFFF
                NavSelIconBg = Color.FromArgb(32, 59, 130, 246);   // #203B82F6
                NavGroupText = TextMuted;

                // 玻璃承载层
                GlassCardBg = Color.FromArgb(13, 255, 255, 255);     // #0DFFFFFF
                GlassCardHover = Color.FromArgb(21, 255, 255, 255);  // #15FFFFFF
                // 卡片/说明栏的边缘：10% 白在深色底上几乎看不见，抬到 16% 让每块面板有明确边界
                GlassBorder = Color.FromArgb(41, 255, 255, 255);     // #29FFFFFF

                // 外壳窗框
                FrameTop = Color.FromArgb(63, 70, 84);        // #3F4654
                FrameSide = Color.FromArgb(42, 46, 56);       // #2A2E38
                FrameBottom = Color.FromArgb(30, 33, 41);     // #1E2129
                FrameInnerHighlight = Color.FromArgb(20, 255, 255, 255); // #14FFFFFF
                HeroGlow = Color.FromArgb(59, 130, 246);      // #3B82F6
                LightBeam = Color.FromArgb(16, 255, 255, 255);// #10FFFFFF
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
        public const int RadiusCard = 12;

        /// <summary>小徽标 / 图标底圆角（小元素不做过度圆角，6 与按钮档拉开一点层级）。</summary>
        public const int RadiusChip = 9;

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

        /// <summary>嵌入模式（被页签宿主内嵌的子页）操作条高度。</summary>
        public const int ActionBarHeight = 46;

        /// <summary>页头操作按钮高度（设计 Token.Size.ButtonHeight = 36）。</summary>
        public const int ActionButtonHeight = 36;

        /// <summary>紧凑按钮高度（设计 Token.Size.ButtonHeightSm = 28，筛选用 chips 用）。</summary>
        public const int ButtonHeightSm = 28;

        /// <summary>
        /// 行内按钮高度（列表行、卡片内工具行）。
        /// 此前各页分别写 30 / 32 两套值，同屏能看到两种高度；统一以本 token 为准。
        /// </summary>
        public const int RowButtonHeight = 30;

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

        /// <summary>
        /// 等宽内容字体。字体风格统一后不再切到 Cascadia Mono / Consolas：
        /// 直接复用界面字体（注册表路径这类内容用界面字体同样清楚，且与全站观感一致）。
        /// </summary>
        private static Font PickMono()
        {
            return new Font(Family, 9F);
        }

        // 字体只创建一次并长期复用，避免频繁绘制产生 GDI 句柄泄漏。
        // 字号单位为 Point（GDI+ 默认单位），随系统 DPI 自动缩放；层级为清晰中文 UI 调校：
        // Micro 8.25 / Small 9 / Body 10 / SubTitle 11 / Title 16 / Metric 21（粗体用于强调与数值）。
        public static readonly Font FontMicro = new Font(Family, 8.25F);
        public static readonly Font FontSmall = new Font(Family, 9F);
        /// <summary>小号粗体：详情栏段标题等紧凑层级用（v2.2 新增）。</summary>
        public static readonly Font FontSmallBold = new Font(Family, 9F, FontStyle.Bold);
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

}
