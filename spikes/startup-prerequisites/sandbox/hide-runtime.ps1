# Runs inside Windows Sandbox (disposable guest, never on the host): makes the guest look like a PC without the
# WebView2 Runtime by renaming its runtime folder and its EdgeUpdate client key; -Restore undoes both.
param([Parameter(Mandatory)][string]$Bin, [switch]$Restore, [string]$Out = 'hide')
if ($env:USERNAME -ne 'WDAGUtilityAccount') { exit 2 }
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$log = Join-Path $outDir 'hide.txt'
$dir = 'C:\Program Files (x86)\Microsoft\EdgeWebView'
$key = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients'
$guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$Error.Clear()
if ($Restore) {
  Rename-Item "$dir.hidden" 'EdgeWebView' -ErrorAction Continue
  Rename-Item "$key\$guid.hidden" $guid -ErrorAction Continue
} else {
  Rename-Item $dir 'EdgeWebView.hidden' -ErrorAction Continue
  Rename-Item "$key\$guid" "$guid.hidden" -ErrorAction Continue
}
"$(Get-Date -Format HH:mm:ss) restore=$Restore folder=$(Test-Path $dir) key=$(Test-Path "$key\$guid") errors=$($Error.Count) $($Error | ForEach-Object { $_.ToString() })" |
  Out-File $log -Append
