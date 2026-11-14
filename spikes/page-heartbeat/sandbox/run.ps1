# Runs inside Windows Sandbox as the logged-in user: one host run per line of -Configs ("label args..."), each with its
# own profile, on screen at (100,80). The guest's WebView2 registration does not match its files, so the runtime folder
# is passed explicitly (spikes/windows-sandbox-checks).
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Configs, [string]$Out = 'run')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
"guest cpus=$([Environment]::ProcessorCount) runtime=$($runtime.Name) user=$env:USERNAME" | Out-File -Encoding ascii -Append (Join-Path $outDir 'runs.txt')
foreach ($line in Get-Content $Configs) {
  if (-not $line.Trim()) { continue }
  $parts = $line.Trim() -split ' '
  $label = $parts[0]
  $argv = @('--log', (Join-Path $outDir "$label.log"), '--profile-root', 'C:\spar\profiles', '--profile', $label,
            '--placement', '100,80', '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  try {
    $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -WindowStyle Normal -ErrorAction Stop
    if (-not $p.WaitForExit(300000)) { "$label timeout" | Out-File -Encoding ascii -Append (Join-Path $outDir 'runs.txt') }
    else { "$label exit=$($p.ExitCode) $(Get-Date -Format HH:mm:ss)" | Out-File -Encoding ascii -Append (Join-Path $outDir 'runs.txt') }
  } catch {
    "$label failed: $_" | Out-File -Encoding ascii -Append (Join-Path $outDir 'runs.txt')
  }
  Start-Sleep -Seconds 1
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path 'C:\spar\profiles' $label)
}
"done $(Get-Date -Format HH:mm:ss)" | Out-File -Encoding ascii -Append (Join-Path $outDir 'runs.txt')
