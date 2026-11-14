# Runs inside Windows Sandbox as the logged-in user: one spike configuration per entry of -Configs, each with real
# mouse and keyboard input (--mode drive). The exe refuses drive mode outside the Sandbox account.
# The guest's WebView2 registration names a version whose files are not on disk, so the runtime folder is passed.
param(
  [Parameter(Mandatory)][string]$Configs,
  [string]$Exe = 'C:\spar\in\Titlebar.Desktop.exe',
  [string]$Out = 'C:\spar\out\drive',
  [string]$Mode = 'drive')
New-Item -ItemType Directory -Force $Out | Out-Null
$runtime = Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory |
  Where-Object Name -match '^[0-9.]+$' | Select-Object -First 1
foreach ($line in ($Configs -split ";")) {
  $parts = $line -split ' '
  $label = $parts[0]
  $argv = @('--out', $Out, '--label', $label, '--profile-root', 'C:\spar\profiles', '--profile', 'sandbox',
            '--placement', '100,80', '--mode', $Mode, '--browser-folder', "`"$($runtime.FullName)`"") + $parts[1..($parts.Length - 1)]
  try {
    $p = Start-Process -FilePath $Exe -ArgumentList $argv -PassThru -Wait -WindowStyle Normal -ErrorAction Stop
    "$label exit=$($p.ExitCode)" | Out-File -Append (Join-Path $Out 'runs.txt')
  } catch {
    "$label failed: $_" | Out-File -Append (Join-Path $Out 'runs.txt')
  }
}
