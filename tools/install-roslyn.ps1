<#
    升级 C# 构建链：拉取 Roslyn 编译器（Microsoft.Net.Compilers.Toolset），
    让 build.ps1 能用现代 C#（C# 9+，表达式体 / 字符串插值 / 空条件 / 记录等）。
    仅当 tools\roslyn\csc.exe 不存在时才下载，可重复运行、可离线回退到旧 csc。

    用法：
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-roslyn.ps1
#>
param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$nuget = Join-Path $root 'nuget.exe'
if (-not (Test-Path -LiteralPath $nuget)) {
    Write-Host 'downloading nuget.exe ...' -ForegroundColor Cyan
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' `
        -OutFile $nuget -UseBasicParsing
}

$roslynDir = Join-Path $root 'roslyn'
$csc = Join-Path $roslynDir 'csc.exe'
if (Test-Path -LiteralPath $csc) {
    Write-Host ('Roslyn csc already present -> ' + $csc) -ForegroundColor Green
    exit 0
}

$tmp = Join-Path $root 'roslyn_pkg'
Write-Host 'restoring Microsoft.Net.Compilers.Toolset (Roslyn) ...' -ForegroundColor Cyan
& $nuget install Microsoft.Net.Compilers.Toolset -Version 4.8.0 -OutputDirectory $tmp -ExcludeVersion

# Roslyn 的 csc 位于 <pkg>\tasks\net472\（csc.exe + 全部依赖 dll）；
# 整目录复制到 tools\roslyn 以保持同目录依赖，使 tools\roslyn\csc.exe 可直接调用
$pkgTools = Join-Path $tmp 'Microsoft.Net.Compilers.Toolset' | Join-Path -ChildPath 'tasks' | Join-Path -ChildPath 'net472'
if (Test-Path -LiteralPath $roslynDir) { Remove-Item -LiteralPath $roslynDir -Recurse -Force }
New-Item -ItemType Directory -Path $roslynDir | Out-Null
Copy-Item -Path (Join-Path $pkgTools '*') -Destination $roslynDir -Recurse -Force

if (Test-Path -LiteralPath $csc) {
    Write-Host ('Roslyn installed -> ' + $csc) -ForegroundColor Green
}
else {
    Write-Host 'Roslyn csc not found after restore. Build will fall back to framework csc (C# 5).' -ForegroundColor Yellow
    exit 1
}
