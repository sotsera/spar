# Runs inside Windows Sandbox: lists (read-only) the EdgeUpdate keys that describe the WebView2 Runtime, and the
# runtime folders on disk.
param([Parameter(Mandatory)][string]$Bin, [string]$Out = 'hide')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$file = Join-Path $outDir 'regdump.txt'
$guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
foreach ($root in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate', 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate', 'HKCU:\Software\Microsoft\EdgeUpdate') {
  foreach ($sub in "Clients\$guid", "ClientState\$guid") {
    $k = "$root\$sub"
    "== $k exists=$(Test-Path $k)" | Out-File $file -Append -Encoding utf8
    if (Test-Path $k) {
      (Get-ItemProperty $k).PSObject.Properties | Where-Object Name -notlike 'PS*' |
        ForEach-Object { "  $($_.Name) = $($_.Value)" } | Out-File $file -Append -Encoding utf8
    }
  }
}
Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -ErrorAction SilentlyContinue |
  ForEach-Object { "folder $($_.Name)" } | Out-File $file -Append -Encoding utf8
