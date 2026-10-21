#requires -Version 7.4
# These helpers own only the process handles explicitly supplied or returned.

function Start-UnrelatedProcess {
  $start = [Diagnostics.ProcessStartInfo]::new('node')
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.ArgumentList.Add('-e')
  $start.ArgumentList.Add('setInterval(() => {}, 1000)')
  $process = [Diagnostics.Process]::Start($start)
  $null = $process.Handle
  return $process
}

function Stop-TestProcess([Diagnostics.Process]$Process) {
  # Failure-path teardown is forced termination, never a graceful-exit receipt.
  if (!$Process.HasExited) { $Process.Kill() }
  if (!$Process.WaitForExit(10000)) {
    throw "Test process $($Process.Id) has not exited; its handle remains owned by the caller."
  }
  $Process.Dispose()
}

function Close-TestSessions($Sessions) {
  $failures = [Collections.Generic.List[string]]::new()
  foreach ($session in $Sessions) {
    try {
      # Killing the coordinator closes its Windows job; never rediscover children by PID/name.
      Stop-TestProcess $session.Process
    } catch {
      $failures.Add("Coordinator teardown ($($session.Log)): $_")
    }
    foreach ($owned in $session.Handles.Values) {
      try {
        if (!$owned.WaitForExit(10000)) {
          throw "Observed process $($owned.Id) has not exited; its handle remains owned by the caller."
        }
        $owned.Dispose()
      } catch {
        $failures.Add("Owned-process teardown ($($session.Log)): $_")
      }
    }
  }
  if ($failures.Count) { throw ($failures -join "`n") }
}
