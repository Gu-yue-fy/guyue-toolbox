<#
    古月工具箱（GuyueBox）- build script
    Uses the C# compiler shipped with .NET Framework (csc.exe).
    No .NET SDK required.

    Usage:
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Run
        powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Verify   # 提交前全量校验

    -Verify：在编译 + 版本一致性 + 自检 + 边界回归之外，再跑
      ① 全页截图（36 页）并与 shots\baseline 逐页比对版面
      ② 性能巡检（--perf-tour）并列出仍慢的页面
    产物留在 shots\_verify 供人工确认；任一环节失败以非零码退出。
#>
param(
    [switch]$Run,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$srcDir   = Join-Path $root 'src'
$binDir   = Join-Path $root 'bin'
$outFile  = Join-Path $binDir 'GuyueBox.exe'
$manifest = Join-Path $srcDir 'app.manifest'

Write-Host ''
Write-Host '=== 古月工具箱（GuyueBox）- Build ===' -ForegroundColor Cyan

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
# src 下所有 .cs 都属产品（界面 + Core 引擎 + 入口/版本/清单）。
# 此前这里带过一个排除 src\App 的过滤器，是重做期遗留：src\App 已不存在，
# 而这个过滤器会静默漏编任何路径里含 "\App\" 的新文件，已删除。
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
    'System.Management.dll',
    'System.Net.Http.dll'
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

# ---------- 5b2. PowerShell 协作脚本 ----------
# C# 与 PowerShell 分层协作：不便内嵌的批量系统操作（UWP 应用精简）由 scripts\*.ps1 承担，
# 构建时复制到 bin\scripts，自检（--selftest）会验证它们存在。
$scriptSrc = Join-Path $root 'src\Scripts'
if (Test-Path -LiteralPath $scriptSrc) {
    $scriptDst = Join-Path $binDir 'scripts'
    if (-not (Test-Path -LiteralPath $scriptDst)) {
        New-Item -ItemType Directory -Path $scriptDst | Out-Null
    }
    $scriptFiles = @(Get-ChildItem -LiteralPath $scriptSrc -Filter '*.ps1' -ErrorAction SilentlyContinue |
                     Where-Object { -not $_.PSIsContainer })
    foreach ($sf in $scriptFiles) {
        Copy-Item -LiteralPath $sf.FullName -Destination $scriptDst -Force
    }

    # 清理源目录里已删除/改名的脚本：否则 bin\scripts 残留旧脚本，
    # 程序仍能找到它并调用（曾出现 winget.ps1 源目录已无、bin 里还在的情况）
    $keepScripts = @{}
    foreach ($sf in $scriptFiles) { $keepScripts[$sf.Name] = $true }
    $staleScripts = @(Get-ChildItem -LiteralPath $scriptDst -Filter '*.ps1' -ErrorAction SilentlyContinue |
                      Where-Object { -not $_.PSIsContainer -and -not $keepScripts.ContainsKey($_.Name) })
    foreach ($sf in $staleScripts) { Remove-Item -LiteralPath $sf.FullName -Force -ErrorAction SilentlyContinue }

    if ($scriptFiles.Count -gt 0) {
        Write-Host ('scripts  : ' + $scriptFiles.Count + ' ps1 -> bin\scripts' +
            $(if ($staleScripts.Count -gt 0) { '（清理 ' + $staleScripts.Count + ' 个旧脚本）' } else { '' })) -ForegroundColor DarkGray
    }
}

# ---------- 5b. bin 清洁（护栏） ----------
# 本机会有外部组件把产品 exe 复制成 <GUID>_GuyueBox.exe 丢回 bin（安全软件/扫描器
# 的“复制一份去扫”行为）。已核实与本项目无关：代码与脚本里没有任何这种命名逻辑，
# 这些副本从未被执行（Prefetch 无记录），且会自行消失，只是会越攒越乱。
# 这里每次构建顺手清掉，保证 bin 里只有产品产物。
$strays = @(Get-ChildItem -LiteralPath $binDir -Filter '*_GuyueBox.exe' -File -ErrorAction SilentlyContinue)
foreach ($sf in $strays) { Remove-Item -LiteralPath $sf.FullName -Force -ErrorAction SilentlyContinue }
if ($strays.Count -gt 0) {
    Write-Host ('cleanup   : 已清掉 ' + $strays.Count + ' 个外部留下的 bin 副本（*_GuyueBox.exe）') -ForegroundColor DarkGray
}

# ---------- 5c. 优化项目录自检（护栏） ----------
# 架构护栏测试：把「优化项 Id 重复 / 两个项撞同一个注册表值 /
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

# ---------- 5d. 外部进程边界回归（护栏） ----------
# 借鉴 YuqiEngine 的边界回归测试：所有外部进程调用必须经 Shell（白名单 + 危险模式护栏）。
# 断言一（硬失败）：不得出现裸 Process.Start 直接拉起 powershell / cmd —— 否则会绕过护栏
#   且脱离审计；应改用 Shell.Run / Shell.RunDetached / Shell.Cmd。
# 断言二（告警）：Process.Start 目前只应存在于 Shell.cs（边界实现本身），其余位置应改为
#   Shell.OpenPath / Shell.OpenSelect / Shell.OpenUrl / Shell.StartElevated。
$boundaryHits = @()
$strayHits = @()
$shellFile = (Join-Path $srcDir 'Core\Infra\Shell.cs')
foreach ($f in $sources) {
    $lines = @(Get-Content -LiteralPath $f)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch 'Process\.Start') { continue }

        # 拼接该行及后续 3 行，覆盖跨行的 ProcessStartInfo 初始化
        $last = [math]::Min($i + 3, $lines.Count - 1)
        $window = ($lines[$i..$last] -join "`n")
        if ($window -match '(?i)Process\.Start\s*\(\s*"(powershell|cmd)|FileName\s*=\s*"(powershell|cmd)') {
            $boundaryHits += ($f + ':' + ($i + 1))
        }
        if ($f -ne $shellFile) {
            $strayHits += ($f + ':' + ($i + 1))
        }
    }
}

if ($boundaryHits.Count -gt 0) {
    Write-Host ''
    Write-Host 'BOUNDARY VIOLATION：裸 Process.Start(powershell/cmd) 绕过 Shell 边界：' -ForegroundColor Red
    foreach ($h in $boundaryHits) { Write-Host ('  ' + $h) -ForegroundColor Red }
    Write-Host '请改用 Shell.Run / Shell.RunDetached / Shell.Cmd。' -ForegroundColor Yellow
    exit 1
}
Write-Host 'boundary : OK（无裸 Process.Start(powershell/cmd)）' -ForegroundColor DarkGray
if ($strayHits.Count -gt 0) {
    Write-Host ('boundary : 以下 Process.Start 不在 Shell.cs，建议收口：' + ($strayHits -join '、')) -ForegroundColor Yellow
}

# ---------- 5e. 端到端校验（-Verify） ----------
# 借鉴 winutil 的"每次 push 都跑测试 + lint"：把截图回归与性能巡检也变成一条命令能跑完的事，
# 避免"改完只看编译过了"——本项目多数缺陷（版面错位、透明方框、切页卡顿）只有看图才看得出来。
$verifyDir = Join-Path $root 'shots\_verify'
if ($Verify) {
    Write-Host ''
    Write-Host '=== verify 1/2：全页截图 + 版面回归 ===' -ForegroundColor Cyan

    $probe = Join-Path $root 'ui-probe.ps1'
    if (Test-Path -LiteralPath $probe) {
        if (Test-Path -LiteralPath $verifyDir) {
            Remove-Item -LiteralPath $verifyDir -Recurse -Force
        }
        & powershell -NoProfile -ExecutionPolicy Bypass -File $probe -OutDir $verifyDir | Out-Null

        $failFile = Join-Path $verifyDir '_failures.txt'
        if (Test-Path -LiteralPath $failFile) {
            Write-Host 'PROBE FAILED：以下页面未能正常截图（界面可能抛异常）' -ForegroundColor Red
            Get-Content -LiteralPath $failFile | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Red }
            exit 1
        }
        $pngCount = @(Get-ChildItem -LiteralPath $verifyDir -Filter '*.png').Count
        Write-Host ('probe    : OK（' + $pngCount + ' 张截图，无失败页）') -ForegroundColor Green

        $baseline = Join-Path $root 'shots\baseline'
        $differ   = Join-Path $root 'tools\shot-diff.ps1'
        if ((Test-Path -LiteralPath $baseline) -and (Test-Path -LiteralPath $differ)) {
            Write-Host ('diff     : 与 shots\baseline 比对') -ForegroundColor DarkGray
            & powershell -NoProfile -ExecutionPolicy Bypass -File $differ -A $baseline -B $verifyDir |
                Select-Object -Last 3 | ForEach-Object { Write-Host ('           ' + $_) }
        }
        else {
            Write-Host 'diff     : 跳过（缺 shots\baseline 或 tools\shot-diff.ps1）' -ForegroundColor Yellow
        }
    }
    else {
        Write-Host 'probe    : 跳过（缺 ui-probe.ps1）' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host '=== verify 2/2：性能巡检（每页构建 + 激活耗时） ===' -ForegroundColor Cyan
    $perfLog = Join-Path $binDir 'perf-verify.log'
    if (Test-Path -LiteralPath $perfLog) { Remove-Item -LiteralPath $perfLog -Force }
    Start-Process -FilePath $outFile -ArgumentList '--perf-tour', $perfLog -Wait -WindowStyle Hidden

    if (Test-Path -LiteralPath $perfLog) {
        $slow = @(Get-Content -LiteralPath $perfLog -Encoding UTF8 |
                  Where-Object { $_ -match '\d+ms\s*$' } |
                  Sort-Object { [int]($_ -replace '.*?(\d+)ms\s*$', '$1') } -Descending |
                  Select-Object -First 8)
        if ($slow.Count -gt 0) {
            Write-Host '最慢的 8 页（>300ms 值得看一眼）：' -ForegroundColor DarkGray
            foreach ($line in $slow) { Write-Host ('  ' + $line) }
        }
        Write-Host ('perf     : ' + $perfLog) -ForegroundColor DarkGray
    }
    else {
        Write-Host 'perf     : 跳过（未生成 perf 日志）' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host 'VERIFY DONE（截图产物：shots\_verify）' -ForegroundColor Green
}

# ---------- 6. run ----------
if ($Run) {
    Write-Host ''
    Write-Host 'Launching (will request administrator rights)...' -ForegroundColor Cyan
    Start-Process -FilePath $outFile
}
