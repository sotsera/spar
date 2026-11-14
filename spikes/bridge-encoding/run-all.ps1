# Runs every bridge-encoding measurement on Windows. From the repository root:
#   pwsh spikes/bridge-encoding/run-all.ps1 [-Runs 3] [-Skip desktop,loopback,bench]
# Publishes the three NativeAOT executables, then runs them one after another (never in parallel, so they do not
# disturb each other). Raw results: artifacts/bridge-encoding/{desktop,loopback}/*.json; summaries: results/.
param(
    [int] $Runs = 3,
    [string[]] $Skip = @()
)

$ErrorActionPreference = 'Stop'
$spike = $PSScriptRoot
$root = (Resolve-Path (Join-Path $spike '..\..')).Path
$artifacts = Join-Path $root 'artifacts\bridge-encoding'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$log = Join-Path $artifacts 'run-log.txt'

function Publish($project) {
    $output = dotnet publish (Join-Path $spike $project) -c Release 2>&1
    $warnings = @($output | Select-String -Pattern ': warning ').Count
    if ($LASTEXITCODE -ne 0) { $output | Out-Host; throw "publish $project failed" }
    "publish $project : $warnings warnings" | Tee-Object -FilePath $log -Append
}

function Run($exe, [string[]] $arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $output = & $exe @arguments 2>&1
    $line = "{0} {1} -> exit {2}, {3:F1} s | {4}" -f (Split-Path $exe -Leaf), ($arguments -join ' '), $LASTEXITCODE, $clock.Elapsed.TotalSeconds, (($output | Select-Object -Last 2) -join ' / ')
    $line | Tee-Object -FilePath $log -Append
}

"=== run-all $(Get-Date -Format s), runs=$Runs" | Tee-Object -FilePath $log -Append
Publish 'Bridge.Bench'
Publish 'Bridge.Desktop'
Publish 'Bridge.Loopback'
$bench = Join-Path $spike 'Bridge.Bench\bin\Release\net11.0\win-x64\publish\Bridge.Bench.exe'
$desktop = Join-Path $spike 'Bridge.Desktop\bin\Release\net11.0-windows\win-x64\publish\Bridge.Desktop.exe'
$loopback = Join-Path $spike 'Bridge.Loopback\bin\Release\net11.0\win-x64\publish\Bridge.Loopback.exe'

if ($Skip -notcontains 'bench') {
    Run $bench @('corpus')
    Run $bench @('sizes')
    Run $bench @('robust')
    Run $bench @('cost')
}

if ($Skip -notcontains 'desktop') {
    Run $desktop @('--mode', 'probe', '--enc', 'A', '--run', '1')
    for ($run = 1; $run -le $Runs; $run++) {
        Run $desktop @('--mode', 'micro', '--enc', 'A', '--run', "$run")
        foreach ($enc in 'A', 'B', 'C', 'Bj') {
            Run $desktop @('--mode', 'full', '--enc', $enc, '--run', "$run")
        }
    }
}

if ($Skip -notcontains 'loopback') {
    for ($run = 1; $run -le $Runs; $run++) {
        Run $loopback @('--mode', 'micro', '--enc', 'A', '--run', "$run")
        foreach ($enc in 'A', 'B', 'C') {
            Run $loopback @('--mode', 'full', '--enc', $enc, '--run', "$run")
        }
    }
}

"=== done $(Get-Date -Format s)" | Tee-Object -FilePath $log -Append
