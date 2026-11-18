# Reads the start-up run logs (run.sh startup <set>) and writes results/startup.txt: per run, the process-to-host-start
# time, the host's start to ui-ready (the host's own clock), their sum, and the working set and threads one second after
# ui-ready; then per set and variant the median of rounds 2-8 (round 1 left out).
param([string] $Logs = (Join-Path $PSScriptRoot '../../artifacts/configuration-cost/logs/startup'))
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
[Threading.Thread]::CurrentThread.CurrentCulture = $inv
function Median([double[]] $values)
{
    $s = $values | Sort-Object
    $n = $s.Count
    if ($n % 2) { return $s[($n - 1) / 2] }
    return ($s[$n / 2 - 1] + $s[$n / 2]) / 2
}

$runs = foreach ($file in Get-ChildItem $Logs -Filter '*.log' | Where-Object Name -notlike '*.app.log')
{
    if ($file.BaseName -notmatch '^s(\d+)-v(\d)-(\d+)$') { continue }
    $text = Get-Content $file.FullName -Raw
    $host0 = [double]::Parse([regex]::Match($text, 'Host started ([\d.]+) ms after the process').Groups[1].Value, $inv)
    $ready = [double]::Parse([regex]::Match($text, 'ui-ready at ([\d.]+) ms').Groups[1].Value, $inv)
    $end = [regex]::Match($text, 'run: app working set (\d+) KiB, peak seen \d+ KiB, private (\d+) KiB, threads (\d+)')
    [pscustomobject]@{
        Set = [int] $Matches[1]; Variant = [int] $Matches[2]; Round = [int] $Matches[3]
        ProcessToHost = $host0; HostToReady = $ready; ProcessToReady = $host0 + $ready
        WorkingSetKiB = [int] $end.Groups[1].Value; PrivateKiB = [int] $end.Groups[2].Value; Threads = [int] $end.Groups[3].Value
    }
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Each run: set, variant, round | process start to host start (ms) | host start to ui-ready (ms) | sum | working set KiB | private KiB | threads (1 s after ui-ready)')
foreach ($r in $runs | Sort-Object Set, Round, Variant)
{
    $lines.Add(('s{0} v{1} r{2} | {3:0.0} | {4:0.0} | {5:0.0} | {6} | {7} | {8}' -f $r.Set, $r.Variant, $r.Round, $r.ProcessToHost, $r.HostToReady, $r.ProcessToReady, $r.WorkingSetKiB, $r.PrivateKiB, $r.Threads))
}

$lines.Add('')
$lines.Add('# Medians of rounds 2-8 (7 runs): set, variant | process to host | host to ui-ready | sum | working set KiB | private KiB | threads | host-to-ui-ready range')
foreach ($g in $runs | Where-Object Round -gt 1 | Group-Object Set, Variant | Sort-Object Name)
{
    $x = $g.Group
    $lines.Add(('{0} | {1:0.0} | {2:0.0} | {3:0.0} | {4} | {5} | {6} | {7:0.0}-{8:0.0}' -f $g.Name, (Median $x.ProcessToHost), (Median $x.HostToReady),
        (Median $x.ProcessToReady), (Median $x.WorkingSetKiB), (Median $x.PrivateKiB), (Median $x.Threads),
        ($x.HostToReady | Measure-Object -Minimum).Minimum, ($x.HostToReady | Measure-Object -Maximum).Maximum))
}

$lines.Add('')
$lines.Add('# All sets together, rounds 2-8 (7 runs per set): variant | process to host | host to ui-ready | sum | working set KiB | private KiB | threads')
foreach ($g in $runs | Where-Object Round -gt 1 | Group-Object Variant | Sort-Object Name)
{
    $x = $g.Group
    $lines.Add(('v{0} | {1:0.0} | {2:0.0} | {3:0.0} | {4} | {5} | {6}' -f $g.Name, (Median $x.ProcessToHost), (Median $x.HostToReady),
        (Median $x.ProcessToReady), (Median $x.WorkingSetKiB), (Median $x.PrivateKiB), (Median $x.Threads)))
}

$lines | Set-Content (Join-Path $PSScriptRoot 'results/startup.txt') -Encoding utf8
$lines | Select-Object -Last 14
