#requires -Version 7.4
[CmdletBinding()]
param(
  [string]$EvidenceDirectory = 'artifacts/development-latency/probe',
  [ValidateRange(2, 10)][int]$Sessions = 3,
  [ValidateRange(1, 10)][int]$EditsPerSession = 3
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
$repository = Split-Path $PSScriptRoot
$coordinator = (Resolve-Path 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$application = (Resolve-Path 'fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $root | Out-Null
$owned = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
$receipts = [Collections.Generic.List[object]]::new()
$timelines = [Collections.Generic.List[object]]::new()
$watcherBuilds = [Collections.Generic.List[object]]::new()
$unrelated = $null
$completed = $false
$sessionsClosed = $false
$sentinelClosed = $false
$source = & git rev-parse HEAD
if ($LASTEXITCODE) { throw 'Cannot identify measured revision.' }
$dirty = [bool](& git status --porcelain)
$tooling = @{ dotnet = (& dotnet --version); node = (& node --version); npm = (& npm --version)
  powershell = $PSVersionTable.PSVersion.ToString(); os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
  processor = $env:PROCESSOR_IDENTIFIER; coordinatorSha256 = (Get-FileHash $coordinator).Hash
  developmentSha256 = (Get-FileHash (Join-Path (Split-Path $coordinator) 'Sotsera.Spar.Development.dll')).Hash
  driverSha256 = (Get-FileHash $PSCommandPath).Hash }

function Add-Sample([string]$Metric, [int]$Run, [int]$Edit, [long]$Started) {
  $samples.Add([pscustomobject]@{ metric = $Metric; run = $Run; edit = $Edit
    elapsedMs = [Math]::Round([Diagnostics.Stopwatch]::GetElapsedTime($Started).TotalMilliseconds, 2) })
}

try {
  $unrelated = Start-UnrelatedProcess
  $fixture = New-BackendFixture 'latency' -Repository $repository -Application $application -EvidenceDirectory $root
  $css = Join-Path $fixture.Frontend 'src/styles.css'
  $backend = Join-Path $fixture.Backend 'SampleBackend.cs'
  $startup = Join-Path $fixture.Backend 'Program.Development.cs'
  $oldColor = '#70e3bd'
  $oldGreeting = '"Hello, " + request.Name'
  $oldTitle = 'Title = "Spar development"'
  for ($run = 1; $run -le $Sessions; $run++) {
    $runDirectory = Join-Path $fixture.Directory "run-$run"
    New-Item -ItemType Directory $runDirectory | Out-Null
    $runFixture = [pscustomobject]@{ Name = "latency-$run"; Directory = $runDirectory
      Settings = $fixture.Settings; Config = (Join-Path $runDirectory 'spar.dev.json') }
    $started = [Diagnostics.Stopwatch]::GetTimestamp()
    $session = Start-Session $runFixture $true -Coordinator $coordinator -WorkingDirectory $repository
    $owned.Add($session)
    Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'native DOM/typed readiness' 120
    $cache = if ($run -eq 1) { 'first' } else { 'repeated' }
    Add-Sample "startup-$cache-ready" $run 0 $started
    Wait-Until $session { @($session.Events | Where-Object kind -eq 'watcher-ready').Count -eq 1 } 'watcher update readiness' 120
    Add-Sample "startup-$cache-update-ready" $run 0 $started
    $initial = Latest-Snapshot $session
    $firstApplication = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
    $frontend = $session.Events | Where-Object kind -eq 'frontend-ready' | Select-Object -First 1
    $timelines.Add(@{ run = $run; events = @($session.Events | Where-Object {
      $_.kind -in @('session-started', 'session-phase', 'build-selected', 'frontend-ready', 'watcher-ready', 'application-ready')
    }) })
    Send-Command $session 'probe' 'seed'
    Wait-Until $session { $state = Latest-Snapshot $session; $state.counter -eq 2 -and $state.local -eq 7 } 'seeded state'
    for ($edit = 1; $edit -le $EditsPerSession; $edit++) {
      $newColor = if ($oldColor -eq '#70e3bd') { '#e9b44c' } else { '#70e3bd' }
      $rgb = if ($newColor -eq '#e9b44c') { 'rgb(233, 180, 76)' } else { 'rgb(112, 227, 189)' }
      $started = [Diagnostics.Stopwatch]::GetTimestamp()
      Replace-Source $css $oldColor $newColor
      Wait-Until $session { (Latest-Snapshot $session).color -eq $rgb } 'changed native DOM computed style'
      Add-Sample 'css-save-to-dom' $run $edit $started
      $oldColor = $newColor

      $greeting = "Hello latency $run/$edit, "
      $newGreeting = '"' + $greeting + '" + request.Name'
      $started = [Diagnostics.Stopwatch]::GetTimestamp()
      Replace-Source $backend $oldGreeting $newGreeting
      Wait-Until $session { (Latest-Snapshot $session).greeting -eq ($greeting + 'Spar') } 'changed backend read in native DOM' 90
      Add-Sample 'backend-save-to-dom' $run $edit $started
      $oldGreeting = $newGreeting
      $state = Latest-Snapshot $session
      Assert-Condition ($state.document -eq $initial.document -and $state.mount -eq $initial.mount -and
        $state.counter -eq 2 -and $state.local -eq 7) 'An in-process edit lost state or replaced the native document.'
      Assert-Condition (@($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1) 'A supported edit restarted the application.'
    }

    # A real startup source save selects a coordinated rebuild, not an explicit restart command.
    $newTitle = 'Title = "Spar latency ' + $run + '"'
    $started = [Diagnostics.Stopwatch]::GetTimestamp()
    Replace-Source $startup $oldTitle $newTitle
    Wait-Until $session { $null -ne (Latest-Snapshot $session 2) } 'startup-save replacement native DOM/typed readiness' 120
    Add-Sample 'startup-save-to-replacement-dom' $run 1 $started
    $oldTitle = $newTitle
    $replacement = Latest-Snapshot $session 2
    $oldProcess = $session.Handles.Values | Where-Object Id -eq $firstApplication.processId | Select-Object -First 1
    Assert-Condition ($oldProcess -and $oldProcess.HasExited -and $replacement.counter -eq 0 -and
      $replacement.document -ne $initial.document) 'Replacement did not retire the old process and reset state.'
    Assert-Condition (@($session.Events | Where-Object kind -eq 'frontend-ready').Count -eq 1) 'Backend replacement restarted the frontend.'
    $frontendProcess = $session.Handles.Values | Where-Object Id -eq $frontend.processId | Select-Object -First 1
    Assert-Condition ($frontendProcess -and !$frontendProcess.HasExited) 'Original frontend did not survive replacement.'
    Send-Command $session 'stop'
    $receipts.Add((Confirm-Exit $session -Unrelated $unrelated))
    $sessionDirectory = ($session.Events | Where-Object kind -eq 'session-started' | Select-Object -First 1).detail
    foreach ($log in Get-ChildItem -LiteralPath $sessionDirectory -Filter 'watcher-*.stderr.log') {
      $commands = @{}
      foreach ($line in Get-Content -LiteralPath $log.FullName) {
        # Diagnostic attribution for the pinned SDK; never a readiness or acceptance condition.
        if ($line -match "arguments '(restore|build) .*: process id (\d+)$") {
          $commands[$Matches[2]] = $Matches[1]
        } elseif ($line -match 'Process id (\d+) ran for (\d+)ms and exited with exit code (\d+)\.') {
          $id = $Matches[1]
          if ($commands.ContainsKey($id)) {
            $watcherBuilds.Add(@{ run = $run; watcher = $log.BaseName; command = $commands[$id]
              elapsedMs = [int]$Matches[2]; exitCode = [int]$Matches[3] })
          }
        }
      }
    }
    Write-Output "Measured session $run/$Sessions; actual owned exit verified."
  }
  $completed = $true
} finally {
  try {
    Close-TestSessions $owned
    $sessionsClosed = $true
  } finally {
    try {
      if ($unrelated) { Stop-TestProcess $unrelated }
      $sentinelClosed = $true
    } finally {
      $summary = @($samples | Group-Object metric | ForEach-Object {
        $values = @($_.Group.elapsedMs | Sort-Object)
        $middle = [int][Math]::Floor($values.Count / 2)
        $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
        [pscustomobject]@{ metric = $_.Name; count = $values.Count; minMs = $values[0]; medianMs = $median; maxMs = $values[-1] }
      })
      @{ schema = 1; source = $source; workingTreeDirty = $dirty; tooling = $tooling
        passed = ($completed -and $sessionsClosed -and $sentinelClosed -and $receipts.Count -eq $Sessions)
        clock = 'Stopwatch; includes file write, 50ms observer polling and public event logging'
        cache = 'Shared tools/dependencies prebuilt; isolated app first build, then same app incremental builds; fresh session profile each time; no OS/cache reset'
        endpoint = 'Typed UI snapshot after committed native WebView DOM/layout opportunity; not pixel latency'
        samples = $samples.ToArray(); summary = $summary; startupTimelines = $timelines.ToArray()
        watcherBuilds = $watcherBuilds.ToArray()
        cleanup = $receipts.ToArray(); sessionsClosed = $sessionsClosed; sentinelClosed = $sentinelClosed
      } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $root 'receipt.json')
    }
  }
}
$summary | Sort-Object metric | Format-Table metric, count, minMs, medianMs, maxMs
