# Builds the probe corpus with no Spar analyzer, with the SPAR050 prototype (Baseline) and with the new rules (New),
# reads each build's SARIF log, and compares the diagnostics with the markers in the case files.
# Usage (from any folder): pwsh spikes/spar050/run-corpus.ps1
# Writes results/corpus-<Analyzer>.txt (every diagnostic per case) and results/cases.md (the case table).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'Corpus/Spar050.Corpus.csproj'
$casesRoot = Join-Path $root 'Corpus/Cases'
$resultsRoot = Join-Path $root 'results'
New-Item -ItemType Directory -Force $resultsRoot | Out-Null

# --- The cases: header (Case, Kind, Why) and line markers (//~ ID ID …).
$cases = foreach ($file in Get-ChildItem $casesRoot -Recurse -Filter '*.cs' | Sort-Object FullName)
{
    $lines = Get-Content $file.FullName
    $header = $lines | Select-String -Pattern '^// Case: (\S+) \| (.+)$' | Select-Object -First 1
    if (-not $header)
    {
        continue
    }

    $kind = ($lines | Select-String -Pattern '^// Kind: (\S+)' | Select-Object -First 1).Matches[0].Groups[1].Value
    $whyLines = [System.Collections.Generic.List[string]]::new()
    $inWhy = $false
    foreach ($line in $lines)
    {
        if ($line -match '^// Why: (.*)$')
        {
            $inWhy = $true
            $whyLines.Add($Matches[1])
        }
        elseif ($inWhy -and $line -match '^// (.*)$')
        {
            $whyLines.Add($Matches[1])
        }
        elseif ($inWhy)
        {
            break
        }
    }

    $why = $whyLines -join ' '
    $expected = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        if ($lines[$i] -match '//~ (.+)$')
        {
            foreach ($id in ($Matches[1] -split '\s+' | Where-Object { $_ }))
            {
                $expected.Add("$($i + 1):$id")
            }
        }
    }

    [pscustomobject]@{
        Id = $header.Matches[0].Groups[1].Value
        Title = $header.Matches[0].Groups[2].Value
        Group = $file.Directory.Name
        Kind = $kind
        Why = $why
        Path = $file.FullName
        Expected = @($expected | Sort-Object -Unique)
    }
}

# --- One build per analyzer; the SARIF log has every diagnostic with its location.
$analyzers = 'None', 'Baseline', 'New'
$actual = @{}
dotnet build $project --nologo -v:q -p:Analyzer=None | Out-Null
foreach ($analyzer in $analyzers)
{
    dotnet build $project --nologo -v:q -clp:NoSummary "-p:Analyzer=$analyzer" | Out-Null
    $sarif = Join-Path $root "Corpus/obj/Debug/$analyzer-all/diagnostics.sarif"
    $log = Get-Content $sarif -Raw | ConvertFrom-Json
    $byFile = @{}
    foreach ($result in $log.runs[0].results)
    {
        $location = $result.locations[0].physicalLocation
        if (-not $location)
        {
            continue
        }

        $path = ([Uri]$location.artifactLocation.uri).LocalPath.ToLowerInvariant()
        if (-not $byFile.ContainsKey($path))
        {
            $byFile[$path] = [System.Collections.Generic.List[object]]::new()
        }

        $byFile[$path].Add([pscustomobject]@{
            Key = "$($location.region.startLine):$($result.ruleId)"
            Message = $result.message.text
        })
    }

    $actual[$analyzer] = $byFile
}

function Get-Keys($analyzer, $case)
{
    $found = $actual[$analyzer][$case.Path.ToLowerInvariant()]
    if (-not $found)
    {
        return @()
    }

    return @($found | ForEach-Object { $_.Key } | Sort-Object -Unique)
}

function Format-Keys($keys)
{
    if (-not $keys -or $keys.Count -eq 0)
    {
        return '-'
    }

    return (($keys | ForEach-Object { ($_ -split ':')[1] } | Sort-Object -Unique) -join ', ')
}

# --- Raw evidence per analyzer.
foreach ($analyzer in $analyzers)
{
    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add("# Corpus diagnostics with Analyzer=$analyzer, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd')")
    foreach ($case in $cases)
    {
        $out.Add("$($case.Id) [$($case.Kind)] $($case.Title)")
        foreach ($entry in @($actual[$analyzer][$case.Path.ToLowerInvariant()]))
        {
            if ($entry)
            {
                $out.Add("    line $($entry.Key): $($entry.Message)")
            }
        }
    }

    $out | Set-Content -Path (Join-Path $resultsRoot "corpus-$analyzer.txt") -Encoding utf8
}

# --- Scoring.
$groupOrder = 'Existing', 'Arrays', 'Fields', 'Structs', 'Unsafe', 'Apis', 'Generics', 'Flow', 'External', 'Clean'
$compact = [System.Collections.Generic.List[object]]::new()
$rows = [System.Collections.Generic.List[string]]::new()
$rows.Add('| Case | Kind | What | Expected | Baseline (prototype) | New | New matches | Why |')
$rows.Add('|---|---|---|---|---|---|---|---|')
$stats = [ordered]@{}
foreach ($case in $cases)
{
    $expected = $case.Expected
    $sparExpected = @($expected | Where-Object { $_ -match ':SPAR' })
    $baseline = Get-Keys 'Baseline' $case
    $new = Get-Keys 'New' $case

    $newMatches = (($new -join ',') -eq ($expected -join ','))

    # The prototype has one rule; it catches a case when it reports SPAR050 on every line a SPAR05x is expected.
    $baselineSpar = @($baseline | Where-Object { $_ -match ':SPAR' })
    $baselineLines = @($baselineSpar | ForEach-Object { ($_ -split ':')[0] })
    $baselineCaught = $sparExpected.Count -gt 0 -and
        @($sparExpected | Where-Object { $baselineLines -notcontains (($_ -split ':')[0]) }).Count -eq 0
    $baselineFalse = @($baselineSpar | Where-Object { $sparExpected.Count -eq 0 -or
        @($sparExpected | ForEach-Object { ($_ -split ':')[0] }) -notcontains (($_ -split ':')[0]) }).Count

    $key = $case.Kind
    if (-not $stats.Contains($key))
    {
        $stats[$key] = [ordered]@{ Cases = 0; NewMatches = 0; BaselineCaught = 0; BaselineFalse = 0 }
    }

    $stats[$key].Cases++
    if ($newMatches) { $stats[$key].NewMatches++ }
    if ($baselineCaught) { $stats[$key].BaselineCaught++ }
    $stats[$key].BaselineFalse += $baselineFalse

    $baselineText = Format-Keys $baseline
    if ($sparExpected.Count -gt 0)
    {
        $baselineText += if ($baselineCaught) { ' (caught)' } else { ' (missed)' }
    }

    $title = $case.Title -replace '\|', '\|'
    $why = $case.Why -replace '\|', '\|'
    $mark = if ($newMatches) { 'yes' } else { '**NO**' }
    $rows.Add("| $($case.Id) | $($case.Kind) | $title | $(Format-Keys $expected) | $baselineText | $(Format-Keys $new) | $mark | $why |")

    # The compact list for RESULTS.md: the outcome with the new rules, and what the prototype did.
    $ids = Format-Keys $new
    $outcome = switch ($case.Kind)
    {
        'detect' { "caught: $ids" }
        'by-design' { "flagged by design: $ids" }
        'compiler' { "caught by the compiler or .NET analyzers: $ids" }
        'impossible' { "does not compile: $ids" }
        'runtime' { 'runtime guard only' }
        'missed' { 'missed; runtime guard' }
        'clean' { if ($ids -eq '-') { 'clean' } else { "clean (.NET: $ids)" } }
    }

    if (-not $newMatches)
    {
        $outcome += " **(mismatch: $ids)**"
    }

    $prototype = if ($sparExpected.Count -gt 0) { if ($baselineCaught) { 'caught' } else { 'missed' } }
        elseif ($baselineSpar.Count -gt 0) { 'false positive' }
        else { '-' }
    $compact.Add([pscustomobject]@{
        Order = [array]::IndexOf($groupOrder, $case.Group)
        Id = $case.Id
        Line = "| $($case.Id) | $title | $outcome | $prototype | $why |"
    })
}

$list = [System.Collections.Generic.List[string]]::new()
$list.Add('| Case | Construct | With SPAR05x | Prototype | Why |')
$list.Add('|---|---|---|---|---|')
$compact | Sort-Object Order, Id | ForEach-Object { $list.Add($_.Line) }
$list | Set-Content -Path (Join-Path $resultsRoot 'case-list.md') -Encoding utf8

$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# Corpus results, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd')")
$summary.Add('')
$summary.Add("$($cases.Count) cases. Expected = the markers in the case file. 'New matches' = the new analyzer's build gives")
$summary.Add('exactly the expected diagnostics (IDs and lines), no more and no fewer. Baseline = the SPAR050 prototype.')
$summary.Add('')
$summary.Add('| Kind | Cases | New matches | Baseline caught | Baseline extra SPAR050 |')
$summary.Add('|---|---|---|---|---|')
foreach ($kind in $stats.Keys)
{
    $s = $stats[$kind]
    $summary.Add("| $kind | $($s.Cases) | $($s.NewMatches) | $($s.BaselineCaught) | $($s.BaselineFalse) |")
}

$summary.Add('')
($summary + $rows) | Set-Content -Path (Join-Path $resultsRoot 'cases.md') -Encoding utf8
$summary
$cases | Where-Object { ((Get-Keys 'New' $_) -join ',') -ne ($_.Expected -join ',') } | ForEach-Object {
    "MISMATCH $($_.Id): expected [$($_.Expected -join ' ')] got [$((Get-Keys 'New' $_) -join ' ')]"
}
