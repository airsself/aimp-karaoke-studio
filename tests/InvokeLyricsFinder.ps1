# Standalone cross-process automation for WPF ShowDialog.
param([Parameter(Mandatory=$true)][long]$WindowHandle,
      [Parameter(Mandatory=$true)][string]$LogPath)
$ErrorActionPreference = "Stop"
try {
  Add-Content -LiteralPath $LogPath -Value "Worker started. HWND=$WindowHandle"
  Add-Type -AssemblyName UIAutomationClient
  Add-Type -AssemblyName UIAutomationTypes
  $window=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($WindowHandle))
  $filter=[System.Windows.Automation.PropertyCondition]::new(
     [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
     [System.Windows.Automation.ControlType]::Button)
  $buttons=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$filter)
  foreach($button in $buttons) {
    Add-Content -LiteralPath $LogPath -Value ("UI button: " + $button.Current.Name)
  }
  $condition=[System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, "Найти")
  $target=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if($null -eq $target) { throw "Footer button named Найти was absent" }
  Add-Content -LiteralPath $LogPath -Value "Target found; invoking"
  $pattern=$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $pattern.Invoke()
  Add-Content -LiteralPath $LogPath -Value "Invoke returned"
} catch {
  Add-Content -LiteralPath $LogPath -Value ("ERROR: " + ($_ | Out-String))
  exit 2
}
