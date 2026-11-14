# Measures the analyzer's build-time cost on the Spar-shaped task project (Probe.Tasks) and the rule catalogue
# (Probe.Rules): full rebuilds with and without the analyzer, plus Roslyn's own per-analyzer timing (ReportAnalyzer).
# Output: results/cost/*.log and results/cost-summary.txt
param(
    [int] $Repetitions = 5
)

$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$costDir = Join-Path $results 'cost'
New-Item -ItemType Directory -Force $costDir | Out-Null
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# repetitions=$Repetitions, $(Get-Date -Format s). Rebuild = dotnet build --no-restore --no-incremental -nodeReuse:false --disable-build-servers.")
$summary.Add("# 'csc' is the Csc task time from the performance summary; 'analyzers' is Roslyn's total analyzer time (ReportAnalyzer).")

function Median([double[]] $values) {
    $s = $values | Sort-Object
    return $s[[int][Math]::Floor($s.Count / 2)]
}

foreach ($project in @('Probe.Tasks', 'Probe.Rules')) {
    $path = Join-Path $spike "$project/$project.csproj"
    $variants = if ($project -eq 'Probe.Tasks') { @('with', 'without') } else { @('with') }
    foreach ($variant in $variants) {
        $props = @('-p:ReportAnalyzer=true')
        if ($variant -eq 'without') { $props += '-p:ProbeNoAnalyzer=true' }

        # The analyzer reference changes the restore graph, so restore for this variant first.
        & dotnet restore $path -nodeReuse:false --disable-build-servers -v:q @props | Out-Null
        & dotnet build $path --no-restore -nodeReuse:false --disable-build-servers -v:q -nologo @props | Out-Null  # warm-up

        $walls = @(); $cscs = @(); $analyzers = @(); $ours = @(); $warnings = @()
        for ($rep = 1; $rep -le $Repetitions; $rep++) {
            $log = Join-Path $costDir "$project-$variant-$rep.log"
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            & dotnet build $path --no-restore --no-incremental -nodeReuse:false --disable-build-servers -nologo -v:d -clp:PerformanceSummary @props *> $log
            $sw.Stop()
            $text = Get-Content $log
            $walls += $sw.ElapsedMilliseconds
            $csc = $text | Select-String '^\s+(\d+) ms\s+Csc\s+\d+ calls' | Select-Object -Last 1
            $cscs += if ($csc) { [double]$csc.Matches[0].Groups[1].Value } else { -1 }
            $total = $text | Select-String 'Total analyzer execution time:\s+([\d.]+) seconds' | Select-Object -First 1
            $analyzers += if ($total) { [double]$total.Matches[0].Groups[1].Value * 1000 } else { 0 }
            $mine = $text | Select-String '^\s+([\d.<]+)\s+\d+\s+Microsoft\.Build\.TaskAuthoring\.Analyzer' | Select-Object -First 1
            $ours += if ($mine) { $v = $mine.Matches[0].Groups[1].Value; if ($v -like '<*') { 0 } else { [double]$v * 1000 } } else { 0 }
            $warnings += @($text | Select-String -Pattern ': warning (NU|MSB|NETSDK|CS|CA|IDE|MSBuildTask)\d+' | ForEach-Object { $_.Matches[0].Value }).Count
        }

        $summary.Add(("{0,-12} {1,-8} wall ms {2} (median {3}) | csc ms {4} (median {5}) | all analyzers ms median {6} | TaskAuthoring analyzer ms {7} (median {8}) | warning lines {9}" -f `
            $project, $variant, ($walls -join ','), (Median $walls), ($cscs -join ','), (Median $cscs), (Median $analyzers), ($ours -join ','), (Median $ours), ($warnings -join ',')))
        Write-Host $summary[-1]
    }
}

# Restore the default (analyzer referenced) graph for the task project.
& dotnet restore (Join-Path $spike 'Probe.Tasks/Probe.Tasks.csproj') -nodeReuse:false --disable-build-servers -v:q | Out-Null
[IO.File]::WriteAllText((Join-Path $results 'cost-summary.txt'), (($summary -join "`n") + "`n"))
