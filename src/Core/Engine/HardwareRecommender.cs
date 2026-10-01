/* ============================================================
 * 文件说明：硬件专属优化推荐引擎：按本机 CPU/GPU 厂商匹配专属优化项
 * 项目：古月工具箱（GuyueBox）
 * ============================================================ */

using System;
using System.Collections.Generic;

namespace GuyueBox.Core
{
    /// <summary>
    /// 硬件专属推荐：根据本机 CPU（Intel/AMD）与 GPU（NVIDIA/AMD/Intel）厂商，
    /// 从优化库中挑出「专属本机硬件」的优化项，供优化中心一键应用。
    /// </summary>
    public static class HardwareRecommender
    {
        /// <summary>已知硬件专属项 ID 白名单（其余项视为通用优化，不参与专属推荐）。</summary>
        private static readonly string[] VendorTweakIds = new string[]
        {
            // CPU 专属
            "intel_scheduling",
            // NVIDIA 显卡专属
            // （原 gpu_dx_perf / nv_gpu_latency_core / nv_gpu_render_fillrate /
            //   nv_gpu_display_sched / nv_gpu_hw_thread / nv_gpu_mem_firmware 已删除：
            //   它们写的是不存在的注册表键名，属无用项）
            "nv_per_cpu_core_dpc", "hdcp_off", "nv_telemetry_off", "wddm_opt",
            "nvidia_latency_deep",
            // AMD 显卡专属
            "amd_latency_deep", "amd_power_thermal_off", "amd_ulps_off",
        };

        /// <summary>本机硬件摘要（优化中心页头提示用）。</summary>
        public static string HardwareSummary()
        {
            string cpu = CpuVendor.IsIntel ? "Intel CPU" : (CpuVendor.IsAmd ? "AMD CPU" : "CPU");
            string gpu = GpuLatencyTweak.DetectVendor();
            string gpuText = gpu.Contains("N") ? "NVIDIA 显卡"
                : (gpu.Contains("A") ? "AMD 显卡"
                : (gpu.Contains("I") ? "Intel 核显" : "未知显卡"));
            return cpu + " + " + gpuText;
        }

        /// <summary>返回所有「适用于本机硬件」的专属推荐项（保持库内顺序）。</summary>
        public static List<ITweak> CollectApplicable()
        {
            List<ITweak> result = new List<ITweak>();
            List<ITweak> all = TweakLibrary.All();
            Dictionary<string, ITweak> byId = new Dictionary<string, ITweak>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < all.Count; i++)
            {
                if (!string.IsNullOrEmpty(all[i].Id) && !byId.ContainsKey(all[i].Id))
                    byId[all[i].Id] = all[i];
            }
            for (int i = 0; i < VendorTweakIds.Length; i++)
            {
                ITweak t;
                if (!byId.TryGetValue(VendorTweakIds[i], out t)) continue;
                if (!IsApplicable(t)) continue;
                result.Add(t);
            }
            return result;
        }

        /// <summary>通用适用性检测：声明式项读取厂商门控；命令式项默认适用。</summary>
        private static bool IsApplicable(ITweak t)
        {
            RegTweak rt = t as RegTweak;
            if (rt == null) return true;
            try { return rt.ApplicableValue == null || rt.ApplicableValue(); }
            catch { return true; }
        }
    }
}