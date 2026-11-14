# Runs dotnet command lines from the spike folder with isolated build processes; reports and stops what is left.
#   pwsh -File scripts/run.ps1 'build App -v:m' 'build Tests -v:m'
param([Parameter(ValueFromRemainingArguments)] [string[]] $Commands)

. (Join-Path $PSScriptRoot 'Isolate.ps1')
Enter-D3
Set-Location (Join-Path $PSScriptRoot '..')

try {
    foreach ($command in $Commands) {
        Write-Host "> dotnet $command" -ForegroundColor Cyan
        $result = Invoke-D3 -Arguments ($command -split ' ')
        Write-Host ("< exit {0} in {1:N2} s" -f $result.ExitCode, $result.Seconds) -ForegroundColor Cyan
    }
}
finally {
    Write-Host 'Stopped (left by the builds):' -ForegroundColor Cyan
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
