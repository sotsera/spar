# Runs inside Windows Sandbox: switches the guest's colour mode (the guest is disposable), then screenshots the desktop.
param([Parameter(Mandatory)][string]$Bin, [Parameter(Mandatory)][string]$Mode, [string]$Out = 'theme')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
& (Join-Path $Bin 'probe.exe') theme $Mode
Start-Sleep -Seconds 3
& (Join-Path $Bin 'probe.exe') shot (Join-Path $outDir "desktop-$Mode.png")
