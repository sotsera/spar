# Compiles every probe case against every Result form and records the compiler diagnostics.
# Usage (from any folder): pwsh spikes/result-api/run-probes.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'Probes/Probe.csproj'
$output = Join-Path $root 'results/probes.txt'

$forms = 'Class', 'Struct', 'UnionStruct', 'UnionClass'
$cases = Get-ChildItem (Join-Path $root 'Probes/Cases') -Filter '*.cs' | ForEach-Object { $_.BaseName } | Sort-Object

dotnet restore $project --nologo -v:q | Out-Null

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Probe diagnostics, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd')")
foreach ($case in $cases)
{
    foreach ($form in $forms)
    {
        $log = dotnet build $project --no-restore --nologo -v:q -clp:NoSummary "-p:Form=$form" "-p:Case=$case" 2>&1
        $codes = $log | Select-String -Pattern ': (error|warning) ([A-Z]+[0-9]+)' -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[2].Value }
        $messages = $log | Select-String -Pattern ': (error|warning) [A-Z]+[0-9]+' |
            ForEach-Object { ($_.Line -replace '^.*Cases[\\/]', '' -replace ' \[.*$', '') } | Sort-Object -Unique
        $summary = if ($LASTEXITCODE -eq 0) { 'compiles' } else { 'fails: ' + (($codes | Sort-Object -Unique) -join ', ') }
        $lines.Add("$case / $form : $summary")
        foreach ($message in $messages)
        {
            $lines.Add("    $message")
        }
    }
}

# The analyzer prototype: what it reports on DefaultValue, and that it stays silent on the valid cases.
$lines.Add('# With the default(Result) analyzer prototype (-p:UseAnalyzer=true)')
$analyzerRuns = @(
    @('Struct', 'DefaultValue'), @('UnionStruct', 'DefaultValue'), @('UnionClass', 'DefaultValue'),
    @('UnionStruct', 'Conversions'), @('UnionStruct', 'Reading'), @('UnionStruct', 'UnionSwitch'))
foreach ($run in $analyzerRuns)
{
    $form = $run[0]
    $case = $run[1]
    $log = dotnet build $project --nologo -v:q -clp:NoSummary "-p:Form=$form" "-p:Case=$case" -p:UseAnalyzer=true 2>&1
    $messages = $log | Select-String -Pattern ': (error|warning) [A-Z]+[0-9]+' |
        ForEach-Object { ($_.Line -replace '^.*Cases[\\/]', '' -replace ' \[.*$', '') } | Sort-Object -Unique
    $summary = if ($LASTEXITCODE -eq 0) { 'compiles' } else { 'fails' }
    $lines.Add("$case / $form + analyzer : $summary")
    foreach ($message in $messages)
    {
        $lines.Add("    $message")
    }
}

$lines | Set-Content -Path $output -Encoding utf8
$lines
