# Runs inside Windows Sandbox (docs/evidence/m1b-host-details.md): the teardown leak probe with windows shown on the
# guest's screen, an anchor window keeping the browser alive, a loopback server, 30 cycles each with the plain and the
# mitigated teardown. The Runtime folder is passed explicitly: the guest's WebView2 registration has pointed at
# missing files before (pitfall "Windows Sandbox").
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Label, [string]$Cycles = '30')
$out = Join-Path 'C:\spar\out' $Label
New-Item -ItemType Directory -Force $out | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
foreach ($teardown in 'plain', 'mitigated', 'plain') {
  $name = "leak-shown-$teardown-$(Get-Date -Format HHmmss)"
  $arguments = @('--out', (Join-Path $out "$name.log"), '--profile', "C:\spar-work\$name", '--cycles', $Cycles, '--window', 'shown',
    '--anchor', 'shown', '--loopback', 'true', '--teardown', $teardown, '--browser-folder', "`"$($runtime.FullName)`"")
  $probe = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
  if (-not $probe.WaitForExit(600000)) { "$name timeout" | Out-File -Append (Join-Path $out 'leak-runs.txt') }
  else { "$name exit=$($probe.ExitCode)" | Out-File -Append (Join-Path $out 'leak-runs.txt') }
  Start-Sleep -Seconds 5
}
