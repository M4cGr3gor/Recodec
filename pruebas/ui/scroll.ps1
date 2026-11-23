param([int]$ProcessId, [double]$Percent)
# Desplaza el ScrollViewer de la pestaña visible al porcentaje indicado.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$root = $A::FromHandle((Get-Process -Id $ProcessId).MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition($A::IsScrollPatternAvailableProperty, $true)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
foreach ($e in $all) {
  if ($e.Current.IsOffscreen) { continue }
  $sp = $e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  if ($sp.Current.VerticallyScrollable -and $e.Current.BoundingRectangle.Height -gt 300) {
    $sp.SetScrollPercent(-1, $Percent); "desplazado: $($e.Current.ClassName) $($e.Current.BoundingRectangle)"; break
  }
}
