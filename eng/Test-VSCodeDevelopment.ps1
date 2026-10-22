#requires -Version 7.4
param([string]$CodeCommand = 'code', [switch]$VerifyRender, [string]$ConsumerDirectory, [switch]$MinimalApplication,
  [switch]$ReviewPresentation)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
$repository = Split-Path $PSScriptRoot
$coordinatorPath = (Resolve-Path -LiteralPath 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$applicationPath = (Resolve-Path -LiteralPath 'fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$evidencePath = [IO.Path]::GetFullPath('artifacts/vscode-development')
New-Item -ItemType Directory -Path $evidencePath -Force | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
$extensionDirectory = Join-Path $repository 'tools/Spar.VSCode'
if ($ConsumerDirectory) {
  $consumerKind = Get-Content -LiteralPath (Join-Path $ConsumerDirectory 'consumer.json') -Raw | ConvertFrom-Json
  $expectedKind = if ($MinimalApplication) { 'minimal-application' } else { 'integration-fixture' }
  if ($consumerKind.kind -ne $expectedKind) {
    throw "This acceptance lane requires a fresh $expectedKind package consumer."
  }
  # TEMP can contain an 8.3 alias. The editor and project system must see the same source identity.
  $consumerRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ConsumerDirectory).Path)
  $coordinatorPath = Join-Path $consumerRoot '.tools/Spar.Dev.exe'
  $extensionDirectory = Join-Path $consumerRoot '.vscode/spar-extension'
  $consumerSettings = Get-Content -LiteralPath (Join-Path $consumerRoot 'spar.dev.json') -Raw | ConvertFrom-Json -AsHashtable
  foreach ($key in @('project', 'application', 'generatedClient', 'contractFile')) {
    $consumerSettings[$key] = [IO.Path]::GetFullPath($consumerSettings[$key], $consumerRoot)
  }
  $consumerSettings.sessionDirectory = Join-Path $consumerRoot 'artifacts/debugger-sessions'
  $fixture = [pscustomobject]@{ Directory = $consumerRoot; Settings = $consumerSettings; Config = (Join-Path $consumerRoot 'spar.dev.json') }
  $consumerSource = Join-Path $consumerRoot 'backend/SampleBackend.cs'
  if (!$MinimalApplication) {
    $source = [IO.File]::ReadAllText($consumerSource).Replace(': ISampleBackend', ': InitialBackendBase, ISampleBackend').Replace('> 5000', '> 15000')
    $source += "`npublic class InitialBackendBase;`npublic class ReplacementBackendBase;`n"
    [IO.File]::WriteAllText($consumerSource, $source)
  }
} else {
  $fixture = New-BackendFixture 'debugger' -Repository $repository -Application $applicationPath -EvidenceDirectory $evidencePath -MinimalApplication:$MinimalApplication
}
$fixture.Settings.watchBackend = $false
$fixture.Settings['debuggerBackend'] = $true
if (!$MinimalApplication -and !$ConsumerDirectory) {
  Replace-Source (Join-Path $fixture.Frontend 'src/App.tsx') '  return <main>' '  return <main><button id="reload-status" onClick={() => location.reload()}>Reload document for acceptance</button>'
}
$fixture.Settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixture.Config
@{ coordinator = $coordinatorPath; settings = $fixture.Config; project = $fixture.Settings.project; probe = !$MinimalApplication; inspect = $true; verifyRender = $VerifyRender.IsPresent; reviewPresentation = $ReviewPresentation.IsPresent; reloadStatus = (!$MinimalApplication -and !$ConsumerDirectory) } |
  ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture.Directory 'owner.json')
if (!$ConsumerDirectory) {
  Set-Content -LiteralPath (Join-Path $fixture.Directory 'Fixture.slnx') -Value '<Solution><Project Path="backend/Spar.NativeSample.csproj" /></Solution>'
}
$profile = Join-Path $fixture.Directory 'profile'
New-Item -ItemType Directory -Path (Join-Path $profile 'User') -Force | Out-Null
@{ 'csharp.experimental.debug.hotReload' = $true; 'csharp.debug.hotReloadOnSave' = $true;
   'csharp.debug.hotReloadVerbosity' = 'diagnostic'; 'extensions.autoUpdate' = $false } | ConvertTo-Json |
  Set-Content -LiteralPath (Join-Path $profile 'User/settings.json')
dotnet build $fixture.Settings.project --disable-build-servers
if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
Write-Output ('Debugger evidence: ' + $fixture.Directory)
$codeScript = (Get-Command $CodeCommand).Source
$codeExecutable = [IO.Path]::GetFullPath((Join-Path (Split-Path $codeScript) '../Code.exe'))
$start = [Diagnostics.ProcessStartInfo]::new($codeExecutable)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$testEntry = if ($MinimalApplication) { 'vscode-development/minimal.cjs' } else { 'vscode-development/test.cjs' }
foreach ($argument in @('--new-window','--wait','--skip-welcome','--skip-release-notes','--disable-workspace-trust',
  '--user-data-dir',$profile,'--extensions-dir',(Join-Path $env:USERPROFILE '.vscode/extensions'),
  '--extensionDevelopmentPath',$extensionDirectory,
  '--extensionTestsPath',(Join-Path $PSScriptRoot $testEntry),$fixture.Directory)) {
  $start.ArgumentList.Add($argument)
}
$handles = [Collections.Generic.Dictionary[string,Diagnostics.Process]]::new()
$editor = [Diagnostics.Process]::Start($start)
$null = $editor.Handle
$editorOutput = $editor.StandardOutput.ReadToEndAsync()
$editorErrors = $editor.StandardError.ReadToEndAsync()
$passed = $false
try {
  $deadline = [DateTime]::UtcNow.AddMinutes(8)
  $eventPath = Join-Path $fixture.Directory 'coordinator.ndjson'
  do {
    if (Test-Path -LiteralPath $eventPath) {
      $records = @((Read-Shared $eventPath) -split "`n" | Where-Object { $_.Trim().EndsWith('}') } | ForEach-Object { $_ | ConvertFrom-Json })
      foreach ($record in $records) {
        $id = if ($record.kind -eq 'session-started') { $record.coordinatorProcessId } else { $record.processId }
        if (!$id) { continue }
        $key = if ($record.processStartedAtUtc) { "$id/$($record.processStartedAtUtc)" } else { "$id/coordinator" }
        if ($handles.ContainsKey($key)) { continue }
        try {
          $process = [Diagnostics.Process]::GetProcessById([int]$id)
          $null = $process.Handle
          if ($record.processStartedAtUtc -and $process.StartTime.ToUniversalTime().Ticks -ne ([DateTime]$record.processStartedAtUtc).ToUniversalTime().Ticks) {
            $process.Dispose()
            throw 'Observed PID was reused before its identity could be retained.'
          }
          $handles.Add($key, $process)
        } catch [ArgumentException] { }
      }
    }
    if ($editor.HasExited) { break }
    $closeRequest = Join-Path $fixture.Directory 'native-close.json'
    $closeDone = Join-Path $fixture.Directory 'native-close.done'
    if ((Test-Path -LiteralPath $closeRequest) -and !(Test-Path -LiteralPath $closeDone)) {
      $close = Get-Content -LiteralPath $closeRequest -Raw | ConvertFrom-Json
      [NativeProbeDesktop]::Close([IntPtr]$close.window)
      Set-Content -LiteralPath $closeDone -Value 'native close requested'
    }
    $captureRequest = Join-Path $fixture.Directory 'capture.json'
    $captureDone = Join-Path $fixture.Directory 'capture.done'
    if ((Test-Path -LiteralPath $captureRequest) -and !(Test-Path -LiteralPath $captureDone)) {
      $capture = Get-Content -LiteralPath $captureRequest -Raw | ConvertFrom-Json
      [NativeProbeDesktop]::ShowWindow([IntPtr]$capture.window, 5) | Out-Null
      $pixels = [NativeProbeDesktop]::CaptureColor([IntPtr]$capture.window, (Join-Path $fixture.Directory 'patched.bmp'), 1000, 233, 180, 76)
      if ($pixels -gt 3000) { Set-Content -LiteralPath $captureDone -Value $pixels }
    }
    Start-Sleep -Milliseconds 100
  } while ([DateTime]::UtcNow -lt $deadline)
  if (!$editor.WaitForExit(10000)) { throw 'Isolated debugger editor did not exit.' }
  foreach ($process in $handles.Values) {
    if (!$process.WaitForExit(10000)) { throw "Observed process $($process.Id) outlived the debugger session." }
  }
  if ($handles.Count -lt 10) { throw 'Insufficient independently retained session processes.' }
  $passed = $true
} finally {
  if (!$passed) {
    foreach ($process in $handles.Values) { if (!$process.HasExited) { $process.Kill(); $null = $process.WaitForExit(10000) } }
    if (!$editor.HasExited) { $editor.Kill($true); $null = $editor.WaitForExit(10000) }
  }
  foreach ($process in $handles.Values) { $process.Dispose() }
  if ($editor.HasExited) {
    $editorOutput.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $fixture.Directory 'editor.stdout.log')
    $editorErrors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $fixture.Directory 'editor.stderr.log')
  }
}
$receiptPath = Join-Path $fixture.Directory 'receipt.json'
if (!(Test-Path -LiteralPath $receiptPath)) { throw ('Missing debugger receipt: ' + $fixture.Directory) }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$receipt | Add-Member -NotePropertyName independentlyRetainedProcesses -NotePropertyValue $handles.Count
$receipt | Add-Member -NotePropertyName observedProcessesExited -NotePropertyValue $true
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath
$receipt | ConvertTo-Json -Depth 8
if ($editor.ExitCode -ne 0 -or $receipt.outcome -ne 'passed') { throw ('Debugger acceptance failed: ' + $fixture.Directory) }
$editor.Dispose()
