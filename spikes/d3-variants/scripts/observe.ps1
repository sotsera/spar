# Shows which processes a test build starts for each variant mode (polls the process tree while it runs).
#   pwsh -File scripts/observe.ps1 [-Operation Publish] [-Modes InProc,Exec]
param([string] $Operation = 'Publish', [string[]] $Modes = @('InProc', 'Exec'), [string[]] $Arguments = @())

. (Join-Path $PSScriptRoot 'Isolate.ps1')
Enter-D3
Set-Location (Join-Path $PSScriptRoot '..')

try {
    [void](Invoke-D3 -Quiet -Arguments @('build', 'Tests', '-nologo', '-v:q', '-p:D3VariantMode=None'))
    foreach ($mode in $Modes) {
        foreach ($state in 'cold', 'warm') {
            if ($state -eq 'cold') { Remove-Item -Recurse -Force App\obj\DesktopTest, App\bin\DesktopTest -ErrorAction SilentlyContinue }
            $before = @{}
            Get-D3Processes | ForEach-Object { $before[[int] $_.Id] = $true }
            $seen = @{}
            $started = Start-D3 -Arguments (@('build', 'Tests', '-nologo', '-v:q', "-p:D3VariantMode=$mode", "-p:D3VariantOperation=$Operation") + $Arguments)
            while (-not $started.Process.HasExited) {
                foreach ($p in Get-D3Processes) {
                    if ($p.Name -eq 'conhost.exe') { continue }
                    if (-not $seen.ContainsKey($p.Id)) { $seen[$p.Id] = $p }
                }
                Start-Sleep -Milliseconds 100
            }
            $r = Complete-D3 $started
            Write-Host ("== {0} {1} {2}: exit {3}, {4:N2} s; processes seen during the build (* = started by it):" -f $Operation, $mode, $state, $r.ExitCode, $r.Seconds)
            $seen.Values | Sort-Object Id | ForEach-Object {
                $new = if ($before.ContainsKey([int] $_.Id)) { ' ' } else { '*' }
                Write-Host ("  {0} {1,6} <- {2,6} {3} {4}" -f $new, $_.Id, $_.Parent, $_.Name, $_.Cmd)
            }
        }
    }
}
finally {
    Write-Host 'Stopped (left by the builds):'
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
