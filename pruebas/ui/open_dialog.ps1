param([int]$ProcessId, [string]$Title, [string]$File)
# Rellena el diálogo estándar de "Abrir" de Windows (de la ventana del proceso) y lo acepta.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$dlg = $null
for ($i = 0; $i -lt 30 -and $null -eq $dlg; $i++) {
  Start-Sleep -Milliseconds 300
  $c = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)),
    (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Title)))
  $dlg = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
if ($null -eq $dlg) { "no aparece el diálogo"; exit 1 }
$combo = $dlg.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, "1148")))
$edit = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($File)
$edit.SetFocus()
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
"aceptado: $File"
