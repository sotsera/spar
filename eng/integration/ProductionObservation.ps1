#requires -Version 7.4
# Definition-only, diagnostic observations. The caller owns readiness, deadlines and process handles.

function New-ProductionObservation([Diagnostics.Process]$Process) {
  return @{
    processId = $Process.Id; processStartedAtUtc = $Process.StartTime.ToUniversalTime()
    phase = 'before-first-application-event'; firstEvent = $null; lastEvent = $null
    window = $false; document = $false; greeting = $false
    samples = [Collections.Generic.List[object]]::new(); nextSampleMs = 0; nextNoticeMs = 10000
  }
}

function Update-ProductionObservation {
  param([hashtable]$State, [Diagnostics.Process]$Process, [double]$ElapsedMs, [object[]]$Events = @(), [switch]$Force)
  $knownStages = @('window-created','document-loaded','bridge-connected','bridge-admitted','bridge-completed','bridge-rejected','closed','failed')
  foreach ($event in $Events) {
    # A reused PID or stale output cannot advance even the diagnostic phase.
    try {
      if ($event.ProcessId -ne $State.processId -or !$event.ProcessStartedAtUtc -or !$event.ObservedAt -or
          [Math]::Abs(([DateTimeOffset]$event.ProcessStartedAtUtc - [DateTimeOffset]$State.processStartedAtUtc).TotalMilliseconds) -gt 1) { continue }
      $observed = ([DateTimeOffset]$event.ObservedAt).ToUniversalTime()
    } catch { continue }
    $stage = if ($event.Stage -in $knownStages) { $event.Stage } else { 'application-event' }
    $summary = @{ stage = $stage; observedAtUtc = $observed }
    if (!$State.firstEvent -or $observed -lt $State.firstEvent.observedAtUtc) { $State.firstEvent = $summary }
    if (!$State.lastEvent -or $observed -ge $State.lastEvent.observedAtUtc) { $State.lastEvent = $summary }
    if ($event.Stage -eq 'window-created') { $State.window = $true }
    if ($event.Stage -eq 'document-loaded') { $State.document = $true }
    if ($event.Stage -eq 'bridge-completed' -and $event.Detail -like '*|greet|*') { $State.greeting = $true }
  }
  $State.phase = if (!$State.firstEvent) { 'before-first-application-event' }
    elseif (!$State.window) { 'waiting-for-window' }
    elseif (!$State.document) { 'waiting-for-document' }
    elseif (!$State.greeting) { 'waiting-for-greeting' } else { 'greeting-observed' }

  if (!$Force -and $ElapsedMs -lt $State.nextSampleMs) { return }
  $sample = @{ observedAtUtc = [DateTimeOffset]::UtcNow; elapsedMs = $ElapsedMs
    cpuMs = $null; workingSetBytes = $null; exited = $null; availability = 'unavailable' }
  try {
    $Process.Refresh()
    $sample.exited = $Process.HasExited
    $sample.cpuMs = $Process.TotalProcessorTime.TotalMilliseconds
    $sample.workingSetBytes = $Process.WorkingSet64
    $sample.availability = 'observed'
  } catch {
    # Diagnostic query failure never changes the test outcome or means zero CPU/memory.
    $sample.errorType = $_.Exception.GetType().FullName
  }
  $State.samples.Add($sample)
  if ($State.samples.Count -gt 64) { $State.samples.RemoveAt(0) }
  $State.nextSampleMs = $ElapsedMs + 1000
  if ($State.phase -ne 'greeting-observed' -and $ElapsedMs -ge $State.nextNoticeMs) {
    $cpu = if ($null -eq $sample.cpuMs) { 'unavailable' } else { [Math]::Round($sample.cpuMs, 1) }
    Write-Host "Production startup pending: $([Math]::Round($ElapsedMs / 1000, 1)) s; phase=$($State.phase); CPU ms=$cpu. Cause unattributed; file/loader and vendor scan traces are needed for antivirus attribution."
    $State.nextNoticeMs = $ElapsedMs + 10000
  }
}

function Get-ProductionObservation([hashtable]$State) {
  return @{
    schema = 1; processId = $State.processId; processStartedAtUtc = $State.processStartedAtUtc
    phase = $State.phase; firstEvent = $State.firstEvent; lastEvent = $State.lastEvent
    observations = $State.samples.ToArray()
    antivirus = @{ attribution = 'unattributed'; evidence = 'not-collected'
      guidance = 'Process progress and CPU do not identify an antivirus cause. Correlate a file/loader wait with vendor scan or kernel/filter evidence.' }
  }
}
