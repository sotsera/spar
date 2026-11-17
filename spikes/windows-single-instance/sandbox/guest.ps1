# Runs inside Windows Sandbox as the signed-in user. -Phase:
#   prepare        creates the data folders, as the user, before a SYSTEM process uses them
#   squatted       a primary and a secondary on C:\SparData\<Name> while a SYSTEM process holds the pipe name
#                  (started by the host first)
#   probe-target   a primary on C:\SparData\<Name> that a SYSTEM process then tries to connect to
#   focus          the focus handoff: a click on a launcher window starts the secondary, as a user's launch would
# -Security picks the probe's pipe security: spar (explicit DACL, server process checked) or dotnet (CurrentUserOnly).
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Phase, [int]$Runs = 3,
      [string]$Security = 'spar', [string]$Name = '', [string]$OutRoot = 'C:\spar\out',
      [string]$DataRoot = "$env:LOCALAPPDATA\SparProbe")
$ErrorActionPreference = 'Stop'
$out = Join-Path $OutRoot $(if ($Name) { $Name } else { $Phase })
New-Item -ItemType Directory -Force $out | Out-Null
"user=$env:USERNAME session=$((Get-Process -Id $PID).SessionId)" | Out-File (Join-Path $out 'whoami.txt')
$data = "C:\SparData\$Name"

function Wait-Log([string]$logs, [string]$pattern, [int]$timeoutMs = 8000) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  while ($watch.ElapsedMilliseconds -lt $timeoutMs) {
    if ((Test-Path $logs) -and (Select-String -Path "$logs\*.log" -Pattern $pattern -Quiet)) { return $true }
    Start-Sleep -Milliseconds 20
  }
  "timed out waiting for '$pattern'" | Out-File -Append (Join-Path $out 'errors.txt')
  return $false
}

switch ($Phase) {
  'prepare' {
    foreach ($folder in 'squat-dotnet', 'squat-spar', 'probe-dotnet', 'probe-spar') {
      New-Item -ItemType Directory -Force "C:\SparData\$folder" | Out-Null
    }
  }
  'squatted' {
    $logs = Join-Path $out 'logs'
    $primary = Start-Process $Exe -ArgumentList @('run', '--data', $data, '--log', $logs, '--security', $Security, '--hold-ms', '3000', '--', 'first') -PassThru -WindowStyle Hidden
    Wait-Log $logs 'primary without a pipe|primary serving' | Out-Null
    & $Exe spawn --log $logs --count 1 -- run --data $data --log $logs --security $Security -- open 'to-system-squatter.sql' | Out-Null
    $primary.WaitForExit()
  }
  'probe-target' {
    $logs = Join-Path $out 'logs'
    & $Exe run --data $data --log $logs --security $Security --hold-ms 10000 -- first | Out-Null
  }
  'focus' {
    $cases = @(
      @{ Name = 'allow'; Window = 'normal'; Extra = @() },
      @{ Name = 'no-allow'; Window = 'normal'; Extra = @('--no-allow-fg') },
      @{ Name = 'minimized'; Window = 'minimized'; Extra = @() },
      @{ Name = 'hidden'; Window = 'hidden'; Extra = @() },
      @{ Name = 'no-click'; Window = 'normal'; Extra = @() })
    foreach ($case in $cases) {
      for ($i = 0; $i -lt $Runs; $i++) {
        $logs = Join-Path $out "logs-$($case.Name)-$i"
        $folder = "$DataRoot\focus-$($case.Name)-$i"
        $run = @('run', '--data', $folder, '--log', $logs) + $case.Extra + @('--', 'open', "focus-$($case.Name)-$i.sql")
        $primary = Start-Process $Exe -ArgumentList @('run', '--data', $folder, '--log', $logs, '--window', $case.Window, '--hold-ms', '6000', '--', 'first') -PassThru
        if (-not (Wait-Log $logs "pid=$($primary.Id) primary serving")) { continue }
        if ($case.Name -eq 'no-click') {
          # Started by this script, which has no window and never had the user's input.
          & $Exe spawn --log $logs --count 1 -- @run | Out-Null
        }
        else {
          $launcher = Start-Process $Exe -ArgumentList (@('launcher', '--exe', $Exe, '--log', $logs, '--hold-ms', '4000', '--') + $run) -PassThru
          if (-not (Wait-Log $logs "spar-si-launcher")) { continue }
          Start-Sleep -Milliseconds 300
          & $Exe click --log $logs --title 'spar-si-launcher' | Out-Null
          $launcher.WaitForExit()
        }
        Wait-Log $logs '300 ms after activating' | Out-Null
        $primary.WaitForExit()
      }
    }
  }
}
