# Checks whether MSBuild 18.12.2 binds the typed task parameters the analyzer recommends (MSBuildTask0006/0007):
# AbsolutePath, FileInfo, DirectoryInfo, ITaskItem<AbsolutePath>[], ITaskItem<bool>; in-process, under -mt and in a
# TaskHost. Output: results/typed/<case>.log and results/typed-summary.txt
$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$typedDir = Join-Path $results 'typed'
New-Item -ItemType Directory -Force $typedDir | Out-Null
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& dotnet build (Join-Path $spike 'Probe.Tasks/Probe.Tasks.csproj') -nodeReuse:false --disable-build-servers -nologo -v:q *> (Join-Path $typedDir 'probe-tasks-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Probe.Tasks failed to build; see results/typed/probe-tasks-build.log' }
$taskAssembly = Join-Path $spike 'Probe.Tasks/bin/Debug/net11.0/Probe.Tasks.dll'

$leafRoot = Join-Path $spike 'obj/typed-leaves'
Remove-Item -Recurse -Force $leafRoot -ErrorAction SilentlyContinue
foreach ($i in 1..2) {
    $dir = Join-Path $leafRoot ('leaf-{0:D2}' -f $i)
    New-Item -ItemType Directory -Force $dir | Out-Null
    "<Project>`n  <Import Project=`"..\..\..\Probe.Build\TypedLeaf.targets`" />`n</Project>`n" | Set-Content (Join-Path $dir 'Leaf.proj') -NoNewline
}

$summary = [System.Collections.Generic.List[string]]::new()
foreach ($case in @(@{ N = 'm1'; S = @('-m:1') }, @{ N = 'mt2'; S = @('-m:2', '-mt') }, @{ N = 'taskhost'; S = @('-m:1', '-p:ProbeTaskHost=true') })) {
    $log = Join-Path $typedDir "$($case.N).log"
    & dotnet msbuild (Join-Path $spike 'Probe.Build/Build.proj') -nodeReuse:false -nologo -v:m "-p:ProbeLeafRoot=obj\typed-leaves" "-p:ProbeTaskAssembly=$taskAssembly" @($case.S) *> $log
    $summary.Add("## $($case.N) exit=$LASTEXITCODE")
    Get-Content $log | Select-String 'TYPED\||error|warning' | ForEach-Object { $l = $_.Line.Trim(); $i = $l.IndexOf('TYPED|'); $summary.Add('  ' + $(if ($i -ge 0) { $l.Substring($i) } else { $l })) }
}

[IO.File]::WriteAllText((Join-Path $results 'typed-summary.txt'), (($summary -join "`n") + "`n"))
$summary
