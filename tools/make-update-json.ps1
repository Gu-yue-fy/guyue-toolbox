# 生成仓库根目录的 update.json（更新检查源）。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-update-json.ps1 -Version 1.2.0 -Notes "..."
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = '',
    [string]$ExePath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = if ($ExePath) { $ExePath } else { Join-Path $root 'bin\GuyueBox.exe' }
if (-not (Test-Path $exe)) { Write-Host 'GuyueBox.exe not found, run build.ps1 first.' -ForegroundColor Red; exit 1 }

$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$safeVer = $Version -replace '[^0-9A-Za-z.\-]', ''
$safeNotes = $Notes -replace '[\\"]', ' '
$url = 'https://github.com/Gu-yue-fy/guyue-toolbox/releases/latest/download/GuyueBox.exe'

$json = "{`n" +
    '  "version": "' + $safeVer + '",' + "`n" +
    '  "url": "' + $url + '",' + "`n" +
    '  "sha256": "' + $sha + '",' + "`n" +
    '  "notes": "' + $safeNotes + '"' + "`n" +
    "}`n"

# 仓库根目录（客户端轮询的更新源）；同时留一份在 bin\release
$out1 = Join-Path $root 'update.json'
$out2 = Join-Path $root 'bin\release\update.json'
$enc = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($out1, $json, $enc)
if (-not (Test-Path (Split-Path $out2))) { New-Item -ItemType Directory -Path (Split-Path $out2) | Out-Null }
[System.IO.File]::WriteAllText($out2, $json, $enc)

Write-Host ''
Write-Host '=== update.json generated ===' -ForegroundColor Cyan
Write-Host ("version : " + $safeVer)
Write-Host ("sha256  : " + $sha)
Write-Host ("output  : " + $out1)
Write-Host 'next: commit update.json to repo root, upload GuyueBox.exe to the release assets.'
