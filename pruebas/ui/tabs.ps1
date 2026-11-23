param([int]$ProcessId, [string]$OutPrefix, [string[]]$Tabs)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process -Id $ProcessId
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
foreach ($t in $Tabs) {
  $cond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $t)))
  $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($null -eq $item) {
    # El encabezado de la cola es dinámico ("Cola (1)")
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)))
    $item = $all | Where-Object { $_.Current.Name -like "$t*" } | Select-Object -First 1
  }
  if ($null -eq $item) { "no encontrado: $t"; continue }
  $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 700
  & "$PSScriptRoot\shot.ps1" -ProcessId $ProcessId -Out "$OutPrefix$t.png" | Out-Null
  "captura: $t"
}
