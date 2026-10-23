#requires -Version 7.4
[CmdletBinding()]
param(
  [string]$EvidenceDirectory = 'artifacts/frontend-restore/source',
  [string]$Feed,
  [string]$AssetsTargets
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$root = Join-Path $evidence ('fixture-' + [guid]::NewGuid().ToString('N'))
$frontend = Join-Path $root 'frontend'
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force $frontend,$tools,(Join-Path $root 'package'),(Join-Path $frontend 'src/generated') | Out-Null
if ($Feed) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $package = Join-Path ([IO.Path]::GetFullPath($Feed)) 'Sotsera.Spar.Build.0.1.0.nupkg'
  [IO.Compression.ZipFile]::ExtractToDirectory($package, (Join-Path $root 'unpacked'))
  $targets = Join-Path $root 'unpacked/build/Spar.Assets.targets'
  Copy-Item (Join-Path $root 'unpacked/tools/build/*') $tools
} else {
  $targets = if ($AssetsTargets) { [IO.Path]::GetFullPath($AssetsTargets) } else { Join-Path $repository 'build/Spar.Assets.targets' }
  Copy-Item (Join-Path $PSScriptRoot 'Restore-Frontend.ps1'),(Join-Path $PSScriptRoot 'Build-AssetBundle.ps1') $tools
}
$checks = [Collections.Generic.List[object]]::new()
$oldEnvironment = @{}
foreach ($name in @('NPM_CONFIG_LOGS_DIR','NPM_CONFIG_LOGS_MAX','NPM_CONFIG_TIMING','NPM_CONFIG_AUDIT')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$env:NPM_CONFIG_LOGS_DIR = Join-Path $root 'npm-logs'
$env:NPM_CONFIG_LOGS_MAX = '200'
$env:NPM_CONFIG_TIMING = 'true'
$env:NPM_CONFIG_AUDIT = 'false'
$buildNumber = 0
$passed = $false
function Invoke-Build([string]$Name, [string[]]$Extra = @(), [switch]$Fail) {
  $script:buildNumber++
  & dotnet build (Join-Path $root 'App.csproj') -c Release --disable-build-servers @Extra *> (Join-Path $evidence "$buildNumber-$Name.log")
  if (($LASTEXITCODE -eq 0) -eq [bool]$Fail) { throw "Unexpected build outcome: $Name" }
}
function Confirm-Installs([string]$Name, [int]$Expected) {
  $count = @(Get-ChildItem $env:NPM_CONFIG_LOGS_DIR -Filter '*timing.json' | ForEach-Object {
    $timing = Get-Content $_.FullName -Raw | ConvertFrom-Json
    if ($timing.metadata.command[0] -eq 'ci') { $timing }
  }).Count
  if ($count -ne $Expected) { throw "$Name`: expected $Expected actual npm ci invocations, observed $count." }
  $checks.Add(@{name=$Name; actualInstalls=$count; passed=$true})
  Write-Output "$Name passed ($count actual npm ci invocations)."
}
function Confirm-Asset([string]$Expected) {
  $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root 'obj/Release/net10.0/spar-assets.zip'))
  try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('index.html').Open())
    try { if ($reader.ReadToEnd() -ne $Expected) { throw 'Frontend build reused stale assets.' } } finally { $reader.Dispose() }
  } finally { $zip.Dispose() }
}
try {
  '{"name":"spar-restore-test","version":"1.0.0","main":"index.js"}' | Set-Content (Join-Path $root 'package/package.json')
  'module.exports = 42;' | Set-Content (Join-Path $root 'package/index.js')
  & npm pack (Join-Path $root 'package') --pack-destination $root *> (Join-Path $evidence 'pack.log')
  if ($LASTEXITCODE) { throw 'Fixture tarball failed.' }
  '{"name":"frontend","version":"1.0.0","private":true,"scripts":{"build":"node build.mjs"},"dependencies":{"spar-restore-test":"file:../spar-restore-test-1.0.0.tgz"}}' | Set-Content (Join-Path $frontend 'package.json')
  & npm install --prefix $frontend --package-lock-only --ignore-scripts --include=optional *> (Join-Path $evidence 'lock.log')
  if ($LASTEXITCODE) { throw 'Fixture lock creation failed.' }
  @'
import fs from 'node:fs';
fs.mkdirSync('dist', {recursive:true});
fs.writeFileSync('dist/index.html', fs.readFileSync('source.txt'));
fs.writeFileSync('dist/frontend-LICENSES.txt', 'fixture notices');
'@ | Set-Content (Join-Path $frontend 'build.mjs')
  'first' | Set-Content (Join-Path $frontend 'source.txt') -NoNewline
  ("const fingerprint = '" + ('a' * 64) + "';") | Set-Content (Join-Path $frontend 'src/generated/client.js')
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><SparProductionAssets>true</SparProductionAssets><SparFrontendDirectory>$frontend</SparFrontendDirectory><SparBuildToolsDirectory>$tools</SparBuildToolsDirectory><DefaultItemExcludes>frontend/**;tools/**;package/**;unpacked/**</DefaultItemExcludes></PropertyGroup><Import Project=`"$targets`" /></Project>" | Set-Content (Join-Path $root 'App.csproj')
  Invoke-Build 'first'
  Confirm-Installs 'first clean install' 1
  Invoke-Build 'unchanged'
  Confirm-Installs 'unchanged build reuses dependencies' 1
  'second' | Set-Content (Join-Path $frontend 'source.txt') -NoNewline
  Invoke-Build 'source-changed'
  Confirm-Installs 'frontend source does not reinstall dependencies' 1
  Confirm-Asset 'second'
  $manifest = Join-Path $frontend 'package.json'
  $originalManifest = [IO.File]::ReadAllText($manifest)
  [IO.File]::WriteAllText($manifest, $originalManifest + "`n")
  Invoke-Build 'manifest-changed'
  Confirm-Installs 'manifest change reinstalls' 2
  Add-Content (Join-Path $frontend 'package-lock.json') ' '
  Invoke-Build 'lock-changed'
  Confirm-Installs 'lock change reinstalls' 3
  "fund=false`n//registry.npmjs.org/:_authToken=local-proof-do-not-copy" | Set-Content (Join-Path $frontend '.npmrc')
  Invoke-Build 'configuration-changed'
  Confirm-Installs 'npm configuration change reinstalls' 4
  if ((Get-Content (Join-Path $root 'obj/Release/net10.0/spar-frontend-restore.json') -Raw).Contains('local-proof-do-not-copy')) { throw 'Restore state disclosed npm credentials.' }
  Remove-Item -LiteralPath (Join-Path $frontend 'node_modules/spar-restore-test/index.js')
  Invoke-Build 'missing-package-file'
  Confirm-Installs 'missing package file repairs installation' 5
  $installed = Join-Path $frontend 'node_modules/spar-restore-test/index.js'
  $modifiedAt = (Get-Item -LiteralPath $installed).LastWriteTimeUtc
  [IO.File]::WriteAllText($installed, [IO.File]::ReadAllText($installed).Replace('42','43'))
  [IO.File]::SetLastWriteTimeUtc($installed, $modifiedAt)
  Invoke-Build 'modified-package-file'
  Confirm-Installs 'modified package file repairs installation' 6
  Remove-Item -LiteralPath (Join-Path $frontend 'node_modules/.package-lock.json')
  Invoke-Build 'missing-install-lock'
  Confirm-Installs 'missing install lock repairs installation' 7
  Invoke-Build 'force' -Extra @('-p:SparForceFrontendRestore=true')
  Confirm-Installs 'explicit clean install' 8
  Add-Content (Join-Path $tools 'Restore-Frontend.ps1') '# Changed tool payload.'
  Invoke-Build 'tool-changed'
  Confirm-Installs 'restore helper change reinstalls' 9
  $bad = $originalManifest | ConvertFrom-Json -AsHashtable
  $bad.dependencies['missing-spar-package'] = 'file:./does-not-exist.tgz'
  $bad | ConvertTo-Json -Depth 5 | Set-Content $manifest
  Invoke-Build 'invalid-lock' -Fail
  if (Test-Path (Join-Path $root 'obj/Release/net10.0/spar-frontend-restore.json')) { throw 'Failed install retained successful state.' }
  [IO.File]::WriteAllText($manifest, $originalManifest)
  Invoke-Build 'recover'
  Confirm-Installs 'failed restore cannot become a cache hit' 11
  Invoke-Build 'design-time' -Extra @('-p:DesignTimeBuild=true','-target:BuildSparAssets')
  Confirm-Installs 'design-time runs no frontend tools' 11
  & dotnet clean (Join-Path $root 'App.csproj') -c Release --disable-build-servers *> (Join-Path $evidence 'clean.log')
  if ($LASTEXITCODE -or (Test-Path (Join-Path $root 'obj/Release/net10.0/spar-frontend-restore.json'))) { throw 'Clean retained owned restore state.' }
  if (!(Test-Path (Join-Path $frontend 'node_modules/spar-restore-test/index.js'))) { throw 'Clean deleted frontend dependencies.' }
  Invoke-Build 'after-clean'
  Confirm-Installs 'clean invalidates only owned state' 12
  $passed = $true
} finally {
  foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
  @{passed=$passed; checks=$checks.ToArray(); root=$root; assetsTargetsSha256=(Get-FileHash $targets).Hash; helperSha256=(Get-FileHash (Join-Path $tools 'Restore-Frontend.ps1')).Hash} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'receipt.json')
}
