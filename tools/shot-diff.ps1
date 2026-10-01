# ============================================================
# 截图像素差分：比较两次自动截图的目录，报告每个页面变了多少。
#
# 为什么不比文件哈希：哈希只回答"变没变"，而界面里大量数字是实时的
# （CPU 占用、运行时长、进程列表…），哈希必然不同、结论没有信息量。
# 像素差能回答"变了多少"，再配合噪声基线就能区分"数据波动"与"版面回归"。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\tools\shot-diff.ps1 -A shots\baseline -B shots\new
#
# 判据（阈值可调）：
#   diff <  Noise   → 噪声内（实时数据波动，视为无变化）
#   Noise ≤ diff < Alert → 需留意（可能是数据变化，也可能是局部改动）
#   diff ≥  Alert   → 必须肉眼看图确认
#
# 退出码：0=全部在噪声内 / 1=存在超出 Alert 的页面 / 2=目录内容不一致
# ============================================================
param(
    [Parameter(Mandatory = $true)][string]$A,
    [Parameter(Mandatory = $true)][string]$B,
    [double]$Noise = 1.0,
    [double]$Alert = 2.0
)

# 活数据页的单独阈值：这些页面展示的是机器实时状态（启动项/服务/任务/网络/还原点/
# 垃圾体积…），两次截图之间机器自身就会变。实测"列表增减一行"约占 2.8%
# （28px 行高 / 880px 视口高 × 全行宽），故给它们放宽到 5%（≈容忍两行以内的数据抖动），
# 超过仍然报"需看图"。不在表里的页面维持全局 Alert（严格）。
# 判定列会显示每页实际使用的阈值，放宽不是静默的。
$LiveTolerance = @{
    'cleaner.png'     = 5.0   # 垃圾扫描：体积与数量随使用变化
    'contextmenu.png' = 5.0   # 右键菜单：随安装的软件变化
    'device.png'      = 5.0   # 设备管理：设备状态会变
    'dns.png'         = 5.0   # DNS：时延/状态实时
    'driver.png'      = 5.0   # 驱动一览：随驱动更新变化
    'features.png'    = 5.0   # 可选功能：随系统组件变化
    'gpuspoof.png'    = 5.0   # 显卡信息：驱动/版本串
    'mtu.png'         = 5.0   # MTU：网卡状态实时
    'network.png'     = 5.0   # 网络诊断：时延/状态实时
    'nvidia.png'      = 5.0   # N 卡设置：驱动/版本串
    'powerplans.png'  = 5.0   # 电源计划：当前计划/值
    'powertuning.png' = 5.0   # 高级电源：当前值
    'process.png'     = 5.0   # 进程管理：进程表与 CPU/内存列实时变化
    'memory.png'      = 5.0   # 内存优化：占用数值实时变化
    'programs.png'    = 5.0   # 已安装程序：列表随安装/升级变化
    'repair.png'      = 5.0   # 修复中心：扫描结果随系统状态变化
    'services.png'    = 5.0   # 服务管理：服务运行状态实时变化
    'restore.png'     = 5.0   # 还原点：列表随时间变化
    'startup.png'     = 5.0   # 启动项：软件自行增删注册项
    'tasks.png'       = 5.0   # 计划任务：上次运行时间/状态实时
}

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Get-PixelDiff([string]$fileA, [string]$fileB) {
    $ia = [System.Drawing.Bitmap]::FromFile($fileA)
    $ib = [System.Drawing.Bitmap]::FromFile($fileB)
    try {
        if ($ia.Width -ne $ib.Width -or $ia.Height -ne $ib.Height) { return -1 }
        $rc = New-Object System.Drawing.Rectangle 0, 0, $ia.Width, $ia.Height
        $d1 = $ia.LockBits($rc, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $d2 = $ib.LockBits($rc, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $n = [Math]::Abs($d1.Stride) * $ia.Height
            $ba = New-Object byte[] $n
            $bb = New-Object byte[] $n
            [System.Runtime.InteropServices.Marshal]::Copy($d1.Scan0, $ba, 0, $n)
            [System.Runtime.InteropServices.Marshal]::Copy($d2.Scan0, $bb, 0, $n)
            $c = 0
            for ($i = 0; $i -lt $n; $i += 4) {
                if ($ba[$i] -ne $bb[$i] -or $ba[$i + 1] -ne $bb[$i + 1] -or $ba[$i + 2] -ne $bb[$i + 2]) { $c++ }
            }
            return $c
        }
        finally {
            $ia.UnlockBits($d1)
            $ib.UnlockBits($d2)
        }
    }
    finally {
        $ia.Dispose()
        $ib.Dispose()
    }
}

$aFiles = @{}
foreach ($f in Get-ChildItem -Path $A -Filter *.png -File) { $aFiles[$f.Name] = $f.FullName }
$bFiles = @{}
foreach ($f in Get-ChildItem -Path $B -Filter *.png -File) { $bFiles[$f.Name] = $f.FullName }

$onlyA = @($aFiles.Keys | Where-Object { -not $bFiles.ContainsKey($_) } | Sort-Object)
$onlyB = @($bFiles.Keys | Where-Object { -not $aFiles.ContainsKey($_) } | Sort-Object)
$mismatch = ($onlyA.Count -gt 0 -or $onlyB.Count -gt 0)

Write-Output ('基线 A : ' + (Resolve-Path $A).Path + '  (' + $aFiles.Count + ' 张)')
Write-Output ('对照 B : ' + (Resolve-Path $B).Path + '  (' + $bFiles.Count + ' 张)')
Write-Output ('判据   : <' + $Noise + '% 噪声内 / ≥' + $Alert + '% 需看图；活数据页单独放宽（见"阈值"列，表在脚本头部）')
Write-Output ''

$rows = @()
$alerts = @()
$same = 0
$noisy = 0

foreach ($name in ($aFiles.Keys | Sort-Object)) {
    if (-not $bFiles.ContainsKey($name)) { continue }
    $ia = [System.Drawing.Bitmap]::FromFile($aFiles[$name])
    $total = $ia.Width * $ia.Height
    $ia.Dispose()

    $diff = Get-PixelDiff $aFiles[$name] $bFiles[$name]
    if ($diff -lt 0) {
        $rows += [PSCustomObject]@{ 页面 = $name; 差异像素 = '尺寸不同'; 占比 = '—'; 判定 = '尺寸不同' }
        $alerts += $name
        continue
    }

    $pageAlert = if ($LiveTolerance.ContainsKey($name)) { $LiveTolerance[$name] } else { $Alert }
    $pct = [Math]::Round(100.0 * $diff / $total, 3)
    $verdict = '噪声内'
    if ($pct -ge $pageAlert) { $verdict = '需看图'; $alerts += $name }
    elseif ($pct -ge $Noise) { $verdict = '留意' }
    elseif ($diff -eq 0) { $verdict = '逐字节相同'; $same++ }
    else { $noisy++ }

    $rows += [PSCustomObject]@{ 页面 = $name; 差异像素 = $diff; 占比 = ($pct.ToString() + '%')
        阈值 = ($pageAlert.ToString() + '%'); 判定 = $verdict }
}

$rows | Format-Table -AutoSize

if ($onlyA.Count -gt 0) { Write-Output ('仅基线有的页面（本次未拍到）: ' + ($onlyA -join ', ')) }
if ($onlyB.Count -gt 0) { Write-Output ('仅对照有的页面（新增截图）: ' + ($onlyB -join ', ')) }

Write-Output ''
Write-Output ('汇总: 逐字节相同 ' + $same + ' 张 / 噪声内 ' + $noisy + ' 张 / 需确认 ' + $alerts.Count + ' 张')

if ($mismatch) {
    Write-Output '结论: 两次截图覆盖的页面不一致——请确认探针是否中途失败。'
    exit 2
}
if ($alerts.Count -gt 0) {
    Write-Output ('结论: ' + ($alerts -join ', ') + ' 超出阈值，必须打开图片肉眼确认是否为版面回归。')
    exit 1
}
Write-Output '结论: 全部在噪声基线内，无版面回归。'
exit 0
