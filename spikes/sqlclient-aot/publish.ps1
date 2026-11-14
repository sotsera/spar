# Publishes every variant twice: strict (repository policy, warnings are errors) and permissive
# (-p:TreatWarningsAsErrors=false -p:IlcTreatWarningsAsErrors=false, only to obtain runnable output for
# the runtime checks). Writes logs, unique warning lists and a summary to artifacts/results/.
param([string[]] $Only)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$results = 'artifacts/results'
New-Item -ItemType Directory -Force $results | Out-Null

$variants = @(
    @{ Name = 'baseline';            Project = 'Baseline.App'; Props = @() }
    @{ Name = 'baseline-invariant';  Project = 'Baseline.App'; Props = @('-p:InvariantGlobalization=true'); StrictOnly = $true }
    @{ Name = 'ms-7.1.2';            Project = 'SqlAot.App';   Props = @() }
    @{ Name = 'ms-7.1.2-singlewarn'; Project = 'SqlAot.App';   Props = @('-p:TrimmerSingleWarn=true'); StrictOnly = $true }
    @{ Name = 'ms-7.1.2-invariant';  Project = 'SqlAot.App';   Props = @('-p:InvariantGlobalization=true') }
    @{ Name = 'ms-7.1.2-managedsni'; Project = 'SqlAot.App';   Props = @('-p:SqlManagedSni=true') }
    @{ Name = 'ms-7.1.2-noeventsource'; Project = 'SqlAot.App'; Props = @('-p:EventSourceSupport=false'); StrictOnly = $true }
    @{ Name = 'ms-7.1.2-minimal';    Project = 'Minimal.App';  Props = @() }
    @{ Name = 'ms-8.0.0-preview1';   Project = 'SqlAot.App';   Props = @('-p:SqlClientVersion=8.0.0-preview1.26313.4') }
    @{ Name = 'ms-7.0.4';            Project = 'SqlAot.App';   Props = @('-p:SqlClientVersion=7.0.4'); StrictOnly = $true }
    @{ Name = 'ms-6.1.8';            Project = 'SqlAot.App';   Props = @('-p:SqlClientVersion=6.1.8'); StrictOnly = $true }
    @{ Name = 'sys-4.9.1';           Project = 'SqlAot.App';   Props = @('-p:SqlProvider=System') }
    @{ Name = 'sys-4.9.1-invariant'; Project = 'SqlAot.App';   Props = @('-p:SqlProvider=System', '-p:InvariantGlobalization=true') }
)
if ($Only) { $variants = $variants | Where-Object { $Only -contains $_.Name } }

function Get-UniqueWarnings([string] $log)
{
    Get-Content $log | Where-Object { $_ -match '(error|warning) (IL\d{4}): ' } | ForEach-Object {
        if ($_ -match "(IL\d{4}): Assembly '([^']+)' produced (.+?)\.")
        {
            "$($matches[1]) | $($matches[2]) | assembly-level: $($matches[3])"
        }
        elseif ($_ -match '(IL\d{4}): (.+?): (.*?) \[')
        {
            $code = $matches[1]; $member = $matches[2]; $message = $matches[3]
            $target = if ($message -match "(?:Using member|in call to|Method) '([^']+)'") { $matches[1] }
                elseif ($message -match "of method '([^']+)'") { $matches[1] }
                else { $message.Substring(0, [Math]::Min(100, $message.Length)) }
            "$code | $member | $target"
        }
    } | Sort-Object -Unique
}

function Invoke-Publish([hashtable] $variant, [string] $mode)
{
    $log = "$results/publish-$mode-$($variant.Name).log"
    $dir = "bin/publish/$mode-$($variant.Name)/"
    $extra = if ($mode -eq 'lax') { @('-p:TreatWarningsAsErrors=false', '-p:IlcTreatWarningsAsErrors=false') } else { @() }

    # Restore separately so the timing covers build + ILC only; force a full ILC run.
    dotnet restore $variant.Project @($variant.Props) -p:RuntimeIdentifier=win-x64 *> "$results/restore-$($variant.Name).log"
    Remove-Item -Recurse -Force "$($variant.Project)/obj/Release/net11.0/win-x64/native" -ErrorAction SilentlyContinue

    $watch = [Diagnostics.Stopwatch]::StartNew()
    dotnet publish $variant.Project -c Release --no-restore "-p:PublishDir=$dir" @($variant.Props) @extra *> $log
    $exit = $LASTEXITCODE
    $watch.Stop()

    $lines = @(Get-Content $log | Where-Object { $_ -match '^ILC : .*(IL\d{4}):|warning IL\d{4}|error IL\d{4}' })
    $unique = @(Get-UniqueWarnings $log)
    Set-Content "$results/warnings-$mode-$($variant.Name).txt" -Value $unique
    $files = if ($exit -eq 0) {
        (Get-ChildItem "$($variant.Project)/$dir" -File | Where-Object Extension -ne '.pdb' |
            ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ' '
    } else { '' }

    [pscustomobject]@{
        Variant = $variant.Name; Mode = $mode; Exit = $exit; Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
        IlLines = $lines.Count; Unique = $unique.Count
        Codes = ($unique | ForEach-Object { $_.Split(' ')[0] } | Group-Object | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ','
        Files = $files
    }
}

$summary = foreach ($variant in $variants)
{
    Invoke-Publish $variant 'strict'
    if (-not $variant.StrictOnly) { Invoke-Publish $variant 'lax' }
}

$summary | Format-Table -AutoSize | Out-String -Width 400 | Tee-Object "$results/publish-summary$(if ($Only) { '-partial' }).txt"
