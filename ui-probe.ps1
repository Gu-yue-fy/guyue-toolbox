Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class W3 {
    public struct RECT { public int L, T, R, B; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    public static List<IntPtr> Collect(uint target) {
        List<IntPtr> list = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == target && IsWindowVisible(h)) {
                RECT r; GetWindowRect(h, out r);
                if (r.R - r.L >= 400 && r.B - r.T >= 400) list.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static void TopMost(IntPtr h) {
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
    }
}
"@
$script:MainH = [IntPtr]::Zero
$dir = 'c:\Users\Administrator\CodeBuddy\系统优化工具箱\shots'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Remove-Item "$dir\*.png" -Force -ErrorAction SilentlyContinue

$exe = 'c:\Users\Administrator\CodeBuddy\系统优化工具箱\bin\GuyueBox.exe'
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$proc.Refresh()
$target = [uint32]$proc.Id
$best = 0
for ($try = 0; $try -lt 10 -and $script:MainH -eq [IntPtr]::Zero; $try++) {
    foreach ($wh in [W3]::Collect($target)) {
        $r = New-Object W3+RECT
        [W3]::GetWindowRect($wh, [ref]$r) | Out-Null
        $area = ($r.R - $r.L) * ($r.B - $r.T)
        if ($area -gt $best) { $best = $area; $script:MainH = $wh }
    }
    if ($script:MainH -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 600; $proc.Refresh(); $target = [uint32]$proc.Id }
}
if ($script:MainH -eq [IntPtr]::Zero) { Write-Output 'main window not found'; Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue; exit 1 }
[W3]::TopMost($script:MainH) | Out-Null
Start-Sleep -Milliseconds 800

function Snap([string]$name) {
    $r = New-Object W3+RECT
    [W3]::GetWindowRect($script:MainH, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Output ("snap " + $name + ": bad rect"); return }
    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $ok = $false
    try {
        $hdc = $g.GetHdc()
        $ok = [W3]::PrintWindow($script:MainH, $hdc, 2)
        $g.ReleaseHdc($hdc)
    } catch { $ok = $false }
    if (-not $ok) { $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $ht))) }
    $bmp.Save(($dir + '\' + $name + '.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output ("snap " + $name + " ok (pw=" + $ok + ")")
}

function Go([int]$itemCenterY, [string]$shot) {
    $r = New-Object W3+RECT
    [W3]::GetWindowRect($script:MainH, [ref]$r) | Out-Null
    [W3]::Click($r.L + 100, $r.T + $itemCenterY)
    Start-Sleep -Milliseconds 1400
    Snap $shot
}

# 侧栏项中心 y（由真实截图测量换算，客户区坐标）
Go 152 'p02_repair'
Go 234 'p03_optimize'
Go 273 'p04_memory'
Go 319 'p05_bench'
Go 399 'p06_network'
Go 480 'p07_power'
Go 520 'p08_services'
Go 567 'p09_process'
Go 611 'p10_programs'
Go 651 'p11_hardware'
Go 733 'p12_cleanup'
Go 814 'p13_settings'
Go 854 'p14_about'
$proc.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
Write-Output 'done'
