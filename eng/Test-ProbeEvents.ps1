#requires -Version 7.4
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Read-ProbeEvents.ps1')

$path = Join-Path ([IO.Path]::GetTempPath()) ('spar-probe-events-' + [guid]::NewGuid().ToString('N') + '.jsonl')
$writer = $null
function Assert-Events([int]$Count, [string]$LastStage = '') {
  $events = @(Read-ProbeEvents $path)
  if ($events.Count -ne $Count -or ($LastStage -and $events[-1].Stage -ne $LastStage)) {
    throw "Unexpected event snapshot: expected $Count records ending with '$LastStage'."
  }
}
function Assert-Rejected([scriptblock]$Action, [string]$Pattern) {
  $failure = $null
  try { & $Action | Out-Null } catch { $failure = $_ }
  if (!$failure -or $failure.ToString() -notmatch $Pattern) {
    throw "Expected failure matching '$Pattern', observed '$failure'."
  }
}
function Write-Fragment([string]$Text) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
  $writer.Write($bytes)
  $writer.Flush()
}

try {
  Assert-Events 0
  Assert-Rejected { Read-ProbeEvents $path -Completed } 'Missing completed probe log'
  $writer = [IO.File]::Open($path, 'CreateNew', 'Write', 'ReadWrite')
  Assert-Events 0

  # Force the same kind of partial UserDataFolder string seen in CI, without relying on timing.
  Write-Fragment '{"Stage":"document-loaded","UserDataFolder":"C:\\profile'
  Assert-Events 0
  Assert-Rejected { Get-Content -LiteralPath $path | ConvertFrom-Json -ErrorAction Stop } 'Unterminated string'
  Assert-Rejected { Read-ProbeEvents $path -Completed } 'Unterminated record'
  Write-Fragment '"}'
  Assert-Events 0
  Write-Fragment "`r"
  Assert-Events 0
  Write-Fragment "`n"
  Assert-Events 1 'document-loaded'

  # A poll in the middle of a UTF-8 character must not corrupt the next complete snapshot.
  Write-Fragment '{"Stage":"caf'
  $writer.WriteByte(0xc3)
  $writer.Flush()
  Assert-Events 1 'document-loaded'
  $writer.WriteByte(0xa9)
  Write-Fragment ('"}' + "`n" + '{"Stage":"closed"}')
  Assert-Events 2 'café'
  Assert-Rejected { Read-ProbeEvents $path -Completed } 'Unterminated record'
  Write-Fragment "`n"
  Assert-Events 3 'closed'
  $writer.Dispose()
  $writer = $null
  if (@(Read-ProbeEvents $path -Completed).Count -ne 3) { throw 'Completed log lost records.' }

  # Never swallow malformed completed records, even if valid evidence follows them.
  [IO.File]::WriteAllText($path, "{invalid}`n{`"Stage`":`"closed`"}`n")
  Assert-Rejected { Read-ProbeEvents $path } 'Conversion from JSON failed'
  Assert-Rejected { Read-ProbeEvents $path -Completed } 'Conversion from JSON failed'
  Write-Output 'Probe event framing checks passed: partial JSON/CRLF/UTF-8, final truncation, malformed records.'
} finally {
  if ($writer) { $writer.Dispose() }
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
}
