#requires -Version 7.4
[CmdletBinding()]
param(
  [string]$EvidenceDirectory = 'artifacts/runtime-pack-restore/source',
  [string]$Feed,
  [string]$DeliveryTargets
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$delivery = if ($DeliveryTargets) { [IO.Path]::GetFullPath($DeliveryTargets) } else { Join-Path $repository 'build/Spar.Delivery.targets' }
New-Item -ItemType Directory -Force $evidence | Out-Null
$root = Join-Path ([IO.Path]::GetTempPath()) ('spar-runtime-pack-' + [guid]::NewGuid().ToString('N'))
if ($Feed) {
  & (Join-Path $PSScriptRoot 'New-PackageConsumer.ps1') -Directory $root -Feed $Feed
  $project = Join-Path $root 'backend/Spar.NativeSample.csproj'
} else {
  # Restore needs the project graph, not a running-app fixture or installed frontend modules.
  $source = Join-Path $repository 'samples/Spar.NativeSample'
  $root = Join-Path $evidence ('application-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory $root | Out-Null
  $project = Join-Path $root 'Spar.NativeSample.csproj'
  $xml = [xml][IO.File]::ReadAllText((Join-Path $source 'Spar.NativeSample.csproj'))
  foreach ($import in $xml.Project.Import) {
    $import.Project = if ($import.Project.EndsWith('Spar.Delivery.targets')) { $delivery }
      else { [IO.Path]::GetFullPath($import.Project, $source) }
  }
  foreach ($reference in $xml.Project.ItemGroup.ProjectReference) {
    if ($reference -is [Xml.XmlElement]) { $reference.Include = [IO.Path]::GetFullPath($reference.Include, $source) }
  }
  $xml.Save($project)
}
$checks = [Collections.Generic.List[object]]::new()
$passed = $false
function Read-Downloads([string]$Name, [string[]]$Extra = @()) {
  & dotnet restore $project --disable-build-servers @Extra *> (Join-Path $evidence "$Name.log")
  if ($LASTEXITCODE) { throw "Restore failed: $Name" }
  $assets = Get-Content (Join-Path (Split-Path $project) 'obj/project.assets.json') -Raw | ConvertFrom-Json
  return @($assets.project.frameworks.'net10.0-windows'.downloadDependencies.name)
}
function Confirm-Packs([string]$Name, [string[]]$Actual, [string[]]$Expected) {
  if (@(Compare-Object @($Actual | Sort-Object) @($Expected | Sort-Object)).Count) { throw "$Name`: unexpected runtime-pack graph: $($Actual -join ', ')" }
  $checks.Add(@{name=$Name; packs=$Actual; passed=$true})
  Write-Output "$Name passed ($($Actual.Count) packs)."
}
try {
  $core = @('Microsoft.NETCore.App.Runtime.win-x64')
  $all = @($core + 'Microsoft.AspNetCore.App.Runtime.win-x64' + 'Microsoft.WindowsDesktop.App.Runtime.win-x64')
  Confirm-Packs 'framework-dependent Debug' (Read-Downloads 'debug') $core
  Confirm-Packs 'explicit opt out' (Read-Downloads 'opt-out' @('-p:DisableTransitiveFrameworkReferenceDownloads=false')) $all
  $release = Read-Downloads 'release' @('-p:Configuration=Release')
  foreach ($pack in $all) { if ($pack -notin $release) { throw "Release lost runtime discovery: $pack" } }
  $checks.Add(@{name='Release retains runtime discovery'; packs=$release; passed=$true})
  Confirm-Packs 'self-contained Debug retains runtime discovery' (Read-Downloads 'self-contained' @('-p:SelfContained=true')) $all
  Confirm-Packs 'ReadyToRun Debug retains runtime discovery' (Read-Downloads 'ready-to-run' @('-p:PublishReadyToRun=true')) @($all + 'Microsoft.NETCore.App.Crossgen2.win-x64')
  # A real library brings ASP.NET Core transitively; an empty-cache publish must obtain its runtime.
  $probe = Join-Path $evidence 'transitive-framework'
  New-Item -ItemType Directory -Force (Join-Path $probe 'library'),(Join-Path $probe 'app') | Out-Null
  '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup></Project>' | Set-Content (Join-Path $probe 'library/Library.csproj')
  'namespace RestoreProbe; public static class Dependency { public static int Status() => new Microsoft.AspNetCore.Http.DefaultHttpContext().Response.StatusCode; }' | Set-Content (Join-Path $probe 'library/Dependency.cs')
  $dynamic = Join-Path $repository 'eng/compatibility-probe/Spar.DynamicDependency.csproj'
  @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><RuntimeIdentifier>win-x64</RuntimeIdentifier><SparDeliveryMode>CoreCLR</SparDeliveryMode><SparCompatibilityDependency>Spar.DynamicDependency</SparCompatibilityDependency><SparCompatibilityReason>RuntimeGreeting executes DynamicMethod</SparCompatibilityReason></PropertyGroup>
  <ItemGroup><ProjectReference Include="../library/Library.csproj" /><ProjectReference Include="$dynamic" /></ItemGroup>
  <Import Project="$delivery" />
</Project>
"@ | Set-Content (Join-Path $probe 'app/App.csproj')
  'System.Console.WriteLine(Spar.DynamicDependency.RuntimeGreeting.Create("Spar") + "/" + RestoreProbe.Dependency.Status());' | Set-Content (Join-Path $probe 'app/Program.cs')
  $cache = Join-Path $probe 'packages'
  $publish = Join-Path $probe 'published'
  & dotnet publish (Join-Path $probe 'app/App.csproj') -c Release -o $publish --disable-build-servers "-p:RestorePackagesPath=$cache" *> (Join-Path $evidence 'transitive-framework.log')
  if ($LASTEXITCODE) { throw 'Cold transitive-framework publication failed.' }
  $result = & (Join-Path $publish 'App.exe')
  if ($LASTEXITCODE -or $result -ne 'Hello from generated code, Spar/200') { throw 'Transitive-framework/dynamic dependency did not execute.' }
  $checks.Add(@{name='cold transitive framework and dynamic dependency publish/execute'; result=$result; passed=$true})
  $passed = $true
} finally {
  @{passed=$passed; root=$root; checks=$checks.ToArray(); deliverySha256=(Get-FileHash $delivery).Hash} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'receipt.json')
}
