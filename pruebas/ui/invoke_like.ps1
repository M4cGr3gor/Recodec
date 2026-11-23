param([int]$ProcessId, [string]$Prefix, [int]$Index = 0)
# Pulsa el botón cuyo texto empieza por $Prefix (el $Index-ésimo si hay varios).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$root = $A::FromHandle((Get-Process -Id $ProcessId).MainWindowHandle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$hits = @($all | Where-Object { $_.Current.Name -like "$Prefix*" -and -not $_.Current.IsOffscreen })
if ($hits.Count -le $Index) { "no encontrado: $Prefix ($($hits.Count))"; exit 1 }
$e = $hits[$Index]
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
while ($e -ne $null -and -not [bool]$e.GetCurrentPropertyValue($A::IsInvokePatternAvailableProperty)) { $e = $walker.GetParent($e) }
$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
"pulsado: $($hits[$Index].Current.Name)"
