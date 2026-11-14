# Runs inside Windows Sandbox: opens the spike window on screen, then invokes the page's caption buttons through the
# native UI Automation client (Invoke pattern, as assistive technology does) and dumps the tree in between.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Configs, [string]$Out = 'invoke')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
foreach ($line in ($Configs -split ';')) {
  $parts = $line.Trim() -split ' '
  $label = $parts[0]
  $hold = Join-Path $outDir "$label.hold"
  $file = Join-Path $outDir "$label-invoke.txt"
  Remove-Item -ErrorAction SilentlyContinue $hold
  $argv = @('--out', $outDir, '--label', $label, '--profile-root', 'C:\spar\profiles', '--profile', 'uia',
            '--placement', '100,80', '--hold', '14000', '--after-ready', '1500',
            '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -WindowStyle Normal
  for ($i = 0; $i -lt 150 -and -not (Test-Path $hold); $i++) { Start-Sleep -Milliseconds 200 }
  $hwnd = (Get-Content $hold).Split(' ')[1]
  Start-Process -FilePath $Exe -ArgumentList @('--uia-dump', $hwnd, (Join-Path $outDir "$label-control-1.txt")) -Wait
  Start-Sleep -Seconds 2
  foreach ($id in 'max', 'max', 'min') {
    Start-Process -FilePath $Exe -ArgumentList @('--uia-invoke', $hwnd, $id, $file) -Wait
    Start-Sleep -Milliseconds 1500
  }
  $p.WaitForExit(60000) | Out-Null
}
