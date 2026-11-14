# Measures analyzer time on the ~200-file sample: a forced recompile of the sample project only, with
# -p:ReportAnalyzer=true, Runs times per analyzer setting. Records the analyzer's own time (csc's per-analyzer table),
# the total analyzer time, and the Csc task time.
# Usage (from any folder): pwsh spikes/spar050/run-timing.ps1 [-Runs 7]
param([int] $Runs = 7)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'Sample/Spar050.Sample.csproj'
$output = Join-Path $root 'results/timing.txt'

pwsh -NoProfile -File (Join-Path $root 'Sample/generate.ps1') | Out-Null
$files = (Get-ChildItem (Join-Path $root 'Sample/obj/sample') -Filter '*.cs').Count
$lines = (Get-ChildItem (Join-Path $root 'Sample/obj/sample') -Filter '*.cs' | Get-Content | Measure-Object -Line).Lines

$report = [System.Collections.Generic.List[string]]::new()
$report.Add("# Analyzer timing, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd'), $files files, $lines lines, $Runs runs each")
$report.Add('# Each run: dotnet build -t:Rebuild -p:BuildProjectReferences=false -p:ReportAnalyzer=true (compiler server warm).')

function Get-Median($values)
{
    $sorted = @($values | Sort-Object)
    return $sorted[[int][Math]::Floor($sorted.Count / 2)]
}

foreach ($analyzer in 'None', 'Baseline', 'New')
{
    # Warm-up: restore, build references and start the compiler server.
    dotnet build $project --nologo -v:q "-p:Analyzer=$analyzer" | Out-Null

    $ours = [System.Collections.Generic.List[double]]::new()
    $total = [System.Collections.Generic.List[double]]::new()
    $csc = [System.Collections.Generic.List[double]]::new()
    for ($run = 1; $run -le $Runs; $run++)
    {
        $log = dotnet build $project --nologo -v:d -t:Rebuild -p:BuildProjectReferences=false -p:ReportAnalyzer=true `
            "-p:Analyzer=$analyzer" -clp:PerformanceSummary 2>&1
        $totalLine = $log | Select-String -Pattern 'Total analyzer execution time: ([0-9.]+) seconds' | Select-Object -First 1
        $ourLine = $log | Select-String -Pattern '^\s*<?([0-9.]+)\s+<?[0-9]+\s+(Spar\.Analyzers\.DefaultResultAnalyzer|Spar\.Analyzers\.DefaultResultAnalyzer)' |
            Select-Object -First 1
        $cscLine = $log | Select-String -Pattern '^\s*([0-9]+) ms\s+Csc\s' | Select-Object -First 1

        $totalSeconds = if ($totalLine) { [double]$totalLine.Matches[0].Groups[1].Value } else { 0 }
        $ourSeconds = if ($ourLine) { [double]$ourLine.Matches[0].Groups[1].Value } else { 0 }
        $cscMs = if ($cscLine) { [double]$cscLine.Matches[0].Groups[1].Value } else { 0 }
        $total.Add($totalSeconds)
        $ours.Add($ourSeconds)
        $csc.Add($cscMs)
        $report.Add("$analyzer run $run : Spar analyzer $ourSeconds s, all analyzers $totalSeconds s, Csc task $cscMs ms")
    }

    $report.Add("$analyzer median : Spar analyzer $(Get-Median $ours) s, all analyzers $(Get-Median $total) s, Csc task $(Get-Median $csc) ms")
}

# The full per-analyzer table of one New run, for reference.
$log = dotnet build $project --nologo -v:d -t:Rebuild -p:BuildProjectReferences=false -p:ReportAnalyzer=true -p:Analyzer=New 2>&1
$start = ($log | Select-String -Pattern 'Total analyzer execution time' | Select-Object -First 1).LineNumber
if ($start)
{
    $report.Add('# Per-analyzer table of one more New run (csc output)')
    $log[($start - 1)..($start + 40)] | ForEach-Object { $report.Add(($_ -replace '^\s+', '    ')) }
}

$report | Set-Content -Path $output -Encoding utf8
$report | Where-Object { $_ -match 'median' }
