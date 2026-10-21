#requires -Version 7.4
param(
  [string]$Feed = 'artifacts/packages',
  [string]$EvidenceDirectory = 'artifacts/minimal/consumer',
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$consumer = Join-Path ([IO.Path]::GetTempPath()) ('spar-minimal-consumer-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'New-PackageConsumer.ps1') -Directory $consumer -Feed $Feed
$consumer | Set-Content -LiteralPath (Join-Path $evidence 'consumer-path.txt')
$passed = $false
Push-Location $consumer
try {
  dotnet build backend/Spar.NativeSample.csproj --disable-build-servers *> (Join-Path $evidence 'build.log')
  if ($LASTEXITCODE) { throw 'Fresh minimal package build failed.' }
  foreach ($assetsPath in @('backend/obj/project.assets.json', 'contracts/obj/project.assets.json')) {
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
      if ($library.Value.type -eq 'project' -and $library.Name -notlike 'Spar.NativeSample.Contracts/*') {
        throw "Unexpected framework project reference: $($library.Name)"
      }
    }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
      if (![IO.Path]::GetFullPath($folder).StartsWith($consumer + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Minimal consumer used a package cache outside its isolated directory.'
      }
    }
  }
  npm ci --prefix backend/frontend --ignore-scripts --include=optional *> (Join-Path $evidence 'npm.log')
  if ($LASTEXITCODE) { throw 'Minimal package frontend restore failed.' }
  npm run check --prefix backend/frontend *> (Join-Path $evidence 'frontend-check.log')
  if ($LASTEXITCODE) { throw 'Minimal package frontend check failed.' }
  & (Join-Path $PSScriptRoot 'Test-MinimalApplication.ps1') -ConsumerDirectory $consumer -EvidenceDirectory (Join-Path $evidence 'development')
  $publish = Join-Path $consumer 'artifacts/publish'
  dotnet publish backend/Spar.NativeSample.csproj -c Release -o $publish --disable-build-servers *> (Join-Path $evidence 'publish.log')
  if ($LASTEXITCODE) { throw 'Minimal package publication failed.' }
  & (Join-Path $PSScriptRoot 'Test-ProductionBoundary.ps1') -MinimalApplication `
    -Assembly (Join-Path $consumer 'backend/bin/Release/net10.0-windows/win-x64/Spar.NativeSample.dll') `
    -HostAssembly (Join-Path $consumer 'backend/bin/Release/net10.0-windows/win-x64/Sotsera.Spar.Windows.dll') *> (Join-Path $evidence 'boundary.log')
  & (Join-Path $PSScriptRoot 'Test-MinimalPublication.ps1') -PublishDirectory $publish -EvidenceDirectory (Join-Path $evidence 'production') -VerifyRender:$VerifyRender
  $passed = $true
} finally {
  Pop-Location
  @{ passed = $passed; consumer = $consumer; kind = 'minimal-application'; observedAt = [DateTimeOffset]::UtcNow } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
}
