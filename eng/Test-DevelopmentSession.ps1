#requires -Version 7.4
[CmdletBinding()]
param(
  [string]$Coordinator = 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe',
  [string]$Application = 'fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe',
  [string]$EvidenceDirectory = 'artifacts/development/probe',
  [switch]$VerifyRender
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
$repository = Split-Path $PSScriptRoot
$coordinatorPath = (Resolve-Path -LiteralPath $Coordinator).Path
$applicationPath = (Resolve-Path -LiteralPath $Application).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$sessions = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$unrelated = $null

try {
  $unrelated = Start-UnrelatedProcess

  $fixture = New-Fixture 'hmr-restart-close' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture $true -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'initial native SPA/typed readiness'
  $initial = Latest-Snapshot $session
  Assert-Condition ($initial.greeting -eq 'Hello, Spar') 'The initial typed greeting did not match.'
  $backend = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
  $frontend = $session.Events | Where-Object kind -eq 'frontend-ready' | Select-Object -First 1
  Send-Command $session 'probe' 'seed'
  Wait-Until $session { $state = Latest-Snapshot $session; $state.local -eq 7 -and $state.counter -eq 2 -and $state.text -eq 'Retain this text through HMR' } 'mutated frontend/backend state'
  if ($VerifyRender) {
    $window = [IntPtr]($session.Events | Where-Object kind -eq 'window-created' | Select-Object -Last 1).window
    [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
    Wait-Until $session { [NativeProbeDesktop]::CaptureColor($window, (Join-Path $fixture.Directory 'before.bmp'), 1000, 112, 227, 189) -gt 3000 } 'rendered initial SPA'
  }
  $css = Join-Path $fixture.Frontend 'src/styles.css'
  [IO.File]::WriteAllText($css, [IO.File]::ReadAllText($css).Replace('#70e3bd', '#e9b44c'))
  Wait-Until $session { (Latest-Snapshot $session).color -eq 'rgb(233, 180, 76)' } 'real CSS HMR'
  $component = Join-Path $fixture.Frontend 'src/App.tsx'
  [IO.File]::WriteAllText($component, [IO.File]::ReadAllText($component).Replace('A live development loop.', 'A live HMR update.'))
  Wait-Until $session { (Latest-Snapshot $session).heading -eq 'A live HMR update.' } 'real React Fast Refresh'
  $updated = Latest-Snapshot $session
  Assert-Condition ($updated.mount -eq $initial.mount -and $updated.document -eq $initial.document -and $updated.local -eq 7 -and $updated.counter -eq 2 -and $updated.text -eq 'Retain this text through HMR') 'HMR lost state or replaced the document.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1) 'HMR restarted the backend.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'process-started' -and $_.role -eq 'build' }).Count -eq 1) 'Frontend edits rebuilt C#, or the initial owned build did not run.'
  if ($VerifyRender) {
    Wait-Until $session { [NativeProbeDesktop]::CaptureColor($window, (Join-Path $fixture.Directory 'after.bmp'), 1000, 233, 180, 76) -gt 3000 } 'rendered CSS/component update'
  }
  Send-Command $session 'restart'
  Wait-Until $session { $null -ne (Latest-Snapshot $session 2) } 'new application generation'
  $restarted = Latest-Snapshot $session 2
  $newBackend = $session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -eq 2 } | Select-Object -Last 1
  Assert-Condition ($newBackend.processId -ne $backend.processId -and $restarted.counter -eq 0) 'Backend restart did not produce new state/identity.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'frontend-ready').Count -eq 1) 'Planned backend replacement restarted the frontend.'
  $frontendHandle = $session.Handles.Values | Where-Object Id -eq $frontend.processId | Select-Object -First 1
  Assert-Condition ($frontendHandle -and !$frontendHandle.HasExited) 'The original frontend did not survive backend replacement.'
  $window = [IntPtr]($session.Events | Where-Object { $_.kind -eq 'window-created' -and $_.generation -eq 2 } | Select-Object -Last 1).window
  [NativeProbeDesktop]::Close($window)
  $results.Add((Confirm-Exit $session -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))

  $fixture = New-Fixture 'owner-pipe-eof' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'owner-loss fixture readiness'
  $session.Process.StandardInput.Close()
  $results.Add((Confirm-Exit $session -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object kind -eq 'forced-cleanup-requested').Count -eq 1) 'Owner loss did not use the explicit forced policy.'

  $fixture = New-Fixture 'malformed-owner-input' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'malformed-owner fixture readiness'
  $session.Process.StandardInput.BaseStream.Write([byte[]]@(255,10))
  $session.Process.StandardInput.BaseStream.Flush()
  $results.Add((Confirm-Exit $session -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-stopped' -and $_.detail -eq 'owner_input_failed' -and $_.code -eq 'forced_cleanup' }).Count -eq 1) 'Invalid UTF-8 owner input did not end ownership with an explicit forced receipt.'

  $fixture = New-Fixture 'abrupt-owner-kill' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $tree = Join-Path $fixture.Directory 'helper-tree.jsonl'
  $fixture.Settings.frontend.fileName = 'node'
  $fixture.Settings.frontend.arguments = @((Join-Path $PSScriptRoot 'development-probe/frontend-wrapper.mjs'))
  $fixture.Settings.frontend.environment = @{ SPAR_TEST_TREE = $tree; SPAR_TEST_VITE = Join-Path $repository 'fixtures/Spar.NativeSample/frontend/node_modules/vite/bin/vite.js' }
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) -and (Test-Path -LiteralPath $tree) -and @(Get-Content -LiteralPath $tree).Count -ge 2 } 'owned helper/grandchild and real frontend'
  Drain-Events $session
  foreach ($entry in Get-Content -LiteralPath $tree | ForEach-Object { $_ | ConvertFrom-Json }) {
    Assert-Condition (@($session.Handles.Values | Where-Object Id -eq $entry.pid).Count -eq 1) 'A long-running helper was not independently observed before owner termination.'
  }
  $session.Process.Kill() # Kill only the coordinator; descendants must exit through the kernel job safeguard.
  $results.Add((Confirm-Exit $session 0 $true -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))

  $fixture = New-Fixture 'frontend-exit' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'frontend-exit fixture readiness'
  $frontend = $session.Events | Where-Object kind -eq 'frontend-ready' | Select-Object -First 1
  $owned = $session.Handles.Values | Where-Object Id -eq $frontend.processId | Select-Object -First 1
  $owned.Kill()
  $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -in @('frontend_exited','frontend_disconnected') }).Count -eq 1) 'Frontend exit had no distinct diagnostic.'

  $fixture = New-Fixture 'application-exit' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'application-exit fixture readiness'
  $readyApplication = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
  $owned = $session.Handles.Values | Where-Object Id -eq $readyApplication.processId | Select-Object -First 1
  $owned.Kill()
  $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'application_exited' }).Count -eq 1) 'Application crash was mistaken for ordinary native close.'

  $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
  $listener.ExclusiveAddressUse = $true
  $listener.Start()
  try {
    $fixture = New-Fixture 'port-conflict' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
    $fixture.Settings.frontend.port = $listener.LocalEndpoint.Port
    $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
    $sessions.Add($session)
    $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
    Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'frontend_port_conflict' }).Count -eq 1) 'Port conflict was not diagnosed.'
    Assert-Condition (@($session.Events | Where-Object kind -eq 'process-started').Count -eq 0) 'A known port conflict started owned processes.'
  } finally { $listener.Stop() }

  $fixture = New-Fixture 'startup-timeout' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $fixture.Settings.startupTimeoutSeconds = 2
  $fixture.Settings.frontend.fileName = 'node'
  $fixture.Settings.frontend.arguments = @('-e','setInterval(() => {}, 1000)')
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'frontend_startup_timeout' }).Count -eq 1) 'Startup timeout had no distinct diagnostic.'

  $fixture = New-Fixture 'application-start-failure' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $fixture.Settings.application = Join-Path $fixture.Directory 'missing-application.exe'
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'application_launch_failed' }).Count -eq 1) 'Application launch failure had no distinct diagnostic.'

  $fixture = New-Fixture 'client-credential-denied' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
  $main = Join-Path $fixture.Frontend 'src/main.tsx'
  [IO.File]::WriteAllText($main, [IO.File]::ReadAllText($main).Replace('await connect(credential)', "await connect('0'.repeat(64))"))
  $session = Start-Session $fixture -Coordinator $coordinatorPath -WorkingDirectory $repository
  $sessions.Add($session)
  $results.Add((Confirm-Exit $session 1 -Unrelated $unrelated -VerifyRender:($VerifyRender -and $fixture.Name -eq 'hmr-restart-close')))
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'client_unauthorized' }).Count -eq 1) 'A wrong renderer capability was not rejected.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'bridge-connected').Count -eq 0) 'An unauthorized client bound the bridge.'
} finally {
  try {
    Close-TestSessions $sessions
  } finally {
    try { if ($unrelated) { Stop-TestProcess $unrelated } }
    finally { $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json') }
  }
}
$results | Format-Table
