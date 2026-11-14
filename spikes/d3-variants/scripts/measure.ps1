# Times the test project's build with the app variant built in-proc (MSBuild task) or by Exec (nested dotnet).
#   pwsh -File scripts/measure.ps1 [-Repeat 5] [-Operations Build,Publish] [-Arguments '-mt']
# Per repetition and mode: cold (variant obj/bin deleted), warm (unchanged repeat), edit (one app source changed).
# Baseline: the same test build with no variant (D3VariantMode=None), warm.
param(
    [int] $Repeat = 5,
    [string[]] $Operations = @('Build', 'Publish'),
    [string[]] $Arguments = @()
)

. (Join-Path $PSScriptRoot 'Isolate.ps1')
Enter-D3
Set-Location (Join-Path $PSScriptRoot '..')

$notes = 'App\Notes.cs'
$original = [IO.File]::ReadAllText($notes)
$rows = [System.Collections.Generic.List[object]]::new()
$edit = 0

function Time([string] $op, [string] $mode, [string] $state, [int] $rep) {
    $r = Invoke-D3 -Quiet -Arguments (@('build', 'Tests', '-nologo', '-v:q', "-p:D3VariantMode=$mode", "-p:D3VariantOperation=$op") + $Arguments)
    if ($r.ExitCode -ne 0) { Write-Host $r.Output }
    $row = [pscustomobject]@{ Op = $op; Mode = $mode; State = $state; Rep = $rep; Exit = $r.ExitCode; Seconds = [Math]::Round($r.Seconds, 3) }
    $rows.Add($row)
    Write-Host ("{0,-8} {1,-7} {2,-6} rep {3}  exit {4}  {5:N2} s" -f $op, $mode, $state, $rep, $r.ExitCode, $r.Seconds)
}

function Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { $sorted[$mid] } else { ($sorted[$mid - 1] + $sorted[$mid]) / 2 }
}

try {
    # Server start: the first build in this isolated session.
    Time 'Build' 'None' 'first' 0
    [void](Invoke-D3 -Quiet -Arguments @('build', 'App', '-nologo', '-v:q'))

    foreach ($op in $Operations) {
        foreach ($rep in 1..$Repeat) {
            Time $op 'None' 'base' $rep
            $modes = if ($rep % 2) { @('InProc', 'Exec') } else { @('Exec', 'InProc') }
            foreach ($mode in $modes) {
                Remove-Item -Recurse -Force App\obj\DesktopTest, App\bin\DesktopTest -ErrorAction SilentlyContinue
                Time $op $mode 'cold' $rep
                Time $op $mode 'warm' $rep
                $edit++
                [IO.File]::WriteAllText($notes, $original.Replace('"second"', "`"second-$edit`""))
                Time $op $mode 'edit' $rep
            }
        }
    }
}
finally {
    [IO.File]::WriteAllText($notes, $original)
    [void](New-Item -ItemType Directory -Force obj)
    $stamp = Get-Date -Format yyyyMMdd-HHmmss
    $rows | Export-Csv (Join-Path $PSScriptRoot "..\obj\measure-$stamp.csv")

    $rows | Where-Object Rep -gt 0 | Group-Object Op, Mode, State | ForEach-Object {
        $s = @($_.Group | ForEach-Object Seconds)
        [pscustomobject]@{
            Group = $_.Name; N = $s.Count; Median = Median $s
            Min = ($s | Measure-Object -Minimum).Minimum; Max = ($s | Measure-Object -Maximum).Maximum
            Failures = @($_.Group | Where-Object Exit -ne 0).Count
        }
    } | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    Write-Host 'Stopped (left by the builds):'
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
