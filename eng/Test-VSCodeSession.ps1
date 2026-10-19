#requires -Version 7.4
param(
  [string]$CodeCommand = 'code',
  [string]$EvidenceDirectory = 'artifacts/development/vscode',
  [switch]$WatchBackend
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$codeScript = (Get-Command $CodeCommand).Source
$codeExecutable = [IO.Path]::GetFullPath((Join-Path (Split-Path $codeScript) '../Code.exe'))
if (!(Test-Path -LiteralPath $codeExecutable)) { throw 'Pass the installed VS Code CLI command; Code.exe was not found beside bin.' }
$coordinator = (Resolve-Path -LiteralPath 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$application = (Resolve-Path -LiteralPath 'samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$frontend = Join-Path $repository 'samples/Spar.NativeSample/frontend'
$results = [Collections.Generic.List[object]]::new()

function Read-Shared([string]$Path) {
  if (!(Test-Path -LiteralPath $Path)) { return '' }
  $stream = [IO.FileStream]::new($Path, 'Open', 'Read', 'ReadWrite')
  $reader = [IO.StreamReader]::new($stream)
  try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

foreach ($scenario in @('task-stop', 'window-close')) {
  $directory = Join-Path $root ($scenario + '-' + [guid]::NewGuid().ToString('N'))
  $profile = Join-Path $directory 'profile'
  $workspace = Join-Path $directory 'workspace'
  New-Item -ItemType Directory -Path (Join-Path $profile 'User'), $workspace -Force | Out-Null
  @{
    'window.confirmBeforeClose' = 'never'; 'terminal.integrated.confirmOnExit' = 'never'
    'task.reconnection' = $false; 'task.confirmOnTerminate' = 'never'; 'telemetry.telemetryLevel' = 'off'
  } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'User/settings.json')
  $sessionPath = Join-Path $directory 'spar.dev.json'
  @{
    project = Join-Path $repository 'samples/Spar.NativeSample/Spar.NativeSample.csproj'; application = $application
    generatedClient = Join-Path $frontend 'src/generated/client.js'; sessionDirectory = Join-Path $directory 'sessions'
    watchBackend = $WatchBackend.IsPresent
    contractFile = Join-Path $repository 'samples/Spar.NativeSample.Contracts/BackendContract.cs'
    frontend = @{ fileName = 'cmd.exe'; arguments = @('/d','/c','npm','run','dev'); workingDirectory = $frontend; port = 0 }
  } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $sessionPath
  $events = Join-Path $directory 'coordinator.jsonl'
  @{
    coordinator = $coordinator; session = $sessionPath; events = $events; repository = $repository; scenario = $scenario
    watchBackend = $WatchBackend.IsPresent
  } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'probe.json')
  $start = [Diagnostics.ProcessStartInfo]::new($codeExecutable)
  $start.UseShellExecute = $false
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($argument in @('--new-window','--wait','--skip-welcome','--skip-release-notes','--disable-workspace-trust',
    '--disable-extensions','--user-data-dir',$profile,'--extensions-dir',(Join-Path $directory 'extensions'),
    '--extensionDevelopmentPath',(Join-Path $PSScriptRoot 'vscode-session-probe'),
    '--extensionTestsPath',(Join-Path $PSScriptRoot 'vscode-session-probe/test.cjs'),$workspace)) {
    $start.ArgumentList.Add($argument)
  }
  $handles = [Collections.Generic.Dictionary[string,Diagnostics.Process]]::new()
  $editor = [Diagnostics.Process]::Start($start)
  $null = $editor.Handle
  $editorOutput = $editor.StandardOutput.ReadToEndAsync()
  $editorErrors = $editor.StandardError.ReadToEndAsync()
  $passed = $false
  try {
    $deadline = [DateTime]::UtcNow.AddSeconds(110)
    $readyAt = $null
    $signalled = $false
    do {
      $records = @((Read-Shared $events) -split "`n" | Where-Object { $_.Trim().EndsWith('}') } | ForEach-Object { $_ | ConvertFrom-Json })
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
      if (!$readyAt -and @($records | Where-Object kind -eq 'application-ready').Count) { $readyAt = [DateTime]::UtcNow }
      if (!$signalled -and $readyAt -and ([DateTime]::UtcNow - $readyAt).TotalSeconds -ge 2) {
        if ($handles.Count -lt 4) { throw 'Insufficient retained process observations.' }
        Set-Content -LiteralPath (Join-Path $workspace 'proceed') -Value 'identities retained'
        $signalled = $true
      }
      if ((Read-Shared (Join-Path $workspace 'editor.jsonl')) -match '"kind":"failed"') { throw 'VS Code probe reported failure.' }
      if ($editor.HasExited) { break }
      Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$signalled) { throw 'Editor task did not reach matching SPA/backend readiness.' }
    if (!$editor.WaitForExit(15000)) { throw 'Isolated VS Code window did not close.' }
    foreach ($process in $handles.Values) {
      if (!$process.WaitForExit(15000)) { throw "Owned process $($process.Id) outlived editor ownership." }
    }
    $actions = Read-Shared (Join-Path $workspace 'editor.jsonl')
    if ($actions -notmatch '"kind":"owner-action-requested"') { throw 'No real editor owner action was observed.' }
    if ($scenario -eq 'task-stop' -and $actions -notmatch '"kind":"completed"') { throw 'Task termination was not confirmed by the editor API.' }
    $passed = $true
    $results.Add(@{ scenario = $scenario; passed = $true; independentlyRetainedProcesses = $handles.Count
      coordinatorAndObservedChildrenExited = $true; editorExitCode = $editor.ExitCode; debuggerHotReload = 'not-exercised' })
  } finally {
    if (!$passed) {
      # These are retained identities from this isolated editor/session, never arbitrary process names.
      foreach ($process in $handles.Values) { if (!$process.HasExited) { $process.Kill(); $null = $process.WaitForExit(10000) } }
      if (!$editor.HasExited) { $editor.Kill($true); $null = $editor.WaitForExit(10000) }
    }
    foreach ($process in $handles.Values) { $process.Dispose() }
    if ($editor.HasExited) {
      $editorOutput.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory 'editor.stdout.log')
      $editorErrors.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $directory 'editor.stderr.log')
    }
    $editor.Dispose()
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'receipt.json')
  }
}
$results | ConvertTo-Json -Depth 6
