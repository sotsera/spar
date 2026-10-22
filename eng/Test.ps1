#requires -Version 7.4
param(
  [ValidateSet('fast', 'processes', 'development', 'delivery', 'packages', 'all')]
  [string]$Suite = 'fast',
  [switch]$List
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$catalog = Get-Content (Join-Path $PSScriptRoot 'test-suites.json') -Raw | ConvertFrom-Json -AsHashtable
$names = if ($Suite -eq 'all') { @($catalog.suites.Keys) } else { @($Suite) }
if ($List) {
  foreach ($name in $names) {
    Write-Output "$name`: $($catalog.suites[$name].description)"
    $catalog.suites[$name].steps | ForEach-Object { Write-Output "  $($_.id): $($_.command) $($_.arguments -join ' ')" }
  }
  return
}

Push-Location $repository
try {
  if ($Suite -eq 'all') {
    foreach ($name in $names) {
      & pwsh -NoProfile -File $PSCommandPath -Suite $name
      if ($LASTEXITCODE) { throw "Suite $name failed (exit $LASTEXITCODE)." }
    }
    return
  }

  $source = & git rev-parse HEAD
  if ($LASTEXITCODE) { throw 'Cannot identify the tested source.' }
  $changes = & git status --porcelain
  if ($LASTEXITCODE) { throw 'Cannot identify working-tree changes.' }
  $evidence = Join-Path $repository ('artifacts/suites/' + $Suite + '/' + [guid]::NewGuid().ToString('N'))
  $reportDirectory = Join-Path $repository 'artifacts/ci'
  New-Item -ItemType Directory -Path $evidence, $reportDirectory -Force | Out-Null
  $checks = @($catalog.suites[$Suite].steps | ForEach-Object {
    [ordered]@{ id = $_.id; outcome = 'not-run'; elapsedMs = 0 }
  })
  $passed = $false
  try {
    for ($index = 0; $index -lt $checks.Count; $index++) {
      $step = $catalog.suites[$Suite].steps[$index]
      $arguments = @($step.arguments | ForEach-Object { $_.Replace('{evidence}', $evidence) })
      Write-Output "[$Suite/$($step.id)] $($step.command) $($arguments -join ' ')"
      $watch = [Diagnostics.Stopwatch]::StartNew()
      $checks[$index].outcome = 'failure'
      try {
        & $step.command @arguments
        if ($LASTEXITCODE -ne 0) { throw "Check $($step.id) failed (exit $LASTEXITCODE). Evidence: $evidence" }
        $checks[$index].outcome = 'success'
      } finally {
        $checks[$index].elapsedMs = $watch.Elapsed.TotalMilliseconds
      }
    }
    $passed = $true
  } finally {
    [ordered]@{
      schema = 1; suite = $Suite; source = $source; observedAt = [DateTimeOffset]::UtcNow
      workingTreeDirty = [bool]$changes
      runId = $env:GITHUB_RUN_ID; attempt = $env:GITHUB_RUN_ATTEMPT
      catalogSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'test-suites.json') -Algorithm SHA256).Hash.ToLowerInvariant()
      passed = $passed; checks = $checks; evidence = $evidence
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $reportDirectory "suite-$Suite.json")
  }
} finally {
  Pop-Location
}
