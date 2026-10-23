#requires -Version 7.4
param(
  [string]$PublishDirectory = 'artifacts/production/publish',
  [string]$EvidenceDirectory = 'artifacts/production/acceptance',
  [ValidateRange(1, 10)][int]$Samples = 3,
  [ValidateSet('NativeAOT', 'CoreCLR')][string]$ExpectedMode = 'NativeAOT',
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$copy = Join-Path ([IO.Path]::GetTempPath()) ('spar-production-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $copy | Out-Null
Get-ChildItem -LiteralPath $publish | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $copy -Recurse
$expected = @('Spar.NativeSample.exe', 'WebView2Loader.dll', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt', 'spar-publish.json', 'frontend-LICENSES.txt')
if ($ExpectedMode -eq 'NativeAOT') {
  if (Compare-Object @((Get-ChildItem -LiteralPath $copy -File).Name) $expected) { throw 'Unexpected production payload.' }
} else {
  foreach ($name in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Spar.NativeSample.runtimeconfig.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $copy $name))) { throw "Self-contained CoreCLR payload missing $name" }
  }
  $runtime = Get-Content -LiteralPath (Join-Path $copy 'Spar.NativeSample.runtimeconfig.json') -Raw | ConvertFrom-Json
  if ($runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'CoreCLR depends on an installed runtime.' }
  if (Get-ChildItem -LiteralPath $copy -Recurse -File | Where-Object Name -Match 'Spar.Development|Spar.Dev\.|CodeAnalysis|ContractGenerator') {
    throw 'Development/compiler tooling shipped in the compatibility payload.'
  }
}
$notices = Get-Content -LiteralPath (Join-Path $copy 'frontend-LICENSES.txt') -Raw
foreach ($package in @('react', 'react-dom', 'scheduler', 'vite')) {
  $sections = @([regex]::Matches($notices, "(?ms)^## $package - [^\r\n]+\r?\n.*?(?=^## |\z)"))
  if ($sections.Count -ne 1 -or $sections[0].Value -notmatch 'Copyright' -or
      $sections[0].Value -notmatch 'Permission is hereby granted' -or
      $sections[0].Value -notmatch 'SOFTWARE\.') { throw "Missing or incomplete bundled notice: $package" }
}
$publication = Get-Content -LiteralPath (Join-Path $copy 'spar-publish.json') -Raw | ConvertFrom-Json
if ($publication.mode -ne $ExpectedMode -or !$publication.selfContained) { throw 'Unexpected publication mode.' }
$expectedFiles = @($publication.payload | Where-Object name -NotLike '*.pdb' | Select-Object -ExpandProperty name)
$actualFiles = @(Get-ChildItem -LiteralPath $copy -Recurse -File | Where-Object Name -ne 'spar-publish.json' |
  ForEach-Object { [IO.Path]::GetRelativePath($copy, $_.FullName) })
if (Compare-Object $actualFiles $expectedFiles) { throw 'Copied payload inventory does not match its receipt.' }
$payloadBytes = (Get-ChildItem -LiteralPath $copy -Recurse -File | Measure-Object Length -Sum).Sum
foreach ($file in $publication.payload | Where-Object name -NotLike '*.pdb') {
  if ((Get-FileHash -LiteralPath (Join-Path $copy $file.name)).Hash -ne $file.sha256) { throw 'Publication hash mismatch.' }
}
$exe = Join-Path $copy 'Spar.NativeSample.exe'
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$results = [Collections.Generic.List[object]]::new()
$allPassed = $false
. (Join-Path $PSScriptRoot 'Read-ProbeEvents.ps1')
. (Join-Path $PSScriptRoot 'integration/ProductionObservation.ps1')
$waitState = @{ phase = 'starting-process' }
function Wait-Until([scriptblock]$condition, [string]$description) {
  $waitState.phase = $description
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do { if (& $condition) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $description. Actual completion is unproven."
}
try {
  foreach ($sample in 1..$Samples) {
    $waitState.phase = 'starting-process'
    $stdout = Join-Path $evidence "run-$sample.jsonl"
    $profile = Join-Path $copy 'profile'
    $process = $null
    $browsers = [Collections.Generic.List[Diagnostics.Process]]::new()
    $passed = $false
    $forced = $false
    $readyMs = $null
    $processToGreetingMs = $null
    $loaded = $null
    $hostMemory = $null
    $memory = @()
    $startup = $null
    $failure = $null
    $renderCaptured = $false
    $rootExitObserved = $null
    $cleanupError = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
      $process = Start-Process -FilePath $exe -WorkingDirectory $copy -ArgumentList ('"' + $profile + '"') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $evidence "run-$sample.log")
      $null = $process.Handle
      $startup = New-ProductionObservation $process
      Wait-Until {
        if ($process.HasExited) { throw "Production exited: $(Get-Content -LiteralPath $stdout -Raw)" }
        $current = @(Read-ProbeEvents $stdout)
        Update-ProductionObservation $startup $process $watch.Elapsed.TotalMilliseconds -Events $current
        @($current | Where-Object { $_.Stage -eq 'bridge-completed' -and $_.Detail -like '*|greet|*' }).Count -gt 0 `
          -and @($current | Where-Object Stage -eq 'document-loaded').Count -gt 0
      } 'typed greeting completion'
      $waitState.phase = 'validating-greeting'
      $readyMs = $watch.Elapsed.TotalMilliseconds
      $events = @(Read-ProbeEvents $stdout)
      Update-ProductionObservation $startup $process $watch.Elapsed.TotalMilliseconds -Events $events -Force
      $greeted = $events | Where-Object { $_.Stage -eq 'bridge-completed' -and $_.Detail -like '*|greet|*' } | Select-Object -First 1
      $processToGreetingMs = ([DateTimeOffset]$greeted.ObservedAt - [DateTimeOffset]$greeted.ProcessStartedAtUtc).TotalMilliseconds
      if (@($events | Where-Object Mode -ne $ExpectedMode).Count -gt 0) { throw 'Unexpected runtime mode.' }
      if (@($events | Where-Object Stage -eq 'bridge-rejected').Count -gt 0) { throw 'Production bridge rejected a normal call.' }
      $loaded = $events | Where-Object Stage -eq 'document-loaded' | Select-Object -Last 1
      $window = [IntPtr]$loaded.Window
      if ($VerifyRender) {
        [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
        Wait-Until { [NativeProbeDesktop]::Capture($window, (Join-Path $evidence "run-$sample.bmp"), 1000) -gt 500 } 'production pixels'
        $renderCaptured = $true
      }
      # Attribute the browser by its profile, then retain and verify creation identities for descendants.
      $waitState.phase = 'observing-descendants'
      $all = @(Get-CimInstance Win32_Process)
      $ids = [Collections.Generic.HashSet[int]]::new()
      $null = $ids.Add($process.Id)
      do {
        $added = $false
        foreach ($item in $all) {
          $parent = $all | Where-Object ProcessId -eq $item.ParentProcessId | Select-Object -First 1
          if ($parent -and $ids.Contains([int]$parent.ProcessId) -and $item.CreationDate -ge $parent.CreationDate `
              -and $ids.Add([int]$item.ProcessId)) { $added = $true }
        }
      } while ($added)
      $owned = @($all | Where-Object { $ids.Contains([int]$_.ProcessId) })
      if (@($owned | Where-Object Name -Match '^(node|dotnet|Spar.Dev)\.exe$').Count) { throw 'A development process shipped or launched.' }
      $memory = @($owned | Where-Object Name -eq 'msedgewebview2.exe' | ForEach-Object {
        $observed = $_
        $browser = [Diagnostics.Process]::GetProcessById([int]$observed.ProcessId)
        $null = $browser.Handle
        if ([Math]::Abs(($browser.StartTime.ToUniversalTime() - $observed.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
          $browser.Dispose(); throw 'Browser PID identity changed.'
        }
        $browsers.Add($browser)
        $browser.Refresh()
        @{ id = $browser.Id; startedAtUtc = $browser.StartTime.ToUniversalTime(); privateBytes = $browser.PrivateMemorySize64; workingSet = $browser.WorkingSet64 }
      })
      if (!$memory.Count) { throw 'No browser processes observed.' }
      $process.Refresh()
      $hostMemory = @{ privateBytes = $process.PrivateMemorySize64; workingSet = $process.WorkingSet64 }
      [NativeProbeDesktop]::Close($window)
      Wait-Until { $process.HasExited } 'production shutdown'
      $process.WaitForExit()
      foreach ($browser in $browsers) { Wait-Until { $browser.HasExited } 'attributed browser exit' }
      if ($process.ExitCode -ne 0 -or @(Read-ProbeEvents $stdout -Completed)[-1].Stage -ne 'closed') { throw 'Unclean production exit.' }
      $waitState.phase = 'validating-cleanup'
      $lease = [IO.File]::Open((Join-Path $profile '.spar-profile.lock'), 'Open', 'ReadWrite', 'None')
      $lease.Dispose()
      $passed = $true
    } catch {
      $scenarioFailure = $_
      $failure = @{ phase = $waitState.phase; message = $scenarioFailure.Exception.Message }
      if ($startup) {
        try {
          Update-ProductionObservation $startup $process $watch.Elapsed.TotalMilliseconds -Events @(Read-ProbeEvents $stdout) -Force
        } catch { $failure.observationErrorType = $_.Exception.GetType().FullName }
      }
      Write-Host "Production failure diagnostics: $(Join-Path $evidence "run-$sample-startup.json")"
      throw $scenarioFailure
    } finally {
      if ($process) {
        if (!$process.HasExited) {
          $forced = $true
          try { $process.Kill($true); $null = $process.WaitForExit(10000) }
          catch { $cleanupError = $_.Exception.Message }
        }
        $rootExitObserved = $process.HasExited
        if (!$rootExitObserved) { Write-Warning 'Production root exit remains unproven after teardown.' }
      }
      $browserExitObservation = if (!$browsers.Count) { 'not-retained' }
        elseif (@($browsers | Where-Object { !$_.HasExited }).Count) { 'unfinished-retained' } else { 'all-retained-exited' }
      $diagnostics = if ($startup) { Get-ProductionObservation $startup } else { $null }
      @{ startup = $diagnostics; failure = $failure; rootExitObserved = $rootExitObserved
        browserExitObservation = $browserExitObservation; cleanupError = $cleanupError
      } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence "run-$sample-startup.json")
      $results.Add(@{ sample = $sample; passed = $passed; forcedCleanup = $forced; greetingCompletionMs = $readyMs
        processToGreetingMs = $processToGreetingMs
        failure = $failure; startup = $diagnostics; rootExitObserved = $rootExitObserved; browserExitObservation = $browserExitObservation
        profile = $(if ($sample -eq 1) { 'fresh' } else { 'reused' }); processId = $process.Id
        startedAtUtc = $(if ($process) { $process.StartTime.ToUniversalTime() } else { $null }); hostMemory = $hostMemory; browserMemory = $memory
        browserVersion = $loaded.BrowserVersion; render = $(if ($renderCaptured) { 'pixels-captured-review-required' } else { 'not-exercised' }) })
      foreach ($browser in $browsers) { if ($browser.HasExited) { $browser.Dispose() } }
      if ($process -and $rootExitObserved) { $process.Dispose() }
    }
  }
  # Production accepts no development/probe command switches.
  foreach ($flag in @('--spar-dev', '--bridge-probe', '--asset-probe', '--inspect')) {
    $output = & $exe $flag
    if ($LASTEXITCODE -ne 1 -or ($output | ConvertFrom-Json).Stage -ne 'failed') { throw "Production accepted $flag" }
    $output | Set-Content -LiteralPath (Join-Path $evidence ($flag.TrimStart('-') + '.jsonl'))
  }
  $previousRuntime = $env:SPAR_WEBVIEW2_RUNTIME_FOLDER
  try {
    $env:SPAR_WEBVIEW2_RUNTIME_FOLDER = Join-Path $copy 'absent-runtime'
    $output = & $exe (Join-Path $copy 'missing-runtime-profile')
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $evidence 'missing-runtime.jsonl')
    $failure = @($output | ConvertFrom-Json)[-1]
    if ($code -ne 1 -or $failure.Stage -ne 'failed' -or $failure.Detail -notmatch 'WebView2') { throw 'No actionable missing runtime error.' }
  } finally { $env:SPAR_WEBVIEW2_RUNTIME_FOLDER = $previousRuntime }
  $allPassed = $true
} finally {
  @{ allPassed = $allPassed; observedAt = [DateTimeOffset]::UtcNow; copiedDirectory = $copy
    executableSha256 = (Get-FileHash -LiteralPath $exe).Hash; samples = $results.ToArray()
    payloadBytes = $payloadBytes
    mode = $ExpectedMode; noSdkMachine = 'not-available'; renderRequested = [bool]$VerifyRender
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
}
Write-Output "Production checks passed. Copied payload: $copy. Evidence: $evidence"
# GitHub's PowerShell wrapper propagates LASTEXITCODE, including expected negative native launches.
exit 0
