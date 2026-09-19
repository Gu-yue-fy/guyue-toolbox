Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# TEST A: Panel BackColor = Transparent (does it throw?)
$okA = $false; $errA = ""
try {
    $p = New-Object System.Windows.Forms.Panel
    $p.BackColor = [System.Drawing.Color]::Transparent
    $okA = $true
} catch { $errA = $_.Exception.Message }
if ($okA) { Write-Output "TEST-A Panel.BackColor=Transparent: OK (no throw)" }
else { Write-Output ("TEST-A Panel.BackColor=Transparent THROWS: " + $errA) }

# TEST B: does a plain Panel receive Click without StandardClick?
$clicked = $false
$f = New-Object System.Windows.Forms.Form
$f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$f.Location = New-Object System.Drawing.Point(60, 60)
$f.ClientSize = New-Object System.Drawing.Size(300, 200)
$f.ShowInTaskbar = $false
$pn = New-Object System.Windows.Forms.Panel
$pn.SetBounds(10, 10, 200, 100)
$pn.BackColor = [System.Drawing.Color]::SteelBlue
$pn.Add_Click({ $script:clicked = $true })
$f.Controls.Add($pn)
$f.Show()
[System.Windows.Forms.Application]::DoEvents()

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class MSG {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
"@
# WM_LBUTTONDOWN=0x0201, WM_LBUTTONUP=0x0202, lParam = y<<16 | x (client coords of the panel)
$l = [IntPtr](50 * 65536 + 50)
[MSG]::PostMessage($pn.Handle, 0x0201, [IntPtr]1, $l) | Out-Null
[MSG]::PostMessage($pn.Handle, 0x0202, [IntPtr]0, $l) | Out-Null
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 100
[System.Windows.Forms.Application]::DoEvents()
if ($script:clicked) { Write-Output "TEST-B Panel Click WITHOUT StandardClick: FIRED (no gate)" }
else { Write-Output "TEST-B Panel Click WITHOUT StandardClick: NOT fired (gated => dead button)" }

# TEST C: same but with StandardClick enabled via subclass
Add-Type -TypeDefinition @"
using System.Windows.Forms;
public class ClickablePanel : Panel {
    public ClickablePanel() {
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
    }
}
"@
$clicked2 = $false
$p2 = New-Object ClickablePanel
$p2.SetBounds(10, 110, 200, 60)
$p2.BackColor = [System.Drawing.Color]::OliveDrab
$p2.Add_Click({ $script:clicked2 = $true })
$f.Controls.Add($p2)
[System.Windows.Forms.Application]::DoEvents()
$l2 = [IntPtr](30 * 65536 + 30)
[MSG]::PostMessage($p2.Handle, 0x0201, [IntPtr]1, $l2) | Out-Null
[MSG]::PostMessage($p2.Handle, 0x0202, [IntPtr]0, $l2) | Out-Null
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 100
[System.Windows.Forms.Application]::DoEvents()
if ($script:clicked2) { Write-Output "TEST-C Panel Click WITH StandardClick: FIRED" }
else { Write-Output "TEST-C Panel Click WITH StandardClick: NOT fired" }

# TEST D: MouseClick event on plain panel (NoticeBar uses OnMouseClick)
$mc = $false
$pn.Add_MouseClick({ $script:mc = $true })
$l3 = [IntPtr](40 * 65536 + 40)
[MSG]::PostMessage($pn.Handle, 0x0201, [IntPtr]1, $l3) | Out-Null
[MSG]::PostMessage($pn.Handle, 0x0202, [IntPtr]0, $l3) | Out-Null
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 100
[System.Windows.Forms.Application]::DoEvents()
if ($script:mc) { Write-Output "TEST-D Panel MouseClick WITHOUT StandardClick: FIRED" }
else { Write-Output "TEST-D Panel MouseClick WITHOUT StandardClick: NOT fired" }

$f.Close()
$f.Dispose()
