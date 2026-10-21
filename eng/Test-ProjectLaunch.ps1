#requires -Version 7.4
param([switch]$PrepareOnly, [switch]$VerifyRender)

$prepareProjectOnly = $PrepareOnly.IsPresent

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
$repository = Split-Path $PSScriptRoot
$coordinatorPath = (Resolve-Path -LiteralPath 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$applicationPath = (Resolve-Path -LiteralPath 'samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$evidencePath = [IO.Path]::GetFullPath('artifacts/project-launch')
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$results = [Collections.Generic.List[object]]::new()
$fixture = New-BackendFixture 'project' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath
$fixture.Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture.Config
$properties = Join-Path $fixture.Backend 'Properties'
New-Item -ItemType Directory -Path $properties | Out-Null
@{ profiles = @{ 'Spar development' = @{ commandName = 'Project'; commandLineArgs = '--spar-dev'; workingDirectory = $fixture.Backend;
    environmentVariables = @{ SPAR_DEV_PROJECT = $fixture.Config; SPAR_DEV_COORDINATOR = $coordinatorPath } } } } |
  ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $properties 'launchSettings.json')
$solution = [xml]'<Solution />'
# Visual Studio builds loaded solution projects. Include the complete reference graph so it cannot reuse stale contracts.
$projects = @($fixture.Settings.project, (Join-Path $fixture.Directory 'contracts/Spar.NativeSample.Contracts.csproj'),
  (Join-Path $repository 'src/Sotsera.Spar.Windows/Sotsera.Spar.Windows.csproj'),
  (Join-Path $repository 'src/Sotsera.Spar.Development/Sotsera.Spar.Development.csproj'),
  (Join-Path $repository 'src/Sotsera.Spar.Bridge/Sotsera.Spar.Bridge.csproj'),
  (Join-Path $repository 'tools/Spar.ContractGenerator/Spar.ContractGenerator.csproj'))
foreach ($project in $projects) {
  $entry = $solution.CreateElement('Project')
  $entry.SetAttribute('Path', [IO.Path]::GetRelativePath($fixture.Directory, $project).Replace('\', '/'))
  $null = $solution.DocumentElement.AppendChild($entry)
}
$solution.Save((Join-Path $fixture.Directory 'Fixture.slnx'))
dotnet build $fixture.Settings.project --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
$fixture | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture.Directory 'fixture.json')
Write-Output ('Project fixture: ' + $fixture.Directory)
if ($prepareProjectOnly) { return }

function Read-ProjectEvents($Root) {
  $logs = @(Get-ChildItem -LiteralPath $Root -Filter 'project-*.jsonl' -ErrorAction SilentlyContinue)
  foreach ($log in $logs) {
    (Read-Shared $log.FullName) -split "`n" | Where-Object { $_.Trim().EndsWith('}') } | ForEach-Object { $_ | ConvertFrom-Json }
  }
}

foreach ($scenario in @('native-close', 'debugger-stop', 'contract-change', 'coordinator-crash')) {
  $root = Join-Path $fixture.Directory $scenario
  $fixture.Settings.sessionDirectory = $root
  $fixture.Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture.Config
  $start = [Diagnostics.ProcessStartInfo]::new($fixture.Settings.application)
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.ArgumentList.Add('--spar-dev')
  $start.Environment['SPAR_DEV_PROJECT'] = $fixture.Config
  $start.Environment['SPAR_DEV_COORDINATOR'] = $coordinatorPath
  $app = [Diagnostics.Process]::Start($start)
  $null = $app.Handle
  $handles = [Collections.Generic.Dictionary[int,Diagnostics.Process]]::new()
  $acted = $false
  try {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    do {
      $events = @(Read-ProjectEvents $root)
      foreach ($event in $events) {
        $processId = if ($event.kind -eq 'session-started') { $event.coordinatorProcessId } else { $event.processId }
        if ($processId -and !$handles.ContainsKey([int]$processId)) {
          try {
            $process = [Diagnostics.Process]::GetProcessById([int]$processId)
            $null = $process.Handle
            if ($event.processStartedAtUtc -and $process.StartTime.ToUniversalTime() -ne [datetime]$event.processStartedAtUtc) {
              $process.Dispose()
              throw 'Process identity changed.'
            }
            $handles.Add([int]$processId, $process)
          } catch [ArgumentException] { }
        }
      }
      if (!$acted -and @($events | Where-Object kind -eq 'application-ready').Count) {
        $window = [IntPtr]($events | Where-Object kind -eq 'window-created' | Select-Object -Last 1).window
        if ($VerifyRender) {
          $pixels = [NativeProbeDesktop]::CaptureColor($window, (Join-Path $root 'ready.bmp'), 1000, 112, 227, 189)
          Assert-Condition ($pixels -gt 3000) 'Native rendering not observed.'
        }
        if ($scenario -eq 'native-close') { [NativeProbeDesktop]::Close($window) }
        elseif ($scenario -eq 'debugger-stop') { $app.Kill() }
        elseif ($scenario -eq 'coordinator-crash') {
          $coordinatorId = [int]($events | Where-Object kind -eq 'session-started' | Select-Object -Last 1).coordinatorProcessId
          $handles[$coordinatorId].Kill()
        }
        else { Add-Content -LiteralPath $fixture.Settings.contractFile -Value '// project contract restart' }
        $acted = $true
      }
      $stopped = $events | Where-Object kind -eq 'session-stopped' | Select-Object -Last 1
      if ($stopped) { break }
      if ($scenario -eq 'coordinator-crash' -and $acted -and $app.HasExited) { break }
      Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    Assert-Condition ($acted -and ($null -ne $stopped -or $scenario -eq 'coordinator-crash')) 'Project session did not reach readiness and cleanup.'
    foreach ($process in $handles.Values) { Assert-Condition ($process.WaitForExit(10000)) 'An owned process remains alive.' }
    Assert-Condition ($app.WaitForExit(10000)) 'Project app remains alive.'
    $expected = if ($scenario -eq 'debugger-stop') { 'forced_cleanup' } elseif ($scenario -eq 'coordinator-crash') { 'kernel_job_close' } else { 'graceful' }
    if ($stopped) { Assert-Condition ($stopped.code -eq $expected) "Unexpected cleanup: $($stopped.code)" }
    if ($scenario -eq 'contract-change') {
      Assert-Condition (@($events | Where-Object kind -eq 'restart-required').Count -eq 1) 'Contract change did not require an IDE relaunch.'
    }
    Assert-Condition (@($events | Where-Object { $_.kind -eq 'backend-owner' -and $_.code -eq 'dotnet-watch' }).Count -eq 0) 'A second Hot Reload owner started.'
    $results.Add(@{ scenario = $scenario; cleanup = $expected; processes = $handles.Count; passed = $true })
  } finally {
    if (!$app.HasExited) { $app.Kill(); $app.WaitForExit() }
    $app.Dispose()
    foreach ($process in $handles.Values) { $process.Dispose() }
  }
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture.Directory 'receipt.json')
$results | ConvertTo-Json -Depth 5
