#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$PublishDirectory,
  [string]$EvidenceDirectory = 'artifacts/minimal/production',
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$copy = Join-Path ([IO.Path]::GetTempPath()) ('spar-minimal-published-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $copy | Out-Null
Get-ChildItem -LiteralPath $publish | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $copy -Recurse
$publication = Get-Content -LiteralPath (Join-Path $copy 'spar-publish.json') -Raw | ConvertFrom-Json
if ($publication.mode -ne 'NativeAOT' -or !$publication.selfContained) { throw 'Expected default self-contained NativeAOT.' }
$expected = @('Spar.NativeSample.exe', 'WebView2Loader.dll', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt', 'spar-publish.json', 'frontend-LICENSES.txt')
if (Compare-Object @((Get-ChildItem -LiteralPath $copy -File).Name) $expected) { throw 'Unexpected minimal AOT payload.' }
foreach ($file in $publication.payload | Where-Object name -NotLike '*.pdb') {
  if ((Get-FileHash -LiteralPath (Join-Path $copy $file.name)).Hash -ne $file.sha256) { throw 'Copied publication hash mismatch.' }
}
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$exe = Join-Path $copy 'Spar.NativeSample.exe'
$process = $null
$browsers = [Collections.Generic.List[Diagnostics.Process]]::new()
$browserCount = 0
$passed = $false
$profile = Join-Path $copy 'profile'
$window = [IntPtr]::Zero
$windowClass = $null
$closeRequested = $false
$exitObserved = $false
$closeWaitCompleted = $false
$actualExitCode = $null
$processId = $null
$startedAtUtc = $null
$forcedRootCleanup = $false
$forcedChildren = [Collections.Generic.List[int]]::new()
$cleanupErrors = [Collections.Generic.List[string]]::new()
$failure = $null
try {
  $process = Start-Process -FilePath $exe -WorkingDirectory $copy -ArgumentList ('"' + $profile + '"') `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'stdout.log') -RedirectStandardError (Join-Path $evidence 'stderr.log')
  $null = $process.Handle
  $processId = $process.Id
  $startedAtUtc = $process.StartTime.ToUniversalTime()
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do {
    if ($process.HasExited) { throw 'Minimal production application exited before its window was available.' }
    $process.Refresh()
    $window = [NativeProbeDesktop]::FindWindow($process.Id)
    if ($window -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 100
  } while ([DateTime]::UtcNow -lt $deadline)
  if ($window -eq [IntPtr]::Zero) { throw 'No production window observed.' }
  $windowClass = [NativeProbeDesktop]::WindowClass($window)
  if ($VerifyRender) {
    [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
    $pixels = 0
    do {
      $pixels = [NativeProbeDesktop]::Capture($window, (Join-Path $evidence 'published.bmp'), 1000)
      if ($pixels -gt 500) { break }
      Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($pixels -le 500) { throw 'Production SPA pixels unavailable.' }
    # Keep the completed frame for manual review of the typed greeting, without application test reporting.
    Start-Sleep -Seconds 1
    $null = [NativeProbeDesktop]::Capture($window, (Join-Path $evidence 'published.bmp'), 1000)
  } else { Start-Sleep -Seconds 2 }
  $all = @(Get-CimInstance Win32_Process)
  $ids = [Collections.Generic.HashSet[int]]::new()
  $null = $ids.Add($process.Id)
  do {
    $added = $false
    foreach ($item in $all) {
      $parent = $all | Where-Object ProcessId -eq $item.ParentProcessId | Select-Object -First 1
      if ($parent -and $ids.Contains([int]$parent.ProcessId) -and $item.CreationDate -ge $parent.CreationDate -and $ids.Add([int]$item.ProcessId)) { $added = $true }
    }
  } while ($added)
  foreach ($item in $all | Where-Object { $ids.Contains([int]$_.ProcessId) -and $_.ProcessId -ne $process.Id }) {
    if ($item.Name -notin @('msedgewebview2.exe', 'conhost.exe')) { throw "Unexpected production descendant: $($item.Name)" }
    if ($item.Name -eq 'msedgewebview2.exe') { $browserCount++ }
    $browser = [Diagnostics.Process]::GetProcessById([int]$item.ProcessId)
    $null = $browser.Handle
    if ([Math]::Abs(($browser.StartTime.ToUniversalTime() - $item.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1) {
      $browser.Dispose()
      throw 'Browser identity changed before retention.'
    }
    $browsers.Add($browser)
  }
  if (!$browserCount) { throw 'No actual browser descendants observed.' }
  [NativeProbeDesktop]::Close($window)
  $closeRequested = $true
  $closeWaitCompleted = $process.WaitForExit(10000)
  if (!$closeWaitCompleted) { throw "Production PID $processId, window $window ($windowClass), did not exit after Close. See $evidence/stdout.log and stderr.log." }
  $actualExitCode = $process.ExitCode
  if ($null -eq $actualExitCode) { throw "Production PID $processId exited after Close but its exit code is unavailable. See $evidence/stdout.log and stderr.log." }
  if ($actualExitCode -ne 0) { throw "Production PID $processId, window $window ($windowClass), exited with code $actualExitCode after Close. See $evidence/stdout.log and stderr.log." }
  foreach ($browser in $browsers) {
    if (!$browser.WaitForExit(10000)) { throw 'Retained browser has not exited.' }
  }
  $lease = [IO.File]::Open((Join-Path $profile '.spar-profile.lock'), 'Open', 'ReadWrite', 'None')
  $lease.Dispose()
  foreach ($flag in @('--spar-dev', '--bridge-probe', '--asset-probe', '--inspect')) {
    & $exe $flag 2>> (Join-Path $evidence 'rejected-arguments.log')
    if ($LASTEXITCODE -ne 1) { throw "Production accepted $flag" }
  }
  $passed = $true
} catch {
  $failure = $_.Exception.Message
  throw
} finally {
  if ($process) {
    try {
      $exitObserved = $process.HasExited
      if ($exitObserved) { $actualExitCode = $process.ExitCode }
      $forcedRootCleanup = !$exitObserved
      Stop-TestProcess $process
    } catch { $cleanupErrors.Add("Production teardown: $_") }
  }
  foreach ($browser in $browsers) {
    try {
      if (!$browser.HasExited) { $forcedChildren.Add($browser.Id) }
      Stop-TestProcess $browser
    } catch { $cleanupErrors.Add("Retained descendant teardown: $_") }
  }
  @{ passed = ($passed -and $cleanupErrors.Count -eq 0); copiedDirectory = $copy; executableSha256 = (Get-FileHash $exe).Hash;
      mode = $publication.mode; retainedBrowserProcesses = $browserCount; retainedDescendants = $browsers.Count; rendering = $(if ($VerifyRender) { 'pixels-captured-greeting-review-required' } else { 'not-exercised' });
      window = $window.ToInt64(); windowClass = $windowClass; closeRequested = $closeRequested
      processId = $processId; startedAtUtc = $startedAtUtc; closeWaitCompleted = $closeWaitCompleted
      exitObservedBeforeCleanup = $exitObserved; exitCodeBeforeCleanup = $actualExitCode; failure = $failure
      forcedRootCleanup = $forcedRootCleanup; forcedDescendants = $forcedChildren.ToArray()
      cleanupCompleted = ($cleanupErrors.Count -eq 0); cleanupErrors = $cleanupErrors.ToArray()
      noSdkMachine = 'not-exercised'; observedAt = [DateTimeOffset]::UtcNow } |
      ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
  if ($cleanupErrors.Count) { throw ($cleanupErrors -join "`n") }
}
Write-Output "Minimal production passed: $evidence"
exit 0
