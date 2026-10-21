#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$helperDirectory = Join-Path $PSScriptRoot 'integration'

# Imports must be definitions only: even setup outside a scenario's try/finally can leak ownership.
foreach ($file in Get-ChildItem -LiteralPath $helperDirectory -Filter '*.ps1') {
  $tokens = $null
  $parseErrors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
  if ($parseErrors.Count -or $ast.ParamBlock -or $ast.BeginBlock -or $ast.ProcessBlock -or $ast.CleanBlock) {
    throw "Helper has parse errors or executable import setup: $($file.Name)"
  }
  foreach ($statement in $ast.EndBlock.Statements) {
    if ($statement -isnot [Management.Automation.Language.FunctionDefinitionAst]) {
      throw "Helper import executes a non-definition: $($file.Name) / $statement"
    }
  }
  . $file.FullName
}

foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Test-*.ps1') {
  $tokens = $null
  $parseErrors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
  if ($parseErrors.Count) { throw "Scenario parse failure: $($file.Name)" }
  foreach ($command in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
    if ($command.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'LibraryOnly' }) {
      throw "Scenario still uses LibraryOnly: $($file.Name)"
    }
    if ($command.InvocationOperator -eq 'Dot' -and $command.CommandElements[0].Extent.Text -match 'Test-[^/\\]+\.ps1') {
      throw "Scenario imports an executable test: $($file.Name)"
    }
  }
}

# Obsolete library-mode callers must fail binding before a scenario can create any resources.
foreach ($name in @('Test-DevelopmentSession.ps1', 'Test-BackendUpdates.ps1')) {
  $driver = Join-Path $PSScriptRoot $name
  $binding = (Get-Command $driver).ScriptBlock.Attributes | Where-Object { $_ -is [Management.Automation.CmdletBindingAttribute] }
  if (!$binding) { throw "Scenario does not reject unknown arguments: $name" }
  $failure = $null
  $obsoleteArguments = @{ LibraryOnly = $true }
  try { & $driver @obsoleteArguments } catch { $failure = $_ }
  if (!$failure -or $failure.FullyQualifiedErrorId -notlike 'NamedParameterNotFound,*') {
    throw "Obsolete library-mode invocation was not rejected before setup: $name"
  }
}

# Real handles: normal forced teardown must leave an unrelated process alive.
$unrelated = Start-UnrelatedProcess
$coordinator = Start-UnrelatedProcess
$owned = $null
try {
  $identity = [Diagnostics.Process]::GetProcessById($coordinator.Id)
  $null = $identity.Handle
  try {
    $session = [pscustomobject]@{ Process = $coordinator; Handles = @{}; Log = 'helper-normal-teardown' }
    Close-TestSessions @($session)
    Assert-Condition ($identity.WaitForExit(1000)) 'Forced test process did not exit.'
    Assert-Condition (!$unrelated.HasExited) 'Teardown killed an unrelated process.'
  } finally { $identity.Dispose() }
  $coordinator = $null

  # This child is deliberately outside a job. A timed-out wait must fail and retain its live handle.
  $coordinator = Start-UnrelatedProcess
  $owned = Start-UnrelatedProcess
  $session = [pscustomobject]@{ Process = $coordinator; Handles = @{ retained = $owned }; Log = 'helper-unfinished-teardown' }
  $failure = $null
  try { Close-TestSessions @($session) } catch { $failure = $_ }
  $coordinator = $null
  Assert-Condition ($failure -and $failure.ToString().Contains('has not exited')) 'Unfinished teardown was reported as complete.'
  Assert-Condition (!$owned.HasExited -and $owned.Handle -ne [IntPtr]::Zero) 'Live handle was abandoned after timeout.'
  Assert-Condition (!$unrelated.HasExited) 'Unfinished teardown killed an unrelated process.'
} finally {
  if ($coordinator) { Stop-TestProcess $coordinator }
  if ($owned) { Stop-TestProcess $owned }
  Stop-TestProcess $unrelated
}
Write-Output 'Integration helpers passed: definition-only imports, independent entry points, actual forced exit and retained unfinished ownership.'
