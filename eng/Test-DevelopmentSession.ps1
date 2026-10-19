#requires -Version 7.4
param(
  [string]$Coordinator = 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe',
  [string]$Application = 'samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe',
  [string]$EvidenceDirectory = 'artifacts/development/probe',
  [switch]$VerifyRender
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$coordinatorPath = (Resolve-Path -LiteralPath $Coordinator).Path
$applicationPath = (Resolve-Path -LiteralPath $Application).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$sessions = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$unrelated = $null

function Read-Shared([string]$Path) {
  $stream = [IO.FileStream]::new($Path, 'Open', 'Read', 'ReadWrite')
  $reader = [IO.StreamReader]::new($stream)
  try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function New-Fixture([string]$Name) {
  $directory = Join-Path $evidencePath ($Name + '-' + [guid]::NewGuid().ToString('N'))
  $frontend = Join-Path $directory 'frontend'
  New-Item -ItemType Directory -Path $frontend -Force | Out-Null
  $original = Join-Path $repository 'samples/Spar.NativeSample/frontend'
  foreach ($file in @('package.json','package-lock.json','tsconfig.json','index.html')) {
    Copy-Item -LiteralPath (Join-Path $original $file) -Destination $frontend
  }
  Copy-Item -LiteralPath (Join-Path $original 'src') -Destination $frontend -Recurse
  New-Item -ItemType Junction -Path (Join-Path $frontend 'node_modules') -Target (Join-Path $original 'node_modules') | Out-Null
  Copy-Item -LiteralPath (Join-Path $repository 'tools/Spar.Dev/frontend/vite-session.mjs') -Destination $frontend
  $config = [IO.File]::ReadAllText((Join-Path $original 'vite.config.mjs')).Replace('../../../tools/Spar.Dev/frontend/vite-session.mjs', './vite-session.mjs')
  [IO.File]::WriteAllText((Join-Path $frontend 'vite.config.mjs'), $config)
  $settings = [ordered]@{
    project = Join-Path $repository 'samples/Spar.NativeSample/Spar.NativeSample.csproj'
    application = $applicationPath
    generatedClient = Join-Path $frontend 'src/generated/client.js'
    sessionDirectory = Join-Path $directory 'sessions'
    startupTimeoutSeconds = 30; shutdownTimeoutSeconds = 3
    frontend = [ordered]@{ fileName = 'cmd.exe'; arguments = @('/d','/c','npm','run','dev'); workingDirectory = $frontend; port = 0; environment = @{} }
  }
  [pscustomobject]@{ Name = $Name; Directory = $directory; Frontend = $frontend; Settings = $settings; Config = (Join-Path $directory 'spar.dev.json') }
}

function Start-Session($Fixture, [bool]$Build = $false) {
  $Fixture.Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Fixture.Config
  $start = [Diagnostics.ProcessStartInfo]::new($coordinatorPath)
  $start.WorkingDirectory = $repository
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($arg in @($Fixture.Config,'--owner-stdin','--probe')) { $start.ArgumentList.Add($arg) }
  if (!$Build) { $start.ArgumentList.Add('--no-build') }
  $process = [Diagnostics.Process]::Start($start)
  $null = $process.Handle
  $session = [pscustomobject]@{
    Fixture = $Fixture; Process = $process; Started = $process.StartTime.ToUniversalTime()
    Events = [Collections.Generic.List[object]]::new(); Handles = [Collections.Generic.Dictionary[string,Diagnostics.Process]]::new()
    Pending = $process.StandardOutput.ReadLineAsync(); Errors = $process.StandardError.ReadToEndAsync(); Ended = $false
    Log = Join-Path $Fixture.Directory 'coordinator.jsonl'; Passed = $false
  }
  $sessions.Add($session)
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

function Confirm-Exit($Session, [int]$ExpectedCode = 0, [bool]$ForcedOwner = $false) {
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
  Assert-Condition (!$unrelated.HasExited) 'An unrelated process was terminated.'
  $Session.Passed = $true
  $results.Add([pscustomobject]@{
    scenario = $Session.Fixture.Name; passed = $true; coordinatorProcessId = $Session.Process.Id; startedAtUtc = $Session.Started
    actualExitCode = $Session.Process.ExitCode; abruptOwnerKill = $ForcedOwner
    forcedCleanupRequested = @($Session.Events | Where-Object kind -eq 'forced-cleanup-requested').Count -gt 0
    independentlyRetainedProcesses = $Session.Handles.Count; unrelatedSurvived = $true
    rendering = $(if ($VerifyRender -and $Session.Fixture.Name -eq 'hmr-restart-close') { 'verified-pixels-review-captures' } else { 'not-exercised' })
  })
}

try {
  $unrelatedStart = [Diagnostics.ProcessStartInfo]::new('node')
  $unrelatedStart.UseShellExecute = $false
  $unrelatedStart.CreateNoWindow = $true
  $unrelatedStart.ArgumentList.Add('-e')
  $unrelatedStart.ArgumentList.Add('setInterval(() => {}, 1000)')
  $unrelated = [Diagnostics.Process]::Start($unrelatedStart)
  $null = $unrelated.Handle

  $fixture = New-Fixture 'hmr-restart-close'
  $session = Start-Session $fixture $true
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
  Confirm-Exit $session

  $fixture = New-Fixture 'owner-pipe-eof'
  $session = Start-Session $fixture
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'owner-loss fixture readiness'
  $session.Process.StandardInput.Close()
  Confirm-Exit $session
  Assert-Condition (@($session.Events | Where-Object kind -eq 'forced-cleanup-requested').Count -eq 1) 'Owner loss did not use the explicit forced policy.'

  $fixture = New-Fixture 'malformed-owner-input'
  $session = Start-Session $fixture
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'malformed-owner fixture readiness'
  $session.Process.StandardInput.BaseStream.Write([byte[]]@(255,10))
  $session.Process.StandardInput.BaseStream.Flush()
  Confirm-Exit $session
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-stopped' -and $_.detail -eq 'owner_input_failed' -and $_.code -eq 'forced_cleanup' }).Count -eq 1) 'Invalid UTF-8 owner input did not end ownership with an explicit forced receipt.'

  $fixture = New-Fixture 'abrupt-owner-kill'
  $tree = Join-Path $fixture.Directory 'helper-tree.jsonl'
  $fixture.Settings.frontend.fileName = 'node'
  $fixture.Settings.frontend.arguments = @((Join-Path $PSScriptRoot 'development-probe/frontend-wrapper.mjs'))
  $fixture.Settings.frontend.environment = @{ SPAR_TEST_TREE = $tree; SPAR_TEST_VITE = Join-Path $repository 'samples/Spar.NativeSample/frontend/node_modules/vite/bin/vite.js' }
  $session = Start-Session $fixture
  Wait-Until $session { $null -ne (Latest-Snapshot $session) -and (Test-Path -LiteralPath $tree) -and @(Get-Content -LiteralPath $tree).Count -ge 2 } 'owned helper/grandchild and real frontend'
  Drain-Events $session
  foreach ($entry in Get-Content -LiteralPath $tree | ForEach-Object { $_ | ConvertFrom-Json }) {
    Assert-Condition (@($session.Handles.Values | Where-Object Id -eq $entry.pid).Count -eq 1) 'A long-running helper was not independently observed before owner termination.'
  }
  $session.Process.Kill() # Kill only the coordinator; descendants must exit through the kernel job safeguard.
  Confirm-Exit $session 0 $true

  $fixture = New-Fixture 'frontend-exit'
  $session = Start-Session $fixture
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'frontend-exit fixture readiness'
  $frontend = $session.Events | Where-Object kind -eq 'frontend-ready' | Select-Object -First 1
  $owned = $session.Handles.Values | Where-Object Id -eq $frontend.processId | Select-Object -First 1
  $owned.Kill()
  Confirm-Exit $session 1
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -in @('frontend_exited','frontend_disconnected') }).Count -eq 1) 'Frontend exit had no distinct diagnostic.'

  $fixture = New-Fixture 'application-exit'
  $session = Start-Session $fixture
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'application-exit fixture readiness'
  $readyApplication = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
  $owned = $session.Handles.Values | Where-Object Id -eq $readyApplication.processId | Select-Object -First 1
  $owned.Kill()
  Confirm-Exit $session 1
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'application_exited' }).Count -eq 1) 'Application crash was mistaken for ordinary native close.'

  $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
  $listener.ExclusiveAddressUse = $true
  $listener.Start()
  try {
    $fixture = New-Fixture 'port-conflict'
    $fixture.Settings.frontend.port = $listener.LocalEndpoint.Port
    $session = Start-Session $fixture
    Confirm-Exit $session 1
    Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'frontend_port_conflict' }).Count -eq 1) 'Port conflict was not diagnosed.'
    Assert-Condition (@($session.Events | Where-Object kind -eq 'process-started').Count -eq 0) 'A known port conflict started owned processes.'
  } finally { $listener.Stop() }

  $fixture = New-Fixture 'startup-timeout'
  $fixture.Settings.startupTimeoutSeconds = 2
  $fixture.Settings.frontend.fileName = 'node'
  $fixture.Settings.frontend.arguments = @('-e','setInterval(() => {}, 1000)')
  $session = Start-Session $fixture
  Confirm-Exit $session 1
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'frontend_startup_timeout' }).Count -eq 1) 'Startup timeout had no distinct diagnostic.'

  $fixture = New-Fixture 'application-start-failure'
  $fixture.Settings.application = Join-Path $fixture.Directory 'missing-application.exe'
  $session = Start-Session $fixture
  Confirm-Exit $session 1
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'application_launch_failed' }).Count -eq 1) 'Application launch failure had no distinct diagnostic.'

  $fixture = New-Fixture 'client-credential-denied'
  $main = Join-Path $fixture.Frontend 'src/main.tsx'
  [IO.File]::WriteAllText($main, [IO.File]::ReadAllText($main).Replace('await connect(credential)', "await connect('0'.repeat(64))"))
  $session = Start-Session $fixture
  Confirm-Exit $session 1
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-failed' -and $_.code -eq 'client_unauthorized' }).Count -eq 1) 'A wrong renderer capability was not rejected.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'bridge-connected').Count -eq 0) 'An unauthorized client bound the bridge.'
} finally {
  foreach ($session in $sessions) {
    if (!$session.Process.HasExited) { $session.Process.Kill(); $session.Process.WaitForExit(10000) | Out-Null }
    foreach ($owned in $session.Handles.Values) { $owned.WaitForExit(10000) | Out-Null; $owned.Dispose() }
    $session.Process.Dispose()
  }
  if ($unrelated) {
    if (!$unrelated.HasExited) { $unrelated.Kill(); $unrelated.WaitForExit(10000) | Out-Null }
    $unrelated.Dispose()
  }
  $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json')
}
$results | Format-Table
