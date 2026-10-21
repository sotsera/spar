#requires -Version 7.4
param([string]$EvidenceDirectory = 'artifacts/minimal/readiness')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
. (Join-Path $PSScriptRoot 'integration/Inspection.ps1')
$repository = Split-Path $PSScriptRoot
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$application = Join-Path $repository 'samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
$fixture = New-BackendFixture 'readiness-boundary' -Repository $repository -Application $application -EvidenceDirectory $evidence -MinimalApplication
$component = Join-Path $fixture.Frontend 'src/App.tsx'
$source = Join-Path $fixture.Backend 'SampleBackend.cs'
Replace-Source $source '        return Task.FromResult(new GreetingReply' ('        if (request.Name == "Fail") { Console.WriteLine("readiness-failure-entered"); throw new InvalidOperationException("Expected fixture failure."); }' + "`n" + '        return Task.FromResult(new GreetingReply')
Replace-Source $component "name: 'Spar'" "name: 'Fail'"
Replace-Source $component '  return <main>' @'
  return <main>
    <button id="reload-readiness" onClick={() => location.reload()}>Reload fixture document</button>
'@
# No successful result and no native scheduling token: this must not establish readiness.
$index = Join-Path $fixture.Frontend 'index.html'
Replace-Source $index '</head>' '<script>window.chrome.webview.postMessage("spar-development-ready:00000000000000000000000000000000")</script></head>'
$fixture.Settings.startupTimeoutSeconds = 90
$sessions = [Collections.Generic.List[object]]::new()
$unrelated = $null
try {
  $unrelated = Start-UnrelatedProcess
  $session = Start-Session $fixture $true $true $false -Coordinator (Join-Path $repository 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe') -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'bridge-connected').Count -eq 1 } 'native bridge without readiness' 120
  $identity = $session.Events | Where-Object kind -eq 'bridge-connected' | Select-Object -Last 1
  $watchLog = Join-Path $fixture.Settings.sessionDirectory "$($identity.session)/watcher-1.stdout.log"
  Wait-Until $session { (Test-Path -LiteralPath $watchLog) -and (Read-Shared $watchLog).Contains('readiness-failure-entered') } 'actual failing handler entry'
  $deadline = [DateTime]::UtcNow.AddSeconds(2)
  while ([DateTime]::UtcNow -lt $deadline) { Drain-Events $session; Start-Sleep -Milliseconds 50 }
  Assert-Condition (@($session.Events | Where-Object kind -eq 'application-ready').Count -eq 0) 'Failed typed call or forged frame signal declared readiness.'
  Replace-Source $component "name: 'Fail'" "name: 'Spar'"
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'successful result and native frame' 60
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello, Spar') } 'actual greeting after failure'
  $old = New-InspectionRequest $session 'click' '#backend'
  $null = Inspect $session 'click' '#reload-readiness'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.document -ne $old.document }).Count -eq 1 } 'new native document readiness'
  Write-InspectionRequest $session $old
  Assert-Condition ((Wait-InspectionResult $session $old.id).code -eq 'stale_target') 'Old ready document admitted a mutation.'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Backend counter: 0') } 'replacement document without stale mutation'
  Send-Command $session 'stop'
  $receipt = Confirm-Exit $session -Unrelated $unrelated
  $receipt | Add-Member -NotePropertyName assertions -NotePropertyValue 'failed call and forged signal not ready; successful call/frame ready; reload retires target'
  $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')

  $fixture = New-Fixture 'minimal-inspection-disabled' -Repository $repository -Application $application -EvidenceDirectory $evidence -MinimalApplication
  $session = Start-Session $fixture $false $false $false -Coordinator (Join-Path $repository 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe') -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'minimal readiness without inspection'
  $disabled = New-InspectionRequest $session 'snapshot'
  Write-InspectionRequest $session $disabled
  Assert-Condition ((Wait-InspectionResult $session $disabled.id).code -eq 'inspection_disabled') 'Minimal startup enabled inspection without owner opt-in.'
  Send-Command $session 'stop'
  $receipt = Confirm-Exit $session -Unrelated $unrelated
  $receipt | Add-Member -NotePropertyName assertions -NotePropertyValue 'minimal startup ready with inspection disabled'
  $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'inspection-disabled.json')
} finally {
  try { Close-TestSessions $sessions }
  finally { if ($unrelated) { Stop-TestProcess $unrelated } }
}
