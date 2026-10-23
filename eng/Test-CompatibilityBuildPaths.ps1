#requires -Version 7.4
[CmdletBinding()]
param([string]$EvidenceDirectory = 'artifacts/compatibility-build-paths')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'integration/CompatibilityBuildPaths.ps1')
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($evidence) | Out-Null
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Build([string]$Directory, [string]$Configuration, [string]$Name) {
  $arguments = @('build',(Join-Path $Directory 'Probe.csproj'),'-c',$Configuration,'--disable-build-servers')
  & dotnet @arguments *> (Join-Path $evidence "$Name.log")
  if ($LASTEXITCODE) { throw "Fixture build failed: $Name" }
  return Join-Path $Directory "bin/$Configuration/net10.0/FixturePathProbe.dll"
}
$directories = foreach ($index in 1..2) {
  $directory = Join-Path $evidence ('fixture-' + [guid]::NewGuid().ToString('N'))
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  Copy-Item (Join-Path $repository 'Directory.Build.props') $directory
  Copy-Item (Join-Path $repository 'global.json') $directory
  @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>FixturePathProbe</AssemblyName>
    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
  </PropertyGroup>
</Project>
'@ | Set-Content (Join-Path $directory 'Probe.csproj')
  @'
namespace FixturePathProbe;

public static class Probe
{
    public static string Value() => "original";
}
'@ | Set-Content (Join-Path $directory 'Probe.cs')
  Set-CompatibilityBuildPaths $directory
  $directory
}
$first = Build $directories[0] Release 'first'
$second = Build $directories[1] Release 'second'
$firstHash = (Get-FileHash $first).Hash
$secondHash = (Get-FileHash $second).Hash
Assert ($firstHash -eq $secondHash) 'Independent Release fixture assemblies differ.'
$stream = [IO.File]::OpenRead($second)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
  $paths = @($pe.ReadDebugDirectory() | Where-Object Type -eq CodeView | ForEach-Object { $pe.ReadCodeViewDebugDirectoryData($_).Path })
  Assert ($paths.Count -eq 1 -and $paths[0].StartsWith('/_/spar-compatibility/')) 'Release debug metadata retains physical fixture paths.'
} finally { $pe.Dispose(); $stream.Dispose() }
$changedSource = [IO.File]::ReadAllText((Join-Path $directories[1] 'Probe.cs')).Replace('"original"', '"changed"')
[IO.File]::WriteAllText((Join-Path $directories[1] 'Probe.cs'), $changedSource)
$changed = Build $directories[1] Release 'changed'
Assert ($firstHash -ne (Get-FileHash $changed).Hash) 'Changed source reused an old image.'
$context = [Runtime.Loader.AssemblyLoadContext]::new('changed-fixture', $true)
try {
  $assembly = $context.LoadFromAssemblyPath($changed)
  Assert ($assembly.GetType('FixturePathProbe.Probe').GetMethod('Value').Invoke($null,@()) -eq 'changed') 'Changed implementation was not compiled.'
} finally { $context.Unload() }
$project = Join-Path $directories[0] 'Probe.csproj'
$debugMap = & dotnet msbuild $project -nologo -p:Configuration=Debug -getProperty:PathMap
if ($LASTEXITCODE) { throw 'Cannot evaluate Debug compiler paths.' }
Assert ([string]::IsNullOrWhiteSpace(($debugMap -join ''))) 'Debug fixture paths were rewritten.'
$explicitMap = & dotnet msbuild $project -nologo -p:Configuration=Release '-p:PathMap=explicit=preserved' -getProperty:PathMap
if ($LASTEXITCODE) { throw 'Cannot evaluate explicit compiler paths.' }
Assert (($explicitMap -join '') -eq 'explicit=preserved') 'Explicit PathMap was replaced.'
@{ passed=$true; firstSha256=$firstHash; secondSha256=$secondHash
  changedSha256=(Get-FileHash $changed).Hash; debugMap=($debugMap -join ''); explicitMap=($explicitMap -join ''); checks=5
} | ConvertTo-Json | Set-Content (Join-Path $evidence 'receipt.json')
Write-Output 'Compatibility build paths passed: independent Release bytes, changed implementation, real Debug paths and explicit override.'
