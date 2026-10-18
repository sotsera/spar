#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$Executable,
  [ValidateSet('CoreCLR', 'NativeAOT')][string]$ExpectedMode = 'CoreCLR',
  [string]$EvidenceDirectory = '',
  [switch]$VerifyRender
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
if (!$EvidenceDirectory) {
  $EvidenceDirectory = Join-Path $PSScriptRoot "../artifacts/native/$ExpectedMode-$([guid]::NewGuid().ToString('N'))"
}
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$results = [Collections.Generic.List[object]]::new()
$completed = $false

function Read-Events([string]$Path) {
  if (Test-Path -LiteralPath $Path) {
    foreach ($line in Get-Content -LiteralPath $Path) {
      if ($line) { $line | ConvertFrom-Json }
    }
  }
}

function Wait-Until([scriptblock]$Condition, [string]$Description, [int]$Seconds = 30) {
  $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
  do {
    if (& $Condition) { return }
    Start-Sleep -Milliseconds 100
  } while ([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $Description. This is a failed check, not proof of cleanup."
}

function Find-Browsers([string]$Profile, $Handles) {
  $all = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'")
  $ownedIds = [Collections.Generic.HashSet[int]]::new()
  foreach ($item in $all) {
    if ($item.CommandLine -and $item.CommandLine.Contains($Profile)) { $null = $ownedIds.Add([int]$item.ProcessId) }
  }
  do {
    $added = $false
    foreach ($item in $all) {
      $parent = $all | Where-Object ProcessId -eq $item.ParentProcessId | Select-Object -First 1
      if ($parent -and $item.CreationDate -ge $parent.CreationDate -and
          $ownedIds.Contains([int]$item.ParentProcessId) -and $ownedIds.Add([int]$item.ProcessId)) { $added = $true }
    }
  } while ($added)
  foreach ($ownedId in $ownedIds) {
    try {
      $browser = [Diagnostics.Process]::GetProcessById($ownedId)
      $null = $browser.Handle
      $observed = $all | Where-Object ProcessId -eq $ownedId | Select-Object -First 1
      if ([Math]::Abs(($browser.StartTime.ToUniversalTime() - $observed.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
        $browser.Dispose()
        throw 'A browser PID changed identity during observation.'
      }
      $Handles.Add($browser)
    } catch [ArgumentException] { }
  }
}

function Invoke-Scenario([string]$Name, [string[]]$Arguments, [bool]$ExpectDocument, [int]$ExitCode = 0) {
  $directory = Join-Path $evidencePath $Name
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $profile = Join-Path $directory 'profile'
  if (!(Test-Path -LiteralPath $profile)) { New-Item -ItemType Directory -Path $profile | Out-Null }
  $sentinel = if ((Get-Item -LiteralPath $profile).PSIsContainer) { Join-Path $profile 'preserve-me.txt' } else { $null }
  if ($sentinel) { Set-Content -LiteralPath $sentinel -Value 'Application-owned browser data must survive shutdown.' }
  $stdout = Join-Path $directory 'stdout.jsonl'
  $stderr = Join-Path $directory 'stderr.log'
  $browserHandles = [Collections.Generic.List[Diagnostics.Process]]::new()
  $process = $null
  $forced = $false
  $recorded = $false
  $scenarioFailure = $null
  $profileOverrideRejected = 'not-exercised'
  try {
    $process = Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path $executablePath) `
      -ArgumentList (@('"' + $profile + '"') + $Arguments) -PassThru -WindowStyle Hidden `
      -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    # Retain the process handle so PID reuse cannot turn an unrelated process into our child.
    $null = $process.Handle
    $started = $process.StartTime.ToUniversalTime()
    if ($ExpectDocument) {
      Wait-Until {
        if ($process.HasExited) { throw "Host exited before document load: $(Get-Content $stdout -Raw)" }
        @(Read-Events $stdout | Where-Object Stage -eq 'document-loaded').Count -gt 0
      } "$Name document load"
      $loaded = Read-Events $stdout | Where-Object Stage -eq 'document-loaded' | Select-Object -Last 1
      $window = [IntPtr]$loaded.Window
      $collisionLog = Join-Path $directory 'profile-collision.jsonl'
      $collision = Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path $executablePath) `
        -ArgumentList @('"' + $profile + '"', '--close-during-startup') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $collisionLog -RedirectStandardError (Join-Path $directory 'profile-collision.stderr.log')
      try {
        if (!$collision.WaitForExit(10000)) {
          $collision.Kill($true)
          $collision.WaitForExit()
          throw 'Concurrent profile attempt did not finish.'
        }
        $collisionEvents = @(Read-Events $collisionLog)
        if ($collision.ExitCode -ne 1 -or $collisionEvents.Count -ne 1 -or $collisionEvents[0].Stage -ne 'failed') {
          throw 'Concurrent profile ownership was not rejected before native creation.'
        }
      } finally { $collision.Dispose() }
      if ($VerifyRender) {
        [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
        [NativeProbeDesktop]::MoveWindow($window, 40, 40, 1000, 700, $true) | Out-Null
        Wait-Until { [NativeProbeDesktop]::Capture($window, (Join-Path $directory 'initial.bmp'), 300) -gt 500 } "$Name rendered pixels"
        [NativeProbeDesktop]::MoveWindow($window, 40, 40, 760, 520, $true) | Out-Null
        Wait-Until { [NativeProbeDesktop]::Capture($window, (Join-Path $directory 'resized.bmp'), 100) -gt 500 } "$Name resized layout"
      }

      # Observe only this profile's browser and its descendants. Never terminate unrelated browser processes.
      Find-Browsers $profile $browserHandles
      if ($browserHandles.Count -eq 0) { throw 'No attributed WebView2 browser process was observed.' }
      [NativeProbeDesktop]::Close($window)
    }

    if ($Name -eq 'redirected-profile') {
      Wait-Until { $process.HasExited -or @(Read-Events $stdout | Where-Object Stage -eq 'document-loaded').Count -gt 0 } 'profile override outcome'
      if (!$process.HasExited) {
        $loaded = Read-Events $stdout | Where-Object Stage -eq 'document-loaded' | Select-Object -Last 1
        if (![string]::Equals([IO.Path]::TrimEndingDirectorySeparator($loaded.UserDataFolder),
            [IO.Path]::TrimEndingDirectorySeparator($profile), [StringComparison]::OrdinalIgnoreCase)) {
          throw 'The host admitted a document outside its leased profile.'
        }
        # Some host/runtime configurations ignore environment overrides. This proves a matching profile, not rejection.
        $profileOverrideRejected = 'not-exercised-platform-ignored-override'
        $ExitCode = 0
        [NativeProbeDesktop]::Close([IntPtr]$loaded.Window)
      } else { $profileOverrideRejected = $true }
    }

    Wait-Until {
      if ($Name -eq 'missing-runtime' -and @(Read-Events $stdout | Where-Object Stage -eq 'document-loaded').Count) {
        throw "Missing-runtime fixture unexpectedly loaded a document: $(Get-Content $stdout -Raw)"
      }
      $process.HasExited
    } "$Name host exit"
    $process.WaitForExit()
    if ($process.ExitCode -ne $ExitCode) { throw "Unexpected exit $($process.ExitCode): $(Get-Content $stdout -Raw)" }
    $events = @(Read-Events $stdout)
    if (@($events | Where-Object Mode -ne $ExpectedMode).Count) { throw 'Unexpected execution mode.' }
    $lastStage = if ($ExitCode -eq 0) { 'closed' } else { 'failed' }
    if ($events.Count -eq 0 -or $events[-1].Stage -ne $lastStage) { throw "Missing $lastStage receipt." }
    if ($Name -eq 'missing-runtime' -and $events[-1].RuntimeFolder -ne (Join-Path $profile 'absent-webview-runtime')) {
      throw 'The missing-runtime fixture did not select its explicit runtime directory.'
    }
    if ($Name -eq 'redirected-profile' -and $ExitCode -eq 1 -and !$events[-1].Error.Contains('redirected the browser profile')) {
      throw 'Profile override failed for an unrelated reason.'
    }
    if ($Name -eq 'worker-dispatch' -and @($events | Where-Object Stage -eq 'ui-dispatch').Count -ne 1) {
      throw 'Worker-thread UI dispatch did not complete.'
    }
    if ($Name -eq 'worker-dispatch') {
      $createdThread = ($events | Where-Object Stage -eq 'window-created' | Select-Object -First 1).ThreadId
      if (($events | Where-Object Stage -eq 'ui-dispatch').ThreadId -ne $createdThread) {
        throw 'Posted work ran on a different thread from window creation.'
      }
    }
    if (!$ExpectDocument) { Find-Browsers $profile $browserHandles }
    foreach ($browser in $browserHandles) {
      Wait-Until { $browser.HasExited } "$Name attributed browser exit" 15
    }
    if (Test-Path -LiteralPath (Join-Path $profile '.spar-profile.lock')) {
      $lease = [IO.File]::Open((Join-Path $profile '.spar-profile.lock'), 'Open', 'ReadWrite', 'None')
      $lease.Dispose()
    }
    if ($sentinel -and !(Test-Path -LiteralPath $sentinel)) { throw 'Profile data was deleted during shutdown.' }
    $results.Add([ordered]@{
      scenario = $Name; passed = $true; processId = $process.Id; processStartedAtUtc = $started
      mode = $ExpectedMode; visibleRendering = $(if ($ExpectDocument -and $VerifyRender) { 'verified-pixels' } else { 'not-exercised' })
      concurrentProfileRejected = $(if ($ExpectDocument) { $true } else { 'not-exercised' })
      profileDataPreserved = $(if ($sentinel) { $true } else { 'not-exercised' })
      profileOverrideRejected = $profileOverrideRejected
      attributedBrowserProcesses = @($browserHandles | ForEach-Object {
        [ordered]@{ processId = $_.Id; processStartedAtUtc = $_.StartTime.ToUniversalTime(); exited = $_.HasExited }
      })
      exitCode = $process.ExitCode; forced = $false
    })
    $recorded = $true
  } catch {
    $scenarioFailure = $_.ToString()
    throw
  } finally {
    if ($process -and !$process.HasExited) {
      $forced = $true
      $process.Kill($true)
      if (!$process.WaitForExit(10000)) { Write-Warning "Owned host did not exit after forced termination: $($process.Id)" }
    }
    if (!$recorded) {
      $results.Add([ordered]@{
        scenario = $Name; passed = $false; error = $scenarioFailure; forced = $forced
        processId = $(if ($process) { $process.Id } else { $null }); processStartedAtUtc = $started
        hostExited = $(if ($process) { $process.HasExited } else { 'not-started' })
        attributedBrowserProcesses = @($browserHandles | ForEach-Object {
          [ordered]@{ processId = $_.Id; processStartedAtUtc = $_.StartTime.ToUniversalTime(); exited = $_.HasExited }
        })
      })
    }
    foreach ($browser in $browserHandles) { $browser.Dispose() }
    if ($process) { $process.Dispose() }
    if ($forced) { Write-Warning "$Name required forced cleanup and did not pass graceful shutdown." }
  }
}

try {
  Invoke-Scenario 'normal' @() $true
  Invoke-Scenario 'close-during-startup' @('--close-during-startup') $false
  Invoke-Scenario 'worker-dispatch' @('--close-from-worker') $false
  Invoke-Scenario 'document-callback-error' @('--throw-on-document') $false 1
  Invoke-Scenario 'missing-runtime' @('--missing-runtime') $false 1
  Invoke-Scenario 'redirected-profile' @('--redirect-profile') $false 1
  # A file cannot be a writable browser profile; this must fail before native/browser ownership starts.
  $invalid = Join-Path $evidencePath 'invalid-profile'
  New-Item -ItemType Directory -Path $invalid -Force | Out-Null
  Set-Content -LiteralPath (Join-Path $invalid 'profile') -Value 'not a directory'
  Invoke-Scenario 'invalid-profile' @() $false 1
  $completed = $true
} finally {
  [ordered]@{
    observedAt = [DateTimeOffset]::UtcNow; executable = $executablePath
    os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; powershell = $PSVersionTable.PSVersion.ToString()
    executableSha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
    payload = @(Get-ChildItem -LiteralPath (Split-Path $executablePath) -File | Where-Object Extension -ne '.pdb' |
      ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } })
    expectedMode = $ExpectedMode; renderRequested = [bool]$VerifyRender; scenarios = @($results.ToArray())
    allScenariosPassed = $completed
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json')
}
Write-Output "Native host checks passed. Evidence: $evidencePath"
