# Interleaved build and test timings for the three suites. Run from this folder:
#   pwsh ./measure.ps1 -Rounds 5
# Writes one CSV row per measurement to ../../artifacts/test-frameworks/timings.csv.
param(
    [int] $Rounds = 5,
    [string[]] $Projects = @('XunitSuite', 'TUnitSuite', 'XunitAotSuite')
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$out = '../../artifacts/test-frameworks'
New-Item -ItemType Directory -Force $out | Out-Null
$csv = Join-Path $out 'timings.csv'
if (-not (Test-Path $csv)) { 'round,project,step,ms,mtp_ms,exit,note' | Set-Content $csv }

function Filter-Args([string] $project, [switch] $UnitOnly) {
    if ($project -eq 'TUnitSuite') {
        if ($UnitOnly) { return @('--treenode-filter', '/**[(Category!=Probe)&(Category!=Browser)]') }
        return @('--treenode-filter', '/**[Category!=Probe]')
    }
    $a = @('--filter-not-trait', 'Category=Probe')
    if ($UnitOnly) { $a += @('--filter-not-trait', 'Category=Browser') }
    return $a
}

function Measure-Step([int] $round, [string] $project, [string] $step, [scriptblock] $action) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $text = (& $action 2>&1 | Out-String)
    $exit = $LASTEXITCODE
    $sw.Stop()
    $mtp = ''
    # MTP summary: "duration: 3s 186ms" or "duration: 658ms".
    if ($text -match 'duration:\s*(?:(\d+)m\s+)?(?:(\d+)s\s*)?(\d+)ms') {
        $mtp = [int]$Matches[3] + 1000 * [int]("0$($Matches[2])") + 60000 * [int]("0$($Matches[1])")
    }
    $note = ''
    if ($text -match 'total:\s*(\d+)') { $note = "total=$($Matches[1])" }
    if ($exit -ne 0) { $note += ' FAILED'; Write-Warning "$project $step exit $exit`n$text" }
    "$round,$project,$step,$($sw.ElapsedMilliseconds),$mtp,$exit,$note" | Add-Content $csv
    Write-Host ("{0,2} {1,-14} {2,-16} {3,7} ms  mtp {4,6}  {5}" -f $round, $project, $step, $sw.ElapsedMilliseconds, $mtp, $note)
}

for ($round = 1; $round -le $Rounds; $round++) {
    # Rotate the order each round so background load spreads across the suites.
    $shift = ($round - 1) % $Projects.Count
    $order = @($Projects[$shift..($Projects.Count - 1)]) + @(if ($shift -gt 0) { $Projects[0..($shift - 1)] })
    foreach ($p in $order) {
        $exe = "$p/bin/Debug/net11.0/win-x64/$p.exe"
        $filter = Filter-Args $p
        $unit = Filter-Args $p -UnitOnly

        Remove-Item -Recurse -Force "$p/bin", "$p/obj", "$p/TestResults" -ErrorAction SilentlyContinue
        Measure-Step $round $p 'restore' { dotnet restore $p }
        Measure-Step $round $p 'build-clean' { dotnet build $p --no-restore }
        Measure-Step $round $p 'test-cold' { dotnet test --project $p --no-build @filter }
        Measure-Step $round $p 'test-warm' { dotnet test --project $p --no-build @filter }
        Measure-Step $round $p 'test-warm' { dotnet test --project $p --no-build @filter }
        Measure-Step $round $p 'test-unit-only' { dotnet test --project $p --no-build @unit }
        Measure-Step $round $p 'list-mtp' { dotnet test --project $p --no-build --list-tests }
        if ($p -eq 'TUnitSuite') {
            Measure-Step $round $p 'list-exe' { & $exe --list-tests }
            Measure-Step $round $p 'run-exe' { & $exe @filter }
        }
        else {
            Measure-Step $round $p 'list-exe' { & $exe -list tests }
            Measure-Step $round $p 'run-exe' { & $exe -trait- 'Category=Probe' }
        }
        Measure-Step $round $p 'build-noop' { dotnet build $p --no-restore }
        # A test source edit: touch one file the project compiles.
        $file = if ($p -eq 'TUnitSuite') { 'TUnitSuite/FactTests.cs' } else { 'XunitSuite/FactTests.cs' }
        (Get-Item $file).LastWriteTime = Get-Date
        Measure-Step $round $p 'build-incr' { dotnet build $p --no-restore }
        Measure-Step $round $p 'test-with-build' { dotnet test --project $p @filter }
    }
}
