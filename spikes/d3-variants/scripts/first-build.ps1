# Fully cold: each run is the first build of a fresh isolated session (new MSBuild server, new compiler server)
# with the variant's obj/bin deleted. The test project's own outputs are already up to date.
#   pwsh -File scripts/first-build.ps1 [-Repeat 3]
param([int] $Repeat = 3, [string[]] $Operations = @('Build', 'Publish'))

. (Join-Path $PSScriptRoot 'Isolate.ps1')
Set-Location (Join-Path $PSScriptRoot '..')
$rows = [System.Collections.Generic.List[object]]::new()

try {
    Enter-D3
    [void](Invoke-D3 -Quiet -Arguments @('build', 'Tests', '-nologo', '-v:q', '-p:D3VariantMode=None'))
    [void](Stop-D3)
    foreach ($op in $Operations) {
        foreach ($rep in 1..$Repeat) {
            foreach ($mode in 'None', 'InProc', 'Exec') {
                Enter-D3
                Remove-Item -Recurse -Force App\obj\DesktopTest, App\bin\DesktopTest -ErrorAction SilentlyContinue
                $r = Invoke-D3 -Quiet -Arguments @('build', 'Tests', '-nologo', '-v:q', "-p:D3VariantMode=$mode", "-p:D3VariantOperation=$op")
                $rows.Add([pscustomobject]@{ Op = $op; Mode = $mode; Rep = $rep; Exit = $r.ExitCode; Seconds = [Math]::Round($r.Seconds, 2) })
                Write-Host ("{0,-8} {1,-7} rep {2} exit {3} {4:N2} s" -f $op, $mode, $rep, $r.ExitCode, $r.Seconds)
                [void](Stop-D3)
                Start-Sleep -Milliseconds 500
            }
        }
    }
}
finally {
    $rows | Group-Object Op, Mode | ForEach-Object {
        $s = @($_.Group | ForEach-Object Seconds | Sort-Object)
        [pscustomobject]@{ Group = $_.Name; N = $s.Count; Median = $s[[int][Math]::Floor($s.Count / 2)]; Min = $s[0]; Max = $s[-1] }
    } | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host 'Stopped (left by the builds):'
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
