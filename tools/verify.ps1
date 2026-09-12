<#
    一键验证流水线：编译 → 冒烟测试 → 快速 UI 探针（无截图）→ 启动。
    每轮改动后只需跑这一个脚本。发版前可另跑 run-ui-probe.ps1 full 取截图。
#>
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot

Write-Host ''
Write-Host '=== 1/3 编译 ===' -ForegroundColor Cyan
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '编译失败，终止。' -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host '=== 2/3 冒烟测试 ===' -ForegroundColor Cyan
& (Join-Path $root 'tools\run-smoke-test.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '冒烟失败，终止。' -ForegroundColor Red; exit 1 }

Write-Host ''
Write-Host '=== 3/3 UI 探针（快速） ===' -ForegroundColor Cyan
& (Join-Path $root 'tools\run-ui-probe.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '探针异常，终止。' -ForegroundColor Red; exit 1 }

Get-Process -Name GuyueBox -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not $NoStart) {
    Start-Process -FilePath (Join-Path $root 'bin\GuyueBox.exe')
    Write-Host ''
    Write-Host '已启动 GuyueBox.exe' -ForegroundColor Green
}
