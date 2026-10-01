using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace GuyueBox.Core
{
    // ---------------------------------------------------------------
    // GROUP_AFFINITY：kernel32 对一个「处理器组 + 组内掩码」的定义
    // ---------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    internal struct GROUP_AFFINITY
    {
        public UIntPtr Mask;       // KAFFINITY = ULONG_PTR（64 位进程下 8 字节）
        public ushort Group;
        public ushort Reserved0;
        public ushort Reserved1;
        public ushort Reserved2;
    }

    /// <summary>
    /// 一个逻辑处理器：跨组连续编号的全局序号，加上它在处理器组里的坐标与硬件属性。
    /// UI 矩阵只认 <see cref="Index"/>（全局序号 0..N-1），底层再映射回 (Group, IndexInGroup)。
    /// </summary>
    public sealed class LogicalCore
    {
        /// <summary>跨所有处理器组连续编号的逻辑处理器序号（供 UI 矩阵使用）。</summary>
        public int Index;
        /// <summary>所属处理器组（Windows 把 >64 核拆成多个组，每组 ≤64 核）。</summary>
        public int Group;
        /// <summary>在该组内的序号（0..63）。</summary>
        public int IndexInGroup;
        /// <summary>效率类：0 通常代表性能核（P 核），>0 为能效核（E 核）。</summary>
        public int Efficiency;
        /// <summary>NUMA 节点号（跨节点访问内存更慢，绑定时应优先同节点）。</summary>
        public int NumaNode;

        /// <summary>是否性能核（P 核）。异构 CPU 上为 true 的是大核。</summary>
        public bool IsPerformanceCore { get { return Efficiency == 0; } }
    }

    /// <summary>
    /// 处理器组感知的亲和性掩码：group -> 该组 64 位掩码（bit i = 该组第 i 个逻辑核）。
    /// 取代原先单 ulong 的偷懒表达，可表达任意核数、任意处理器组组合。
    /// </summary>
    public sealed class CpuAffinityMask
    {
        // internal（而非 private）以便同程序集的 AffinityApi 直接读写各组的 64 位掩码
        internal readonly Dictionary<int, ulong> _map = new Dictionary<int, ulong>();

        /// <summary>只读视图：每个处理器组对应的 64 位掩码。</summary>
        public IReadOnlyDictionary<int, ulong> Groups { get { return _map; } }

        /// <summary>是否一个核心都没选。</summary>
        public bool IsEmpty { get { return _map.Count == 0; } }

        /// <summary>选中 (group, indexInGroup) 这个逻辑核。</summary>
        public void Set(int group, int indexInGroup)
        {
            if (group < 0 || indexInGroup < 0 || indexInGroup > 63) return;
            ulong m = 0;
            _map.TryGetValue(group, out m);
            m |= (1UL << indexInGroup);
            _map[group] = m;
        }

        /// <summary>取消选中 (group, indexInGroup)。</summary>
        public void Clear(int group, int indexInGroup)
        {
            ulong m;
            if (!_map.TryGetValue(group, out m)) return;
            m &= ~(1UL << indexInGroup);
            if (m == 0) _map.Remove(group);
            else _map[group] = m;
        }

        /// <summary>该 (group, indexInGroup) 是否被选中。</summary>
        public bool IsSet(int group, int indexInGroup)
        {
            ulong m;
            return _map.TryGetValue(group, out m) && (m & (1UL << indexInGroup)) != 0;
        }

        /// <summary>选中的逻辑核总数（跨所有组）。</summary>
        public int CountCores()
        {
            int n = 0;
            foreach (ulong m in _map.Values) n += PopCount(m);
            return n;
        }

        /// <summary>是否完全包含于 superset（是 superset 的子集）。</summary>
        public bool IsSubsetOf(CpuAffinityMask superset)
        {
            if (superset == null) return IsEmpty;
            foreach (KeyValuePair<int, ulong> kv in _map)
            {
                ulong s;
                if (!superset._map.TryGetValue(kv.Key, out s) || (kv.Value & ~s) != 0) return false;
            }
            return true;
        }

        /// <summary>按全局逻辑处理器序号选中（需要 order 提供序号->(组,组内)的映射）。</summary>
        public void SetByGlobal(int globalIndex, IList<LogicalCore> order)
        {
            if (order == null || globalIndex < 0 || globalIndex >= order.Count) return;
            LogicalCore c = order[globalIndex];
            Set(c.Group, c.IndexInGroup);
        }

        /// <summary>把当前选择展开成「全局逻辑处理器序号」列表（供 UI 矩阵回填）。</summary>
        public List<int> ToGlobalList(IList<LogicalCore> order)
        {
            List<int> list = new List<int>();
            if (order == null) return list;
            for (int i = 0; i < order.Count; i++)
            {
                LogicalCore c = order[i];
                if (IsSet(c.Group, c.IndexInGroup)) list.Add(i);
            }
            return list;
        }

        /// <summary>序列化为注册表友好的字符串：group:mask(16进制),group:mask…（无空格）。</summary>
        public string ToRegistry()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<int, ulong> kv in _map)
                parts.Add(kv.Key + ":" + kv.Value.ToString("X"));
            return string.Join(",", parts.ToArray());
        }

        /// <summary>从 <see cref="ToRegistry"/> 生成的字符串解析；空串返回空掩码。</summary>
        public static CpuAffinityMask FromRegistry(string s)
        {
            CpuAffinityMask m = new CpuAffinityMask();
            if (string.IsNullOrEmpty(s)) return m;
            string[] parts = s.Split(',');
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                int sep = part.IndexOf(':');
                if (sep <= 0 || sep >= part.Length - 1) continue;
                int g;
                ulong v;
                if (int.TryParse(part.Substring(0, sep), out g) &&
                    ulong.TryParse(part.Substring(sep + 1), System.Globalization.NumberStyles.HexNumber, null, out v))
                {
                    m._map[g] = v;
                }
            }
            return m;
        }

        /// <summary>兼容旧单组 ulong（取首组掩码）。</summary>
        public ulong ToLegacyMask()
        {
            ulong best = 0;
            foreach (KeyValuePair<int, ulong> kv in _map) { best = kv.Value; break; }
            return best;
        }

        /// <summary>从旧单组 ulong 构造（全部放进 0 组）。</summary>
        public static CpuAffinityMask FromLegacy(ulong mask)
        {
            CpuAffinityMask m = new CpuAffinityMask();
            if (mask != 0) m._map[0] = mask;
            return m;
        }

        private static int PopCount(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }
    }

    // ---------------------------------------------------------------
    // 完整 CPU 拓扑探测（重写自 CpuTopology.cs 的单一 IsHybrid，提供更细信息）
    // ---------------------------------------------------------------
    public static class CpuTopologyEx
    {
        private const int RelationAll = 0xFFFF;
        private const int RelationProcessorCore = 0;
        private const int RelationNumaNode = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref int returnedLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNumaNodeProcessorMaskEx(ushort node, out GROUP_AFFINITY groupAffinity);

        private static readonly object _lock = new object();
        private static bool _probed;
        private static readonly List<LogicalCore> _cores = new List<LogicalCore>();
        private static int _groupCount;
        private static bool _hybrid;

        /// <summary>全部逻辑处理器（按组、组内序号排序，Index 为全局连续序号）。</summary>
        public static IList<LogicalCore> Cores { get { Ensure(); return _cores; } }

        /// <summary>逻辑处理器总数（= 可被调度的核心数，含超线程）。</summary>
        public static int LogicalCount { get { Ensure(); return _cores.Count; } }

        /// <summary>处理器组数量（>1 表示机器跨多个处理器组）。</summary>
        public static int GroupCount { get { Ensure(); return _groupCount; } }

        /// <summary>是否为大小核（异构）CPU。</summary>
        public static bool IsHybrid { get { Ensure(); return _hybrid; } }

        private static void Ensure()
        {
            lock (_lock)
            {
                if (!_probed) { _probed = true; Probe(); }
            }
        }

        private static void Probe()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                int len = 0;
                // 第一次调用拿所需缓冲区长度（失败时 len 仍会被写入所需大小）
                GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref len);
                if (len <= 0) return;
                buf = Marshal.AllocHGlobal(len);
                if (!GetLogicalProcessorInformationEx(RelationAll, buf, ref len)) return;

                int offset = 0;
                // 用于把 NUMA 节点号关联到每个 (group, index) 的临时表
                Dictionary<long, int> numaOfCore = new Dictionary<long, int>();

                while (offset + 8 <= len)
                {
                    int relationship = Marshal.ReadInt32(buf, offset);
                    int size = Marshal.ReadInt32(buf, offset + 4);
                    if (size <= 0) break;

                    if (relationship == RelationProcessorCore)
                    {
                        // PROCESSOR_RELATIONSHIP：Flags(@+8) + EfficiencyClass(@+9) + Reserved(20)
                        //   + ProcessorMask(GROUP_AFFINITY @+30) + GroupCount(ushort @+46)
                        //   + GroupMask[GroupCount]（每个 16 字节，从 @+48 起）
                        byte efficiency = Marshal.ReadByte(buf, offset + 9);
                        int groupCount = Marshal.ReadInt16(buf, offset + 46);
                        int gmBase = offset + 48;
                        for (int g = 0; g < groupCount; g++)
                        {
                            long mask = Marshal.ReadInt64(buf, gmBase + g * 16);     // GROUP_AFFINITY.Mask @ +0
                            int grp = Marshal.ReadInt16(buf, gmBase + g * 16 + 8);  // GROUP_AFFINITY.Group @ +8
                            for (int bit = 0; bit < 64; bit++)
                            {
                                if ((mask & (1L << bit)) != 0)
                                {
                                    LogicalCore c = new LogicalCore();
                                    c.Group = grp;
                                    c.IndexInGroup = bit;
                                    c.Efficiency = efficiency;
                                    _cores.Add(c);
                                }
                            }
                        }
                    }
                    else if (relationship == RelationNumaNode)
                    {
                        // NUMA_NODE_RELATIONSHIP：NodeNumber(@+8, 4 字节) + GroupMask(GROUP_AFFINITY @+12)…
                        int node = Marshal.ReadInt32(buf, offset + 8);
                        long mask = Marshal.ReadInt64(buf, offset + 12);
                        int grp = Marshal.ReadInt16(buf, offset + 12 + 8);
                        for (int bit = 0; bit < 64; bit++)
                        {
                            if ((mask & (1L << bit)) != 0)
                                numaOfCore[Pack(grp, bit)] = node;
                        }
                    }

                    offset += size;
                }

                // 按 (Group, IndexInGroup) 排序后赋予全局连续序号，保证 UI 矩阵顺序稳定
                _cores.Sort(delegate (LogicalCore a, LogicalCore b)
                {
                    if (a.Group != b.Group) return a.Group.CompareTo(b.Group);
                    return a.IndexInGroup.CompareTo(b.IndexInGroup);
                });
                for (int i = 0; i < _cores.Count; i++)
                {
                    _cores[i].Index = i;
                    int node;
                    if (numaOfCore.TryGetValue(Pack(_cores[i].Group, _cores[i].IndexInGroup), out node))
                        _cores[i].NumaNode = node;
                    else
                        _cores[i].NumaNode = 0;
                }

                _groupCount = 0;
                foreach (LogicalCore c in _cores)
                    if (c.Group + 1 > _groupCount) _groupCount = c.Group + 1;

                _hybrid = false;
                if (_cores.Count > 0)
                {
                    int first = _cores[0].Efficiency;
                    foreach (LogicalCore c in _cores)
                        if (c.Efficiency != first) { _hybrid = true; break; }
                }
            }
            catch
            {
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                // 兜底：解析失败或 API 调用失败（含静默 return 路径）时，
                // 按 Environment.ProcessorCount 造一个单组拓扑，保证界面不显示 0。
                if (_cores.Count == 0)
                {
                    for (int i = 0; i < Environment.ProcessorCount; i++)
                    {
                        LogicalCore c = new LogicalCore();
                        c.Group = 0; c.IndexInGroup = i; c.Efficiency = 0; c.NumaNode = 0; c.Index = i;
                        _cores.Add(c);
                    }
                    _groupCount = 1;
                }
            }
        }

        private static long Pack(int group, int index)
        {
            return ((long)group << 32) | (uint)index;
        }

        /// <summary>用 GetNumaNodeProcessorMaskEx 构建某 NUMA 节点的亲和性掩码（group-aware）。</summary>
        public static CpuAffinityMask NumaMask(ushort node)
        {
            CpuAffinityMask m = new CpuAffinityMask();
            try
            {
                GROUP_AFFINITY ga;
                if (GetNumaNodeProcessorMaskEx(node, out ga))
                {
                    m.Set(ga.Group, 0); // 占位，随后用 Mask 覆盖
                    m._map[ga.Group] = (ulong)ga.Mask;
                }
            }
            catch { }
            return m;
        }

    }

    // ---------------------------------------------------------------
    // 进程级 group-aware 亲和性读写
    // ---------------------------------------------------------------
    public static class AffinityApi
    {
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_SET_INFORMATION = 0x0200;
        private const int THREAD_QUERY_INFORMATION = 0x0040;
        private const int THREAD_SET_INFORMATION = 0x0020;
        private const int THREAD_QUERY_LIMITED_INFORMATION = 0x0800;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(int access, bool inherit, int tid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessGroupAffinity(IntPtr hProcess, ref ushort groupCount, [Out] ushort[] groupArray);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadGroupAffinity(IntPtr hThread, out GROUP_AFFINITY affinity);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadGroupAffinity(IntPtr hThread, ref GROUP_AFFINITY affinity, IntPtr previous);

        /// <summary>系统全量可用核心（所有逻辑处理器都置位）。</summary>
        public static CpuAffinityMask SystemAffinity()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            IList<LogicalCore> cores = CpuTopologyEx.Cores;
            foreach (LogicalCore c in cores) m.Set(c.Group, c.IndexInGroup);
            return m;
        }

        /// <summary>
        /// 读取进程当前亲和性。进程可能跨多个处理器组，这里逐线程读取后合并成完整掩码。
        /// 失败返回空掩码并给出原因（权限不足 / 进程已退出 / 不支持）。
        /// </summary>
        public static CpuAffinityMask GetProcessAffinity(int pid, out string error)
        {
            error = "";
            CpuAffinityMask mask = new CpuAffinityMask();
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    error = "无法打开该进程（可能已退出或权限不足，需以管理员身份运行）。";
                    return mask;
                }

                // 先拿进程所在的处理器组集合（用于后续校验）
                ushort gc = 0;
                ushort[] groups = null;
                if (GetProcessGroupAffinity(h, ref gc, null) && gc > 0)
                {
                    groups = new ushort[gc];
                    if (!GetProcessGroupAffinity(h, ref gc, groups))
                    {
                        groups = null;
                    }
                }

                Process p = null;
                try { p = Process.GetProcessById(pid); }
                catch (Exception ex) { error = "进程已退出：" + ex.Message; return mask; }

                try
                {
                    foreach (ProcessThread t in p.Threads)
                    {
                        IntPtr ht = IntPtr.Zero;
                        try
                        {
                            ht = OpenThread(THREAD_QUERY_LIMITED_INFORMATION | THREAD_QUERY_INFORMATION, false, t.Id);
                            if (ht == IntPtr.Zero) continue;
                            GROUP_AFFINITY ga;
                            if (GetThreadGroupAffinity(ht, out ga) && (ulong)ga.Mask != 0)
                            {
                                mask._map[ga.Group] = (mask._map.ContainsKey(ga.Group) ? mask._map[ga.Group] : 0) | (ulong)ga.Mask;
                            }
                        }
                        catch { }
                        finally { if (ht != IntPtr.Zero) CloseHandle(ht); }
                    }
                }
                finally { p.Dispose(); }
                return mask;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return mask;
            }
            finally
            {
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }

        /// <summary>
        /// 设置进程亲和性（group-aware）。对每个线程调用 SetThreadGroupAffinity，把该线程
        /// 放到「掩码里它当前所在组（若有）否则第一个含位的组」的对应掩码上，从而支持 >64 核。
        /// 空掩码会被拒绝（进程将无核可调度）。
        /// </summary>
        public static bool SetProcessAffinity(int pid, CpuAffinityMask mask, out string error)
        {
            error = "";
            if (mask == null || mask.IsEmpty)
            {
                error = "至少要保留一个核心。";
                return false;
            }

            CpuAffinityMask system = SystemAffinity();
            if (!mask.IsSubsetOf(system))
            {
                error = "选中的核心超出了本机可用范围。";
                return false;
            }

            IntPtr h = IntPtr.Zero;
            Process p = null;
            try
            {
                h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, pid);
                if (h == IntPtr.Zero)
                {
                    error = "无法打开该进程（可能已退出或权限不足，需以管理员身份运行）。";
                    return false;
                }
                try { p = Process.GetProcessById(pid); }
                catch (Exception ex) { error = "进程已退出：" + ex.Message; return false; }

                int firstGroup = -1; ulong firstMask = 0;
                foreach (KeyValuePair<int, ulong> kv in mask.Groups)
                {
                    if (kv.Value != 0) { firstGroup = kv.Key; firstMask = kv.Value; break; }
                }

                bool anyOk = false;
                string lastErr = "";
                foreach (ProcessThread t in p.Threads)
                {
                    IntPtr ht = IntPtr.Zero;
                    try
                    {
                        ht = OpenThread(THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION, false, t.Id);
                        if (ht == IntPtr.Zero) continue;

                        // 优先保持线程在它原有组（若该组在掩码里有位）
                        int targetGroup = firstGroup;
                        ulong targetMask = firstMask;
                        GROUP_AFFINITY cur;
                        if (GetThreadGroupAffinity(ht, out cur) && cur.Mask != UIntPtr.Zero)
                        {
                            ulong inMask;
                            if (mask.Groups.TryGetValue(cur.Group, out inMask) && inMask != 0)
                            {
                                targetGroup = cur.Group;
                                targetMask = inMask;
                            }
                        }

                        GROUP_AFFINITY ga = new GROUP_AFFINITY();
                        ga.Mask = (UIntPtr)targetMask;
                        ga.Group = (ushort)targetGroup;
                        if (SetThreadGroupAffinity(ht, ref ga, IntPtr.Zero))
                            anyOk = true;
                        else
                            lastErr = "错误码 " + Marshal.GetLastWin32Error();
                    }
                    catch (Exception ex) { lastErr = ex.Message; }
                    finally { if (ht != IntPtr.Zero) CloseHandle(ht); }
                }

                if (!anyOk)
                {
                    error = "设置失败（" + (lastErr.Length > 0 ? lastErr : "未知原因") + "），该进程可能不允许修改。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (p != null) try { p.Dispose(); } catch { }
                if (h != IntPtr.Zero) CloseHandle(h);
            }
        }
    }

    // ---------------------------------------------------------------
    // 面向真实硬件结构的智能预设（消除「前 N 核」这种偷懒预设）
    // ---------------------------------------------------------------
    public static class AffinityPresets
    {
        /// <summary>全核：所有逻辑处理器都选上。</summary>
        public static CpuAffinityMask All()
        {
            return AffinityApi.SystemAffinity();
        }

        /// <summary>仅性能核（P 核）：异构 CPU 上跑游戏 / 渲染优先；同构机上等价于全核。</summary>
        public static CpuAffinityMask PerformanceCores()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            foreach (LogicalCore c in CpuTopologyEx.Cores)
                if (c.IsPerformanceCore) m.Set(c.Group, c.IndexInGroup);
            if (m.IsEmpty) return All();
            return m;
        }

        /// <summary>仅能效核（E 核）：把后台 / 下载类进程赶到小核，给前台腾出 P 核。</summary>
        public static CpuAffinityMask EfficientCores()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            foreach (LogicalCore c in CpuTopologyEx.Cores)
                if (!c.IsPerformanceCore) m.Set(c.Group, c.IndexInGroup);
            if (m.IsEmpty) return All();
            return m;
        }

        /// <summary>指定 NUMA 节点的全部核心（跨节点内存访问更慢，绑同节点更优）。</summary>
        public static CpuAffinityMask NumaNode(int node)
        {
            CpuAffinityMask m = CpuTopologyEx.NumaMask((ushort)node);
            return m.IsEmpty ? All() : m;
        }

        /// <summary>前半（按组与组内序号排序后的前一半）。</summary>
        public static CpuAffinityMask FirstHalf()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            IList<LogicalCore> cores = CpuTopologyEx.Cores;
            int half = cores.Count / 2;
            for (int i = 0; i < half; i++) m.Set(cores[i].Group, cores[i].IndexInGroup);
            if (m.IsEmpty) return All();
            return m;
        }

        /// <summary>后半（按组与组内序号排序后的后一半）。</summary>
        public static CpuAffinityMask SecondHalf()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            IList<LogicalCore> cores = CpuTopologyEx.Cores;
            int half = cores.Count / 2;
            for (int i = half; i < cores.Count; i++) m.Set(cores[i].Group, cores[i].IndexInGroup);
            if (m.IsEmpty) return All();
            return m;
        }

        /// <summary>单核：选性能核里序号最小的一个（最快核），用于极致降抖动的专用进程。</summary>
        public static CpuAffinityMask SingleFastest()
        {
            CpuAffinityMask m = new CpuAffinityMask();
            foreach (LogicalCore c in CpuTopologyEx.Cores)
            {
                if (c.IsPerformanceCore) { m.Set(c.Group, c.IndexInGroup); break; }
            }
            if (m.IsEmpty && CpuTopologyEx.Cores.Count > 0)
            {
                LogicalCore c = CpuTopologyEx.Cores[0];
                m.Set(c.Group, c.IndexInGroup);
            }
            return m;
        }

    }
}
