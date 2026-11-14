# Runs inside Windows Sandbox (disposable guest): restores the EdgeUpdate client key hidden by hide-runtime.ps1 and
# points its pv value at the runtime folder that exists (the guest ships with pv 157.0.4341.42 but files
# 157.0.4341.46), to show that the stale registration alone makes the installed runtime "not found".
param([Parameter(Mandatory)][string]$Bin, [string]$Out = 'hide')
if ($env:USERNAME -ne 'WDAGUtilityAccount') { exit 2 }
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$key = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients'
$guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$Error.Clear()
if (Test-Path "$key\$guid.hidden") { Rename-Item "$key\$guid.hidden" $guid }
$folder = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory | Where-Object Name -match '^[0-9.]+$' |
  Select-Object -First 1
$before = (Get-ItemProperty "$key\$guid").pv
Set-ItemProperty "$key\$guid" -Name pv -Value $folder.Name
"$(Get-Date -Format HH:mm:ss) regfix pv $before -> $((Get-ItemProperty "$key\$guid").pv) errors=$($Error.Count)" | Out-File (Join-Path $outDir 'hide.txt') -Append
# The loader follows ClientState's EBWebView path (157.0.4341.42 in the guest), so point it at the existing folder too.
$state = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\$guid"
$beforePath = (Get-ItemProperty $state).EBWebView
Set-ItemProperty $state -Name EBWebView -Value $folder.FullName
Set-ItemProperty $state -Name pv -Value $folder.Name
"$(Get-Date -Format HH:mm:ss) regfix ClientState EBWebView $beforePath -> $((Get-ItemProperty $state).EBWebView) errors=$($Error.Count)" | Out-File (Join-Path $outDir 'hide.txt') -Append
