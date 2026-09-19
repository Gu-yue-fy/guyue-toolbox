Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class ZT {
    public struct PT { public int X; public int Y; }
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(PT p);
}
"@
function Get-TopAt([System.Windows.Forms.Form]$form, [int]$cx, [int]$cy, [System.Windows.Forms.Control[]]$ctrls) {
    $pt = $form.PointToScreen((New-Object System.Drawing.Point($cx, $cy)))
    $p = New-Object ZT+PT
    $p.X = $pt.X; $p.Y = $pt.Y
    $h = [ZT]::WindowFromPoint($p)
    $c = [System.Windows.Forms.Control]::FromHandle($h)
    foreach ($x in $ctrls) { if ($c -eq $x) { return $x.Name } }
    if ($c -eq $form) { return "FORM" }
    if ($c -eq $null) { return "NULL" }
    return $c.Name
}
$f = New-Object System.Windows.Forms.Form
$f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$f.Location = New-Object System.Drawing.Point(50, 50)
$f.ClientSize = New-Object System.Drawing.Size(400, 400)
$f.ShowInTaskbar = $false
$a = New-Object System.Windows.Forms.Panel
$a.Name = 'A'
$a.SetBounds(0, 0, 300, 300)
$a.BackColor = [System.Drawing.Color]::Red
$f.Controls.Add($a)
$b = New-Object System.Windows.Forms.Panel
$b.Name = 'B'
$b.SetBounds(50, 50, 300, 300)
$b.BackColor = [System.Drawing.Color]::Green
$f.Controls.Add($b)
$f.Show()
[System.Windows.Forms.Application]::DoEvents()
Write-Output ("TEST1 top@(100,100) after Add A then Add B: " + (Get-TopAt $f 100 100 @($a,$b)))
$b.BringToFront()
[System.Windows.Forms.Application]::DoEvents()
Write-Output ("TEST2 top@(100,100) after B.BringToFront(): " + (Get-TopAt $f 100 100 @($a,$b)))
$a.BringToFront()
[System.Windows.Forms.Application]::DoEvents()
Write-Output ("TEST3 top@(100,100) after A.BringToFront(): " + (Get-TopAt $f 100 100 @($a,$b)))
Write-Output ("TEST4 collection index: A=" + $f.Controls.GetChildIndex($a) + " B=" + $f.Controls.GetChildIndex($b))
$c = New-Object System.Windows.Forms.Panel
$c.Name = 'C'
$c.SetBounds(100, 100, 300, 300)
$c.BackColor = [System.Drawing.Color]::Blue
$f.Controls.Add($c)
[System.Windows.Forms.Application]::DoEvents()
Write-Output ("TEST5 top@(150,150) after C added post-CreateControl: " + (Get-TopAt $f 150 150 @($a,$b,$c)))
Write-Output ("TEST6 collection index: A=" + $f.Controls.GetChildIndex($a) + " B=" + $f.Controls.GetChildIndex($b) + " C=" + $f.Controls.GetChildIndex($c))
$f.Close()
$f.Dispose()
