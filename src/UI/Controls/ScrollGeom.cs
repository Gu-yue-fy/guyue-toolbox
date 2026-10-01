/* ============================================================
 * 文件说明：滚动条缩略几何：把 ScrollHost / DarkGrid 各自计算的缩略块尺寸与位置
 * 收口到一处，消除重复的几何数学。
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

namespace GuyueBox.UI
{
    /// <summary>
    /// 自绘滚动条缩略几何：按内容总长与单屏可见长，算出缩略块尺寸与位置。
    /// 缩略块比例 = track * viewport / content（夹在 [minThumb, track] 内），
    /// 位置按当前滚动位置在行程上的占比映射——ScrollHost 与 DarkGrid 共用同一套数学。
    /// </summary>
    internal static class ScrollGeom
    {
        /// <summary>
        /// 计算缩略块尺寸与位置。
        /// </summary>
        /// <param name="content">内容总长（像素或行数）。</param>
        /// <param name="viewport">单屏可见长（像素或行数）。</param>
        /// <param name="inset">滚动条上下留白（两侧缩进）。</param>
        /// <param name="minThumb">缩略块最小高。</param>
        /// <param name="pos">当前滚动位置（像素或行索引）。</param>
        /// <param name="thumbSize">输出：缩略块高；无溢出时为 0。</param>
        /// <param name="thumbTop">输出：缩略块顶；无溢出时为 0。</param>
        public static void Compute(int content, int viewport, int inset, int minThumb, int pos,
            out int thumbSize, out int thumbTop)
        {
            int track = viewport - inset * 2;
            if (content <= viewport || viewport <= 0 || track <= 0)
            {
                thumbSize = 0;
                thumbTop = 0;
                return;
            }

            thumbSize = (int)((double)track * viewport / content);
            if (thumbSize < minThumb) thumbSize = minThumb;
            if (thumbSize > track) thumbSize = track;

            int travel = track - thumbSize;
            int max = content - viewport;
            thumbTop = inset + (max <= 0 ? 0 : (int)((double)pos * travel / max));
        }
    }
}
