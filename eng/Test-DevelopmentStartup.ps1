#requires -Version 7.4
param(
  [string]$Coordinator = 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe',
  [string]$EvidenceDirectory = 'artifacts/development-startup/probe'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $root | Out-Null
$tool = (Resolve-Path $Coordinator).Path
$library = Join-Path (Split-Path $tool) 'Sotsera.Spar.Development.dll'
Add-Type -Path $library
$settings = @{
  project = "$root/app.csproj"; application = "$root/app.exe"
  generatedClient = "$root/client.js"; sessionDirectory = "$root/must-not-exist"
  frontend = @{ workingDirectory = $root }
}
$valid = Join-Path $root 'valid.json'
$settings | ConvertTo-Json -Depth 5 | Set-Content $valid
$invalid = Join-Path $root 'limits.json'
$settings + @{ startupTimeoutSeconds = 0 } | ConvertTo-Json -Depth 5 | Set-Content $invalid
'{' | Set-Content "$root/malformed.json"
'null' | Set-Content "$root/null.json"
(' ' * 65537) | Set-Content "$root/oversized.json"
'preserve event file' | Set-Content "$root/existing.jsonl"
'session path is a file' | Set-Content "$root/session-file"
$lineBreakConfig = "$root/line$([char]0x2028)break.json"
Copy-Item $invalid $lineBreakConfig
$watch = $settings + @{ watchBackend = $true; contractFile = "$root/contract.cs" }
$watch | ConvertTo-Json -Depth 5 | Set-Content "$root/watch.json"
$longMissing = "$root/" + ((1..20 | ForEach-Object { 'x' * 100 }) -join '/') + '/missing.json'
$settings.sessionDirectory = "$root/session-file"
$settings | ConvertTo-Json -Depth 5 | Set-Content "$root/blocked-session.json"
$denied = Join-Path $root 'denied-session'
New-Item -ItemType Directory -Force $denied | Out-Null
$originalAcl = Get-Acl -LiteralPath $denied
$restrictedAcl = Get-Acl -LiteralPath $denied
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
$restriction = [Security.AccessControl.FileSystemAccessRule]::new($identity,
  [Security.AccessControl.FileSystemRights]::CreateDirectories, [Security.AccessControl.AccessControlType]::Deny)
$restrictedAcl.AddAccessRule($restriction)
$settings.sessionDirectory = $denied
$settings | ConvertTo-Json -Depth 5 | Set-Content "$root/denied-session.json"
$prospective = "$root/must-not-create.jsonl"
$cases = @(
  @{ name = 'usage'; arguments = @(); code = $null },
  @{ name = 'missing-config'; arguments = @("$root/absent.json"); code = 'configuration_not_found' },
  @{ name = 'malformed-config'; arguments = @("$root/malformed.json"); code = 'configuration_invalid' },
  @{ name = 'null-config'; arguments = @("$root/null.json"); code = 'configuration_invalid' },
  @{ name = 'oversized-config'; arguments = @("$root/oversized.json"); code = 'configuration_invalid' },
  @{ name = 'invalid-limits'; arguments = @($invalid); code = 'configuration_invalid' },
  @{ name = 'config-is-directory'; arguments = @($root); code = 'configuration_not_found' },
  @{ name = 'locked-config'; arguments = @($valid); code = 'configuration_unavailable'; lockConfiguration = $true },
  @{ name = 'bounded-message'; arguments = @($longMissing); code = 'configuration_not_found'; truncated = $true },
  @{ name = 'normalized-message'; arguments = @($lineBreakConfig); code = 'configuration_invalid' },
  @{ name = 'unknown-before-config'; arguments = @("$root/absent.json",'--unknown-private-marker'); code = 'invalid_arguments' },
  @{ name = 'unknown-before-events'; arguments = @($valid,'--events-file',$prospective,'--unknown'); code = 'invalid_arguments' },
  @{ name = 'duplicate-flag'; arguments = @($valid,'--inspect','--inspect'); code = 'invalid_arguments' },
  @{ name = 'extra-positional'; arguments = @($valid,'extra.json'); code = 'invalid_arguments' },
  @{ name = 'option-first'; arguments = @('--inspect',$valid); code = 'invalid_arguments' },
  @{ name = 'missing-events-value'; arguments = @($valid,'--events-file'); code = 'invalid_arguments' },
  @{ name = 'empty-events-value'; arguments = @($valid,'--events-file',''); code = 'invalid_arguments' },
  @{ name = 'events-value-is-option'; arguments = @($valid,'--events-file','--inspect'); code = 'invalid_arguments' },
  @{ name = 'missing-owner-values'; arguments = @($valid,'--project-owner'); code = 'invalid_arguments' },
  @{ name = 'missing-owner-ticks'; arguments = @($valid,'--project-owner','1'); code = 'invalid_arguments' },
  @{ name = 'invalid-owner-pid'; arguments = @($valid,'--project-owner','bad','1'); code = 'invalid_arguments' },
  @{ name = 'invalid-owner-ticks'; arguments = @($valid,'--project-owner','1','bad'); code = 'invalid_arguments' },
  @{ name = 'owner-ticks-overflow'; arguments = @($valid,'--project-owner','1','9223372036854775807'); code = 'invalid_arguments' },
  @{ name = 'duplicate-events-option'; arguments = @($valid,'--events-file',$prospective,'--events-file',$prospective); code = 'invalid_arguments' },
  @{ name = 'owner-unavailable'; arguments = @($valid,'--project-owner','2147483647','1'); code = 'project_owner_unavailable' },
  @{ name = 'owner-mismatch'; arguments = @($valid,'--project-owner',"$PID",'1'); code = 'project_owner_mismatch' },
  @{ name = 'debugger-contract-required'; arguments = @($valid,'--debugger'); code = 'contract_required' },
  @{ name = 'watch-build-required'; arguments = @("$root/watch.json",'--no-build'); code = 'build_required' },
  @{ name = 'debugger-build-required'; arguments = @("$root/watch.json",'--debugger','--no-build'); code = 'build_required' },
  @{ name = 'existing-event-file'; arguments = @($valid,'--events-file',"$root/existing.jsonl"); code = 'events_file_unavailable' },
  @{ name = 'event-destination-directory'; arguments = @($valid,'--events-file',$root); code = 'events_file_unavailable' },
  @{ name = 'missing-event-directory'; arguments = @($valid,'--events-file',"$root/absent/events.jsonl"); code = 'events_file_unavailable' },
  @{ name = 'blocked-session-directory'; arguments = @("$root/blocked-session.json"); code = 'session_directory_unavailable' },
  @{ name = 'denied-session-subdirectory'; arguments = @("$root/denied-session.json"); code = 'session_directory_unavailable' }
)
$results = [Collections.Generic.List[object]]::new()
function Observe-Children($Job, $Coordinator, $Children, $Helpers, $Unclassified) {
  foreach ($id in $Job.ActiveProcessIds()) {
    if ($id -eq $Coordinator.Id -or $Helpers.Contains($id) -or $Children.Contains($id)) { continue }
    $child = $null
    try {
      $child = [Diagnostics.Process]::GetProcessById($id)
      $null = $child.Handle
      if (!$Job.Contains($child)) { continue }
      $image = $child.MainModule.FileName
      $consoleHost = Join-Path ([Environment]::SystemDirectory) 'conhost.exe'
      if ([string]::Equals($image, $consoleHost, [StringComparison]::OrdinalIgnoreCase)) {
        $null = $Helpers.Add($id)
      } else { $null = $Children.Add($id) }
    } catch [ArgumentException] { $null = $Unclassified.Add($id) }
      catch [InvalidOperationException] { $null = $Unclassified.Add($id) }
    finally { if ($child) { $child.Dispose() } }
  }
}
try {
  Set-Acl -LiteralPath $denied -AclObject $restrictedAcl
  foreach ($case in $cases) {
    $job = [Sotsera.Spar.Development.OwnedProcessJob]::new()
    $process = $null
    $stdout = Join-Path $root "$($case.name).stdout.log"
    $stderr = Join-Path $root "$($case.name).stderr.log"
    $children = [Collections.Generic.HashSet[int]]::new()
    $helpers = [Collections.Generic.HashSet[int]]::new()
    $unclassified = [Collections.Generic.HashSet[int]]::new()
    $locked = $null
    try {
      if ($case.lockConfiguration) {
        $locked = [IO.File]::Open($valid, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
      }
      $process = $job.Start($tool, [string[]]$case.arguments, $PWD.Path,
        [Collections.Generic.Dictionary[string,string]]::new(), $stdout, $stderr)
      $null = $process.Handle
      $deadline = [DateTime]::UtcNow.AddSeconds(30)
      while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Observe-Children $job $process $children $helpers $unclassified
        Start-Sleep -Milliseconds 10
      }
      if (!$process.HasExited) { throw "Startup case $($case.name) did not exit." }
      $process.WaitForExit()
      # Actual process exit can precede the job's accounting update. Observe the job becoming empty too.
      do {
        $remaining = $job.ActiveProcessIds()
        Observe-Children $job $process $children $helpers $unclassified
        if (!$remaining.Length) { break }
        Start-Sleep -Milliseconds 10
      } while ([DateTime]::UtcNow -lt $deadline)
      if ($process.ExitCode -ne 1) { throw "Unexpected exit $($process.ExitCode) for $($case.name)." }
      $output = @(Get-Content $stdout | Where-Object { $_.Trim() })
      $errors = @(Get-Content $stderr | Where-Object { $_.Trim() })
      if ($case.name -eq 'usage') {
        if ($output.Count -ne 0 -or $errors.Count -ne 1 -or !$errors[0].StartsWith('Usage: Spar.Dev')) { throw 'Usage changed unexpectedly.' }
      } else {
        if ($output.Count -ne 1 -or $errors.Count -ne 1) { throw "Startup output is not bounded for $($case.name)." }
        $startupError = $output[0] | ConvertFrom-Json
        if ($startupError.kind -ne 'startup-error' -or $startupError.code -ne $case.code -or $startupError.message.Length -gt 1024 -or
           $startupError.PSObject.Properties.Name -contains 'session' -or $startupError.PSObject.Properties.Name -contains 'generation') {
          throw "Invalid pre-session error for $($case.name)."
        }
        if (!$errors[0].StartsWith("Spar startup failed [$($case.code)]:") -or $errors[0].Contains('Unhandled exception') -or
           ($output[0] + $errors[0]).Contains('private-marker')) { throw 'Startup error exposes unexpected content.' }
        if ($startupError.message.IndexOfAny([char[]]@([char]0x2028,[char]0x2029)) -ge 0) { throw 'Unnormalized line separator.' }
        if ($case.truncated -and ($startupError.message.Length -ne 1024 -or !$startupError.message.EndsWith('...'))) { throw 'Long message was not bounded.' }
      }
      if ($children.Count -ne 0 -or $job.ActiveProcessIds().Length -ne 0 -or (Test-Path $prospective) -or
        (Test-Path "$root/must-not-exist")) { throw "Rejected startup $($case.name) created child/session resources: $($children -join ',')." }
      if ((Get-Content "$root/existing.jsonl" -Raw).Trim() -ne 'preserve event file') { throw 'Existing event file was modified.' }
      if (@(Get-ChildItem -LiteralPath $denied -Force).Count) { throw 'Denied startup created a session subdirectory.' }
      $results.Add(@{ name = $case.name; passed = $true; exitCode = $process.ExitCode; code = $case.code
        processId = $process.Id; processStartedAtUtc = $process.StartTime.ToUniversalTime().ToString('o')
        observedApplicationChildren = $children.Count; consoleHelpers = $helpers.Count
        shortLivedIdentityUnavailable = $unclassified.Count; ownedJobEmpty = $true
        stdoutLines = $output.Count; stderrLines = $errors.Count })
    } finally {
      if ($locked) { $locked.Dispose() }
      try {
        if ($job.ActiveProcessIds().Length) { $job.Terminate() }
        if ($process -and !$process.HasExited -and !$process.WaitForExit(30000)) {
          throw "Coordinator cleanup did not complete for $($case.name)."
        }
        $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(30)
        while ($job.ActiveProcessIds().Length -and [DateTime]::UtcNow -lt $cleanupDeadline) {
          Start-Sleep -Milliseconds 10
        }
        if ($job.ActiveProcessIds().Length) { throw "Owned job cleanup did not complete for $($case.name)." }
      } finally {
        if ($process) { $process.Dispose() }
        $job.Dispose()
      }
    }
  }
} finally {
  Set-Acl -LiteralPath $denied -AclObject $originalAcl
  @{ allPassed = ($results.Count -eq $cases.Count); cases = $results.ToArray(); coordinatorSha256 = (Get-FileHash $tool).Hash } |
    ConvertTo-Json -Depth 7 | Set-Content (Join-Path $root 'receipt.json')
}
Write-Output "Passed $($results.Count) preflight processes; no observed application children/session creation; owned jobs emptied."
