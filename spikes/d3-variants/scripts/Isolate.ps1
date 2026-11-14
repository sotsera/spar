# Dot-source this. It isolates the spike's build processes and stops them afterwards.
#   MSBUILDNODEHANDSHAKESALT  a private salt: these builds neither use nor leave MSBuild server/nodes for others.
#   SharedCompilationId       a private compiler-server pipe, for the same reason.
# A job object does not work here: the host's job allows silent breakaway, so children never join a nested job.
# Instead every dotnet process is started through Invoke-D3, and Stop-D3 ends the tree of processes descended from
# them (MSBuild server, worker nodes, task hosts, compiler server), found by parent id and start time.

$script:D3Roots = [System.Collections.Generic.List[int]]::new()
$script:D3Start = Get-Date

function Enter-D3 {
    $env:MSBUILDNODEHANDSHAKESALT = "d3-" + [Guid]::NewGuid().ToString('N')
    $env:SharedCompilationId = "d3-" + [Guid]::NewGuid().ToString('N')
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $script:D3Start = (Get-Date).AddSeconds(-1)
}

# Runs dotnet with the given arguments in the current directory; returns @{ ExitCode; Seconds; Output }.
function Invoke-D3 {
    param([string[]] $Arguments, [switch] $Quiet)
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($a in $Arguments) { $info.ArgumentList.Add($a) }
    $info.WorkingDirectory = (Get-Location).Path
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    $script:D3Roots.Add($process.Id)
    $stderr = $process.StandardError.ReadToEndAsync()
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    $watch.Stop()
    $output += $stderr.Result
    if (-not $Quiet) { Write-Host $output }
    [pscustomobject]@{ ExitCode = $process.ExitCode; Seconds = $watch.Elapsed.TotalSeconds; Output = $output }
}

# Starts dotnet without waiting; Complete-D3 waits and returns the same shape as Invoke-D3.
function Start-D3 {
    param([string[]] $Arguments)
    $info = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($a in $Arguments) { $info.ArgumentList.Add($a) }
    $info.WorkingDirectory = (Get-Location).Path
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    $script:D3Roots.Add($process.Id)
    [pscustomobject]@{
        Process = $process; Watch = $watch
        Out = $process.StandardOutput.ReadToEndAsync(); Err = $process.StandardError.ReadToEndAsync()
    }
}

function Complete-D3 {
    param($Started)
    $Started.Process.WaitForExit()
    $Started.Watch.Stop()
    [pscustomobject]@{
        ExitCode = $Started.Process.ExitCode; Seconds = $Started.Watch.Elapsed.TotalSeconds
        Output = $Started.Out.Result + $Started.Err.Result
    }
}

# Live processes descended from the processes Invoke-D3 started.
function Get-D3Processes {
    $all = Get-CimInstance Win32_Process | Where-Object { $_.CreationDate -ge $script:D3Start }
    $ours = [System.Collections.Generic.HashSet[int]]::new([int[]] $script:D3Roots.ToArray())
    $found = [System.Collections.Generic.List[object]]::new()
    do {
        $added = $false
        foreach ($p in $all) {
            if ($ours.Contains([int] $p.ProcessId)) { continue }
            if ($ours.Contains([int] $p.ParentProcessId)) {
                [void] $ours.Add([int] $p.ProcessId)
                $found.Add($p)
                $added = $true
            }
        }
    } while ($added)
    foreach ($p in $found) {
        $cmd = $p.CommandLine -replace '^"[^"]*\\([^\\"]+)"', '$1'
        [pscustomobject]@{ Id = $p.ProcessId; Parent = $p.ParentProcessId; Name = $p.Name; Cmd = $cmd.Substring(0, [Math]::Min(100, $cmd.Length)) }
    }
}

function Stop-D3 {
    $left = @(Get-D3Processes)
    foreach ($p in $left) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { }
    }
    $left
}
