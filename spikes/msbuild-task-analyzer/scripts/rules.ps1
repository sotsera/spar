# Builds the rule catalogue (Probe.Rules) under each analyzer configuration and records every diagnostic.
# Output: results/rules-<config>.sarif, results/rules-<config>.build.log, results/rules-<config>.tsv,
#         results/rules-<config>.compare.txt (diagnostics against the "expect:" comments in the sources).
param(
    [string[]] $Configs = @('default', 'all-tasks', 'enable-0013', 'wrong-option')
)

$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$project = Join-Path $spike 'Probe.Rules/Probe.Rules.csproj'
New-Item -ItemType Directory -Force $results | Out-Null

$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Get-Expectations([string] $directory) {
    $map = @{}
    foreach ($file in Get-ChildItem $directory -Filter *.cs) {
        $lines = Get-Content $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '//\s*expect:\s*(.*)$') {
                $map["$($file.Name):$($i + 1)"] = $Matches[1].Trim()
            }
        }
    }
    return $map
}

$expect = Get-Expectations (Join-Path $spike 'Probe.Rules')

foreach ($config in $Configs) {
    $sarif = Join-Path $results "rules-$config.sarif"
    $log = Join-Path $results "rules-$config.build.log"
    $props = @("-p:ProbeSarif=$sarif")
    switch ($config) {
        'all-tasks' { $props += '-p:ProbeAllTasks=true' }
        'enable-0013' { $props += '-p:ProbeEnable0013=true' }
        'wrong-option' { $props += '-p:ProbeWrongOption=true' }
    }

    # --no-incremental: the compiler must run so the SARIF log is rewritten for this configuration.
    & dotnet build $project --no-incremental -nodeReuse:false --disable-build-servers -nologo -v:n @props *> $log
    $exit = $LASTEXITCODE

    $json = Get-Content $sarif -Raw | ConvertFrom-Json
    $rows = foreach ($r in $json.runs[0].results) {
        $loc = $r.locations[0].physicalLocation
        $file = if ($loc) { Split-Path -Leaf ([Uri]$loc.artifactLocation.uri).LocalPath } else { '' }
        $line = if ($loc) { $loc.region.startLine } else { 0 }
        $suppressed = if ($r.suppressions) { 'suppressed' } else { '' }
        [pscustomobject]@{ Id = $r.ruleId; Level = $r.level; File = $file; Line = $line; Suppressed = $suppressed; Message = $r.message.text }
    }

    $rows | Sort-Object File, Line, Id | Export-Csv (Join-Path $results "rules-$config.tsv") -Delimiter "`t" -NoTypeInformation -Encoding utf8

    # Compare MSBuildTask* and CS8618 diagnostics against the expectations (default configuration semantics).
    $relevant = $rows | Where-Object { $_.Id -like 'MSBuildTask*' -or $_.Id -eq 'CS8618' }
    $byLine = $relevant | Group-Object { "$($_.File):$($_.Line)" }
    $report = [System.Collections.Generic.List[string]]::new()
    $report.Add("# config=$config exit=$exit diagnostics=$(@($rows).Count) relevant=$(@($relevant).Count)")
    foreach ($key in ($expect.Keys + $byLine.Name | Sort-Object -Unique)) {
        $actual = (($byLine | Where-Object Name -eq $key).Group | ForEach-Object { if ($_.Suppressed) { "$($_.Id)(suppressed)" } else { $_.Id } }) -join ' '
        $wanted = $expect[$key]
        $report.Add(("{0,-34} expect: {1,-50} actual: {2}" -f $key, $wanted, $actual))
    }
    $report | Set-Content (Join-Path $results "rules-$config.compare.txt") -Encoding utf8

    $summary = $relevant | Group-Object Id, Level | Sort-Object Name | ForEach-Object { "$($_.Name) x$($_.Count)" }
    Write-Host "[$config] exit=$exit  $($summary -join '; ')"
}
