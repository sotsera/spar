#requires -Version 7.4
# Session handles are returned to the caller; no script-scoped registry or results.

function Read-Shared([string]$Path) {
  $stream = [IO.FileStream]::new($Path, 'Open', 'Read', 'ReadWrite')
  $reader = [IO.StreamReader]::new($stream)
  try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Start-Session {
  param(
    [Parameter(Mandatory)]$Fixture,
    [bool]$Build = $false,
    [bool]$Inspect = $false,
    [bool]$Probe = $true,
    [Parameter(Mandatory)][string]$Coordinator,
    [Parameter(Mandatory)][string]$WorkingDirectory
  )
  $Fixture.Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Fixture.Config
  $start = [Diagnostics.ProcessStartInfo]::new($Coordinator)
  $start.WorkingDirectory = $WorkingDirectory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($arg in @($Fixture.Config,'--owner-stdin')) { $start.ArgumentList.Add($arg) }
  if ($Probe) { $start.ArgumentList.Add('--probe') }
  if ($Inspect) { $start.ArgumentList.Add('--inspect') }
  if (!$Build) { $start.ArgumentList.Add('--no-build') }
  $process = [Diagnostics.Process]::Start($start)
  $null = $process.Handle
  $session = [pscustomobject]@{
    Fixture = $Fixture; Process = $process; Started = $process.StartTime.ToUniversalTime()
    Events = [Collections.Generic.List[object]]::new(); Handles = [Collections.Generic.Dictionary[string,Diagnostics.Process]]::new()
    Pending = $process.StandardOutput.ReadLineAsync(); Errors = $process.StandardError.ReadToEndAsync(); Ended = $false
    Log = Join-Path $Fixture.Directory 'coordinator.jsonl'; Passed = $false
  }
  return $session
}

function Drain-Events($Session) {
  while (!$Session.Ended -and $Session.Pending.IsCompleted) {
    $line = $Session.Pending.GetAwaiter().GetResult()
    if ($null -eq $line) { $Session.Ended = $true; break }
    $line | Add-Content -LiteralPath $Session.Log
    $event = $line | ConvertFrom-Json
    $Session.Events.Add($event)
    if ($event.processId -and $event.processStartedAtUtc) {
      $key = "$($event.processId)/$($event.processStartedAtUtc)"
      if (!$Session.Handles.ContainsKey($key)) {
        try {
          $owned = [Diagnostics.Process]::GetProcessById([int]$event.processId)
          $null = $owned.Handle
          if ($owned.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$event.processStartedAtUtc).ToUniversalTime().Ticks) {
            $owned.Dispose()
            throw 'Process identity changed before observation; never target a reused PID.'
          }
          $Session.Handles.Add($key, $owned)
        } catch [ArgumentException] { } # Already exited before the independent observation; not a complete process history.
      }
    }
    $Session.Pending = $Session.Process.StandardOutput.ReadLineAsync()
  }
}

function Wait-Until($Session, [scriptblock]$Condition, [string]$Description, [int]$Seconds = 45) {
  $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
  do {
    Drain-Events $Session
    if (& $Condition) { return }
    if ($Session.Process.HasExited) {
      Drain-Events $Session
      throw "Coordinator exited before $Description; see $($Session.Log). $($Session.Errors.GetAwaiter().GetResult())"
    }
    Start-Sleep -Milliseconds 50
  } while ([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $Description; this does not establish cleanup. See $($Session.Log)"
}

function Send-Command($Session, [string]$Command, [string]$Detail = '', [bool]$Force = $false) {
  $Session.Process.StandardInput.WriteLine((@{ command = $Command; id = [guid]::NewGuid().ToString('N'); detail = $Detail; force = $Force } | ConvertTo-Json -Compress))
  $Session.Process.StandardInput.Flush()
}

function Latest-Snapshot($Session, [int]$Generation = 1) {
  $event = $Session.Events | Where-Object { $_.kind -eq 'ui-snapshot' -and $_.generation -eq $Generation } | Select-Object -Last 1
  if ($event) { $event.detail | ConvertFrom-Json }
}

function Assert-Condition([bool]$Condition, [string]$Description) {
  if (!$Condition) { throw $Description }
}

function Confirm-Exit {
  param(
    [Parameter(Mandatory)]$Session,
    [int]$ExpectedCode = 0,
    [bool]$ForcedOwner = $false,
    [Parameter(Mandatory)][Diagnostics.Process]$Unrelated,
    [switch]$VerifyRender
  )
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  while (!$Session.Process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Drain-Events $Session; Start-Sleep -Milliseconds 50 }
  if (!$Session.Process.HasExited) { throw 'Coordinator did not actually exit.' }
  $drainDeadline = [DateTime]::UtcNow.AddSeconds(5)
  while (!$Session.Ended -and [DateTime]::UtcNow -lt $drainDeadline) { Drain-Events $Session; Start-Sleep -Milliseconds 10 }
  Assert-Condition $Session.Ended 'Coordinator stdout did not actually reach EOF.'
  $Session.Errors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $Session.Fixture.Directory 'coordinator.stderr.log')
  if (!$ForcedOwner) {
    Assert-Condition ($Session.Process.ExitCode -eq $ExpectedCode) "Unexpected coordinator exit $($Session.Process.ExitCode): $($Session.Log)"
    Assert-Condition (@($Session.Events | Where-Object kind -eq 'cleanup-failed').Count -eq 0) 'Cleanup failed; no successful exit claim is allowed.'
    Assert-Condition (@($Session.Events | Where-Object kind -eq 'owned-processes-ended').Count -eq 1) 'No actual owned-job completion receipt.'
  }
  foreach ($owned in $Session.Handles.Values) {
    if (!$owned.WaitForExit(10000)) { throw "Observed owned process $($owned.Id) is still alive." }
  }
  Assert-Condition (!$Unrelated.HasExited) 'An unrelated process was terminated.'
  $Session.Passed = $true
  return [pscustomobject]@{
    scenario = $Session.Fixture.Name; passed = $true; coordinatorProcessId = $Session.Process.Id; startedAtUtc = $Session.Started
    actualExitCode = $Session.Process.ExitCode; abruptOwnerKill = $ForcedOwner
    forcedCleanupRequested = @($Session.Events | Where-Object kind -eq 'forced-cleanup-requested').Count -gt 0
    independentlyRetainedProcesses = $Session.Handles.Count; unrelatedSurvived = $true
    rendering = $(if ($VerifyRender) { 'verified-pixels-review-captures' } else { 'not-exercised' })
  }
}
