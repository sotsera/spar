# Applies the analyzer package's code fixes to the rule catalogue, two ways:
#  1. dotnet format analyzers (the SDK's command-line fixer) on a throwaway copy under obj/fix-copy;
#  2. FixHarness, which loads the fix providers directly and applies each offered fix in isolation.
# Output: results/format-analyzers.log, results/format-report/, results/codefix-report.txt
$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$rules = Join-Path $spike 'Probe.Rules'
$project = Join-Path $rules 'Probe.Rules.csproj'

# Restore and compile once so GlobalUsings.g.cs exists, and collect the compile references.
& dotnet build $project -nodeReuse:false --disable-build-servers -nologo -v:q | Out-Null
$json = & dotnet msbuild $project -nodeReuse:false -t:ResolveAssemblyReferences`;FindReferenceAssembliesForReferences -getItem:ReferencePathWithRefAssemblies | ConvertFrom-Json
$refsFile = Join-Path $spike 'obj/codefix-references.txt'
$json.Items.ReferencePathWithRefAssemblies | ForEach-Object { $_.FullPath } | Set-Content $refsFile

$packageRoot = & dotnet msbuild $project -nodeReuse:false -getProperty:NuGetPackageRoot
$analyzer = (Get-ChildItem (Join-Path $packageRoot 'microsoft.build.taskauthoring.analyzer/18.13.0/analyzers/dotnet/cs') -Filter *.dll).FullName

# 1. dotnet format on a copy (it rewrites files in place).
$copy = Join-Path $spike 'obj/fix-copy/Probe.Rules'
Remove-Item -Recurse -Force (Join-Path $spike 'obj/fix-copy') -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $copy | Out-Null
Copy-Item (Join-Path $rules '*') $copy -Recurse -Exclude obj, bin
$ids = 'MSBuildTask0001 MSBuildTask0002 MSBuildTask0003 MSBuildTask0004 MSBuildTask0005 MSBuildTask0006 MSBuildTask0007 MSBuildTask0008 MSBuildTask0011 MSBuildTask0012'.Split(' ')
Push-Location $copy
try {
    & dotnet restore Probe.Rules.csproj -nodeReuse:false --disable-build-servers -v:q | Out-Null
    & dotnet format analyzers Probe.Rules.csproj --diagnostics @ids --severity info --no-restore --verbosity diagnostic --report (Join-Path $results 'format-report') *> (Join-Path $results 'format-analyzers.log')
    Write-Host "dotnet format exit=$LASTEXITCODE"
}
finally {
    Pop-Location
}
$changed = Get-ChildItem $copy -Filter *.cs | Where-Object { (Get-FileHash $_.FullName).Hash -ne (Get-FileHash (Join-Path $rules $_.Name)).Hash }
Write-Host "dotnet format changed $(@($changed).Count) file(s)"

# 2. The harness.
$sources = @(Get-ChildItem $rules -Filter *.cs | ForEach-Object FullName) + (Join-Path $rules 'obj/Debug/net11.0/Probe.Rules.GlobalUsings.g.cs')
& dotnet (Join-Path $spike 'FixHarness/bin/Debug/net11.0/FixHarness.dll') $analyzer $refsFile $results @sources | Out-Null
Write-Host "harness exit=$LASTEXITCODE -> results/codefix-report.txt"
