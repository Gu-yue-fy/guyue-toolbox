/* ============================================================
 * 文件说明：CPU 拓扑探测：判断本机是否为「大小核（异构）CPU」。
 *           通过 GetLogicalProcessorInformationEx(RelationProcessorCore) 读取每个核心的
 *           EfficiencyClass，存在不同取值即视为大小核。结果缓存，供需要机型适配的优化项
 *           做适用性护栏（如异类线程调度策略在不支持的机器上自动判定不适用）。
 * 项目：古月工具包（GuyueBox）
 * ============================================================ */

using System;
using System.Runtime.InteropServices;

namespace GuyueBox.Core
{
    public static class CpuTopology
    {
        private const int RelationProcessorCore = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int relationshipType, IntPtr buffer, ref int returnedLength);

        private static bool _probed;
        private static bool _hybrid;

        /// <summary>本机是否为大小核 CPU（探测一次后缓存）。探测失败按 false 处理。</summary>
        public static bool IsHybrid
        {
            get
            {
                if (!_probed)
                {
                    _probed = true;
                    _hybrid = Probe();
                }
                return _hybrid;
            }
        }

        private static bool Probe()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int len = 0;
                GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref len);
                if (len <= 0) return false;

                buf = Marshal.AllocHGlobal(len);
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref len)) return false;

                int offset = 0;
                int firstClass = -1;
                // 需读取到条目起始 +9 字节（EfficiencyClass），故按 +10 做边界检查，避免越界读
                while (offset + 10 <= len)
                {
                    int relationship = Marshal.ReadInt32(buf, offset);
                    int size = Marshal.ReadInt32(buf, offset + 4);
                    if (size <= 0) break;

                    // 结构：Relationship(4) + Size(4) + PROCESSOR_RELATIONSHIP
                    // PROCESSOR_RELATIONSHIP = Flags(1) + EfficiencyClass(1) + Reserved(20) + ...
                    // 故 EfficiencyClass 位于条目起始 +9 字节
                    if (relationship == RelationProcessorCore)
                    {
                        int eff = Marshal.ReadByte(buf, offset + 9);
                        if (firstClass < 0) firstClass = eff;
                        else if (eff != firstClass) return true; // 出现不同能效等级 → 大小核
                    }

                    offset += size;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
        }
    }
}
