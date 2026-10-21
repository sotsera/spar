#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$Directory,
  [string]$Feed = 'artifacts/packages',
  [switch]$IntegrationFixture
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = [IO.Path]::GetFullPath($Directory)
if (Test-Path -LiteralPath $root) { throw 'Choose a new consumer directory; existing work is never overwritten.' }
if ($root.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw 'The package consumer must live outside the framework checkout.'
}
$feedPath = (Resolve-Path -LiteralPath $Feed).Path
foreach ($folder in @('', 'backend', 'backend/frontend/src', 'backend/Properties', 'contracts', '.feed', '.vscode')) {
  New-Item -ItemType Directory -Path (Join-Path $root $folder) -Force | Out-Null
}
Copy-Item -Path (Join-Path $feedPath '*.nupkg') -Destination (Join-Path $root '.feed')
Copy-Item -LiteralPath (Join-Path $repository 'global.json') -Destination $root
$sourceRoot = if ($IntegrationFixture) { 'fixtures' } else { 'samples' }
$sample = Join-Path $repository "$sourceRoot/Spar.NativeSample"
Get-ChildItem -LiteralPath $sample -File | Where-Object Extension -in @('.cs', '.html', '.js') |
  Copy-Item -Destination (Join-Path $root 'backend')
Copy-Item -LiteralPath (Join-Path $repository "$sourceRoot/Spar.NativeSample.Contracts/BackendContract.cs") -Destination (Join-Path $root 'contracts')
foreach ($name in @('package.json', 'package-lock.json', 'tsconfig.json', 'index.html', 'write-notices.mjs')) {
  Copy-Item -LiteralPath (Join-Path $sample "frontend/$name") -Destination (Join-Path $root 'backend/frontend')
}
Get-ChildItem -LiteralPath (Join-Path $sample 'frontend/src') -File | Copy-Item -Destination (Join-Path $root 'backend/frontend/src')
@'
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(async ({ command }) => ({
  plugins: [react(), ...(command === 'serve' ? [(await import('./.spar/vite-session.mjs')).sparSession()] : [])],
  build: { license: { fileName: 'frontend-LICENSES.txt' } },
  cacheDir: '.vite-cache',
  clearScreen: false,
  logLevel: 'error',
}));
'@ | Set-Content -LiteralPath (Join-Path $root 'backend/frontend/vite.config.mjs')
@'
<configuration>
  <packageSources>
    <clear />
    <add key="spar-local" value=".feed" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="spar-local"><package pattern="Sotsera.Spar.*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
  <config><add key="globalPackagesFolder" value=".packages" /></config>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $root 'NuGet.Config')
@'
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
    <SparSourceDirectory>$(MSBuildThisFileDirectory)</SparSourceDirectory>
    <SparDevelopmentToolsDirectory>$(MSBuildThisFileDirectory).tools</SparDevelopmentToolsDirectory>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
@'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Sotsera.Spar.Bridge" Version="0.1.0" />
    <PackageVersion Include="Sotsera.Spar.Windows" Version="0.1.0" />
    <PackageVersion Include="Sotsera.Spar.Development" Version="0.1.0" />
    <PackageVersion Include="Sotsera.Spar.Build" Version="0.1.0" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $root 'Directory.Packages.props')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <BuiltInComInteropSupport>false</BuiltInComInteropSupport>
    <DefaultItemExcludes>$(DefaultItemExcludes);frontend/**</DefaultItemExcludes>
    <SparApplication>true</SparApplication>
    <SparFrontendDirectory>$(MSBuildProjectDirectory)/frontend</SparFrontendDirectory>
    <SparContractProject>../contracts/Spar.NativeSample.Contracts.csproj</SparContractProject>
    <!-- SDK restore must see these before package targets are available on the first build. -->
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <PublishAot Condition="'$(Configuration)' == 'Release' and ('$(SparDeliveryMode)' != 'CoreCLR' or '$(SparStrictAot)' == 'true')">true</PublishAot>
    <SelfContained Condition="'$(Configuration)' == 'Release'">true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Sotsera.Spar.Build" PrivateAssets="all" />
    <PackageReference Include="Sotsera.Spar.Windows" Condition="'$(Configuration)' != 'Debug'" />
    <PackageReference Include="Sotsera.Spar.Development" Condition="'$(Configuration)' == 'Debug'" PrivateAssets="all" />
    <ProjectReference Include="$(SparContractProject)" Condition="'$(DotNetWatchBuild)' != 'true'"
                      GlobalPropertiesToRemove="RuntimeIdentifier;PublishAot;SelfContained" />
  </ItemGroup>

  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <Compile Remove="Program.Production.cs" />
    <EmbeddedResource Include="App.html" LogicalName="Spar.Sample.App.html" />
    <EmbeddedResource Include="App.js" LogicalName="Spar.Sample.App.js" />
    <EmbeddedResource Include="Probe.js" LogicalName="Spar.Sample.Probe.js" />
  </ItemGroup>

  <ItemGroup Condition="'$(Configuration)' != 'Debug'">
    <Compile Remove="Program.cs;Program.Development.cs;SampleDocument.cs" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $root 'backend/Spar.NativeSample.csproj')
if (!$IntegrationFixture) {
  $projectPath = Join-Path $root 'backend/Spar.NativeSample.csproj'
  $project = [IO.File]::ReadAllText($projectPath)
  $project = [regex]::Replace($project, '(?m)^    <EmbeddedResource[^\r\n]+\r?\n', '')
  $project = $project.Replace('Remove="Program.cs;Program.Development.cs;SampleDocument.cs"', 'Remove="Program.Development.cs"')
  [IO.File]::WriteAllText($projectPath, $project)
}
@{ kind = $(if ($IntegrationFixture) { 'integration-fixture' } else { 'minimal-application' }) } |
  ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'consumer.json')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsAotCompatible>true</IsAotCompatible>
    <SparContractFile>$(MSBuildProjectDirectory)/BackendContract.cs</SparContractFile>
    <SparClientOutputDirectory>$(MSBuildProjectDirectory)/../backend/frontend/src/generated</SparClientOutputDirectory>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Sotsera.Spar.Bridge" />
    <PackageReference Include="Sotsera.Spar.Build" PrivateAssets="all" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $root 'contracts/Spar.NativeSample.Contracts.csproj')
'<Solution><Project Path="backend/Spar.NativeSample.csproj" /><Project Path="contracts/Spar.NativeSample.Contracts.csproj" /></Solution>' |
  Set-Content -LiteralPath (Join-Path $root 'Consumer.slnx')
$settings = [ordered]@{
  project = 'backend/Spar.NativeSample.csproj'; application = 'backend/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
  generatedClient = 'backend/frontend/src/generated/client.js'; sessionDirectory = 'artifacts/sessions'
  watchBackend = $true; contractFile = 'contracts/BackendContract.cs'
  restartFiles = @('backend/Program.cs', 'backend/Program.Development.cs')
  frontend = @{ workingDirectory = 'backend/frontend' }
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'spar.dev.json')
$coordinator = Join-Path $root '.tools/Spar.Dev.exe'
@{ profiles = @{ 'Spar development' = @{ commandName = 'Project'; commandLineArgs = '--spar-dev'; workingDirectory = (Join-Path $root 'backend')
    environmentVariables = @{ SPAR_DEV_PROJECT = (Join-Path $root 'spar.dev.json'); SPAR_DEV_COORDINATOR = $coordinator } } } } |
  ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'backend/Properties/launchSettings.json')
@{ version = '0.2.0'; configurations = @(@{ name = 'Spar development'; type = 'spar'; request = 'launch'
  spar = @{ coordinator = '.tools/Spar.Dev.exe'; settings = 'spar.dev.json' } }) } | ConvertTo-Json -Depth 6 |
  Set-Content -LiteralPath (Join-Path $root '.vscode/launch.json')
# The small adapter is a distributable local extension, independent of framework source paths.
Copy-Item -LiteralPath (Join-Path $repository 'tools/Spar.VSCode') -Destination (Join-Path $root '.vscode/spar-extension') -Recurse
Write-Output $root
