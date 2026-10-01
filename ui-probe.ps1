# ============================================================
# UI 截图探针（自动模式）
# 说明：截图由程序自身完成——bin\GuyueBox.exe --shots <目录>
#       启动后逐页切换、含多页签页的每个页签，用 PrintWindow 离屏渲染
#       保存 PNG，全部拍完自动退出。本脚本只是调用方，不模拟任何
#       鼠标/键盘输入（旧的 SetCursorPos + mouse_event 方案已废弃）。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File ui-probe.ps1 [输出目录]
# 谁跟它配合：截完两组后用 tools\shot-diff.ps1 出像素差分报告（含噪声判据与退出码）
# ============================================================
param([string]$OutDir)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'bin\GuyueBox.exe'

if (-not (Test-Path $exe)) { throw ('未找到程序：' + $exe + '，请先运行 build.ps1') }
if (-not $OutDir) { $OutDir = Join-Path $root 'shots\auto' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --shots 用独立互斥体名，因此本机已有正常实例在跑也不影响截图
$proc = Start-Process -FilePath $exe -ArgumentList '--shots', $OutDir -PassThru
if (-not $proc.WaitForExit(300000)) {
    try { $proc.Kill() } catch { }
    throw '截图超时（300s），已终止进程'
}
if ($proc.ExitCode -ne 0) { throw ('截图进程退出码 ' + $proc.ExitCode) }

Get-ChildItem $OutDir -File | Sort-Object Name | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Output ('截图完成：' + $OutDir)
