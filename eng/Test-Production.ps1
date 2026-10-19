#requires -Version 7.4
param(
  [string]$PublishDirectory = 'artifacts/production/publish',
  [string]$EvidenceDirectory = 'artifacts/production/acceptance',
  [ValidateRange(1, 10)][int]$Samples = 3,
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$copy = Join-Path ([IO.Path]::GetTempPath()) ('spar-production-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $copy | Out-Null
Get-ChildItem -LiteralPath $publish -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $copy
$expected = @('Spar.NativeSample.exe', 'WebView2Loader.dll', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt', 'spar-publish.json')
if (Compare-Object @((Get-ChildItem -LiteralPath $copy -File).Name) $expected) { throw 'Unexpected production payload.' }
$publication = Get-Content -LiteralPath (Join-Path $copy 'spar-publish.json') -Raw | ConvertFrom-Json
if ($publication.mode -ne 'NativeAOT') { throw 'Default publication must be NativeAOT.' }
foreach ($file in $publication.payload | Where-Object name -NotLike '*.pdb') {
  if ((Get-FileHash -LiteralPath (Join-Path $copy $file.name)).Hash -ne $file.sha256) { throw 'Publication hash mismatch.' }
}
$exe = Join-Path $copy 'Spar.NativeSample.exe'
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$results = [Collections.Generic.List[object]]::new()
$allPassed = $false
function Events($path) { if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path | Where-Object { $_ } | ConvertFrom-Json } }
function Wait-Until([scriptblock]$condition, [string]$description) {
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do { if (& $condition) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
  throw "Timed out: $description. Actual completion is unproven."
}
try {
  foreach ($sample in 1..$Samples) {
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
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
      $process = Start-Process -FilePath $exe -WorkingDirectory $copy -ArgumentList ('"' + $profile + '"') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError (Join-Path $evidence "run-$sample.log")
      $null = $process.Handle
      Wait-Until {
        if ($process.HasExited) { throw "Production exited: $(Get-Content -LiteralPath $stdout -Raw)" }
        $current = @(Events $stdout)
        @($current | Where-Object { $_.Stage -eq 'bridge-completed' -and $_.Detail -like '*|greet|*' }).Count -gt 0 `
          -and @($current | Where-Object Stage -eq 'document-loaded').Count -gt 0
      } 'typed greeting completion'
      $readyMs = $watch.Elapsed.TotalMilliseconds
      $events = @(Events $stdout)
      $greeted = $events | Where-Object { $_.Stage -eq 'bridge-completed' -and $_.Detail -like '*|greet|*' } | Select-Object -First 1
      $processToGreetingMs = ([DateTimeOffset]$greeted.ObservedAt - [DateTimeOffset]$greeted.ProcessStartedAtUtc).TotalMilliseconds
      if (@($events | Where-Object Mode -ne 'NativeAOT').Count -gt 0) { throw 'Unexpected runtime mode.' }
      if (@($events | Where-Object Stage -eq 'bridge-rejected').Count -gt 0) { throw 'Production bridge rejected a normal call.' }
      $loaded = $events | Where-Object Stage -eq 'document-loaded' | Select-Object -Last 1
      $window = [IntPtr]$loaded.Window
      if ($VerifyRender) {
        [NativeProbeDesktop]::ShowWindow($window, 5) | Out-Null
        Wait-Until { [NativeProbeDesktop]::Capture($window, (Join-Path $evidence "run-$sample.bmp"), 1000) -gt 500 } 'production pixels'
      }
      # Attribute the browser by its profile, then retain and verify creation identities for descendants.
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
      if ($process.ExitCode -ne 0 -or @(Events $stdout)[-1].Stage -ne 'closed') { throw 'Unclean production exit.' }
      $lease = [IO.File]::Open((Join-Path $profile '.spar-profile.lock'), 'Open', 'ReadWrite', 'None')
      $lease.Dispose()
      $passed = $true
    } finally {
      if ($process -and !$process.HasExited) { $forced = $true; $process.Kill($true); $null = $process.WaitForExit(10000) }
      $results.Add(@{ sample = $sample; passed = $passed; forcedCleanup = $forced; greetingCompletionMs = $readyMs
        processToGreetingMs = $processToGreetingMs
        profile = $(if ($sample -eq 1) { 'fresh' } else { 'reused' }); processId = $process.Id
        startedAtUtc = $(if ($process) { $process.StartTime.ToUniversalTime() } else { $null }); hostMemory = $hostMemory; browserMemory = $memory
        browserVersion = $loaded.BrowserVersion; render = $(if ($VerifyRender) { 'pixels-captured-review-required' } else { 'not-exercised' }) })
      foreach ($browser in $browsers) { $browser.Dispose() }
      if ($process) { $process.Dispose() }
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
    payloadBytes = (Get-ChildItem -LiteralPath $copy -File | Measure-Object Length -Sum).Sum
    mode = 'NativeAOT'; noSdkMachine = 'not-available'; renderRequested = [bool]$VerifyRender
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
}
Write-Output "Production checks passed. Copied payload: $copy. Evidence: $evidence"
# GitHub's PowerShell wrapper propagates LASTEXITCODE, including expected negative native launches.
exit 0
