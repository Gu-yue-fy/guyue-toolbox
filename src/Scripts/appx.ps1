# 古月工具箱 · UWP 应用精简协作脚本（appx.ps1）
# list: 输出 [{Name,FullName,Category}]，仅"可安全移除"/"谨慎"两类
# remove: -Pkgs "名或全名;..." 仅卸载当前用户级包，输出 {Removed:[],Failed:[{FullName,Error}]}
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File appx.ps1 -Action <list|remove> [-Pkgs "a;b"]
# 约定: stdout 仅输出严格 JSON(UTF-8 无注释)；异常输出 {"Error":"..."} 且 exit 1
param(
    [Parameter(Mandatory=$true)][ValidateSet('list','remove')][string]$Action,
    [string]$Pkgs = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Write-Json($data) {
    Write-Output (ConvertTo-Json -InputObject $data -Depth 6 -Compress)
}

function Test-Pattern($name, $pattern) {
    if ($pattern.Contains('*')) { return ($name -like $pattern) }
    return ($name -like ('*' + $pattern + '*'))
}

# 中文常量用 Unicode 码点构造（PowerShell 5.1 读无 BOM 文件按 ANSI，直接写中文会乱码）
$CatSafe = -join [char[]](0x53EF,0x5B89,0x5168,0x79FB,0x9664)                                # 可安全移除
$CatCaution = -join [char[]](0x8C28,0x614E)                                                 # 谨慎
$ErrNoUserPkg = -join [char[]](0x672A,0x627E,0x5230,0x5F53,0x524D,0x7528,0x6237,0x7EA7,0x5305)  # 未找到当前用户级包

$Safe = @(
    'Microsoft.Xbox*', 'Microsoft.GamingApp', 'BingNews', 'BingWeather',
    'Microsoft.GetHelp', 'Microsoft.Getstarted', 'Microsoft.MicrosoftOfficeHub',
    'Microsoft.People', 'Microsoft.MicrosoftSolitaireCollection',
    'Microsoft.MixedReality.Portal', 'Microsoft.YourPhone', 'Microsoft.ZuneMusic',
    'Microsoft.ZuneVideo', 'Microsoft.Clipchamp', 'MicrosoftTeams',
    'MicrosoftCorporationII.MicrosoftFamily', 'Microsoft.DevHome',
    'Microsoft.PowerAutomateDesktop', 'Microsoft.Todos', 'Microsoft.WindowsFeedbackHub',
    'Microsoft.WindowsMaps', 'Microsoft.549981C3F5F10', 'Microsoft.BingSearch',
    'Microsoft.Wallet', 'Microsoft.OneConnect', 'Microsoft.SkypeApp', 'king.com*',
    'Microsoft.Microsoft3DViewer', 'Microsoft.WindowsAlarms',
    'Microsoft.WindowsSoundRecorder', 'Microsoft.MicrosoftJournal',
    'Microsoft.Office.OneNote', 'Microsoft.Advertising.Xaml', 'Microsoft.BingTranslator'
)

$Caution = @(
    'Microsoft.WindowsCamera', 'Microsoft.Windows.Photos',
    'Microsoft.WindowsCommunicationsApps', 'Microsoft.OutlookForWindows',
    'Microsoft.MicrosoftStickyNotes', 'Microsoft.GamingServices',
    'Microsoft.StorePurchaseApp'
)

# 系统组件黑名单（PackageFullName 前缀）；分类命中优先于本名单
$Exclude = @(
    'Microsoft.Windows.', 'Microsoft.WinAppRuntime.', 'Microsoft.VCLibs.',
    'Microsoft.UI.Xaml.', 'Microsoft.WindowsAppRuntime.', 'Microsoft.StoreExperienceHost',
    'Microsoft.StartMenuExperienceHost', 'Microsoft.ShellExperienceHost',
    'MicrosoftWindows.Client.', 'Microsoft.WindowsTerminal', 'Microsoft.Paint',
    'Microsoft.WindowsCalculator', 'Microsoft.WindowsNotepad', 'Microsoft.ScreenSketch',
    'Microsoft.Windows.Photos', 'MicrosoftWindows.CrossDevice',
    'Microsoft.HEIFImageExtension', 'Microsoft.WebpImageExtension',
    'Microsoft.VP9VideoExtensions', 'Microsoft.MPEG2VideoExtension',
    'Microsoft.RawImageExtension'
)

function Get-Category($name, $full) {
    foreach ($p in $Safe) {
        if ((Test-Pattern $name $p) -or (Test-Pattern $full $p)) { return $CatSafe }
    }
    foreach ($p in $Caution) {
        if ((Test-Pattern $name $p) -or (Test-Pattern $full $p)) { return $CatCaution }
    }
    return $null
}

function Test-Excluded($pkg) {
    $fn = $pkg.PackageFullName
    foreach ($pre in $Exclude) {
        if ($fn.StartsWith($pre, [System.StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    $name = $pkg.Name
    if ($name -like 'Windows*' -or $name -like 'Microsoft Edge*') { return $true }
    if ($null -ne $pkg.SignatureKind -and ($pkg.SignatureKind.ToString() -eq 'System')) { return $true }
    return $false
}

try {
    if ($Action -eq 'list') {
        $items = @()
        foreach ($pkg in Get-AppxPackage) {
            $cat = Get-Category $pkg.Name $pkg.PackageFullName
            if ($cat) {
                $items += [pscustomobject]@{
                    Name = $pkg.Name
                    FullName = $pkg.PackageFullName
                    Category = $cat
                }
                continue
            }
            # 未命中分类：一律不输出（前缀黑名单/名称/签名启发式兜底排查系统组件）
            if (Test-Excluded $pkg) { continue }
        }
        $items = @($items | Sort-Object Name, FullName)
        Write-Json $items
        exit 0
    }

    # remove：仅当前用户级包（Get-AppxPackage 不带 -AllUsers）
    $Removed = @()
    $Failed = @()
    $tokens = @($Pkgs -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    foreach ($t in $tokens) {
        $found = @(Get-AppxPackage -Name $t -ErrorAction SilentlyContinue)
        if ($found.Count -eq 0) { $found = @(Get-AppxPackage | Where-Object { $_.PackageFullName -eq $t }) }
        if ($found.Count -eq 0) {
            $Failed += [pscustomobject]@{ FullName = $t; Error = $ErrNoUserPkg }
            continue
        }
        foreach ($p in $found) {
            try {
                Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop | Out-Null
                $Removed += $p.PackageFullName
            } catch {
                $Failed += [pscustomobject]@{ FullName = $p.PackageFullName; Error = $_.Exception.Message }
            }
        }
    }
    Write-Json ([pscustomobject]@{ Removed = $Removed; Failed = $Failed })
    exit 0
}
catch {
    Write-Json ([pscustomobject]@{ Error = $_.Exception.Message })
    exit 1
}