#requires -Version 7.4
# Callers import Sessions.ps1 and retain the session and command log.

function New-InspectionRequest($Session, [string]$Operation, [string]$Selector = '', [string]$Text = '', [int]$Timeout = 5000) {
  $ready = $Session.Events | Where-Object kind -eq 'application-ready' | Select-Object -Last 1
  return @{ command = 'inspect'; id = [guid]::NewGuid().ToString('N'); detail = $Operation;
    session = $ready.session; generation = $ready.generation; document = $ready.document; contract = $ready.contract;
    selector = $Selector; text = $Text; timeoutMilliseconds = $Timeout }
}

function Write-InspectionRequest($Session, $Request) {
  $line = $Request | ConvertTo-Json -Compress
  Add-Content -LiteralPath (Join-Path $Session.Fixture.Directory 'commands.jsonl') -Value $line
  $Session.Process.StandardInput.WriteLine($line)
  $Session.Process.StandardInput.Flush()
}

function Wait-InspectionResult($Session, [string]$Id) {
  Wait-Until $Session { @($Session.Events | Where-Object { $_.correlation -eq $Id -and $_.kind -in @('command-completed','command-rejected') }).Count -gt 0 } "inspection $Id" 30
  return $Session.Events | Where-Object { $_.correlation -eq $Id -and $_.kind -in @('command-completed','command-rejected') } | Select-Object -Last 1
}

function Inspect($Session, [string]$Operation, [string]$Selector = '', [string]$Text = '') {
  $request = New-InspectionRequest $Session $Operation $Selector $Text
  Write-InspectionRequest $Session $request
  $result = Wait-InspectionResult $Session $request.id
  Assert-Condition ($result.code -eq 'ok') "Inspection failed: $Operation / $($result.code) / $($result.detail)"
  return ($result.detail | ConvertFrom-Json).result
}
