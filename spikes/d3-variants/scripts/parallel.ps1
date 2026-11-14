# Two test projects build the same app variant at the same time. Each run starts from deleted variant folders.
#   pwsh -File scripts/parallel.ps1 [-Repeat 3] [-Operations Build,Publish] [-Scenarios ...]
param(
    [int] $Repeat = 3,
    [string[]] $Operations = @('Build', 'Publish'),
    [string[]] $Scenarios = @('SlnInProcGuid', 'SlnInProcFixed', 'SlnExec', 'SlnMtInProcGuid', 'TwoProcInProc', 'TwoProcExec'),
    # Without the MSBuild server both processes start alike, so their variant builds overlap.
    [switch] $NoServer,
    # Hold the cross-process variant lock (D3Lock) around the variant build.
    [switch] $Lock
)

. (Join-Path $PSScriptRoot 'Isolate.ps1')
Enter-D3
if ($NoServer) { $env:DOTNET_CLI_USE_MSBUILD_SERVER = 'false' }
Set-Location (Join-Path $PSScriptRoot '..')

$marker = 'D3MARKER-DESKTOPTEST'
$rows = [System.Collections.Generic.List[object]]::new()

function Summarize($output) {
    $errors = [regex]::Matches($output, '(error|warning) [A-Z]+[0-9]+: [^\[\r\n]{0,110}') | ForEach-Object { $_.Value } | Sort-Object -Unique
    [pscustomobject]@{
        Restores = [regex]::Matches($output, 'Restored [^\r\n]*App\.csproj').Count
        Done = [regex]::Matches($output, 'D3: (in-proc|exec) \w+ of DesktopTest done').Count
        Waited = [regex]::Matches($output, 'D3: lock acquired after \d+ ms \(waited\)').Count
        Errors = ($errors -join ' | ')
    }
}

function VariantOk([string] $op) {
    $exe = if ($op -eq 'Publish') { 'App\bin\DesktopTest\Release\net11.0\win-x64\publish\App.exe' } else { 'App\bin\DesktopTest\Debug\net11.0\App.exe' }
    if (-not (Test-Path $exe)) { return 'missing' }
    $out = & $exe | Out-String
    if ($out.Contains($marker)) { 'ok' } else { 'wrong output' }
}

try {
    # Prepare: the test projects and the Production app are built once, without the variant.
    [void](Invoke-D3 -Arguments @('build', 'D3.slnx', '-nologo', '-v:q', '-p:D3VariantMode=None') -Quiet)
    [void](Invoke-D3 -Arguments @('build', 'App', '-nologo', '-v:q') -Quiet)

    foreach ($op in $Operations) {
        foreach ($i in 1..$Repeat) {
            foreach ($scenario in $Scenarios) {
                Remove-Item -Recurse -Force App\obj\DesktopTest, App\bin\DesktopTest -ErrorAction SilentlyContinue
                $common = @('-nologo', '-v:m', "-p:D3VariantOperation=$op")
                if ($Lock) { $common += '-p:D3Lock=true' }
                $watch = [Diagnostics.Stopwatch]::StartNew()
                switch ($scenario) {
                    'SlnInProcGuid' { $r = Invoke-D3 -Quiet -Arguments (@('build', 'D3.slnx', '-m') + $common + '-p:D3VariantMode=InProc' + '-p:D3RestoreSession=Guid') }
                    'SlnInProcFixed' { $r = Invoke-D3 -Quiet -Arguments (@('build', 'D3.slnx', '-m') + $common + '-p:D3VariantMode=InProc' + '-p:D3RestoreSession=Fixed') }
                    'SlnExec' { $r = Invoke-D3 -Quiet -Arguments (@('build', 'D3.slnx', '-m') + $common + '-p:D3VariantMode=Exec') }
                    'SlnMtInProcGuid' { $r = Invoke-D3 -Quiet -Arguments (@('build', 'D3.slnx', '-mt') + $common + '-p:D3VariantMode=InProc' + '-p:D3RestoreSession=Guid') }
                    { $_ -in 'TwoProcInProc', 'TwoProcExec' } {
                        $mode = if ($scenario -eq 'TwoProcInProc') { 'InProc' } else { 'Exec' }
                        $a = Start-D3 -Arguments (@('build', 'Tests') + $common + "-p:D3VariantMode=$mode")
                        $b = Start-D3 -Arguments (@('build', 'Tests2') + $common + "-p:D3VariantMode=$mode")
                        $ra = Complete-D3 $a
                        $rb = Complete-D3 $b
                        $r = [pscustomobject]@{
                            ExitCode = "$($ra.ExitCode)/$($rb.ExitCode)"
                            Output = $ra.Output + "`n" + $rb.Output
                        }
                    }
                }
                $s = Summarize $r.Output
                $rows.Add([pscustomobject]@{
                    Op = $op; Run = $i; Scenario = $scenario; Exit = $r.ExitCode; Seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
                    Restores = $s.Restores; Done = $s.Done; Waited = $s.Waited; Exe = (VariantOk $op); Errors = $s.Errors
                })
                $rows[-1] | Format-Table -HideTableHeaders | Out-String -Width 250 | ForEach-Object { $_.Trim() } | Write-Host
            }
        }
    }
}
finally {
    $rows | Format-Table -AutoSize -Wrap | Out-String -Width 250 | Write-Host
    [void](New-Item -ItemType Directory -Force obj)
    $rows | Export-Csv -NoTypeInformation (Join-Path $PSScriptRoot "..\obj\parallel-$(Get-Date -Format yyyyMMdd-HHmmss).csv") -ErrorAction SilentlyContinue
    Write-Host 'Stopped (left by the builds):'
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
