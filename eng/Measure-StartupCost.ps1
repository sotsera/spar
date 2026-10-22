#requires -Version 7.4
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SettingsFile,
  [string]$Coordinator = 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe',
  [string]$EvidenceDirectory = 'artifacts/startup-cost/probe',
  [ValidateRange(2, 10)][int]$Sessions = 3
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
$configuration = (Resolve-Path -LiteralPath $SettingsFile).Path
$configurationRoot = Split-Path $configuration
$settings = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json -AsHashtable
foreach ($key in @('project','application','generatedClient','sessionDirectory','contractFile')) {
  if ($settings[$key]) { $settings[$key] = [IO.Path]::GetFullPath($settings[$key], $configurationRoot) }
}
foreach ($key in @('restartFiles','backendSourceDirectories')) {
  if ($settings[$key]) { $settings[$key] = @($settings[$key] | ForEach-Object { [IO.Path]::GetFullPath($_, $configurationRoot) }) }
}
$settings.frontend.workingDirectory = [IO.Path]::GetFullPath($settings.frontend.workingDirectory, $configurationRoot)
$tool = (Resolve-Path -LiteralPath $Coordinator).Path
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $root | Out-Null
$settings.sessionDirectory = Join-Path $root 'sessions'
$owned = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$unrelated = $null
$completed = $false
$cleaned = $false
$sessionsClosed = $false
try {
  $unrelated = Start-UnrelatedProcess
  for ($run = 1; $run -le $Sessions; $run++) {
    $directory = Join-Path $root "run-$run"
    New-Item -ItemType Directory $directory | Out-Null
    $fixture = [pscustomobject]@{ Name = "startup-cost-$run"; Directory = $directory
      Settings = $settings; Config = (Join-Path $directory 'spar.dev.json') }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $session = Start-Session $fixture $true -Probe $false -Coordinator $tool -WorkingDirectory $configurationRoot
    $owned.Add($session)
    Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 -and
      @($session.Events | Where-Object kind -eq 'watcher-ready').Count -eq 1 } 'native/typed and watcher readiness' 120
    $startupMs = $clock.Elapsed.TotalMilliseconds
    $initial = $session.Events | Where-Object kind -eq 'application-ready' | Select-Object -First 1
    $clock.Restart()
    Send-Command $session 'restart'
    Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -eq 2 }).Count -eq 1 } 'rebuilt replacement readiness' 120
    $restartMs = $clock.Elapsed.TotalMilliseconds
    $old = $session.Handles.Values | Where-Object Id -eq $initial.processId | Select-Object -First 1
    Assert-Condition ($old -and $old.HasExited) 'Restart did not retire the previous process.'
    Assert-Condition (@($session.Events | Where-Object kind -eq 'frontend-ready').Count -eq 1) 'Restart replaced the frontend.'
    Send-Command $session 'stop'
    $exit = Confirm-Exit $session -Unrelated $unrelated
    $results.Add(@{ run = $run; startupMs = $startupMs; commandRebuildMs = $restartMs; cleanup = $exit
      events = @($session.Events | Where-Object { $_.kind -in @('session-phase','build-selected','frontend-ready',
        'application-ready','backend-restarting','application-stopped','watcher-stopped') } |
        Select-Object kind,code,generation,observedAt) })
    Write-Output "Session $run`: startup $([Math]::Round($startupMs)) ms, command rebuild $([Math]::Round($restartMs)) ms; owned exit verified."
  }
  $completed = $true
} finally {
  try {
    Close-TestSessions $owned
    $sessionsClosed = $true
  } finally {
    try {
      if ($unrelated) { Stop-TestProcess $unrelated }
      $cleaned = $sessionsClosed
    } finally {
      @{ passed = ($completed -and $cleaned -and $results.Count -eq $Sessions); source = (& git rev-parse HEAD)
        workingTreeDirty = [bool](& git status --porcelain); coordinatorSha256 = (Get-FileHash $tool).Hash
        developmentSha256 = (Get-FileHash (Join-Path (Split-Path $tool) 'Sotsera.Spar.Development.dll')).Hash
        driverSha256 = (Get-FileHash $PSCommandPath).Hash; settingsFile = $configuration
        endpoint = 'Native/typed application-ready; startup also waits for watcher-ready; 50ms observer polling'
        restart = 'Explicit command rebuild with unchanged source, not source-save latency'
        cache = 'Caller prepares dependencies and outputs; no caches cleared; new session profile on every start'
        runs = $results.ToArray(); cleaned = $cleaned
      } | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $root 'receipt.json')
    }
  }
}
