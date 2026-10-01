# ============================================================
# UI responsiveness probe (ASCII only: PS 5.1 reads .ps1 as ANSI,
# UTF-8 Chinese text in strings breaks parsing).
# Runs the app in --shots mode (heaviest path: every page built+drawn)
# and pings the UI thread with SendMessageTimeout(WM_NULL).
# A window that does not process messages within TimeoutMs counts as a stall
# (that is the freeze a user can actually feel).
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\hung-probe.ps1 [-Exe bin\GuyueBox.exe] [-ShotDir shots\hung] [-TimeoutMs 120]
# ============================================================
param(
    [string]$Exe = "bin\GuyueBox.exe",
    [string]$ShotDir = "shots\hung",
    [int]$TimeoutMs = 120
)

$ErrorActionPreference = "Stop"

Add-Type -Namespace W -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint Msg, System.IntPtr wParam,
    System.IntPtr lParam, uint fuFlags, uint uTimeout, out System.IntPtr lpdwResult);
'@

$p = Start-Process -FilePath $Exe -ArgumentList "--shots", $ShotDir -PassThru

# wait for the main window (the app uses a borderless form, MainWindowHandle still works)
$wait = [System.Diagnostics.Stopwatch]::StartNew()
while (-not $p.HasExited -and $p.MainWindowHandle -eq [System.IntPtr]::Zero -and $wait.Elapsed.TotalSeconds -lt 20) {
    Start-Sleep -Milliseconds 100
    $p.Refresh()
}
if ($p.HasExited) {
    Write-Output "probe: process exited before a window appeared (single-instance mutex or startup failure)"
    exit 0
}

$stalls = 0
$worst = 0
$sumStall = 0
$probes = 0
$errors = 0
$ERROR_TIMEOUT = 1460

while (-not $p.HasExited) {
    $p.Refresh()
    $hwnd = $p.MainWindowHandle
    if ($hwnd -eq [System.IntPtr]::Zero) {
        Start-Sleep -Milliseconds 40
        continue
    }

    $r = [System.IntPtr]::Zero
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    # SMTO_ABORTIFHUNG(2) with explicit timeout; timeout => 0 with GetLastError = ERROR_TIMEOUT
    $ok = [W.U]::SendMessageTimeout($hwnd, 0, [System.IntPtr]::Zero, [System.IntPtr]::Zero, 2, [uint32]$TimeoutMs, [ref]$r)
    $err = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
    $sw.Stop()
    $probes++

    if ($ok -eq [System.IntPtr]::Zero -and -not $p.HasExited) {
        if ($err -eq $ERROR_TIMEOUT) {
            $stalls++
            $ms = [int]$sw.Elapsed.TotalMilliseconds
            $sumStall += $ms
            if ($ms -gt $worst) { $worst = $ms }
        } else {
            $errors++      # window handle transiently invalid etc. - not a stall
        }
    }
    Start-Sleep -Milliseconds 40
}

$avg = 0
if ($stalls -gt 0) { $avg = [math]::Round($sumStall / $stalls, 0) }

Write-Output ("probes       = " + $probes)
Write-Output ("ui_stalls    = " + $stalls + "   (no response for more than " + $TimeoutMs + "ms)")
Write-Output ("worst_ms     = " + $worst)
Write-Output ("avg_stall_ms = " + $avg)
Write-Output ("probe_errs   = " + $errors + "   (handle-related, not counted as stalls)")
