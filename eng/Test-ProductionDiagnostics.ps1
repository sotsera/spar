#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/ProductionObservation.ps1')
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
$start = [Diagnostics.ProcessStartInfo]::new('node')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add('-e')
$start.ArgumentList.Add('setInterval(() => {}, 1000)')
$process = [Diagnostics.Process]::Start($start)
$null = $process.Handle
try {
  $state = New-ProductionObservation $process
  Update-ProductionObservation $state $process 0
  $observed = Get-ProductionObservation $state
  Assert ($observed.phase -eq 'before-first-application-event') 'No event was reported as application progress.'
  Assert ($observed.observations[0].availability -eq 'observed' -and $null -ne $observed.observations[0].cpuMs) 'Live process CPU unavailable.'
  Assert ($observed.antivirus.attribution -eq 'unattributed') 'Idle process was mislabeled as antivirus work.'

  $event = @{ ProcessId=$process.Id; ProcessStartedAtUtc=$process.StartTime.ToUniversalTime(); ObservedAt=[DateTimeOffset]::UtcNow; Stage='window-created' }
  $stale = $event.Clone()
  $stale.ProcessStartedAtUtc = ([DateTimeOffset]$event.ProcessStartedAtUtc).AddSeconds(-1)
  Update-ProductionObservation $state $process 1 -Events @($stale)
  Assert ($state.phase -eq 'before-first-application-event') 'Stale PID identity advanced the phase.'
  Update-ProductionObservation $state $process 2 -Events @($event)
  Assert ($state.phase -eq 'waiting-for-document') 'Window creation was reported as ready.'
  $event.Stage = 'document-loaded'
  Update-ProductionObservation $state $process 3 -Events @($event)
  Assert ($state.phase -eq 'waiting-for-greeting') 'Document loading was reported as typed greeting completion.'
  $event.Stage = 'bridge-completed'
  $event.Detail = '|counter|1|private-capability'
  Update-ProductionObservation $state $process 4 -Events @($event)
  Assert ($state.phase -eq 'waiting-for-greeting') 'Another operation was reported as greeting completion.'
  $event.Detail = '|greet|1|private-capability'
  Update-ProductionObservation $state $process 5 -Events @($event)
  Assert ($state.phase -eq 'greeting-observed') 'Matching greeting was not observed.'
  $event.Stage = 'private-application-data'
  Update-ProductionObservation $state $process 6 -Events @($event)
  $json = Get-ProductionObservation $state | ConvertTo-Json -Depth 8
  Assert ($json -notmatch 'private-capability|private-application-data') 'Observation persisted private application data.'
  foreach ($index in 1..70) { Update-ProductionObservation $state $process $index -Force }
  Assert ($state.samples.Count -eq 64) 'Observation buffer is not bounded.'
  Assert (!(Get-ProductionObservation $state).firstEvent.ContainsKey('Detail')) 'Raw event detail leaked into the summary.'
  $process.Kill($true)
  Assert ($process.WaitForExit(5000)) 'Diagnostic test process did not exit.'
  Update-ProductionObservation $state $process 100 -Force
  Assert ($state.samples[-1].exited -eq $true) 'Actual process exit was not observed.'
  Assert ((Get-ProductionObservation $state).antivirus.evidence -eq 'not-collected') 'Process exit established false scan evidence.'
} finally {
  if (!$process.HasExited) { $process.Kill($true); $null = $process.WaitForExit(5000) }
  if (!$process.HasExited) { throw 'Diagnostic test cleanup remains unfinished.' }
  $process.Dispose()
}
Write-Output 'Production observations passed: real CPU/exit, stale identity rejection, distinct progress, privacy, bounds and unattributed cause.'
