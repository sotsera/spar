#requires -Version 7.4
[CmdletBinding()]
param(
  [string]$EvidenceDirectory = 'artifacts/contract-incrementality/source',
  [string]$Feed
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Force $evidence | Out-Null
$root = if ($Feed) {
  Join-Path ([IO.Path]::GetTempPath()) ('spar-contract-consumer-' + [guid]::NewGuid().ToString('N'))
} else {
  Join-Path $evidence ('fixture-' + [guid]::NewGuid().ToString('N'))
}
if ($Feed) {
  & (Join-Path $PSScriptRoot 'New-PackageConsumer.ps1') -Directory $root -Feed $Feed
} else {
  $source = Join-Path $repository 'samples/Spar.NativeSample.Contracts'
  New-Item -ItemType Directory (Join-Path $root 'contracts') | Out-Null
  Copy-Item (Join-Path $source 'BackendContract.cs') (Join-Path $root 'contracts')
  $projectText = Get-Content (Join-Path $source 'Spar.NativeSample.Contracts.csproj') -Raw
  $projectText = [regex]::Replace($projectText, '(?<=Include="|Project=")\.\./[^"\r\n]+', {
    param($match) [IO.Path]::GetFullPath($match.Value, $source).Replace('\', '/')
  })
  $projectText | Set-Content (Join-Path $root 'contracts/Spar.NativeSample.Contracts.csproj')
}
$project = Join-Path $root 'contracts/Spar.NativeSample.Contracts.csproj'
$contract = Join-Path $root 'contracts/BackendContract.cs'
$original = [IO.File]::ReadAllText($contract)
$client = Join-Path $root 'client'
$calls = Join-Path $evidence 'generator-calls.txt'
if (Test-Path $calls) { throw 'Choose a new evidence directory; generator call evidence already exists.' }
$toolDirectory = Join-Path $root 'probe'
$tool = Join-Path $toolDirectory 'ContractProbe.dll'
$previousLog = $env:SPAR_TEST_CONTRACT_CALLS
$env:SPAR_TEST_CONTRACT_CALLS = $calls
$checks = [Collections.Generic.List[object]]::new()
$buildNumber = 0
$passed = $false

function Invoke-ContractBuild([string]$Name, [string[]]$Extra = @(), [switch]$ExpectFailure, [switch]$Seed) {
  $script:buildNumber++
  $arguments = @('build', $project, '--disable-build-servers', "-p:SparClientOutputDirectory=$client")
  if (!$Seed) { $arguments += "-p:SparContractTool=$script:tool" }
  $arguments += $Extra
  & dotnet @arguments *> (Join-Path $evidence "$buildNumber-$Name.log")
  $exitCode = $LASTEXITCODE
  if (($exitCode -eq 0) -eq [bool]$ExpectFailure) { throw "$Name returned unexpected exit $exitCode." }
}

function Confirm-Calls([string]$Name, [int]$Expected) {
  $actual = if (Test-Path $calls) { @(Get-Content $calls).Count } else { 0 }
  if ($actual -ne $Expected) { throw "$Name`: expected $Expected generator invocations, observed $actual." }
  $checks.Add(@{ name = $Name; invocations = $actual; passed = $true })
  Write-Output "$Name`: passed ($actual generator invocations)."
}

function Confirm-Assembly {
  $stream = [IO.File]::OpenRead($script:assembly)
  $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
  try {
    $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $types = @($metadata.TypeDefinitions | ForEach-Object { $metadata.GetString($metadata.GetTypeDefinition($_).Name) })
    $resources = @($metadata.ManifestResources | ForEach-Object { $metadata.GetString($metadata.GetManifestResource($_).Name) })
    if ('SampleBackendBridge' -notin $types -or 'Spar.Generated.Client.js' -notin $resources) {
      throw 'The compiled contract lost its generated bridge or embedded client.'
    }
  } finally {
    $pe.Dispose()
    $stream.Dispose()
  }
}

try {
  Add-Type -AssemblyName System.Reflection.Metadata
  $query = Join-Path $evidence 'seed.json'
  Invoke-ContractBuild 'seed' -Seed -Extra @('-target:Build', '-getProperty:SparGeneratedDirectory,TargetPath,_SparContractTargetsFile',
    '-getItem:_SparContractTool', "-getResultOutputFile:$query")
  $seed = Get-Content $query -Raw | ConvertFrom-Json
  $generator = $seed.Items._SparContractTool[0].Identity
  $targetsFile = if ($seed.Properties._SparContractTargetsFile) { $seed.Properties._SparContractTargetsFile }
    else { Join-Path $repository 'build/Spar.Contracts.targets' }
  $generated = [IO.Path]::GetFullPath($seed.Properties.SparGeneratedDirectory, (Split-Path $project))
  $assembly = $seed.Properties.TargetPath
  dotnet build (Join-Path $PSScriptRoot 'contract-probe/ContractProbe.csproj') --disable-build-servers -o $toolDirectory *> (Join-Path $evidence 'probe-build.log')
  if ($LASTEXITCODE) { throw 'Contract forwarding probe build failed.' }
  Copy-Item (Join-Path (Split-Path $generator) '*') $toolDirectory -Recurse -Force

  Invoke-ContractBuild 'first'
  Confirm-Calls 'first generation' 1
  Confirm-Assembly
  Remove-Item -LiteralPath $assembly
  Invoke-ContractBuild 'unchanged-recompile'
  Confirm-Calls 'unchanged inputs, real recompile' 1
  Confirm-Assembly
  $expected = 1
  foreach ($name in @('Contract.g.cs', 'client.js', 'client.d.ts')) {
    Remove-Item -LiteralPath (Join-Path $generated $name)
    Invoke-ContractBuild "missing-$name"
    Confirm-Calls "recover $name" (++$expected)
    if (!(Test-Path (Join-Path $generated $name))) { throw "Missing regenerated $name." }
    Confirm-Assembly
  }
  Remove-Item -LiteralPath (Join-Path $client 'client.js'), (Join-Path $client 'client.d.ts')
  Invoke-ContractBuild 'missing-client-copies'
  Confirm-Calls 'restore copied clients without generation' $expected
  if (!(Test-Path (Join-Path $client 'client.js')) -or !(Test-Path (Join-Path $client 'client.d.ts'))) {
    throw 'An up-to-date generation did not restore the frontend clients.'
  }
  [IO.File]::AppendAllText($contract, "`n// A source change with identical generated bytes.`n")
  Invoke-ContractBuild 'comment-change'
  Confirm-Calls 'source changed without output changes' (++$expected)
  Invoke-ContractBuild 'comment-unchanged'
  Confirm-Calls 'unchanged generated bytes still become up to date' $expected
  $changed = $original.Replace('CounterReply(int Value)', 'CounterReply(int Value, int Revision)')
  [IO.File]::WriteAllText($contract, $changed)
  Invoke-ContractBuild 'dto-change'
  Confirm-Calls 'DTO change' (++$expected)
  if ((Get-Content (Join-Path $client 'client.d.ts') -Raw) -notmatch 'revision') { throw 'DTO change did not reach TypeScript.' }
  [IO.File]::WriteAllText($contract, $changed + "`ninvalid contract syntax !")
  foreach ($attempt in 1..2) {
    Invoke-ContractBuild "invalid-$attempt" -ExpectFailure
    Confirm-Calls "invalid contract attempt $attempt" (++$expected)
  }
  [IO.File]::WriteAllText($contract, $original)
  Invoke-ContractBuild 'recovered'
  Confirm-Calls 'recover valid contract' (++$expected)
  (Get-Item (Join-Path $toolDirectory 'Spar.ContractGenerator.dll')).LastWriteTimeUtc = [DateTime]::UtcNow
  Invoke-ContractBuild 'tool-change'
  Confirm-Calls 'generator dependency changed' (++$expected)
  Invoke-ContractBuild 'tool-unchanged'
  Confirm-Calls 'generator dependency unchanged' $expected
  $otherDirectory = Join-Path $root 'other-probe'
  New-Item -ItemType Directory $otherDirectory | Out-Null
  Copy-Item (Join-Path $toolDirectory '*') $otherDirectory -Recurse
  Get-ChildItem $otherDirectory -File | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-1) }
  $tool = Join-Path $otherDirectory 'ContractProbe.dll'
  Invoke-ContractBuild 'tool-path-change'
  Confirm-Calls 'older tool at another path' (++$expected)
  $alternate = Join-Path $root 'alternate-contract.txt'
  [IO.File]::WriteAllText($alternate, $original)
  (Get-Item $alternate).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-1)
  Invoke-ContractBuild 'contract-path-change' -Extra @("-p:SparContractFile=$alternate")
  Confirm-Calls 'older contract at another path' (++$expected)
  Invoke-ContractBuild 'contract-path-unchanged' -Extra @("-p:SparContractFile=$alternate")
  Confirm-Calls 'alternate contract unchanged' $expected
  $ownedFiles = @('Contract.g.cs', 'client.js', 'client.d.ts', 'generator.inputs', 'generation.complete') |
    ForEach-Object { Join-Path $generated $_ }
  Remove-Item -LiteralPath $ownedFiles
  Invoke-ContractBuild 'design-time' -Extra @('-target:GenerateSparContract', '-p:DesignTimeBuild=true')
  Confirm-Calls 'design time starts no generator' $expected
  if (@($ownedFiles | Where-Object { Test-Path $_ }).Count) { throw 'Design-time generation wrote outputs.' }
  Invoke-ContractBuild 'after-design-time'
  Confirm-Calls 'real build after design time' (++$expected)
  $sentinel = Join-Path $generated 'unrelated.keep'
  'preserve' | Set-Content $sentinel
  dotnet clean $project "-p:SparContractTool=$tool" --disable-build-servers *> (Join-Path $evidence 'clean.log')
  if ($LASTEXITCODE -or @($ownedFiles | Where-Object { Test-Path $_ }).Count -or !(Test-Path $sentinel)) {
    throw 'Clean failed to remove exactly the owned generated files.'
  }
  Confirm-Calls 'clean preserves unrelated file' $expected
  Invoke-ContractBuild 'after-clean'
  Confirm-Calls 'real build after clean' (++$expected)
  Confirm-Assembly
  $passed = $true
} finally {
  [IO.File]::WriteAllText($contract, $original)
  $env:SPAR_TEST_CONTRACT_CALLS = $previousLog
  @{ passed = $passed; source = (& git -C $repository rev-parse HEAD); fixture = $root
    kind = $(if ($Feed) { 'fresh-package' } else { 'source' }); generator = $generator
    generatorSha256 = $(if ($generator) { (Get-FileHash $generator).Hash })
    targetsFile = $targetsFile; targetsSha256 = $(if ($targetsFile) { (Get-FileHash $targetsFile).Hash })
    driverSha256 = (Get-FileHash $PSCommandPath).Hash; checks = $checks.ToArray()
  } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'receipt.json')
}
