# Runs inside Windows Sandbox as the logged-in user: one spike configuration per ';'-separated entry of -Configs
# ("label args..."), each in its own profile unless the entry passes --profile. -Scale first sets the display scale
# (guest only). The guest's WebView2 registration does not match its files, so the runtime folder is passed.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Configs, [string]$Out = 'drive',
      [string]$Mode = 'drive', [string]$Scale = '')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
if ($Scale) {
  Start-Process -FilePath $Exe -ArgumentList @('--scale', $Scale, (Join-Path $outDir 'scale.txt')) -Wait
  Start-Sleep -Seconds 3
}
foreach ($line in ($Configs -split ';')) {
  $parts = $line.Trim() -split ' '
  $label = $parts[0]
  $argv = @('--out', $outDir, '--label', $label, '--profile-root', 'C:\spar\profiles', '--profile', $label,
            '--placement', '100,80', '--mode', $Mode, '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  try {
    $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -WindowStyle Normal -ErrorAction Stop
    if (-not $p.WaitForExit(240000)) { "$label timeout" | Out-File -Append (Join-Path $outDir 'runs.txt') }
    else { "$label exit=$($p.ExitCode)" | Out-File -Append (Join-Path $outDir 'runs.txt') }
  } catch {
    "$label failed: $_" | Out-File -Append (Join-Path $outDir 'runs.txt')
  }
}
