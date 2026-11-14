# Runs the probe tasks across several MSBuild scheduling modes and records where each task executed.
# Each task logs: kind, leaf, process id, thread id, process name, node mode, per-process execution count, start, end,
# project directory as the task saw it, and PROBE_VAR as the task saw it.
# Output: results/modes/<mode>-<n>.log (raw console log) and results/modes-summary.txt.
param(
    [int] $Leaves = 8,
    [int] $Repetitions = 3,
    [int] $Delay = 300,
    [ValidateSet('all', 'threadsafe', 'legacy')] [string] $Kinds = 'all',
    [string] $Tag = '',
    [string[]] $Modes = @('m1', 'm8', 'mt8', 'm8-taskhost', 'mt8-taskhost')
)

$ErrorActionPreference = 'Stop'
$spike = Split-Path -Parent $PSScriptRoot
$results = Join-Path $spike 'results'
$modesDir = Join-Path $results 'modes'
New-Item -ItemType Directory -Force $modesDir | Out-Null
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PROBE_VAR = 'from-entry-process'

# Build the task assembly first, in its own invocation, so no build of the probe graph has it loaded.
& dotnet build (Join-Path $spike 'Probe.Tasks/Probe.Tasks.csproj') -nodeReuse:false --disable-build-servers -nologo -v:q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Probe.Tasks failed to build' }
$taskAssembly = Join-Path $spike 'Probe.Tasks/bin/Debug/net11.0/Probe.Tasks.dll'

# Generate the leaf projects (disposable, under obj/). Each sits in its own folder, so each has its own project directory.
$leafRoot = Join-Path $spike 'obj/leaves'
Remove-Item -Recurse -Force $leafRoot -ErrorAction SilentlyContinue
for ($i = 1; $i -le $Leaves; $i++) {
    $dir = Join-Path $leafRoot ('leaf-{0:D2}' -f $i)
    New-Item -ItemType Directory -Force $dir | Out-Null
    "<Project>`n  <Import Project=`"..\..\..\Probe.Build\Leaf.targets`" />`n</Project>`n" | Set-Content (Join-Path $dir 'Leaf.proj') -NoNewline
}

$buildProj = Join-Path $spike 'Probe.Build/Build.proj'
$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add("# leaves=$Leaves delay=${Delay}ms per task, kinds=$Kinds, repetitions=$Repetitions, $(Get-Date -Format s)")

foreach ($mode in $Modes) {
    $switches = switch ($mode) {
        'm1' { @('-m:1') }
        'm8' { @('-m:8') }
        'mt8' { @('-m:8', '-mt') }
        'm8-taskhost' { @('-m:8', '-p:ProbeTaskHost=true') }
        'mt8-taskhost' { @('-m:8', '-mt', '-p:ProbeTaskHost=true') }
    }

    $walls = @()
    for ($rep = 1; $rep -le $Repetitions; $rep++) {
        $log = Join-Path $modesDir "$Tag$mode-$rep.log"
        $psi = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
        foreach ($a in @('msbuild', $buildProj, '-nodeReuse:false', '-nologo', '-v:m', "-p:ProbeTaskAssembly=$taskAssembly", "-p:ProbeDelay=$Delay", "-p:ProbeKinds=$Kinds") + $switches) {
            $psi.ArgumentList.Add($a)
        }
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $p = [System.Diagnostics.Process]::Start($psi)
        $entryPid = $p.Id
        $out = $p.StandardOutput.ReadToEndAsync()
        $err = $p.StandardError.ReadToEndAsync()
        $p.WaitForExit()
        $sw.Stop()
        $text = "# entry pid=$entryPid exit=$($p.ExitCode) wall=$($sw.ElapsedMilliseconds)ms switches=$($switches -join ' ')`n" + $out.Result + $err.Result
        [IO.File]::WriteAllText($log, $text.Replace("`r`n", "`n"))
        $walls += $sw.ElapsedMilliseconds

        $probes = foreach ($line in ($out.Result -split "`n")) {
            $idx = $line.IndexOf('PROBE|')
            if ($idx -ge 0) {
                $f = $line.Substring($idx).Trim().Split('|')
                [pscustomobject]@{ Kind = $f[1]; Leaf = $f[2]; ProcId = [int]$f[3]; Tid = $f[4]; Name = $f[5]; Node = $f[6]; Count = [int]$f[7]
                    Start = [datetime]::ParseExact($f[8], 'HH:mm:ss.fff', $null); End = [datetime]::ParseExact($f[9], 'HH:mm:ss.fff', $null); Dir = $f[10]; Env = $f[11] }
            }
        }

        $summary.Add("")
        $summary.Add("## $mode rep $rep  exit=$($p.ExitCode) wall=$($sw.ElapsedMilliseconds) ms  entry pid=$entryPid  probes=$(@($probes).Count)")
        foreach ($group in ($probes | Group-Object Kind | Sort-Object Name)) {
            $g = $group.Group
            $pids = $g.ProcId | Sort-Object -Unique
            $inEntry = @($g | Where-Object ProcId -eq $entryPid).Count
            $nodes = ($g | Group-Object Node | ForEach-Object { "$($_.Name) x$($_.Count)" }) -join ', '
            # Peak concurrency: the largest number of this kind's executions whose [start, end] intervals overlap.
            $events = @($g | ForEach-Object { [pscustomobject]@{ T = $_.Start; D = 1 }; [pscustomobject]@{ T = $_.End; D = -1 } }) | Sort-Object T, D
            $cur = 0; $peak = 0
            foreach ($e in $events) { $cur += $e.D; if ($cur -gt $peak) { $peak = $cur } }
            $dirs = @($g | ForEach-Object { Split-Path -Leaf $_.Dir } | Sort-Object -Unique)
            $dirOk = @($g | Where-Object { (Split-Path -Leaf $_.Dir) -eq $_.Leaf }).Count
            $envs = ($g.Env | Sort-Object -Unique) -join ','
            $summary.Add(("  {0,-15} processes={1} ({2}) in-entry={3}/{4} nodes=[{5}] peak-concurrent={6} dir=own-leaf {7}/{4} ({8} distinct) env={9}" -f `
                $group.Name, $pids.Count, (($g.Name | Sort-Object -Unique) -join '/'), $inEntry, $g.Count, $nodes, $peak, $dirOk, $dirs.Count, $envs))
        }
    }

    $sorted = $walls | Sort-Object
    $median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    $summary.Add("=> $mode wall ms: $($walls -join ', ') (median $median)")
    Write-Host "$mode : $($walls -join ', ') ms (median $median)"
}

[IO.File]::WriteAllText((Join-Path $results "${Tag}modes-summary.txt"), (($summary -join "`n") + "`n"))
