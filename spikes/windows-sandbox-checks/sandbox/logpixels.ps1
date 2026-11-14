# Runs inside Windows Sandbox (disposable guest): sets the user's custom display scaling (LogPixels, Win8DpiScaling)
# in the guest registry, optionally tells the guest's remote-session server to ignore the client's scale factor,
# and signs the guest user out, so the next session starts at that DPI.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][int]$Dpi, [switch]$IgnoreClient, [string]$Out = 'logpixels')
if ($env:USERNAME -ne 'WDAGUtilityAccount') { exit 2 }
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name LogPixels -Value $Dpi -Type DWord
Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name Win8DpiScaling -Value 1 -Type DWord
if ($IgnoreClient) {
  Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations' -Name IgnoreClientDesktopScaleFactor -Value 1 -Type DWord
}
"$(Get-Date -Format HH:mm:ss) LogPixels=$Dpi ignoreClient=$IgnoreClient; signing out" | Out-File -Append (Join-Path $outDir 'log.txt')
& logoff.exe
