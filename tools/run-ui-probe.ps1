<#
    Build and run the UI probe: shows the real window, screenshots every page,
    and captures unhandled exceptions. Development-only, changes nothing on the system.
#>
$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $root 'src'
$binDir = Join-Path $root 'bin'
$out    = Join-Path $binDir 'UiProbe.exe'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$fxDir = Split-Path -Parent $csc
$q = [char]34

if (-not (Test-Path -LiteralPath $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

$sources = @(Get-ChildItem -LiteralPath $srcDir -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName })
$sources += (Join-Path $root 'tools\UiProbe.cs')

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
$a += '/main:GuyueBox.Test.UiProbe'
$a += ('/out:' + $q + $out + $q)
$a += $refs
foreach ($s in $sources) { $a += ($q + $s + $q) }

$build = & $csc $a 2>&1
if ($build) { $build | ForEach-Object { Write-Host $_ } }
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Probe build FAILED.' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ''
if ($args.Count -gt 0 -and $args[0] -eq 'full') {
    Write-Host 'Running UI probe (FULL: screenshots + deep checks)...' -ForegroundColor Cyan
    & $out full
} else {
    Write-Host 'Running UI probe (fast: no screenshots)...' -ForegroundColor Cyan
    & $out
}
exit $LASTEXITCODE
