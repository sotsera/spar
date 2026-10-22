#requires -Version 7.4
param([string]$EvidenceDirectory = 'artifacts/process-image/probe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $root | Out-Null
Add-Type -Path (Join-Path $PSScriptRoot 'integration/ProcessImage.cs')
Add-Type -Path (Join-Path $PSScriptRoot 'startup-probe/SuspendedProcess.cs')
$results = [Collections.Generic.List[object]]::new()
try {
  foreach ($name in @('conhost.exe', 'cmd.exe')) {
    $expected = Join-Path ([Environment]::SystemDirectory) $name
    $fixture = [Spar.Integration.SuspendedProcess]::new($expected)
    $process = $fixture.Process
    $identity = @{ name = $name; processId = $process.Id; exited = $false; passed = $false }
    $results.Add($identity)
    try {
      if ($process.HasExited) { throw 'Suspended child unexpectedly exited.' }
      try { $identity.legacyImage = $process.MainModule.FileName }
      catch { $identity.legacyError = $_.Exception.Message }
      $identity.image = [Spar.Integration.ProcessImage]::Read($process)
      if (![string]::Equals($identity.image, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Live suspended process image unavailable or incorrect: $name / $($identity.image)"
      }
      if ($process.HasExited) { throw 'Image query terminated the suspended child.' }
      $identity.passed = $true
    } finally {
      $fixture.Dispose()
      $identity.exited = $true
    }
  }
} finally {
  @{ allPassed = ($results.Count -eq 2 -and @($results | Where-Object { !$_.passed -or !$_.exited }).Count -eq 0)
    cases = $results.ToArray() } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $root 'receipt.json')
}
Write-Output 'Process image query passed for two live suspended children; actual exit observed for both.'
