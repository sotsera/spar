#requires -Version 7.4
param([string]$Output = 'artifacts/packages')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputPath = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
dotnet build (Join-Path $root 'tools/Spar.Dev') -c Debug --disable-build-servers
if ($LASTEXITCODE) { throw 'Development coordinator build failed.' }
# Debug host and development tools are distinct packages; production resolves only the Release host.
$packages = @(
  @('src/Sotsera.Spar.Bridge', 'Release'),
  @('src/Sotsera.Spar.Windows', 'Release'),
  @('src/Sotsera.Spar.Windows', 'Debug'),
  @('src/Sotsera.Spar.Development', 'Debug'),
  @('tools/Sotsera.Spar.Build', 'Release')
)
foreach ($package in $packages) {
  dotnet pack (Join-Path $root $package[0]) -c $package[1] -o $outputPath --disable-build-servers
  if ($LASTEXITCODE) { throw "Package build failed: $($package[0]) / $($package[1])" }
}
Get-ChildItem -LiteralPath $outputPath -Filter '*.nupkg' | ForEach-Object {
  @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash; bytes = $_.Length }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'packages.json')
