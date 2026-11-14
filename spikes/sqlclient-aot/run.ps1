# Runs the published variants against a throwaway SQL Server 2022 container.
# - Uses only the locally present image (--pull never); never pulls.
# - The SA password is generated here and lives only in this process's environment; it is never printed.
# - The container is removed on exit, whatever happens.
# Run publish.ps1 first. Output goes to artifacts/results/.
param([int] $TimingRuns = 5, [string[]] $Steps = @('features', 'tls', 'timing'))

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$results = 'artifacts/results'
New-Item -ItemType Directory -Force $results | Out-Null

$container = 'spar-sqlclient-aot'
$image = 'mcr.microsoft.com/mssql/server:2022-latest'

$exes = [ordered]@{
    'aot-ms-7.1.2'               = 'SqlAot.App/bin/publish/lax-ms-7.1.2/SqlAot.App.exe'
    'aot-ms-7.1.2-invariant'     = 'SqlAot.App/bin/publish/lax-ms-7.1.2-invariant/SqlAot.App.exe'
    'aot-ms-7.1.2-managedsni'    = 'SqlAot.App/bin/publish/lax-ms-7.1.2-managedsni/SqlAot.App.exe'
    'aot-ms-8.0.0-preview1'      = 'SqlAot.App/bin/publish/lax-ms-8.0.0-preview1/SqlAot.App.exe'
    'aot-sys-4.9.1'              = 'SqlAot.App/bin/publish/lax-sys-4.9.1/SqlAot.App.exe'
    'aot-sys-4.9.1-invariant'    = 'SqlAot.App/bin/publish/lax-sys-4.9.1-invariant/SqlAot.App.exe'
    'jit-ms-7.1.2'               = 'SqlAot.App/bin/jit/SqlAot.App.exe'
}
$minimal = 'Minimal.App/bin/publish/lax-ms-7.1.2-minimal/Minimal.App.exe'
$baseline = 'Baseline.App/bin/publish/strict-baseline/Baseline.App.exe'

# JIT build of the same code and defaults (framework-dependent, apphost).
dotnet build SqlAot.App -c Release -o SqlAot.App/bin/jit *> "$results/build-jit.log"
if ($LASTEXITCODE -ne 0) { throw 'JIT build failed' }

# The native-SNI AOT exe alone, without its publish folder siblings.
$exeOnly = 'SqlAot.App/bin/publish/exe-only-ms-7.1.2'
Remove-Item -Recurse -Force $exeOnly -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $exeOnly | Out-Null
Copy-Item $exes['aot-ms-7.1.2'] $exeOnly
$exes['aot-ms-7.1.2-exe-only'] = "$exeOnly/SqlAot.App.exe"

foreach ($exe in @($exes.Values) + $minimal + $baseline)
{
    if (-not (Test-Path $exe)) { Write-Warning "missing $exe" }
}

if (docker ps -a --filter "name=^/$container$" --format '{{.Names}}') { throw "container $container already exists" }

# Password: 32 characters from upper, lower, digits and '#', with each class present.
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789#'
$env:MSSQL_SA_PASSWORD = 'Aa1#' + -join (1..28 | ForEach-Object {
    $alphabet[[Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Length)] })

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()

try
{
    # -e NAME without a value passes the variable from this process's environment.
    docker run --pull never -d --name $container -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD -e MSSQL_PID=Developer `
        -p "127.0.0.1:${port}:1433" $image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run failed' }

    $env:SPAR_SQL_CS = "Server=127.0.0.1,$port;User ID=sa;Password=$env:MSSQL_SA_PASSWORD;" +
        'Encrypt=true;TrustServerCertificate=true'

    $ready = [Diagnostics.Stopwatch]::StartNew()
    do
    {
        Start-Sleep -Milliseconds 1000
        $up = (docker logs $container 2>&1 | Select-String 'SQL Server is now ready for client connections') -and
            ((& $exes['jit-ms-7.1.2'] first-query 2>&1) -match 'TIMING')
    } until ($up -or $ready.Elapsed.TotalSeconds -gt 180)
    if (-not $up) { throw 'SQL Server did not become ready' }
    "server ready after $([Math]::Round($ready.Elapsed.TotalSeconds, 1)) s on port $port" |
        Tee-Object "$results/run-server.txt"
    docker exec $container cat /etc/os-release 2>$null | Select-String 'PRETTY' | Add-Content "$results/run-server.txt"

    # Feature checks.
    foreach ($name in $(if ($Steps -contains 'features') { $exes.Keys } else { @() }))
    {
        $out = & $exes[$name] features 2>&1 | Out-String
        "exit $LASTEXITCODE" + [Environment]::NewLine + $out | Set-Content "$results/features-$name.txt"
        "features $name -> exit $LASTEXITCODE"
    }
    if ($Steps -contains 'features')
    {
        $out = & $minimal 2>&1 | Out-String
        "exit $LASTEXITCODE" + [Environment]::NewLine + $out | Set-Content "$results/features-aot-minimal.txt"
        "minimal -> exit $LASTEXITCODE"
    }

    # TLS / Encrypt combinations.
    foreach ($name in $(if ($Steps -contains 'tls') { 'aot-ms-7.1.2', 'aot-ms-7.1.2-managedsni', 'jit-ms-7.1.2', 'aot-sys-4.9.1' }))
    {
        & $exes[$name] tls 2>&1 | Set-Content "$results/tls-$name.txt"
    }

    # Startup to first query: run 1 is the first launch of that exe in this script ("cold"), then warm runs.
    $timingExes = if ($Steps -contains 'timing') { 'baseline', 'aot-ms-7.1.2', 'aot-ms-7.1.2-managedsni', 'jit-ms-7.1.2' }
    $timing = foreach ($name in $timingExes)
    {
        $exe = if ($name -eq 'baseline') { $baseline } else { $exes[$name] }
        $arguments = @(if ($name -ne 'baseline') { 'first-query' })
        for ($run = 0; $run -le $TimingRuns; $run++)
        {
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $output = & $exe @arguments 2>&1 | Out-String
            $watch.Stop()
            $line = ($output -split "`r?`n" | Select-String '^TIMING').Line
            if (-not $line) { Write-Warning "$name run $run produced no TIMING line: $output"; continue }
            $fields = @{}
            foreach ($pair in $line.Substring(7).Split(' ')) { $kv = $pair.Split('='); $fields[$kv[0]] = $kv[1] }
            [pscustomobject]@{
                Exe = $name; Run = $(if ($run -eq 0) { 'cold' } else { "warm$run" })
                WallMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 1)
                MainMs = $fields['main']; FirstQueryMs = $fields['firstQuery']; PooledMs = $fields['pooledOpenQuery']
            }
        }
    }
    $timing | Export-Csv "$results/timing.csv"
    $timing | Format-Table -AutoSize | Out-String -Width 200 | Tee-Object "$results/timing.txt"
}
finally
{
    docker logs $container *> "$results/container.log"
    docker rm -f $container 2>&1 | Out-Null
    Remove-Item Env:MSSQL_SA_PASSWORD, Env:SPAR_SQL_CS -ErrorAction SilentlyContinue
    "container removed: $(-not (docker ps -a --filter "name=^/$container$" --format '{{.Names}}'))"
}
