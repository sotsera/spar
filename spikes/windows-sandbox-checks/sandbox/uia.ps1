# Runs inside Windows Sandbox: for each ';'-separated configuration, opens the spike window on screen, waits until
# it holds, and dumps its UI Automation tree with the native client (control view, then raw view; read-only).
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Configs, [string]$Out = 'uia', [string]$Scale = '')
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
  $hold = Join-Path $outDir "$label.hold"
  Remove-Item -ErrorAction SilentlyContinue $hold
  $argv = @('--out', $outDir, '--label', $label, '--profile-root', 'C:\spar\profiles', '--profile', 'uia',
            '--placement', '100,80', '--hold', '8000', '--after-ready', '2000',
            '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -WindowStyle Normal
  for ($i = 0; $i -lt 150 -and -not (Test-Path $hold); $i++) { Start-Sleep -Milliseconds 200 }
  if (Test-Path $hold) {
    $hwnd = (Get-Content $hold).Split(' ')[1]
    Start-Process -FilePath $Exe -ArgumentList @('--uia-dump', $hwnd, (Join-Path $outDir "$label-control-1.txt")) -Wait
    Start-Sleep -Seconds 3
    Start-Process -FilePath $Exe -ArgumentList @('--uia-dump', $hwnd, (Join-Path $outDir "$label-control.txt")) -Wait
    Start-Process -FilePath $Exe -ArgumentList @('--uia-dump', $hwnd, (Join-Path $outDir "$label-raw.txt"), 'raw') -Wait
  } else { "$label no hold" | Out-File -Append (Join-Path $outDir 'runs.txt') }
  $p.WaitForExit(60000) | Out-Null
}
