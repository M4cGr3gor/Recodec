param([int]$ProcessId, [string]$Action, [string]$Name, [string]$Out)
# Pequeño ayudante de UI Automation: invoke | toggle | select | shotwin
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$p = Get-Process -Id $ProcessId
$root = $A::FromHandle($p.MainWindowHandle)
function Find($n) {
  $c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $n)
  $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
switch ($Action) {
  "invoke" {
    $e = Find $Name
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($e -ne $null -and -not [bool]$e.GetCurrentPropertyValue($A::IsInvokePatternAvailableProperty)) { $e = $walker.GetParent($e) }
    $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); "invocado: $Name"
  }
  "toggle" { (Find $Name).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); "conmutado: $Name" }
  "select" { (Find $Name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); "seleccionado: $Name" }
  "shotwin" {
    $c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Name)
    $w = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
    if ($null -eq $w) { $w = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c) }
    $h = [IntPtr]$w.Current.NativeWindowHandle
    Add-Type -AssemblyName System.Drawing
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W2 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f); public struct R { public int L,T,Rr,B; } }
"@
    $r = New-Object W2+R; [void][W2]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [void][W2]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc)
    $bmp.Save($Out); "captura ventana: $Name"
  }
}
