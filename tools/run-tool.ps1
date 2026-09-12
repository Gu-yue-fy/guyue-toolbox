<#
    Generic build-and-run helper for development tools under tools\.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-tool.ps1 -Tool FontDiag
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\run-tool.ps1 -Tool UiProbe
#>
param(
    [Parameter(Mandatory = $true)][string]$Tool
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'src'
$binDir = Join-Path $root 'bin'
$file   = Join-Path $root ('tools\' + $Tool + '.cs')
$out    = Join-Path $binDir ($Tool + '.exe')

if (-not (Test-Path -LiteralPath $file)) {
    Write-Host ('Source not found: ' + $file) -ForegroundColor Red
    exit 1
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$fxDir = Split-Path -Parent $csc
$q = [char]34

if (-not (Test-Path -LiteralPath $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

$sources = @(Get-ChildItem -LiteralPath $srcDir -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += $file

$refs = @()
foreach ($n in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll')) {
    $full = Join-Path $fxDir $n
    if (Test-Path -LiteralPath $full) { $refs += ('/r:' + $q + $full + $q) }
}

$a = @()
$a += '/nologo'
$a += '/noconfig'
$a += '/target:exe'
$a += '/platform:anycpu'
$a += '/langversion:5'
$a += '/codepage:65001'
$a += ('/main:GuyueBox.Test.' + $Tool)
$a += ('/out:' + $q + $out + $q)
$a += $refs
foreach ($s in $sources) { $a += ($q + $s + $q) }

$build = & $csc $a 2>&1
if ($build) { $build | ForEach-Object { Write-Host $_ } }
if ($LASTEXITCODE -ne 0) {
    Write-Host ('Build FAILED for ' + $Tool) -ForegroundColor Red
    exit $LASTEXITCODE
}

& $out
exit $LASTEXITCODE
