#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$Executable,
  [ValidateSet('CoreCLR', 'NativeAOT')][string]$ExpectedMode = 'CoreCLR',
  [Parameter(Mandatory)][string]$EvidenceDirectory,
  [switch]$VerifyRender
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$evidencePath = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$results = [Collections.Generic.List[object]]::new()

function Read-Events([string]$Path) {
  if (Test-Path -LiteralPath $Path) {
    foreach ($line in Get-Content -LiteralPath $Path) {
      if ($line) { $line | ConvertFrom-Json }
    }
  }
}

function Wait-Until([scriptblock]$Condition, [string]$Description) {
  $deadline = [DateTime]::UtcNow.AddSeconds(40)
  do {
    if (& $Condition) { return }
    Start-Sleep -Milliseconds 100
  } while ([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $Description. Outstanding work has not been proven complete."
}

foreach ($scenario in @('roundtrips', 'asset-roundtrips', 'close-with-work')) {
  $directory = Join-Path $evidencePath $scenario
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $profile = Join-Path $directory ('profile-' + [guid]::NewGuid().ToString('N'))
  $stdout = Join-Path $directory 'stdout.jsonl'
  $stderr = Join-Path $directory 'stderr.log'
  $flag = switch ($scenario) { 'roundtrips' { '--bridge-probe' }; 'asset-roundtrips' { '--asset-probe' }; default { '--bridge-probe-close' } }
  $process = $null
  $passed = $false
  $forced = $false
  try {
    $process = Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path $executablePath) `
      -ArgumentList @('"' + $profile + '"', $flag) -PassThru -WindowStyle Hidden `
      -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null = $process.Handle
    $started = $process.StartTime.ToUniversalTime()
    if ($scenario -ne 'close-with-work') {
      Wait-Until {
        $events = @(Read-Events $stdout)
        if ($process.HasExited -or @($events | Where-Object { $_.Stage -eq 'failed' -or $_.Stage -like 'report-bridge-failed:*' }).Count) {
          throw "Bridge probe failed: $(Get-Content -LiteralPath $stdout -Raw)"
        }
        @($events | Where-Object Stage -like 'report-client-pass:*').Count -eq 1
      } 'generated client roundtrips, retirement and navigation rejection'
      $events = @(Read-Events $stdout)
      foreach ($code in @('permission_denied', 'unknown_operation', 'contract_mismatch', 'document_rejected',
                           'invalid_payload', 'invalid_request_id', 'invalid_message', 'navigation_denied', 'timeout')) {
        if (!@($events | Where-Object { $_.Stage -eq 'bridge-rejected' -and $_.Error.StartsWith($code + '|') }).Count) {
          throw "No native rejection evidence for $code"
        }
      }
      if (@($events | Where-Object Stage -eq 'counter-entered').Count -ne 2 -or
          @($events | Where-Object Stage -eq 'denied-entered').Count -ne 0 -or
          @($events | Where-Object Stage -eq 'bridge-reply_suppressed').Count -eq 0 -or
          @($events | Where-Object Stage -eq 'document-loaded').Count -ne 3) {
        throw 'Mutation count, denied entry, stale reply suppression, or reload count did not match.'
      }
      $window = [IntPtr]($events | Where-Object Stage -eq 'window-created' | Select-Object -First 1).Window
      if ($VerifyRender) {
        [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
        Wait-Until {
          [NativeProbeDesktop]::CaptureColor($window, (Join-Path $directory 'bridge.bmp'), 1000, 68, 221, 136) -gt 10000
        } 'actual renderer success pixels'
      }
      # Close only this retained child identity. No process-name or arbitrary browser termination.
      [NativeProbeDesktop]::Close($window)
    }
    Wait-Until { $process.HasExited } "$scenario actual host exit"
    $events = @(Read-Events $stdout)
    if ($process.ExitCode -ne 0 -or $events[-1].Stage -ne 'closed' -or @($events | Where-Object Mode -ne $ExpectedMode).Count) {
      throw 'Host exit/mode receipt failed.'
    }
    if ($scenario -eq 'close-with-work') {
      $stages = @($events.Stage)
      $requested = [Array]::FindIndex($stages, [Predicate[object]]{ param($value) $value -like 'report-close:*' })
      $finished = [Array]::IndexOf($stages, 'delay-finished')
      $closed = [Array]::IndexOf($stages, 'closed')
      if ($requested -lt 0 -or $finished -le $requested -or $closed -le $finished) {
        throw 'Close did not retain ownership until the uncancellable handler finished.'
      }
    }
    $lease = [IO.File]::Open((Join-Path $profile '.spar-profile.lock'), 'Open', 'ReadWrite', 'None')
    $lease.Dispose()
    $passed = $true
  } finally {
    if ($process) {
      if (!$process.HasExited) { $forced = $true; $process.Kill($true); $process.WaitForExit(10000) | Out-Null }
      $results.Add([pscustomobject]@{
        scenario = $scenario; passed = $passed; forcedCleanup = $forced; processId = $process.Id
        startedAtUtc = $started; mode = $ExpectedMode; render = $(if ($VerifyRender -and $scenario -ne 'close-with-work') { 'verified-pixels' } else { 'not-exercised' })
      })
      $process.Dispose()
    }
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json')
  }
}
$results | Format-Table
