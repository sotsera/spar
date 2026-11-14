# Dumps the UI Automation subtree of one window (by handle) to a text file: control type, name, automation id,
# class name, bounding rectangle. Read-only. Usage: powershell -File uia-dump.ps1 -Hwnd <decimal> -Out <file>
param([Parameter(Mandatory)][long]$Hwnd, [Parameter(Mandatory)][string]$Out, [int]$Depth = 12, [switch]$Raw)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, UIAutomationClientsideProviders
# Win32 proxies (title bar, scroll bars, standard controls) come from the client-side provider table.
[System.Windows.Automation.ClientSettings]::RegisterClientSideProviderAssembly([UIAutomationClientsideProviders.UIAutomationClientSideProviders].Assembly.GetName())
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$Hwnd)
$walker = if ($Raw) { [System.Windows.Automation.TreeWalker]::RawViewWalker } else { [System.Windows.Automation.TreeWalker]::ControlViewWalker }
$lines = [System.Collections.Generic.List[string]]::new()
function Walk($el, $level) {
  if ($level -gt $Depth) { return }
  $c = $el.Current
  $r = $c.BoundingRectangle
  $lines.Add(('{0}{1} name="{2}" id="{3}" class="{4}" rect=[{5},{6},{7},{8}] offscreen={9} focusable={10}' -f ('  ' * $level),
    $c.ControlType.ProgrammaticName.Replace('ControlType.', ''), $c.Name, $c.AutomationId, $c.ClassName,
    $r.Left, $r.Top, $r.Right, $r.Bottom, $c.IsOffscreen, $c.IsKeyboardFocusable))
  $child = $walker.GetFirstChild($el)
  while ($child -ne $null) { Walk $child ($level + 1); $child = $walker.GetNextSibling($child) }
}
Walk $root 0
$lines | Set-Content -Encoding utf8 $Out
"$($lines.Count) elements"
