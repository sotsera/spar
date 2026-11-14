# Runs inside Windows Sandbox: records the guest (OS, WebView2 registration and folders, colour mode) and makes
# fixed-version runtime folders from the guest's own runtime copy under C:\rt (guest disk, not a mapped folder):
#   fixed-ok (a full copy), broken-noexe (msedgewebview2.exe deleted), broken-fakeexe (msedgewebview2.exe replaced by
#   where.exe, which exits at once), broken-noclient (EBWebView\x64\EmbeddedBrowserWebView.dll deleted), empty.
param([Parameter(Mandatory)][string]$Bin, [string]$Out = 'prepare')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$log = Join-Path $outDir 'guest-info.txt'
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
"user=$env:USERNAME os=$($os.ProductName) $($os.DisplayVersion) build=$($os.CurrentBuild).$($os.UBR)" | Out-File $log
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
"elevated=$($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))" | Out-File $log -Append
$app = 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application'
Get-ChildItem $app -Directory -ErrorAction SilentlyContinue | ForEach-Object { "webview2 folder=$($_.Name)" } | Out-File $log -Append
foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
               'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') {
  "registry $k pv=$((Get-ItemProperty $k -ErrorAction SilentlyContinue).pv)" | Out-File $log -Append
}
$p = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue
"AppsUseLightTheme=$($p.AppsUseLightTheme) SystemUsesLightTheme=$($p.SystemUsesLightTheme)" | Out-File $log -Append
"edge=$(Test-Path 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe')" | Out-File $log -Append
$src = Get-ChildItem $app -Directory | Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
New-Item -ItemType Directory -Force 'C:\rt\empty' | Out-Null
$sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($name in 'fixed-ok', 'broken-noexe', 'broken-fakeexe', 'broken-noclient') {
  if (-not (Test-Path "C:\rt\$name")) { Copy-Item $src.FullName "C:\rt\$name" -Recurse }
}
Remove-Item 'C:\rt\broken-noexe\msedgewebview2.exe' -ErrorAction SilentlyContinue
Copy-Item 'C:\Windows\System32\where.exe' 'C:\rt\broken-fakeexe\msedgewebview2.exe' -Force
Remove-Item 'C:\rt\broken-noclient\EBWebView\x64\EmbeddedBrowserWebView.dll' -ErrorAction SilentlyContinue
"copies from $($src.FullName) in $($sw.ElapsedMilliseconds) ms" | Out-File $log -Append
& (Join-Path $Bin 'probe.exe') shot (Join-Path $outDir 'desktop.png')
"done" | Out-File $log -Append
