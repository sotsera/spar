# Median, min and max per project and step from measure.ps1's CSV.
param([string] $Csv = '../../artifacts/test-frameworks/timings.csv')

Set-Location $PSScriptRoot
$rows = Import-Csv $Csv
function Median([double[]] $v) {
    $s = $v | Sort-Object
    if ($s.Count -eq 0) { return '' }
    if ($s.Count % 2) { return $s[[int][math]::Floor($s.Count / 2)] }
    return ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2
}

$rows | Group-Object step, project | ForEach-Object {
    $ms = [double[]]($_.Group | ForEach-Object { [double]$_.ms })
    $mtpValues = @($_.Group | Where-Object { $_.mtp_ms -ne '' } | ForEach-Object { [double]$_.mtp_ms })
    $first = $_.Group[0]
    [pscustomobject]@{
        Step = $first.step
        Project = $first.project
        N = $ms.Count
        Median = Median $ms
        Min = ($ms | Measure-Object -Minimum).Minimum
        Max = ($ms | Measure-Object -Maximum).Maximum
        MtpMedian = if ($mtpValues.Count) { Median $mtpValues } else { '' }
        MtpMin = if ($mtpValues.Count) { ($mtpValues | Measure-Object -Minimum).Minimum } else { '' }
        MtpMax = if ($mtpValues.Count) { ($mtpValues | Measure-Object -Maximum).Maximum } else { '' }
        Failures = @($_.Group | Where-Object { $_.exit -ne '0' }).Count
    }
} | Sort-Object Step, Project | Format-Table -AutoSize | Out-String -Width 200
