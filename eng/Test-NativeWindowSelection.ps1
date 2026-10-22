#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/Desktop.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'native-probe/WindowSelection.cs')
$fixture = [WindowSelectionFixture]::new()
try {
  $selected = [NativeProbeDesktop]::FindWindow($PID)
  if ($selected -ne $fixture.Application) {
    throw "Selected a helper window instead of the owned Spar application (handle $selected, expected $($fixture.Application))."
  }
  Write-Output 'Selected the hidden Spar application with a same-process decoy first in enumeration.'
} finally {
  $fixture.Dispose()
}
