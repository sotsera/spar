param(
    [string] $CodeCommand = 'code',
    [ValidateSet('net10.0', 'net11.0')]
    [string] $TargetFramework = 'net10.0'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $repository ('artifacts/vscode-probe/' + [guid]::NewGuid().ToString('N'))
$fixtureDirectory = Join-Path $runDirectory 'fixture'
$profileDirectory = Join-Path $runDirectory 'profile'
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null

Get-ChildItem -LiteralPath (Join-Path $repository 'experiments/Spar.HotReloadProbe') -File |
    Where-Object { $_.Extension -in @('.cs', '.csproj') } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $fixtureDirectory }

$projectPath = Join-Path $fixtureDirectory 'Spar.HotReloadProbe.csproj'
$projectSource = Get-Content -LiteralPath $projectPath -Raw
$projectSource.Replace('<TargetFramework>net10.0</TargetFramework>', ('<TargetFramework>' + $TargetFramework + '</TargetFramework>')) |
    Set-Content -LiteralPath $projectPath
$sdkVersion = if ($TargetFramework -eq 'net10.0') { '10.0.402' } else { '11.0.100-rc.2.26504.105' }
@{ sdk = @{ version = $sdkVersion; rollForward = 'disable'; allowPrerelease = ($TargetFramework -eq 'net11.0') } } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runDirectory 'global.json')
$sourceHashes = Get-ChildItem -LiteralPath $fixtureDirectory -File |
    Sort-Object Name |
    ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name }
$sourceHashes | Set-Content -LiteralPath (Join-Path $runDirectory 'sources.sha256')

New-Item -ItemType Directory -Path (Join-Path $profileDirectory 'User') -Force | Out-Null
# C# Dev Kit declares these settings at machine scope; workspace values are ignored.
Set-Content -LiteralPath (Join-Path $profileDirectory 'User/settings.json') -Value @'
{
  "csharp.experimental.debug.hotReload": true,
  "csharp.debug.hotReloadOnSave": true,
  "csharp.debug.hotReloadVerbosity": "diagnostic"
}
'@
Set-Content -LiteralPath (Join-Path $fixtureDirectory 'Probe.slnx') -Value '<Solution><Project Path="Spar.HotReloadProbe.csproj" /></Solution>'

Push-Location $fixtureDirectory
try { dotnet build Spar.HotReloadProbe.csproj --disable-build-servers }
finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw 'Debugger fixture build failed.' }

$extensionDirectory = Join-Path $env:USERPROFILE '.vscode/extensions'
Write-Output ('VS Code evidence directory: ' + $fixtureDirectory)
& $CodeCommand --new-window --wait --skip-welcome --skip-release-notes --disable-workspace-trust `
    --user-data-dir $profileDirectory --extensions-dir $extensionDirectory `
    --extensionDevelopmentPath (Join-Path $PSScriptRoot 'vscode-probe') `
    --extensionTestsPath (Join-Path $PSScriptRoot 'vscode-probe/test.cjs') $fixtureDirectory

$editorExitCode = $LASTEXITCODE
$receiptPath = Join-Path $fixtureDirectory 'receipt.json'
if (-not (Test-Path -LiteralPath $receiptPath)) { throw ('VS Code produced no acceptance receipt; inspect ' + $runDirectory) }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$receipt | Add-Member -NotePropertyName sdk -NotePropertyValue $sdkVersion
$receipt | Add-Member -NotePropertyName targetFramework -NotePropertyValue $TargetFramework
$processEvents = Get-Content -LiteralPath (Join-Path $fixtureDirectory 'debugger.ndjson') -ErrorAction SilentlyContinue |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.event -eq 'process' }
$exitChecks = @()
foreach ($event in $processEvents) {
    $expectedPath = [IO.Path]::GetFullPath($event.body.name)
    if (-not $expectedPath.StartsWith($fixtureDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Debugger reported an application outside the owned fixture.'
    }

    $application = Get-Process -Id $event.body.systemProcessId -ErrorAction SilentlyContinue
    $outcome = 'exited'
    if ($null -ne $application) {
        try {
            if (-not [string]::Equals($application.Path, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
                $outcome = 'original exited; PID reused'
            }
            elseif (-not $application.WaitForExit(10000)) {
                $application.Kill($true)
                if (-not $application.WaitForExit(10000)) { throw 'Owned fixture process did not exit after forced cleanup.' }
                $outcome = 'forced cleanup after debugger Stop failed'
                $receipt.outcome = 'failed'
            }
        }
        finally { $application.Dispose() }
    }

    $exitChecks += @{ processId = $event.body.systemProcessId; outcome = $outcome }
}
$receipt | Add-Member -NotePropertyName processExitChecks -NotePropertyValue $exitChecks
$receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $receiptPath
if ($editorExitCode -ne 0) { throw ('VS Code debugger probe failed; inspect ' + $runDirectory) }
if ($receipt.outcome -ne 'passed') { throw ('VS Code debugger acceptance did not pass; inspect ' + $receiptPath) }
$receipt | ConvertTo-Json -Depth 6
