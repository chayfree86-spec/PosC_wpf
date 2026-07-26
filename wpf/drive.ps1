param([string]$Match)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process Pos.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "NO_WINDOW"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
foreach ($b in $buttons) {
  $name = $b.Current.Name
  if ($name -and $name.ToLower().Contains($Match.ToLower())) {
    try {
      $inv = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
      $inv.Invoke()
      Write-Output "INVOKED: $name"
      exit 0
    } catch { Write-Output "ERR invoking $name : $_" }
  }
}
Write-Output "NOT_FOUND: $Match"
