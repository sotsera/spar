#requires -Version 7.4
param([string]$EvidenceDirectory = 'artifacts/inspection/probe')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
. (Join-Path $PSScriptRoot 'integration/Inspection.ps1')
$repository = Split-Path $PSScriptRoot
$coordinatorPath = (Resolve-Path -LiteralPath 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$applicationPath = (Resolve-Path -LiteralPath 'fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$sessions = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$unrelated = $null

try {
  $unrelated = Start-UnrelatedProcess

  $fixture = New-BackendFixture 'native-inspection' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $component = Join-Path $fixture.Frontend 'src/App.tsx'
  $extra = @'
    <button id="slow" onClick={() => { const end = performance.now() + 1200; while (performance.now() < end) { /* deterministic unfinished renderer work */ } setLocal(value => value + 1); }}>Slow action</button>
    <button id="log-test" onClick={() => { console.log('inspection console marker'); setTimeout(() => { throw new Error('inspection error marker'); }, 0); }}>Log test</button>
    <button id="reload-test" onClick={() => location.reload()}>Reload document</button>
'@
  Replace-Source $component '  return <main>' ('  return <main>' + "`n" + $extra)
  $session = Start-Session $fixture $true $true $false -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'native typed readiness' 120
  Write-InspectionRequest $session @{ command = 'status'; id = 'initial-status' }
  $status = Wait-InspectionResult $session 'initial-status'
  Assert-Condition ($status.code -eq 'ready' -and $status.document -and $status.contract -and $status.processId) 'Status omitted readiness identity.'
  $initial = Inspect $session 'snapshot'
  $initial | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture.Directory 'initial-snapshot.json')
  Assert-Condition ($initial.text.Contains('Hello, Spar')) 'Snapshot did not inspect the rendered greeting.'
  $inputSelector = ($initial.elements | Where-Object tag -eq 'input' | Select-Object -First 1).selector
  Assert-Condition (![string]::IsNullOrWhiteSpace($inputSelector)) 'No editable input in actual DOM snapshot.'
  $null = Inspect $session 'type' $inputSelector 'Text entered by structured inspection'
  $null = Inspect $session 'click' '#local'
  $mutation = New-InspectionRequest $session 'click' '#backend'
  Write-InspectionRequest $session $mutation
  Assert-Condition ((Wait-InspectionResult $session $mutation.id).code -eq 'ok') 'Counter click failed.'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Backend counter: 1') } 'typed counter UI result'
  Write-InspectionRequest $session $mutation
  Wait-Until $session { @($session.Events | Where-Object { $_.correlation -eq $mutation.id -and $_.kind -eq 'command-rejected' -and $_.code -eq 'duplicate_id' }).Count -eq 1 } 'duplicate mutation rejection'
  $null = Inspect $session 'click' '#log-test'
  Wait-Until $session { ((Inspect $session 'logs') | ConvertTo-Json -Depth 6).Contains('inspection error marker') } 'frontend error capture'
  Inspect $session 'logs' | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $fixture.Directory 'frontend-logs.json')

  $wrong = New-InspectionRequest $session 'click' '#backend'
  $wrong.session = 'wrong-session'
  Write-InspectionRequest $session $wrong
  Assert-Condition ((Wait-InspectionResult $session $wrong.id).code -eq 'stale_target') 'Wrong session was admitted.'
  $unknown = New-InspectionRequest $session 'evaluate'
  Write-InspectionRequest $session $unknown
  Assert-Condition ((Wait-InspectionResult $session $unknown.id).code -eq 'invalid_inspection') 'Arbitrary evaluation was accepted.'
  foreach ($case in @('generation', 'contract', 'document', 'selector', 'text', 'timeoutMilliseconds')) {
    $bad = New-InspectionRequest $session 'click' '#backend'
    $bad[$case] = switch ($case) {
      'generation' { 99 }
      'contract' { 'wrong-contract' }
      'document' { 'wrong-document' }
      'selector' { 'x' * 513 }
      'text' { 'x' * 1025 }
      'timeoutMilliseconds' { 1 }
    }
    Write-InspectionRequest $session $bad
    $rejected = Wait-InspectionResult $session $bad.id
    Assert-Condition ($rejected.kind -eq 'command-rejected') "Invalid $case was accepted."
  }

  Replace-Source $component 'A live development loop.' 'An agent verified this edit.'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('An agent verified this edit.') } 'frontend edit through generic snapshot'
  $source = Join-Path $fixture.Backend 'SampleBackend.cs'
  Replace-Source $source '"Hello, " + request.Name' '"Hello inspected, " + request.Name'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello inspected, Spar') } 'backend edit through generic snapshot' 90
  $after = Inspect $session 'snapshot'
  Assert-Condition ($after.text.Contains('Local counter: 1') -and $after.text.Contains('Backend counter: 1')) 'An action was replayed or state was lost.'
  Assert-Condition (@($after.elements | Where-Object value -eq 'Text entered by structured inspection').Count -eq 1) 'Typing/HMR lost the input value.'
  $after | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture.Directory 'edited-snapshot.json')
  $imagePath = Inspect $session 'screenshot'
  Copy-Item -LiteralPath $imagePath -Destination (Join-Path $fixture.Directory 'edited.png')

  Replace-Source $source '"Hello inspected, " + request.Name' 'MissingInspectionGreeting + request.Name'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'watcher-diagnostic' -and $_.detail -like '*CS0103*' }).Count -gt 0 } 'compiler failure' 90
  $diagnosticId = [guid]::NewGuid().ToString('N')
  Write-InspectionRequest $session @{ command = 'diagnostics'; id = $diagnosticId }
  $null = Wait-InspectionResult $session $diagnosticId
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'diagnostic-log' -and $_.correlation -eq $diagnosticId -and $_.detail -like '*CS0103*' }).Count -gt 0) 'Structured diagnostics missed the compiler error.'
  Replace-Source $source 'MissingInspectionGreeting + request.Name' '"Hello recovered inspection, " + request.Name'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello recovered inspection, Spar') } 'compiler correction' 90

  $slow = New-InspectionRequest $session 'click' '#slow' '' 50
  Write-InspectionRequest $session $slow
  Wait-Until $session { @($session.Events | Where-Object { $_.correlation -eq $slow.id -and $_.kind -eq 'command-timeout' }).Count -eq 1 } 'honest unfinished timeout'
  $busy = New-InspectionRequest $session 'click' '#backend'
  Write-InspectionRequest $session $busy
  Assert-Condition ((Wait-InspectionResult $session $busy.id).code -eq 'inspection_busy') 'Timed-out work did not retain its slot.'
  Assert-Condition ((Wait-InspectionResult $session $slow.id).code -eq 'ok') 'Late action completion was not retained.'
  Assert-Condition ((Inspect $session 'snapshot').text.Contains('Local counter: 2')) 'Timed-out click was lost or replayed.'

  $oldDocument = New-InspectionRequest $session 'click' '#backend'
  $reload = New-InspectionRequest $session 'click' '#reload-test'
  Write-InspectionRequest $session $reload
  $null = Wait-InspectionResult $session $reload.id
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.document -ne $oldDocument.document }).Count -gt 0 } 'fresh document in the same process' 60
  Write-InspectionRequest $session $oldDocument
  Assert-Condition ((Wait-InspectionResult $session $oldDocument.id).code -eq 'stale_target') 'Retired same-generation document was admitted.'

  $stale = New-InspectionRequest $session 'click' '#backend'
  Write-InspectionRequest $session @{ command = 'restart'; id = 'inspection-restart' }
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -eq 2 }).Count -eq 1 } 'new generation readiness' 120
  $restart = Wait-InspectionResult $session 'inspection-restart'
  Assert-Condition ($restart.code -eq 'restarted' -and $restart.document -and $restart.contract -and $restart.processId) 'Restart did not complete with replacement identity.'
  Write-InspectionRequest $session $stale
  Assert-Condition ((Wait-InspectionResult $session $stale.id).code -eq 'stale_target') 'Retired document action was admitted.'
  Assert-Condition ((Inspect $session 'snapshot').text.Contains('Backend counter: 0')) 'Stale action mutated the replacement backend.'
  Copy-Item -LiteralPath (Inspect $session 'screenshot') -Destination (Join-Path $fixture.Directory 'restarted.png')
  $closingAction = New-InspectionRequest $session 'click' '#slow' '' 50
  Write-InspectionRequest $session $closingAction
  Wait-Until $session { @($session.Events | Where-Object { $_.correlation -eq $closingAction.id -and $_.kind -eq 'command-timeout' }).Count -eq 1 } 'unfinished action before Stop'
  Write-InspectionRequest $session @{ command = 'stop'; id = 'inspection-stop' }
  $results.Add((Confirm-Exit $session -Unrelated $unrelated))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-stopped' -and $_.correlation -eq 'inspection-stop' -and $_.code -eq 'graceful' }).Count -eq 1) 'Correlated Stop did not establish cleanup.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'command-completed' -and $_.correlation -eq $closingAction.id -and $_.code -in @('document_retired', 'process_ended') }).Count -eq 1) 'Retiring inspection did not receive an honest completion outcome.'
  $results[$results.Count - 1] | Add-Member -NotePropertyName inspection -NotePropertyValue 'actual-native-dom-actions-screenshots-logs-timeout-restart'
  $results[$results.Count - 1].rendering = 'webview-png-captured-review-required'
  Write-Output ('Inspection evidence: ' + $fixture.Directory)

  $fixture = New-Fixture 'inspection-disabled' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture $false $false $false -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'non-inspectable session readiness'
  $disabled = New-InspectionRequest $session 'snapshot'
  Write-InspectionRequest $session $disabled
  Assert-Condition ((Wait-InspectionResult $session $disabled.id).code -eq 'inspection_disabled') 'Inspection did not require explicit owner opt-in.'
  Send-Command $session 'stop'
  $results.Add((Confirm-Exit $session -Unrelated $unrelated))

  $fixture = New-BackendFixture 'failed-restart-completion' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture $true $false $false -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'restart failure fixture readiness' 120
  Replace-Source (Join-Path $fixture.Backend 'SampleBackend.cs') '"Hello, " + request.Name' 'MissingRestartGreeting + request.Name'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'watcher-diagnostic' -and $_.detail -like '*CS0103*' }).Count -gt 0 } 'failed restart source diagnostic' 90
  Write-InspectionRequest $session @{ command = 'restart'; id = 'failed-restart' }
  $failed = Wait-InspectionResult $session 'failed-restart'
  Assert-Condition ($failed.code -eq 'build_failed') 'Failed rebuild lost its correlated completion.'
  Write-InspectionRequest $session @{ command = 'status'; id = 'stopped-status' }
  Assert-Condition ((Wait-InspectionResult $session 'stopped-status').code -eq 'stopped') 'Failed rebuild reported an active application.'
  Send-Command $session 'stop'
  $results.Add((Confirm-Exit $session -Unrelated $unrelated))
} finally {
  try {
    Close-TestSessions $sessions
  } finally {
    try { if ($unrelated) { Stop-TestProcess $unrelated } }
    finally { $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json') }
  }
}
$results | Format-Table
