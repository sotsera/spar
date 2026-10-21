#requires -Version 7.4
param(
  [string]$ConsumerDirectory,
  [string]$EvidenceDirectory = 'artifacts/minimal/development'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/Fixtures.ps1')
. (Join-Path $PSScriptRoot 'integration/Sessions.ps1')
. (Join-Path $PSScriptRoot 'integration/Processes.ps1')
. (Join-Path $PSScriptRoot 'integration/Inspection.ps1')
$repository = Split-Path $PSScriptRoot
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$coordinator = Join-Path $repository 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe'
$workingDirectory = $repository
if ($ConsumerDirectory) {
  $workingDirectory = (Resolve-Path -LiteralPath $ConsumerDirectory).Path
  $kind = Get-Content -LiteralPath (Join-Path $workingDirectory 'consumer.json') -Raw | ConvertFrom-Json
  Assert-Condition ($kind.kind -eq 'minimal-application') 'Use a fresh ordinary package consumer.'
  $coordinator = Join-Path $workingDirectory '.tools/Spar.Dev.exe'
  $settings = Get-Content -LiteralPath (Join-Path $workingDirectory 'spar.dev.json') -Raw | ConvertFrom-Json -AsHashtable
  foreach ($key in @('project', 'application', 'generatedClient', 'contractFile')) {
    $settings[$key] = [IO.Path]::GetFullPath($settings[$key], $workingDirectory)
  }
  $settings.frontend.workingDirectory = Join-Path $workingDirectory 'backend/frontend'
  $settings.restartFiles = @($settings.restartFiles | ForEach-Object { [IO.Path]::GetFullPath($_, $workingDirectory) })
  $settings.sessionDirectory = Join-Path $evidence 'sessions'
  $fixture = [pscustomobject]@{ Name = 'minimal-package'; Directory = $evidence; Settings = $settings;
    Config = (Join-Path $evidence 'spar.dev.json'); Backend = (Join-Path $workingDirectory 'backend'); Frontend = $settings.frontend.workingDirectory }
} else {
  $application = Join-Path $repository 'samples/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
  $fixture = New-BackendFixture 'minimal-source' -Repository $repository -Application $application -EvidenceDirectory $evidence -MinimalApplication
}
$source = Join-Path $fixture.Backend 'SampleBackend.cs'
$component = Join-Path $fixture.Frontend 'src/App.tsx'
$originalSource = [IO.File]::ReadAllText($source)
$originalComponent = [IO.File]::ReadAllText($component)
$sessions = [Collections.Generic.List[object]]::new()
$unrelated = $null
try {
  $unrelated = Start-UnrelatedProcess
  $session = Start-Session $fixture $true $true $false -Coordinator $coordinator -WorkingDirectory $workingDirectory
  $sessions.Add($session)
  Wait-Until $session { @($session.Events | Where-Object kind -eq 'application-ready').Count -eq 1 } 'minimal application readiness' 150
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello, Spar') } 'typed greeting'
  $null = Inspect $session 'type' 'input' 'Ordinary application state'
  $null = Inspect $session 'click' '#local'
  $null = Inspect $session 'click' '#backend'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Backend counter: 1') } 'backend mutation completion'
  Replace-Source $component 'A live development loop.' 'A minimal application, updated.'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('A minimal application, updated.') } 'frontend HMR'
  Replace-Source $source '"Hello, " + request.Name' '"Hello minimal, " + request.Name'
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello minimal, Spar') } 'backend Hot Reload' 90
  $snapshot = Inspect $session 'snapshot'
  Assert-Condition ($snapshot.text.Contains('Local counter: 1') -and $snapshot.text.Contains('Backend counter: 1')) 'HMR lost state or replayed a mutation.'
  Assert-Condition (@($snapshot.elements | Where-Object value -eq 'Ordinary application state').Count -eq 1) 'HMR lost input state.'
  $snapshot | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $fixture.Directory 'updated-snapshot.json')
  Copy-Item -LiteralPath (Inspect $session 'screenshot') -Destination (Join-Path $fixture.Directory 'updated.png')
  Send-Command $session 'restart'
  Wait-Until $session { @($session.Events | Where-Object { $_.kind -eq 'application-ready' -and $_.generation -eq 2 }).Count -eq 1 } 'coordinated restart' 150
  Wait-Until $session { (Inspect $session 'snapshot').text.Contains('Hello minimal, Spar') } 'rebuilt greeting'
  Assert-Condition ((Inspect $session 'snapshot').text.Contains('Backend counter: 0')) 'Restart did not reset backend state.'
  Assert-Condition (@($session.Events | Where-Object kind -eq 'ui-snapshot').Count -eq 0) 'Minimal app emitted a fixture UI report.'
  Copy-Item -LiteralPath (Inspect $session 'screenshot') -Destination (Join-Path $fixture.Directory 'restarted.png')
  Send-Command $session 'stop'
  $receipt = Confirm-Exit $session -Unrelated $unrelated
  $receipt | Add-Member -NotePropertyName observation -NotePropertyValue 'public-inspection-no-probe-commands; PNG review required'
  $receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
  Write-Output "Minimal application passed: $($fixture.Directory)"
} finally {
  try { Close-TestSessions $sessions }
  finally {
    try { if ($unrelated) { Stop-TestProcess $unrelated } }
    finally {
      [IO.File]::WriteAllText($source, $originalSource)
      [IO.File]::WriteAllText($component, $originalComponent)
    }
  }
}
