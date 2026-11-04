# Runs out of process because the target WPF button opens ShowDialog.
param([Parameter(Mandatory=$true)][long]$WindowHandle)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$window=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($WindowHandle))
$condition=[System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::NameProperty, "Найти")
$button=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
if($null -eq $button) { throw "Footer Find Lyrics button was not exposed to UI Automation." }
$pattern=$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$pattern.Invoke()
