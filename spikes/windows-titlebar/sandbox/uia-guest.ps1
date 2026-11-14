# Runs inside Windows Sandbox: for each configuration, starts the spike window on screen, waits until it holds,
# dumps its UI Automation tree (read-only), and waits for the exe to exit.
param([Parameter(Mandatory)][string]$Configs, [Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Dump,
      [string]$Out = 'C:\spar\out\uia')
New-Item -ItemType Directory -Force $Out | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
foreach ($line in ($Configs -split ';')) {
  $parts = $line -split ' '
  $label = $parts[0]
  Remove-Item -ErrorAction SilentlyContinue (Join-Path $Out "$label.hold")
  $argv = @('--out', $Out, '--label', $label, '--profile-root', 'C:\spar\profiles', '--profile', 'sandbox',
            '--placement', '100,80', '--hold', '9000', '--after-ready', '1500',
            '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -WindowStyle Normal
  for ($i = 0; $i -lt 100 -and -not (Test-Path (Join-Path $Out "$label.hold")); $i++) { Start-Sleep -Milliseconds 200 }
  $hwnd = (Get-Content (Join-Path $Out "$label.hold")).Split(' ')[1]
  & powershell -NoProfile -ExecutionPolicy Bypass -File $Dump -Hwnd $hwnd -Out (Join-Path $Out "$label-1.txt")
  Start-Sleep -Seconds 3
  & powershell -NoProfile -ExecutionPolicy Bypass -File $Dump -Hwnd $hwnd -Out (Join-Path $Out "$label-2.txt")
  $p.WaitForExit(60000) | Out-Null
}
