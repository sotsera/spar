# Runs the SqlTool harness, NativeAOT and CoreCLR (JIT), against two throwaway SQL Server 2022 containers.
# - Uses only the locally present image (--pull never); never pulls.
# - The SA password is generated here. It lives in this process's environment, or, with -StateFile, in that file
#   (keep it outside the repository). It is never printed.
# - Without -Start/-Stop the containers are created and removed in one go, whatever happens.
# Output: artifacts/results/<runtime>-<mode>-<arg>.txt
param(
    [string[]] $Steps = @('build', 'setup', 'bulk', 'bulk-cancel', 'query-cancel', 'read', 'schema', 'getschema',
        'monitor', 'managedsni', 'sspi'),
    [switch] $Start,
    [switch] $Stop,
    [string] $StateFile,
    [int] $Rows = 1000000,
    [int] $MonitorSeconds = 30)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$results = 'artifacts/results'
New-Item -ItemType Directory -Force $results | Out-Null

$image = 'mcr.microsoft.com/mssql/server:2022-latest'
$containers = 'spar-sqltool-a', 'spar-sqltool-b'
$exes = [ordered]@{
    aot              = 'SqlTool.App/bin/aot/Release/net11.0/win-x64/publish/SqlTool.App.exe'
    jit              = 'SqlTool.App/bin/jit/Release/net11.0/win-x64/SqlTool.App.exe'
    'aot-managedsni' = 'SqlTool.App/bin/aot-managedsni/Release/net11.0/win-x64/publish/SqlTool.App.exe'
}

function Invoke-Harness([string] $Runtime, [string] $Mode, [string] $Argument = '')
{
    $name = "$Runtime-$Mode$(if ($Argument) { "-$Argument" })"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & $exes[$Runtime] $Mode $Argument 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $watch.Stop()
    "exit $exit wallMs $([Math]::Round($watch.Elapsed.TotalMilliseconds))" + [Environment]::NewLine + $output |
        Set-Content "$results/$name.txt"
    $summary = ($output -split "`r?`n" | Where-Object { $_ -match '^(RESULT|FAIL|PASS)' }) -join [Environment]::NewLine
    "[$name] exit $exit $([Math]::Round($watch.Elapsed.TotalSeconds, 1)) s" + [Environment]::NewLine + $summary
}

function Start-Containers
{
    foreach ($container in $containers)
    {
        if (docker ps -a --filter "name=^/$container$" --format '{{.Names}}') { throw "container $container exists" }
    }

    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789#'
    $env:MSSQL_SA_PASSWORD = 'Aa1#' + -join (1..28 | ForEach-Object {
        $alphabet[[Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Length)] })
    $ports = foreach ($container in $containers)
    {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
        # -e NAME without a value passes the variable from this process's environment.
        docker run --pull never -d --name $container -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD -e MSSQL_PID=Developer `
            -e MSSQL_MEMORY_LIMIT_MB=4096 -p "127.0.0.1:${port}:1433" $image | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "docker run $container failed" }
        $port
    }

    if ($StateFile)
    {
        "$($ports[0]) $($ports[1]) $env:MSSQL_SA_PASSWORD" | Set-Content $StateFile
    }

    return $ports
}

function Set-ConnectionStrings([int[]] $Ports)
{
    $common = "User ID=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=true;TrustServerCertificate=true"
    $env:SPAR_SQL_A = "Server=127.0.0.1,$($Ports[0]);$common"
    $env:SPAR_SQL_B = "Server=127.0.0.1,$($Ports[1]);$common"
}

function Wait-Ready
{
    $ready = [Diagnostics.Stopwatch]::StartNew()
    do
    {
        Start-Sleep -Seconds 2
        $up = (& $exes['jit'] ping 2>&1 | Out-String) -match 'PING 11'
    } until ($up -or $ready.Elapsed.TotalSeconds -gt 180)
    if (-not $up) { throw 'SQL Server did not become ready' }
    "servers ready after $([Math]::Round($ready.Elapsed.TotalSeconds, 1)) s"
}

function Remove-Containers
{
    foreach ($container in $containers)
    {
        docker logs $container *> "$results/container-$container.log"
        docker rm -f $container 2>&1 | Out-Null
    }

    Remove-Item Env:MSSQL_SA_PASSWORD, Env:SPAR_SQL_A, Env:SPAR_SQL_B -ErrorAction SilentlyContinue
    if ($StateFile) { Remove-Item $StateFile -ErrorAction SilentlyContinue }
    "containers removed: $(-not (docker ps -a --filter 'name=^/spar-sqltool-' --format '{{.Names}}'))"
}

if ($Steps -contains 'build')
{
    dotnet build SqlTool.App -c Release -p:SqlToolRuntime=jit -p:TreatWarningsAsErrors=false *> "$results/build-jit.log"
    if ($LASTEXITCODE -ne 0) { throw 'JIT build failed' }
    foreach ($runtime in 'aot', 'aot-managedsni')
    {
        # App-level acceptance of the AOT warnings (user decision), for this app only.
        Remove-Item -Recurse -Force "SqlTool.App/obj/$runtime/Release/net11.0/win-x64/native" -ErrorAction SilentlyContinue
        $watch = [Diagnostics.Stopwatch]::StartNew()
        dotnet publish SqlTool.App -c Release "-p:SqlToolRuntime=$runtime" -p:TreatWarningsAsErrors=false `
            -p:IlcTreatWarningsAsErrors=false *> "$results/publish-$runtime.log"
        if ($LASTEXITCODE -ne 0) { throw "$runtime publish failed" }
        $warnings = Select-String -Path "$results/publish-$runtime.log" -Pattern 'warning (IL\d+): (.*?) \[' -AllMatches |
            ForEach-Object { $_.Matches[0].Groups[1].Value + ": " + $_.Matches[0].Groups[2].Value } | Sort-Object -Unique
        $warnings | Set-Content "$results/warnings-$runtime.txt"
        "warnings ${runtime}: $($warnings.Count) unique"
        "publish $runtime $([Math]::Round($watch.Elapsed.TotalSeconds, 1)) s"
    }
}

if ($Stop)
{
    Remove-Containers
    return
}


# Windows integrated authentication against a local Windows SQL Server instance; needs no container.
if ($Steps -contains "sspi")
{
    foreach ($runtime in "aot", "jit", "aot-managedsni") { Invoke-Harness $runtime sspi }
}

$needsServers = $Start -or @($Steps | Where-Object { $_ -notin "build", "sspi" }).Count -gt 0
if (-not $needsServers) { return }

$own = -not $StateFile
try
{
    if ($Start -or $own)
    {
        $ports = Start-Containers
    }
    else
    {
        $state = (Get-Content $StateFile -Raw).Trim().Split(' ')
        $ports = [int] $state[0], [int] $state[1]
        $env:MSSQL_SA_PASSWORD = $state[2]
    }

    Set-ConnectionStrings $ports
    Wait-Ready
    if ($Start) { return }

    $both = 'aot', 'jit'
    if ($Steps -contains 'setup') { Invoke-Harness aot setup $Rows }

    if ($Steps -contains 'bulk')
    {
        $variants = 'default', 'default-tablock', 'b10k', 'b100k', 'b100k-stream', 'b100k-tablock', 'b100k-internaltx',
            'b100k-externaltx'
        foreach ($variant in $variants) { foreach ($runtime in $both) { Invoke-Harness $runtime bulk $variant } }
        # Repeats of two variants for spread (files are overwritten; the console log keeps each run).
        foreach ($repeat in 1..2)
        {
            foreach ($variant in 'default', 'b100k') { foreach ($runtime in $both) { Invoke-Harness $runtime bulk $variant } }
        }
    }

    if ($Steps -contains 'bulk-cancel')
    {
        foreach ($variant in 'none', 'none-timed', 'none-drain', 'internal', 'external', 'abort', 'batch0', 'batch0-internal',
            'batch0-external', 'batch0-abort', 'batch0-tablock')
        {
            foreach ($runtime in $both) { Invoke-Harness $runtime bulk-cancel $variant }
        }
    }

    if ($Steps -contains 'query-cancel')
    {
        foreach ($case in 'waitfor-async', 'waitfor-sync', 'cpu-async', 'cpu-parallel-async', 'blocked-async',
            'stream-async', 'dispose-drain', 'dispose-cancel')
        {
            foreach ($runtime in $both) { Invoke-Harness $runtime query-cancel $case }
        }
    }

    if ($Steps -contains 'read')
    {
        foreach ($variant in 'typed-async', 'typed-sync', 'seq-async', 'seq-sync', 'seq-stream-async', 'values-sync')
        {
            foreach ($runtime in $both) { Invoke-Harness $runtime read $variant }
        }
    }

    if ($Steps -contains 'schema') { foreach ($runtime in $both) { Invoke-Harness $runtime schema } }
    if ($Steps -contains 'getschema') { foreach ($runtime in $both) { Invoke-Harness $runtime getschema } }
    if ($Steps -contains 'monitor') { foreach ($runtime in $both) { Invoke-Harness $runtime monitor $MonitorSeconds } }

    if ($Steps -contains 'managedsni')
    {
        Invoke-Harness aot-managedsni bulk b100k
        Invoke-Harness aot-managedsni read typed-async
        Invoke-Harness aot-managedsni query-cancel waitfor-async
        Invoke-Harness aot-managedsni bulk-cancel none
    }
}
finally
{
    if ($own) { Remove-Containers }
}
