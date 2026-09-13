param(
    [string]$SourcePath = "",
    [string]$Ico = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
if (-not $Ico) { $Ico = Join-Path $root 'src\Assets\app.ico' }

$srcImage = [System.Drawing.Image]::FromFile($SourcePath)
$sizes = @(256, 64, 48, 32, 16)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $g.DrawImage($srcImage, $rect)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($s, $ms.ToArray())
    $bmp.Dispose()
}
$srcImage.Dispose()

$fs = [System.IO.File]::Create($Ico)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]
    $dim = 0
    if ($s -lt 256) { $dim = $s }
    $data = $f[1]
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) {
    $bw.Write($f[1])
}
$bw.Flush()
$bw.Dispose()

Write-Host ("ico: " + (Get-Item $Ico).Length + " bytes, frames: " + $frames.Count)
