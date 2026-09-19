$admin = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Output ("elevated: " + $admin)
Write-Output ("roslyn: " + (Test-Path 'c:\Users\Administrator\CodeBuddy\系统优化工具箱\tools\roslyn\csc.exe'))
Get-ChildItem 'c:\Users\Administrator\CodeBuddy\系统优化工具箱\bin' | ForEach-Object { Write-Output ("bin: " + $_.Name + "  " + $_.Length) }
$log = 'c:\Users\Administrator\CodeBuddy\系统优化工具箱\bin\rail_diag.log'
if (Test-Path $log) {
    Write-Output '--- rail_diag.log (tail 30) ---'
    Get-Content $log -Tail 30
}
