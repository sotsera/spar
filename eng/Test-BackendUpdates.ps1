#requires -Version 7.4
param(
  [string]$EvidenceDirectory = 'artifacts/backend-loop/probe',
  [switch]$VerifyRender,
  [switch]$LibraryOnly
)

$backendLibraryOnly = $LibraryOnly.IsPresent
. (Join-Path $PSScriptRoot 'Test-DevelopmentSession.ps1') -EvidenceDirectory $EvidenceDirectory -VerifyRender:$VerifyRender -LibraryOnly

function New-BackendFixture([string]$Name) {
  $fixture = New-Fixture $Name 'backend/frontend'
  $backend = Join-Path $fixture.Directory 'backend'
  $original = Join-Path $repository 'samples/Spar.NativeSample'
  Get-ChildItem -LiteralPath $original -File | Where-Object Extension -in @('.cs','.csproj','.html','.js') |
    Copy-Item -Destination $backend
  $project = Join-Path $backend 'Spar.NativeSample.csproj'
  $contracts = Join-Path $fixture.Directory 'contracts'
  New-Item -ItemType Directory -Path $contracts | Out-Null
  $originalContracts = Join-Path $repository 'samples/Spar.NativeSample.Contracts'
  Copy-Item -Path (Join-Path $originalContracts '*.cs'), (Join-Path $originalContracts '*.csproj') -Destination $contracts
  $contractProject = Join-Path $contracts 'Spar.NativeSample.Contracts.csproj'
  $contractXml = [xml][IO.File]::ReadAllText($contractProject)
  foreach ($reference in $contractXml.Project.ItemGroup.ProjectReference) {
    if ($reference -is [Xml.XmlElement]) { $reference.Include = [IO.Path]::GetFullPath($reference.Include, $originalContracts) }
  }
  $contractXml.Project.Import.Project = [IO.Path]::GetFullPath($contractXml.Project.Import.Project, $originalContracts)
  $contractXml.SelectSingleNode('/Project/PropertyGroup/SparClientOutputDirectory').InnerText = Join-Path $fixture.Frontend 'src/generated'
  $contractXml.Save($contractProject)
  $xml = [xml][IO.File]::ReadAllText($project)
  foreach ($import in $xml.Project.Import) {
    $import.Project = [IO.Path]::GetFullPath($import.Project, $original)
  }
  foreach ($reference in $xml.Project.ItemGroup.ProjectReference) {
    if ($reference -is [Xml.XmlElement]) {
      $reference.Include = if ($reference.Include.Contains('Spar.NativeSample.Contracts')) { $contractProject }
        else { [IO.Path]::GetFullPath($reference.Include, $original) }
    }
  }
  $xml.Save($project)
  $source = Join-Path $backend 'SampleBackend.cs'
  $text = [IO.File]::ReadAllText($source).Replace(': ISampleBackend', ': InitialBackendBase, ISampleBackend')
  $text = $text.Replace('> 5000', '> 15000') # Isolated fixture only: keep work alive beyond the SDK's 5-second cleanup timeout.
  $text += "`npublic class InitialBackendBase;`npublic class ReplacementBackendBase;`n"
  [IO.File]::WriteAllText($source, $text)
  $fixture.Settings.project = $project
  $fixture.Settings.application = Join-Path $backend 'bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
  $fixture.Settings['watchBackend'] = $true
  $fixture.Settings['contractFile'] = Join-Path $contracts 'BackendContract.cs'
  $fixture.Settings['restartFiles'] = @((Join-Path $backend 'Program.cs'), (Join-Path $backend 'Program.Development.cs'))
  $fixture.Settings.startupTimeoutSeconds = 90
  $fixture | Add-Member -NotePropertyName Backend -NotePropertyValue $backend
  Get-ChildItem -LiteralPath $backend, $contracts -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($fixture.Directory,$_.FullName)
  } | Set-Content -LiteralPath (Join-Path $fixture.Directory 'sources.initial.sha256')
  return $fixture
}

function Replace-Source([string]$Path, [string]$Before, [string]$After) {
  $source = [IO.File]::ReadAllText($Path)
  if (!$source.Contains($Before)) { throw "Replacement input not found: $Before" }
  [IO.File]::WriteAllText($Path, $source.Replace($Before,$After))
}

if ($backendLibraryOnly) { return }

try {
  $priorWatchLogLevel = [Environment]::GetEnvironmentVariable('DOTNET_WATCH_LOG_LEVEL')
  [Environment]::SetEnvironmentVariable('DOTNET_WATCH_LOG_LEVEL','Critical')
  $unrelatedStart = [Diagnostics.ProcessStartInfo]::new('node')
  $unrelatedStart.UseShellExecute = $false
  $unrelatedStart.CreateNoWindow = $true
  foreach ($argument in @('-e','setInterval(() => {}, 1000)')) { $unrelatedStart.ArgumentList.Add($argument) }
  $unrelated = [Diagnostics.Process]::Start($unrelatedStart)
  $null = $unrelated.Handle

  $fixture = New-BackendFixture 'patch-error-restart'
  $session = Start-Session $fixture $true
  Wait-Until $session { $null -ne (Latest-Snapshot $session) -and @($session.Events | Where-Object kind -eq 'watcher-ready').Count -eq 1 } 'native/typed and SDK watcher readiness' 120
  $initial = Latest-Snapshot $session
  $firstApplication = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
  $frontend = $session.Events | Where-Object kind -eq 'frontend-ready' | Select-Object -First 1
  Send-Command $session 'probe' 'seed'
  Wait-Until $session { $state = Latest-Snapshot $session; $state.counter -eq 2 -and $state.local -eq 7 } 'seeded backend/frontend state'
  $css = Join-Path $fixture.Frontend 'src/styles.css'
  Replace-Source $css '#70e3bd' '#e9b44c'
  Wait-Until $session { (Latest-Snapshot $session).color -eq 'rgb(233, 180, 76)' } 'frontend HMR alongside the C# watcher'
  $source = Join-Path $fixture.Backend 'SampleBackend.cs'
  Replace-Source $source '"Hello, " + request.Name' '"Hello after patch, " + request.Name'
  Wait-Until $session { $state = Latest-Snapshot $session; $state.greeting -eq 'Hello after patch, Spar' -and $state.backendRevision -ge 1 } 'real .NET patch and SPA read refresh' 90
  $patched = Latest-Snapshot $session
  Assert-Condition ($patched.counter -eq 2 -and $patched.local -eq 7 -and $patched.text -eq 'Retain this text through HMR' -and $patched.mount -eq $initial.mount -and $patched.document -eq $initial.document) 'Backend patch/refresh lost state or replaced the document.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1) 'A supported patch restarted the application.'
  if ($VerifyRender) {
    $window = [IntPtr]($session.Events | Where-Object kind -eq 'window-created' | Select-Object -Last 1).window
    Wait-Until $session { [NativeProbeDesktop]::CaptureColor($window, (Join-Path $fixture.Directory 'patched.bmp'), 1000, 233, 180, 76) -gt 3000 } 'rendered patched greeting'
  }

  Replace-Source $source '"Hello after patch, " + request.Name' 'MissingGreetingSymbol + request.Name'
  Wait-Until $session { $state = Latest-Snapshot $session; $state.developmentStatus -like '*Edit not applied:*CS0103*' } 'compiler diagnostic in the actual SPA' 90
  $failed = Latest-Snapshot $session
  Assert-Condition ($failed.greeting -eq $patched.greeting -and $failed.counter -eq 2 -and $failed.backendRevision -eq $patched.backendRevision) 'Failed compilation activated new behavior/state.'
  Replace-Source $source 'MissingGreetingSymbol + request.Name' '"Hello after correction, " + request.Name'
  Wait-Until $session { $state = Latest-Snapshot $session; $state.greeting -eq 'Hello after correction, Spar' -and $state.backendRevision -gt $patched.backendRevision } 'in-process correction recovery' 90
  Replace-Source $source '"Hello after correction, " + request.Name' '"Hello rapid first, " + request.Name'
  Start-Sleep -Milliseconds 30
  Replace-Source $source '"Hello rapid first, " + request.Name' '"Hello rapid final, " + request.Name'
  Wait-Until $session { (Latest-Snapshot $session).greeting -eq 'Hello rapid final, Spar' } 'latest rapid method edit' 90

  Send-Command $session 'probe' 'delay'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'backend-activity' -and $_.detail -eq 'delay-entered' }).Count -eq 1 } 'actual cancellation-ignoring backend operation'
  Replace-Source $source ': InitialBackendBase, ISampleBackend' ': ReplacementBackendBase, ISampleBackend'
  Wait-Until $session { $null -ne (Latest-Snapshot $session 2) } 'coordinated unsupported-edit rebuild/restart after actual old work completion' 120
  $second = Latest-Snapshot $session 2
  $stopped = $session.Events | Where-Object { $_.kind -eq 'application-stopped' -and $_.generation -eq 1 } | Select-Object -First 1
  $started = $session.Events | Where-Object { $_.kind -eq 'process-started' -and $_.role -eq 'application' -and $_.generation -eq 2 } | Select-Object -First 1
  $delayEnded = $session.Events | Where-Object { $_.kind -eq 'backend-activity' -and $_.detail -eq 'delay-finished' } | Select-Object -First 1
  Assert-Condition ($delayEnded -and $stopped -and $started -and $delayEnded.observedAt -le $stopped.observedAt -and $stopped.observedAt -lt $started.observedAt) 'New application overlapped retiring execution or actual delay completion was lost.'
  Assert-Condition ($second.counter -eq 0 -and $started.processId -ne $firstApplication.processId) 'Restart did not create fresh backend state.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'backend-activity' -and $_.detail -eq 'delay-entered' }).Count -eq 1) 'Retirement replayed the operation.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'forced-cleanup-requested').Count -eq 0) 'Unsupported edit forced termination of unfinished application work.'

  # A failed full rebuild must stay stopped; a source correction automatically regenerates both contracts.
  $contract = $fixture.Settings.contractFile
  Replace-Source $contract 'record CounterReply(int Value)' 'record CounterReply(int Value, int Revision)'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'backend-stopped' -and $_.code -eq 'build_failed' }).Count -eq 1 } 'stopped failed rebuild' 90
  Assert-Condition (@($session.Events | Where-Object kind -eq 'contract-source-changed').Count -eq 1) 'Contract source change bypassed the coordinator rebuild boundary.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'contract-update-observed').Count -eq 0) 'Contract source entered the implementation patch workspace.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -eq 3 }).Count -eq 0) 'A broken build activated an application.'
  Replace-Source $source 'new CounterReply(Interlocked.Add(ref _counter, request.Delta))' 'new CounterReply(Interlocked.Add(ref _counter, request.Delta), 7)'
  Wait-Until $session { $null -ne (Latest-Snapshot $session 4) } 'corrected matching-contract generation' 120
  $corrected = Latest-Snapshot $session 4
  Assert-Condition ($corrected.contract -ne $initial.contract -and $corrected.counter -eq 0) 'Contract regeneration did not change identity or reset backend state.'
  Send-Command $session 'probe' ('stale-contract:' + $initial.contract)
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'bridge-diagnostic' -and $_.generation -eq 4 -and $_.code -eq 'contract_mismatch' }).Count -eq 1 } 'stale fingerprint rejection before mutation'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'backend-activity' -and $_.generation -eq 4 -and $_.detail -eq 'counter-entered' }).Count -eq 0) 'A stale contract entered the mutation handler.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'frontend-ready').Count -eq 1) 'Backend update/rebuild restarted Vite.'
  $originalFrontend = $session.Handles.Values | Where-Object Id -eq $frontend.processId | Select-Object -First 1
  Assert-Condition ($originalFrontend -and !$originalFrontend.HasExited) 'Original frontend process did not survive backend changes.'
  Send-Command $session 'stop'
  Confirm-Exit $session
  @{
    initial = $initial; patched = $patched; failedEdit = $failed; correctedContract = $corrected
    firstApplication = $firstApplication; originalFrontend = $frontend
    delayFinished = $delayEnded; oldApplicationStopped = $stopped; replacementStarted = $started
    mutationReplayed = $false; backendRestartOverlappedOldWork = $false
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixture.Directory 'steps.json')
  if ($VerifyRender) { $results[$results.Count - 1].rendering = 'verified-pixels-review-captures' }

  # Pause a real full build after compilation to make overlapping edits deterministic.
  $fixture = New-BackendFixture 'startup-dependency-build-races'
  $gateScript = Join-Path $fixture.Directory 'build-gate.ps1'
  $gateFlag = Join-Path $fixture.Directory 'build.pause'
  $gateEntered = Join-Path $fixture.Directory 'build.entered'
  @'
param([string]$Flag, [string]$Entered)
if (Test-Path -LiteralPath $Flag) {
  [IO.File]::WriteAllText($Entered, 'compiled')
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  while (Test-Path -LiteralPath $Flag) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'Build gate was not released.' }
    Start-Sleep -Milliseconds 50
  }
  if (Test-Path -LiteralPath ($Flag + '.fail')) {
    Remove-Item -LiteralPath ($Flag + '.fail')
    throw 'Injected obsolete build failure after the gate.'
  }
}
'@ | Set-Content -LiteralPath $gateScript
  $project = $fixture.Settings.project
  $xml = [xml][IO.File]::ReadAllText($project)
  $dependencyDirectory = Join-Path $fixture.Directory 'dependency'
  New-Item -ItemType Directory -Path $dependencyDirectory | Out-Null
  $dependencyProject = Join-Path $dependencyDirectory 'Dependency.csproj'
  '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DefineConstants>INITIAL_DEPENDENCY</DefineConstants></PropertyGroup></Project>' |
    Set-Content -LiteralPath $dependencyProject
  @'
namespace Spar.NativeSample;

public static class DependencyValue
{
#if UPDATED_DEPENDENCY
    public const int InitialCounter = 12;
#else
    public const int InitialCounter = 11;
#endif
}
'@ | Set-Content -LiteralPath (Join-Path $dependencyDirectory 'DependencyValue.cs')
  $fixture.Settings['backendSourceDirectories'] = @($dependencyDirectory)
  $references = $xml.CreateElement('ItemGroup')
  $reference = $xml.CreateElement('ProjectReference')
  $reference.SetAttribute('Include',$dependencyProject)
  $null = $references.AppendChild($reference)
  $null = $xml.Project.AppendChild($references)
  $target = $xml.CreateElement('Target')
  $target.SetAttribute('Name','SparFixtureBuildGate')
  $target.SetAttribute('AfterTargets','Build')
  $target.SetAttribute('Condition',"'`$(DotNetWatchBuild)' != 'true'")
  $exec = $xml.CreateElement('Exec')
  $exec.SetAttribute('Command', ('pwsh -NoProfile -File "{0}" -Flag "{1}" -Entered "{2}"' -f $gateScript,$gateFlag,$gateEntered))
  $null = $target.AppendChild($exec)
  $null = $xml.Project.AppendChild($target)
  $xml.Save($project)
  Get-ChildItem -LiteralPath $fixture.Backend, (Split-Path $fixture.Settings.contractFile), $dependencyDirectory -File |
    Sort-Object FullName | ForEach-Object {
      (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($fixture.Directory,$_.FullName)
    } | Set-Content -LiteralPath (Join-Path $fixture.Directory 'sources.initial.sha256')
  $session = Start-Session $fixture $true
  Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'race fixture readiness' 120
  $startup = Join-Path $fixture.Backend 'Program.Development.cs'
  Replace-Source $startup '            SparApplication.Run(new SparWindowOptions' '            backend.CounterAsync(new CounterRequest(DependencyValue.InitialCounter), CancellationToken.None).GetAwaiter().GetResult();
            SparApplication.Run(new SparWindowOptions'
  Wait-Until $session { (Latest-Snapshot $session 2).counter -eq 11 } 'startup rerun with fresh backend state' 120
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'backend-restarting' -and $_.code -eq 'startup_or_dependency_changed' }).Count -eq 1) 'Startup file did not request an explicit policy restart.'

  Replace-Source $dependencyProject 'INITIAL_DEPENDENCY' 'UPDATED_DEPENDENCY'
  Wait-Until $session { (Latest-Snapshot $session 3).counter -eq 12 } 'referenced dependency rebuilt and startup rerun' 120

  [IO.File]::WriteAllText($gateFlag,'pause')
  Replace-Source $startup 'new CounterRequest(DependencyValue.InitialCounter)' 'new CounterRequest(21)'
  Wait-Until $session { Test-Path -LiteralPath $gateEntered } 'full build paused after compilation' 120
  $source = Join-Path $fixture.Backend 'SampleBackend.cs'
  Replace-Source $source '"Hello, " + request.Name' '"Hello selected latest, " + request.Name'
  Replace-Source $startup 'new CounterRequest(21)' 'new CounterRequest(31)'
  Remove-Item -LiteralPath $gateFlag
  Wait-Until $session { $state = Latest-Snapshot $session 5; $state.counter -eq 31 -and $state.greeting -eq 'Hello selected latest, Spar' } 'latest source build selected after superseded success' 120
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'build-superseded' -and $_.generation -eq 4 }).Count -eq 1) 'Overlapping edit did not supersede the completed build.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'process-started' -and $_.role -eq 'application' -and $_.generation -eq 4 }).Count -eq 0) 'Superseded build launched an application.'

  # A newer invalid edit must not activate the previous successful compilation.
  Remove-Item -LiteralPath $gateEntered
  [IO.File]::WriteAllText($gateFlag,'pause')
  Replace-Source $startup 'new CounterRequest(31)' 'new CounterRequest(41)'
  Wait-Until $session { Test-Path -LiteralPath $gateEntered } 'second build paused after compilation' 120
  Replace-Source $source '"Hello selected latest, " + request.Name' 'MissingRaceSymbol + request.Name'
  Remove-Item -LiteralPath $gateFlag
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'backend-stopped' -and $_.generation -eq 7 }).Count -eq 1 } 'newest invalid source remains stopped' 120
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -ge 6 }).Count -eq 0) 'Invalid latest source activated an older successful build.'
  Replace-Source $source 'MissingRaceSymbol + request.Name' '"Hello recovered latest, " + request.Name'
  Wait-Until $session { $state = Latest-Snapshot $session 8; $state.counter -eq 41 -and $state.greeting -eq 'Hello recovered latest, Spar' } 'automatic stopped recovery after overlapping invalid edit' 120
  Remove-Item -LiteralPath $gateEntered
  [IO.File]::WriteAllText($gateFlag,'pause')
  [IO.File]::WriteAllText(($gateFlag + '.fail'),'fail after release')
  Replace-Source $startup 'new CounterRequest(41)' 'new CounterRequest(51)'
  Wait-Until $session { Test-Path -LiteralPath $gateEntered } 'obsolete failing build paused' 120
  Replace-Source $startup 'new CounterRequest(51)' 'new CounterRequest(61)'
  Remove-Item -LiteralPath $gateFlag
  Wait-Until $session { (Latest-Snapshot $session 10).counter -eq 61 } 'newest build wins over obsolete failure' 120
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'build-superseded' -and $_.generation -eq 9 -and $_.code -eq 'build_failed' }).Count -eq 1) 'Obsolete failure was not discarded.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'backend-stopped' -and $_.generation -eq 9 }).Count -eq 0) 'Obsolete failure replaced current status.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'build-diagnostic' -and $_.generation -eq 7 -and $_.detail -like '*CS0103*' }).Count -ge 1) 'Selected failed rebuild did not expose its file/line compiler diagnostic.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'build-diagnostic' -and $_.generation -eq 9 }).Count -eq 0) 'Obsolete failure published a current diagnostic.'
  if ($VerifyRender) {
    $window = [IntPtr]($session.Events | Where-Object kind -eq 'window-created' | Select-Object -Last 1).window
    Wait-Until $session { [NativeProbeDesktop]::CaptureColor($window, (Join-Path $fixture.Directory 'recovered.bmp'), 1000, 112, 227, 189) -gt 3000 } 'rendered latest recovered generation'
  }
  Assert-Condition (@($session.Events | Where-Object kind -eq 'frontend-ready').Count -eq 1) 'Races replaced the frontend process.'
  Send-Command $session 'stop'
  Confirm-Exit $session
  if ($VerifyRender) { $results[$results.Count - 1].rendering = 'verified-pixels-review-captures' }

  $fixture = New-BackendFixture 'explicit-force-during-retirement'
  $session = Start-Session $fixture $true
  Wait-Until $session { $null -ne (Latest-Snapshot $session) -and @($session.Events | Where-Object kind -eq 'watcher-ready').Count -eq 1 } 'force fixture watcher readiness' 120
  Send-Command $session 'probe' 'delay'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'backend-activity' -and $_.detail -eq 'delay-entered' }).Count -eq 1 } 'force fixture actual work'
  Replace-Source (Join-Path $fixture.Backend 'SampleBackend.cs') ': InitialBackendBase, ISampleBackend' ': ReplacementBackendBase, ISampleBackend'
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-stop-requested').Count -eq 1 } 'graceful retirement waiting for work'
  Send-Command $session 'stop' '' $true
  Confirm-Exit $session
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'session-stopped' -and $_.code -eq 'forced_cleanup' }).Count -eq 1) 'Explicit force was reported as graceful.'
  Assert-Condition (@($session.Events | Where-Object { $_.kind -eq 'process-started' -and $_.role -eq 'application' -and $_.generation -gt 1 }).Count -eq 0) 'Force/stop launched a replacement application.'
} finally {
  [Environment]::SetEnvironmentVariable('DOTNET_WATCH_LOG_LEVEL',$priorWatchLogLevel)
  foreach ($session in $sessions) {
    if (!$session.Process.HasExited) { $session.Process.Kill(); $null = $session.Process.WaitForExit(10000) }
    foreach ($owned in $session.Handles.Values) { $null = $owned.WaitForExit(10000); $owned.Dispose() }
    $session.Process.Dispose()
  }
  if ($unrelated) {
    if (!$unrelated.HasExited) { $unrelated.Kill(); $null = $unrelated.WaitForExit(10000) }
    $unrelated.Dispose()
  }
  $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidencePath 'receipt.json')
}
$results | Format-Table
