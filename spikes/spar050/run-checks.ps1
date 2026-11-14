# The checks besides the corpus and the timing:
#   1. the copy of spikes/app-model-check built with the SPAR05x analyzer: any diagnostic is a false positive;
#   2. the code fixes, applied in an AdhocWorkspace and recompiled;
#   3. the runtime probes: what a reader observes for each runtime-only construct.
# Usage (from any folder): pwsh spikes/spar050/run-checks.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$results = Join-Path $root 'results'

# 1. App-model copy. -p:ReportAnalyzer lists the analyzers that ran, to show SPAR05x was loaded.
$appModel = Join-Path $root 'AppModelCheck/SqlTool/SqlTool.csproj'
$log = dotnet build $appModel --nologo -v:d --no-incremental -p:ReportAnalyzer=true 2>&1
$exit = $LASTEXITCODE
$report = [System.Collections.Generic.List[string]]::new()
$report.Add("# App-model copy with SPAR05x, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd'), exit code $exit")
$log | Select-String -Pattern ': (error|warning) [A-Z]+[0-9]+' | ForEach-Object { $report.Add($_.Line -replace '^.*AppModelCheck[\\/]', '') }
$log | Select-String -Pattern 'Spar\.Analyzers\.DefaultResultAnalyzer|Warning\(s\)|Error\(s\)|Build succeeded' |
    ForEach-Object { $report.Add($_.Line.Trim()) } | Out-Null
$report | Set-Content -Path (Join-Path $results 'appmodel-check.txt') -Encoding utf8
$report

# 2 and 3. The harness.
$harness = Join-Path $root 'Harness/Spar050.Harness.csproj'
dotnet build $harness --nologo -v:q | Out-Null
dotnet run --project $harness --no-build -- codefix $root | Set-Content -Path (Join-Path $results 'codefixes.txt') -Encoding utf8
"code fixes: exit $LASTEXITCODE"
dotnet run --project $harness --no-build -- runtime | Set-Content -Path (Join-Path $results 'runtime-probes.txt') -Encoding utf8
"runtime probes: exit $LASTEXITCODE"
