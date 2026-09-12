<#
    System Optimization Toolbox - build script
    Uses the C# compiler shipped with .NET Framework (csc.exe).
    No .NET SDK required.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Run
#>
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$srcDir   = Join-Path $root 'src'
$binDir   = Join-Path $root 'bin'
$outFile  = Join-Path $binDir 'GuyueBox.exe'
$manifest = Join-Path $srcDir 'app.manifest'

Write-Host ''
Write-Host '=== System Optimization Toolbox - Build ===' -ForegroundColor Cyan

# ---------- 1. locate compiler ----------
$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)

$csc = $null
foreach ($c in $candidates) {
    if (Test-Path -LiteralPath $c) { $csc = $c; break }
}

if (-not $csc) {
    Write-Host 'csc.exe not found. Please make sure .NET Framework 4.x is installed.' -ForegroundColor Red
    exit 1
}

$fxDir = Split-Path -Parent $csc
Write-Host ('compiler : ' + $csc) -ForegroundColor DarkGray

# ---------- 2. collect sources ----------
$sources = @(Get-ChildItem -LiteralPath $srcDir -Recurse -Filter '*.cs' |
             Sort-Object FullName |
             ForEach-Object { $_.FullName })

if ($sources.Count -eq 0) {
    Write-Host ('No .cs file found under ' + $srcDir) -ForegroundColor Red
    exit 1
}

Write-Host ('sources  : ' + $sources.Count + ' files') -ForegroundColor DarkGray

# ---------- 3. output directory ----------
if (-not (Test-Path -LiteralPath $binDir)) {
    New-Item -ItemType Directory -Path $binDir | Out-Null
}

# ---------- 4. references and switches ----------
$refNames = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Management.dll'
)

$refs = @()
foreach ($name in $refNames) {
    $full = Join-Path $fxDir $name
    if (Test-Path -LiteralPath $full) {
        $refs += ('/r:' + [char]34 + $full + [char]34)
    }
}

$q = [char]34

$cscArgs = @()
$cscArgs += '/nologo'
$cscArgs += '/noconfig'
$cscArgs += '/target:winexe'
$cscArgs += '/platform:anycpu'

$iconFile = Join-Path $srcDir 'Assets\app.ico'
if (Test-Path -LiteralPath $iconFile) {
    $cscArgs += ('/win32icon:' + [char]34 + $iconFile + [char]34)
}
$cscArgs += '/langversion:5'
$cscArgs += '/optimize+'
$cscArgs += '/warn:4'
# 65001 = UTF-8, so Chinese literals in sources are read correctly
$cscArgs += '/codepage:65001'
$cscArgs += ('/out:' + $q + $outFile + $q)
$cscArgs += ('/win32manifest:' + $q + $manifest + $q)
$cscArgs += $refs
foreach ($s in $sources) { $cscArgs += ($q + $s + $q) }

# ---------- 5. compile ----------
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$output = & $csc $cscArgs 2>&1
$code = $LASTEXITCODE
$sw.Stop()

if ($output) { $output | ForEach-Object { Write-Host $_ } }

if ($code -ne 0) {
    Write-Host ''
    Write-Host ('BUILD FAILED (exit code ' + $code + ')') -ForegroundColor Red
    exit $code
}

Write-Host ''
Write-Host ('BUILD OK : ' + $outFile) -ForegroundColor Green
Write-Host ('elapsed  : ' + [math]::Round($sw.Elapsed.TotalSeconds, 2) + ' s') -ForegroundColor DarkGray

if (Test-Path -LiteralPath $outFile) {
    $size = (Get-Item -LiteralPath $outFile).Length
    Write-Host ('size     : ' + [math]::Round($size / 1KB, 1) + ' KB') -ForegroundColor DarkGray
}

# ---------- 6. run ----------
if ($Run) {
    Write-Host ''
    Write-Host 'Launching (will request administrator rights)...' -ForegroundColor Cyan
    Start-Process -FilePath $outFile
}
