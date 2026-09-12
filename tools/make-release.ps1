<#
    Release builder: build -> hash -> generate update.json (upload both to your host).

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 1.1.0 -Notes "..." -BaseUrl "https://your-host/downloads"

    Output (bin\release\):
        GuyueBox.exe   new binary (upload to the folder BaseUrl points to)
        update.json      upload as <your update source>/update.json
        SHA256.txt       checksum record
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = '',
    # 默认走 GitHub Release 稳定直链：发版时把 GuyueBox.exe 传到该版本 Release 的 assets 即可
    [string]$BaseUrl = 'https://github.com/Gu-yue-fy/guyue-toolbox/releases/latest/download'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 1. build
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED, abort release.' -ForegroundColor Red; exit 1 }

$exe = Join-Path $root 'bin\GuyueBox.exe'
if (-not (Test-Path $exe)) { Write-Host 'Binary not found.' -ForegroundColor Red; exit 1 }

# 2. hash
$sha = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()

# 3. output dir
$outDir = Join-Path $root 'bin\release'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
Copy-Item -LiteralPath $exe (Join-Path $outDir 'GuyueBox.exe') -Force

# 4. update.json (pure ASCII safe)
$downloadUrl = if ($BaseUrl) { ($BaseUrl.TrimEnd('/')) + '/GuyueBox.exe' } else { '' }
$safeVer = $Version -replace '[^0-9A-Za-z.\-]', ''
$safeNotes = $Notes -replace '[\\"]', ' '
$safeUrl = $downloadUrl -replace '[\\"]', ' '

$json = "{`n" +
    '  "version": "' + $safeVer + '",' + "`n" +
    '  "url": "' + $safeUrl + '",' + "`n" +
    '  "sha256": "' + $sha + '",' + "`n" +
    '  "notes": "' + $safeNotes + '"' + "`n" +
    "}`n"

$jsonPath = Join-Path $outDir 'update.json'
[System.IO.File]::WriteAllText($jsonPath, $json, (New-Object System.Text.UTF8Encoding($false)))

$shaPath = Join-Path $outDir 'SHA256.txt'
[System.IO.File]::WriteAllText($shaPath, $sha + '  GuyueBox.exe', (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host '=== Release ready ===' -ForegroundColor Cyan
Write-Host ("Version : " + $safeVer)
Write-Host ("SHA256  : " + $sha)
Write-Host ("Folder  : " + $outDir)
Write-Host ''
if (-not $BaseUrl) {
    Write-Host 'NOTE: no -BaseUrl given, "url" in update.json is empty - fill it before uploading.' -ForegroundColor Yellow
}
Write-Host 'Next steps:'
Write-Host '  1. Upload GuyueBox.exe to your download location (must match "url" in update.json)'
Write-Host '  2. Upload update.json to your update source (what clients point to)'
Write-Host '  3. Old clients will get the update prompt automatically on next launch'
Write-Host ''
