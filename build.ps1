<#
    古月工具包（GuyueBox）- build script
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
Write-Host '=== 古月工具包（GuyueBox）- Build ===' -ForegroundColor Cyan

# ---------- 1. locate compiler ----------
# 优先 Roslyn（tools\roslyn\csc.exe，由 tools\install-roslyn.ps1 一次性安装），
# 以启用现代 C#（字符串插值 / 空条件 / 模式匹配 / 记录等）；
# 未安装时回退到 .NET Framework 自带编译器（仅支持 C# 5）。
$roslynCsc = Join-Path $root 'tools\roslyn\csc.exe'
$useRoslyn = Test-Path -LiteralPath $roslynCsc

$csc = $null
if ($useRoslyn) {
    $csc = $roslynCsc
}
else {
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { $csc = $c; break }
    }
}

if (-not $csc) {
    Write-Host 'csc.exe not found. Run tools\install-roslyn.ps1 or install .NET Framework 4.x.' -ForegroundColor Red
    exit 1
}

# 引用程序集目录：framework csc 本身就在框架目录里，取其所在目录即可；
# Roslyn 独立安装，必须显式指向 .NET Framework 目录才能找到 System.dll 等引用
if ($useRoslyn) {
    $fxDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path -LiteralPath $fxDir)) {
        $fxDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    }
}
else {
    $fxDir = Split-Path -Parent $csc
}

$compilerTag = if ($useRoslyn) { '  (Roslyn, modern C#)' } else { '  (legacy, C# 5)' }
Write-Host ('compiler : ' + $csc + $compilerTag) -ForegroundColor DarkGray

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
if ($useRoslyn) { $cscArgs += '/langversion:latest' } else { $cscArgs += '/langversion:5' }
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

# ---------- 5a. version consistency ----------
# 版本号唯一来源是 src\AppInfo.cs；这里校验它与发版清单 update.json 是否一致。
# 不一致只警告不阻断（开发途中允许 update.json 暂时落后于代码），
# 但它能拦住"改了代码版本却忘了重新生成 update.json"这类发布事故。
$infoFile = Join-Path $srcDir 'AppInfo.cs'
$updFile  = Join-Path $root 'update.json'
$codeVer  = $null
if (Test-Path -LiteralPath $infoFile) {
    if ((Get-Content -LiteralPath $infoFile -Raw) -match 'Version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"') { $codeVer = $Matches[1] }
}
$jsonVer = $null
if (Test-Path -LiteralPath $updFile) {
    if ((Get-Content -LiteralPath $updFile -Raw) -match '"version"\s*:\s*"([^"]+)"') { $jsonVer = $Matches[1] }
}
if ($codeVer -and $jsonVer -and ($codeVer -ne $jsonVer)) {
    Write-Host ('version  : MISMATCH  code=' + $codeVer + '  update.json=' + $jsonVer) -ForegroundColor Yellow
    Write-Host '           run: powershell -File tools\make-update-json.ps1 -Version <new> -Notes "..."' -ForegroundColor Yellow
}
elseif ($codeVer) {
    # 注意：PowerShell 不允许 (if ...) 直接当表达式用（会报 "if 不是 cmdlet"），
    # 必须先赋值再拼接
    $tail = if ($jsonVer) { ' (AppInfo.cs = update.json)' } else { ' (AppInfo.cs)' }
    Write-Host ('version  : ' + $codeVer + $tail) -ForegroundColor DarkGray
}

# ---------- 5b. tweak packs ----------
# 仓库根目录 packs\*.json 是外部优化包清单示例/内置包，复制到 bin\packs 供程序启动时装载
$packSrc = Join-Path $root 'packs'
if (Test-Path -LiteralPath $packSrc) {
    $packDst = Join-Path $binDir 'packs'
    if (-not (Test-Path -LiteralPath $packDst)) {
        New-Item -ItemType Directory -Path $packDst | Out-Null
    }
    $packFiles = @(Get-ChildItem -LiteralPath $packSrc -Filter '*.json' -ErrorAction SilentlyContinue |
                   Where-Object { -not $_.PSIsContainer })
    foreach ($pf in $packFiles) {
        Copy-Item -LiteralPath $pf.FullName -Destination $packDst -Force
    }

    # 清理源目录里已不存在的旧包：否则删掉或改名的包会残留在 bin\packs，
    # 程序启动时仍会装载它——表现为"已经删掉的包还在生效"。
    $keep = @{}
    foreach ($pf in $packFiles) { $keep[$pf.Name] = $true }
    $stale = @(Get-ChildItem -LiteralPath $packDst -Filter '*.json' -ErrorAction SilentlyContinue |
               Where-Object { -not $_.PSIsContainer -and -not $keep.ContainsKey($_.Name) })
    foreach ($sf in $stale) { Remove-Item -LiteralPath $sf.FullName -Force -ErrorAction SilentlyContinue }

    if ($packFiles.Count -gt 0) {
        Write-Host ('packs    : ' + $packFiles.Count + ' file(s) -> bin\packs' +
            $(if ($stale.Count -gt 0) { ' (removed ' + $stale.Count + ' stale)' } else { '' })) -ForegroundColor DarkGray
    }
}

# ---------- 5c. 优化项目录自检（护栏） ----------
# 借鉴宇奇引擎的架构护栏测试：把「优化项 Id 重复 / 两个项撞同一个注册表值 /
# 优化项无法还原」这类问题变成构建期可发现的问题。
# 程序清单声明 requireAdministrator，因此只在**已经提权**时自动跑（不会再弹 UAC 打断构建）；
# 未提权时跳过，可自行运行 GuyueBox.exe --selftest 查看。
$isAdmin = (New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ($isAdmin) {
    $selfTestLog = Join-Path $binDir 'selftest.log'
    if (Test-Path -LiteralPath $selfTestLog) { Remove-Item -LiteralPath $selfTestLog -Force }
    $st = Start-Process -FilePath $outFile -ArgumentList '--selftest' -Wait -PassThru -WindowStyle Hidden
    $stText = if (Test-Path -LiteralPath $selfTestLog) { Get-Content -LiteralPath $selfTestLog -Raw } else { '(未生成日志)' }
    if ($st.ExitCode -ne 0) {
        Write-Host ''
        Write-Host $stText
        Write-Host 'SELFTEST FAILED' -ForegroundColor Red
        exit 1
    }
    Write-Host ('selftest  : PASS') -ForegroundColor Green
}
else {
    Write-Host 'selftest  : skipped (需要管理员权限；可手动运行 GuyueBox.exe --selftest)' -ForegroundColor DarkGray
}

# ---------- 6. run ----------
if ($Run) {
    Write-Host ''
    Write-Host 'Launching (will request administrator rights)...' -ForegroundColor Cyan
    Start-Process -FilePath $outFile
}
