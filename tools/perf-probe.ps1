# ============================================================
# 性能基线探针：以 --shots 模式跑完所有页面（最重负载路径：
# 每页的构建 + 布局 + 全量绘制都会走到），期间采样进程的内存与 CPU。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf-probe.ps1 [-Exe bin\GuyueBox.exe] [-ShotDir shots\perf] [-IdleSeconds 6]
# 输出：elapsed / peak_ws / peak_private / cpu / exitcode（均为可直接对比的数字）
# ============================================================
param(
    [string]$Exe = "bin\GuyueBox.exe",
    [string]$ShotDir = "shots\perf",
    [int]$IdleSeconds = 0
)

$ErrorActionPreference = "Stop"

# 1) 负载路径：遍历所有页面截图后自动退出
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -ArgumentList "--shots", $ShotDir -PassThru
$peakWs = 0
$peakPrivate = 0
$samples = 0

while (-not $p.HasExited) {
    Start-Sleep -Milliseconds 150
    try {
        $p.Refresh()
        if ($p.WorkingSet64 -gt $peakWs) { $peakWs = $p.WorkingSet64 }
        if ($p.PrivateMemorySize64 -gt $peakPrivate) { $peakPrivate = $p.PrivateMemorySize64 }
        $samples++
    } catch { }
}

$sw.Stop()
$p.Refresh()
$cpu = 0.0
try { $cpu = $p.TotalProcessorTime.TotalSeconds } catch { }

Write-Output ("elapsed_s    = " + [math]::Round($sw.Elapsed.TotalSeconds, 2))
Write-Output ("peak_ws_MB   = " + [math]::Round($peakWs / 1MB, 1))
Write-Output ("peak_priv_MB = " + [math]::Round($peakPrivate / 1MB, 1))
Write-Output ("cpu_s        = " + [math]::Round($cpu, 2))
Write-Output ("samples      = " + $samples)
Write-Output ("exitcode     = " + $p.ExitCode)

# 2) 空闲占用（可选）：正常启动后静置，采样空闲 CPU 与内存
if ($IdleSeconds -gt 0) {
    $p2 = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 4                      # 等启动完成
    $p2.Refresh()
    $cpu0 = $p2.TotalProcessorTime.TotalSeconds
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $IdleSeconds
    $sw2.Stop()
    $p2.Refresh()
    $cpuIdle = $p2.TotalProcessorTime.TotalSeconds - $cpu0
    Write-Output ("idle_cpu_s   = " + [math]::Round($cpuIdle, 2) + "  (over " + $IdleSeconds + "s, 0 = 完全空闲)")
    Write-Output ("idle_ws_MB   = " + [math]::Round($p2.WorkingSet64 / 1MB, 1))
    try { $p2.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800 } catch { }
    if (-not $p2.HasExited) { try { $p2.Kill() } catch { } }
}
