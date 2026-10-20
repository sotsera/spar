#requires -Version 7.4

function Read-ProbeEvents {
  param([Parameter(Mandatory)][string]$Path, [switch]$Completed)

  if (!(Test-Path -LiteralPath $Path)) {
    if ($Completed) { throw "Missing completed probe log: $Path" }
    return
  }

  $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
  $reader = [IO.StreamReader]::new($stream)
  try { $snapshot = $reader.ReadToEnd() } finally { $reader.Dispose() }

  # A file reader can observe part of WriteLine, including a complete JSON value before its newline.
  # Re-read on the next poll; never treat an unfinished record as evidence of completion.
  $lastNewline = $snapshot.LastIndexOf("`n")
  $pending = $snapshot.Substring($lastNewline + 1)
  if ($Completed -and ![string]::IsNullOrWhiteSpace($pending)) {
    throw "Unterminated record in completed probe log: $Path"
  }

  if ($lastNewline -lt 0) { return }
  foreach ($line in $snapshot.Substring(0, $lastNewline + 1).Split("`n")) {
    if (![string]::IsNullOrWhiteSpace($line)) {
      # A malformed newline-terminated record is a real failure, not a retryable partial write.
      $line | ConvertFrom-Json -ErrorAction Stop
    }
  }
}
