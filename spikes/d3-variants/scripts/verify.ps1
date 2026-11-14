# Verifies the variant builds by running them. Starts from clean App/Tests outputs.
#   pwsh -File scripts/verify.ps1 [-Mode InProc|Exec]
param([ValidateSet('InProc', 'Exec')] [string] $Mode = 'InProc')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Isolate.ps1')
Enter-D3
Set-Location (Join-Path $PSScriptRoot '..')

$marker = 'D3MARKER-DESKTOPTEST'
$results = [System.Collections.Generic.List[object]]::new()

function Check([string] $name, [bool] $ok, [string] $detail = '') {
    $results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'pass' } else { 'FAIL' }); Detail = $detail })
}

function Run([string] $command) {
    $r = Invoke-D3 -Arguments ($command -split ' ') -Quiet
    Write-Host ("dotnet {0}  -> exit {1}, {2:N2} s" -f $command, $r.ExitCode, $r.Seconds)
    if ($r.ExitCode -ne 0) { Write-Host $r.Output }
    $r
}

# Files under App/ with their write times, excluding sources.
function Snapshot([string] $filter) {
    $map = @{}
    Get-ChildItem App -Recurse -File | Where-Object { $_.FullName -match $filter } | ForEach-Object {
        $map[$_.FullName.Substring((Get-Location).Path.Length + 1)] = $_.LastWriteTimeUtc.Ticks
    }
    $map
}

function Diff($before, $after) {
    $changed = @()
    foreach ($k in $after.Keys) { if (-not $before.ContainsKey($k) -or $before[$k] -ne $after[$k]) { $changed += $k } }
    foreach ($k in $before.Keys) { if (-not $after.ContainsKey($k)) { $changed += "-$k" } }
    $changed
}

function ContainsMarker([string] $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $utf16 = [Text.Encoding]::Unicode.GetBytes($marker)
    $utf8 = [Text.Encoding]::UTF8.GetBytes($marker)
    ([Text.Encoding]::Latin1.GetString($bytes).Contains([Text.Encoding]::Latin1.GetString($utf16))) -or
    ([Text.Encoding]::Latin1.GetString($bytes).Contains([Text.Encoding]::Latin1.GetString($utf8)))
}

try {
    Remove-Item -Recurse -Force App\bin, App\obj, Tests\bin, Tests\obj, Tests2\bin, Tests2\obj -ErrorAction SilentlyContinue

    # 1. The normal build.
    $r = Run 'build App -nologo'
    Check 'Production build' ($r.ExitCode -eq 0)
    $production = Snapshot '\\(bin|obj)\\Production\\'

    # 2. Test build that builds the DesktopTest variant (Build), then (Publish).
    $r = Run "build Tests -nologo -p:D3VariantMode=$Mode -p:D3VariantOperation=Build"
    Check "Test build + variant Build ($Mode)" ($r.ExitCode -eq 0)
    $r = Run "build Tests -nologo -p:D3VariantMode=$Mode -p:D3VariantOperation=Publish"
    Check "Test build + variant AOT Publish ($Mode)" ($r.ExitCode -eq 0)

    $all = Get-ChildItem App -Recurse -File | ForEach-Object { $_.FullName.Substring((Get-Location).Path.Length + 1) }
    $stray = @($all | Where-Object { $_ -notmatch '^App\\[^\\]+\.(cs|csproj)$' -and $_ -notmatch '^App\\(bin|obj)\\(Production|DesktopTest)\\' })
    Check 'Outputs only in obj/<variant>, bin/<variant>' ($stray.Count -eq 0) ($stray -join ', ')
    $changed = @(Diff $production (Snapshot '\\(bin|obj)\\Production\\'))
    Check 'Production outputs untouched by variant builds' ($changed.Count -eq 0) ($changed -join ', ')

    # 3. Marker scan and runs.
    $variantBuild = 'App\bin\DesktopTest\Debug\net11.0\App.dll'
    $variantExe = 'App\bin\DesktopTest\Release\net11.0\win-x64\publish\App.exe'
    $prodDll = 'App\bin\Production\Debug\net11.0\App.dll'
    Check 'Variant Build dll contains marker' (ContainsMarker $variantBuild)
    Check 'Variant AOT exe contains marker' (ContainsMarker $variantExe) ("{0:N0} bytes" -f (Get-Item $variantExe).Length)
    Check 'Production dll lacks marker' (-not (ContainsMarker $prodDll))
    $out = & $variantExe | Out-String
    Check 'Variant AOT exe runs variant code' ($out.Contains('variant: DesktopTest') -and $out.Contains($marker)) ($out.Trim() -replace "`r?`n", ' | ')
    $out = & 'App\bin\Production\Debug\net11.0\App.exe' | Out-String
    Check 'Production exe runs without variant code' ($out.Contains('variant: Production') -and -not $out.Contains($marker)) ($out.Trim() -replace "`r?`n", ' | ')

    # 4. Production publish for comparison (normal dotnet publish, own folder).
    $r = Run 'publish App -nologo -p:RuntimeIdentifier=win-x64'
    $prodExe = 'App\bin\Production\Release\net11.0\win-x64\publish\App.exe'
    Check 'Production AOT exe lacks marker' (($r.ExitCode -eq 0) -and -not (ContainsMarker $prodExe)) ("{0:N0} bytes" -f (Get-Item $prodExe).Length)
    $production = Snapshot '\\(bin|obj)\\Production\\'

    # 5. Normal build afterwards is a no-op.
    $r = Run 'build App -nologo -v:d'
    $compiled = $r.Output -match 'Building target "CoreCompile" completely|Building target "CoreCompile" partially'
    $changed = @(Diff $production (Snapshot '\\(bin|obj)\\Production\\'))
    Check 'Normal build afterwards: CoreCompile skipped' (($r.ExitCode -eq 0) -and -not $compiled)
    Check 'Normal build afterwards: no Production file rewritten' ($changed.Count -eq 0) ($changed -join ', ')

    # 6. Incremental second test build skips the variant's work.
    $variantFiles = Snapshot '\\(bin|obj)\\DesktopTest\\'
    foreach ($op in 'Build', 'Publish') {
        $r = Run "build Tests -nologo -v:d -p:D3VariantMode=$Mode -p:D3VariantOperation=$op"
        $ran = @([regex]::Matches($r.Output, 'Building target "(CoreCompile|IlcCompile|LinkNative|_CopyFilesMarkedCopyLocal|CopyFilesToOutputDirectory)" (completely|partially)') | ForEach-Object { $_.Groups[1].Value })
        Check "Second test build ($op): CoreCompile/IlcCompile/LinkNative skipped" (($r.ExitCode -eq 0) -and $ran.Count -eq 0) ($ran -join ', ')
    }
    $changed = @(Diff $variantFiles (Snapshot '\\(bin|obj)\\DesktopTest\\'))
    Check 'Second test builds: no variant file rewritten' ($changed.Count -eq 0) ($changed -join ', ')

    # 7. Design-time builds.
    $before = Snapshot '\\(bin|obj)\\'
    $r = Run 'build App -nologo -t:Compile -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true'
    Check 'Design-time Compile of App succeeds' ($r.ExitCode -eq 0)
    $r = Invoke-D3 -Arguments @('msbuild', 'App', '-nologo', '-getItem:Compile', '-p:DesignTimeBuild=true') -Quiet
    $items = ($r.Output | ConvertFrom-Json).Items.Compile | ForEach-Object { $_.Identity }
    Check 'Design-time Compile items: sources only, no obj files' (@($items | Where-Object { $_ -match '\\obj\\' }).Count -eq 0) ($items -join ', ')
    $r = Run "build Tests -nologo -t:Compile -p:DesignTimeBuild=true -p:D3VariantMode=$Mode -v:d"
    Check 'Design-time Compile of Tests does not build the variant' (($r.ExitCode -eq 0) -and -not ($r.Output -match 'D3: (in-proc|exec)'))
    $changed = @(Diff $before (Snapshot '\\(bin|obj)\\') | Where-Object { $_ -notmatch '\\obj\\Production\\' })
    Check 'Design-time builds touch no variant or bin file' ($changed.Count -eq 0) ($changed -join ', ')

    # 8. The test project's own tests run the variant.
    $r = Run "test --project Tests -p:D3VariantMode=$Mode -p:D3VariantOperation=Publish"
    Check 'dotnet test passes' ($r.ExitCode -eq 0)
}
finally {
    $results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    Write-Host 'Stopped (left by the builds):'
    Stop-D3 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}
