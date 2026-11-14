# Checks MSBuild's multithreaded strict mode on SDK 11.0.100: what an in-process task sees as the process working
# directory, and whether changing it or writing through a relative path is reported (MSB4286/MSB4287).
# Output: results/strict/<case>.log and results/strict-summary.txt
$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$strictDir = Join-Path $results 'strict'
New-Item -ItemType Directory -Force $strictDir | Out-Null
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& dotnet build (Join-Path $spike 'Probe.Rules/Probe.Rules.csproj') -nodeReuse:false --disable-build-servers -nologo -v:q | Out-Null
$rulesAssembly = Join-Path $spike 'Probe.Rules/bin/Debug/net11.0/Probe.Rules.dll'

$leafRoot = Join-Path $spike 'obj/strict-leaves'
Remove-Item -Recurse -Force $leafRoot -ErrorAction SilentlyContinue
for ($i = 1; $i -le 4; $i++) {
    $dir = Join-Path $leafRoot ('leaf-{0:D2}' -f $i)
    New-Item -ItemType Directory -Force $dir | Out-Null
    "<Project>`n  <Import Project=`"..\..\..\Probe.Build\StrictLeaf.targets`" />`n</Project>`n" | Set-Content (Join-Path $dir 'Leaf.proj') -NoNewline
}

$cases = @(
    @{ Name = 'm4'; Switches = @('-m:4'); Misbehave = 'false'; Disable = $false },
    @{ Name = 'mt4'; Switches = @('-m:4', '-mt'); Misbehave = 'false'; Disable = $false },
    @{ Name = 'mt4-misbehave'; Switches = @('-m:4', '-mt'); Misbehave = 'true'; Disable = $false },
    @{ Name = 'mt4-wave-disabled'; Switches = @('-m:4', '-mt'); Misbehave = 'false'; Disable = $true }
)

$summary = [System.Collections.Generic.List[string]]::new()
Push-Location $spike
try {
    foreach ($case in $cases) {
        $log = Join-Path $strictDir "$($case.Name).log"
        if ($case.Disable) { $env:MSBUILDDISABLEFEATURESFROMVERSION = '18.12' } else { Remove-Item Env:MSBUILDDISABLEFEATURESFROMVERSION -ErrorAction SilentlyContinue }
        & dotnet msbuild (Join-Path $spike 'Probe.Build/Build.proj') -nodeReuse:false -nologo -v:n "-p:ProbeLeafRoot=obj\strict-leaves" `
            "-p:ProbeRulesAssembly=$rulesAssembly" "-p:ProbeMisbehave=$($case.Misbehave)" @($case.Switches) *> $log
        $exit = $LASTEXITCODE
        $text = Get-Content $log
        $summary.Add("## $($case.Name) exit=$exit (cwd of the script: $spike)")
        $text | Select-String 'STRICT\|' | ForEach-Object { $_.Line.Substring($_.Line.IndexOf('STRICT|')) } | Sort-Object -Unique | ForEach-Object { $summary.Add("  $_") }
        $text | Select-String 'MSB42[89]\d|sentinel' | ForEach-Object { $summary.Add("  $($_.Line.Trim())") }
    }
}
finally {
    Remove-Item Env:MSBUILDDISABLEFEATURESFROMVERSION -ErrorAction SilentlyContinue
    Pop-Location
}

[IO.File]::WriteAllText((Join-Path $results 'strict-summary.txt'), (($summary -join "`n") + "`n"))
$summary
