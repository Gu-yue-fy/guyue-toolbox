using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GuyueBox.Core;

namespace GuyueBox.UI.Views
{
    /// <summary>
    /// 「通知条 + 统计条 + 表格」型页面的公共骨架。
    /// 启动项 / 服务 / 计划任务 / 已安装程序 / 设备 / 右键菜单这 6 个页面此前各自复制了
    /// 同一套字段、<c>IsBusy</c>、<c>OnActivated</c> 懒加载、三段式布局与表格高度账；
    /// 这里只收编这些"纯脚手架"，各页保留自己的列定义、取数逻辑与动作按钮。
    ///
    /// 用法：子类构造函数末尾调用 <see cref="InitializeGridPage"/>。
    /// 不能放进基类构造函数——那时子类的按钮等字段还没初始化。
    /// </summary>
    public abstract class GridPageView : ViewBase
    {
        /// <summary>撑满剩余高度的那张表。</summary>
        protected readonly DarkGrid Grid = new DarkGrid();

        private readonly StatStrip _summary = new StatStrip();
        private readonly NoticeBar _notice = new NoticeBar();
        private bool _busy;
        private bool _loaded;

        /// <summary>把忙碌状态暴露给基类（加载遮罩 / 状态栏指示 / 截图探针的「等到不忙再拍」）：
        /// 子页只需设 <see cref="Busy"/>，遮罩与探针等待自动生效。</summary>
        public override bool IsBusy { get { return _busy; } }

        protected GridPageView(string title, string subtitle)
            : base(title, subtitle)
        {
        }

        /// <summary>顶部统计条；<see cref="HasSummary"/> 为 false 的页面不加入布局。</summary>
        protected StatStrip Summary
        {
            get { return _summary; }
        }

        /// <summary>顶部通知条。</summary>
        protected NoticeBar Notice
        {
            get { return _notice; }
        }

        /// <summary>忙碌标记：后台取数或执行操作期间为 true（影响遮罩与按钮可用性）。</summary>
        protected bool Busy
        {
            get { return _busy; }
            set { _busy = value; }
        }

        /// <summary>是否已完成过首次加载。</summary>
        protected bool Loaded
        {
            get { return _loaded; }
            set { _loaded = value; }
        }



        // ---------------- 子类可覆盖的形态参数 ----------------

        /// <summary>是否有统计条（设备管理页没有）。</summary>
        protected virtual bool HasSummary
        {
            get { return true; }
        }

        /// <summary>通知条行高。</summary>
        protected virtual int NoticeHeight
        {
            get { return 42; }
        }

        /// <summary>通知条下方间距。</summary>
        protected virtual int NoticeGap
        {
            get { return 18; }
        }

        /// <summary>
        /// 统计行自身的下方间距。
        /// 之所以与 <see cref="NoticeGap"/> 分开：有个别页面通知条收得更紧（12px）
        /// 而统计行仍按标准 18px 呼吸，混用一个值会挪动整页高度。
        /// </summary>
        protected virtual int SummaryRowGap
        {
            get { return NoticeGap; }
        }

        /// <summary>表格行在布局中的占位高度（实际高度由 Relayout 重算）。</summary>
        protected virtual int GridRowHeight
        {
            get { return 320; }
        }

        /// <summary>
        /// 布局账里的额外占位。
        /// 有统计条时为「统计条与表格之间」的固定高度（如工具行 34+18）；
        /// 无统计条时为「页头以下全部固定高度」（此时含通知条本身）。
        /// </summary>
        protected virtual int ExtraUsedHeight
        {
            get { return 0; }
        }

        /// <summary>表格最小高度。</summary>
        protected virtual int MinGridHeight
        {
            get { return 200; }
        }

        // ---------------- 子类实现的钩子 ----------------

        /// <summary>配置列，以及本页特有的选中/格式化事件。</summary>
        protected abstract void BuildColumns();

        /// <summary>按钮可用性：选中项或忙碌状态变化时调用。</summary>
        protected virtual void UpdateActions()
        {
        }

        /// <summary>取数与回填（各页差异较大，不做模板化）。</summary>
        protected virtual void Load()
        {
        }

        /// <summary>首次显示时是否需要加载（启动项页按条目是否为空判断，故可覆盖）。</summary>
        protected virtual bool NeedsLoad()
        {
            return !_loaded;
        }

        /// <summary>表格通用行为开关；需要可编辑勾选列的页面（启动项）可覆盖。</summary>
        protected virtual void ConfigureGrid()
        {
            Grid.ReadOnly = true;
            Grid.ColumnClickSort = true;
            Grid.UseOwnScrollbar = true;
        }

        /// <summary>在统计行与表格之间插入本页特有的行（搜索行、安装行等）。</summary>
        protected virtual void AddExtraLayoutRows()
        {
        }

        // ---------------- 收编的公共流程 ----------------

        /// <summary>子类构造函数末尾调用：建表 + 拼三段式布局 + 定稿高度。</summary>
        protected void InitializeGridPage()
        {
            ConfigureGrid();
            BuildColumns();
            Grid.SelectionChanged += delegate { UpdateActions(); };

            AddFull(_notice, NoticeHeight, NoticeGap);
            if (HasSummary)
            {
                FlowLayoutPanel row = MakeRow(0, SummaryRowGap);
                row.Controls.Add(_summary);
                AddRow(row);
            }
            AddExtraLayoutRows();
            AddFull(Grid, GridRowHeight, 0);

            Body.Resize += delegate { Relayout(); };
            Relayout();
            UpdateActions();
        }

        /// <summary>表格高度账：有统计条走 LayoutGrid，无统计条走 LayoutFill。</summary>
        protected void Relayout()
        {
            if (HasSummary) LayoutGrid(Grid, _summary, ExtraUsedHeight, MinGridHeight);
            else LayoutFill(Grid, ExtraUsedHeight, MinGridHeight);
        }

        public override void OnActivated()
        {
            if (NeedsLoad()) Load();
        }

        /// <summary>当前选中行绑定的数据对象；未选中时为 null。</summary>
        protected T SelectedRow<T>() where T : class
        {
            return SelectedFrom<T>(Grid);
        }
    }
}
