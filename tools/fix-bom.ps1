# ============================================================
# Make the given .ps1 files UTF-8 **with BOM**.
#
# Why: PowerShell 5.1 reads BOM-less scripts as ANSI; Chinese comments/strings
# then turn into mojibake and can even break parsing ("string is missing the
# terminator"). Editors / patch tools that write UTF-8 without BOM silently
# reintroduce this, so run this after touching any .ps1 with Chinese text.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\fix-bom.ps1 -Files build.ps1,ui-probe.ps1
# ============================================================
param([string[]]$Files = @())

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$changed = 0

# powershell -File 会把 "a.ps1,b.ps1" 整串当成一个参数，这里自行按逗号拆开
$list = @()
foreach ($item in $Files) {
    foreach ($part in ([string]$item -split ',')) {
        $t = $part.Trim()
        if ($t) { $list += $t }
    }
}

foreach ($f in $list) {
    if (-not (Test-Path -LiteralPath $f)) {
        Write-Host ('skip (not found): ' + $f) -ForegroundColor DarkGray
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.IO.File]::ReadAllText($f, $utf8NoBom)
    if ($hasBom) {
        Write-Host ((Split-Path -Leaf $f) + '  already has BOM') -ForegroundColor DarkGray
        continue
    }
    [System.IO.File]::WriteAllText($f, $text, $utf8Bom)
    $changed++
    Write-Host ((Split-Path -Leaf $f) + '  -> UTF-8 with BOM') -ForegroundColor Green
}

Write-Host ('fixed: ' + $changed + ' file(s)') -ForegroundColor Green
