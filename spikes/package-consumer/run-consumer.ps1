<#
.SYNOPSIS
  Packs the spike packages and exercises a package-only consumer in a fresh folder under the system temp folder.

.DESCRIPTION
  Every step runs for real and is timed. The consumer folder has no repository ancestor. NuGet's package folder,
  HTTP cache and the dotnet new template hive are isolated inside the temp area, so the user's global NuGet state
  and template store are never touched. MSBuild nodes get a per-run handshake salt; the script stops only the
  processes descended from the dotnet and MSBuild invocations it started (never VBCSCompiler).

.PARAMETER SkipPack
  Reuse the packages already in the feed.

.PARAMETER SlimRestore
  Sets DisableTransitiveFrameworkReferenceDownloads for every consumer build.

.PARAMETER Probe
  Also open the consumer through Roslyn MSBuildWorkspace (ide-probe.cs, copied into the temp area and run there).

.PARAMETER Keep
  Keep the temp area (for inspection) instead of deleting it.
#>
param(
    [switch] $SkipPack,
    [switch] $SlimRestore,
    [switch] $Probe,
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
$spike = $PSScriptRoot
$feed = [IO.Path]::GetFullPath((Join-Path $spike '../../artifacts/package-consumer/feed'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = Join-Path ([IO.Path]::GetTempPath()) "spar-pc-$stamp"
$apps = Join-Path $root 'apps'
$hive = Join-Path $root 'hive'
$started = [Collections.Generic.List[int]]::new()
$results = [ordered]@{}

# Packing gets its own salt, so the consumer's first build starts a fresh MSBuild server.
$env:MSBUILDNODEHANDSHAKESALT = "spar-pc-$stamp-pack"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Tool([string] $FileName, [string[]] $Arguments, [string] $WorkingDirectory, [switch] $AllowFailure, [switch] $Quiet)
{
    $info = [Diagnostics.ProcessStartInfo]::new($FileName)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.WorkingDirectory = $WorkingDirectory
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    $started.Add($process.Id)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $watch.Stop()
    $output = $stdout.Result + $stderr.Result
    $seconds = [math]::Round($watch.Elapsed.TotalSeconds, 2)
    Write-Host "> $FileName $($Arguments -join ' ')  [exit $($process.ExitCode), $seconds s]" -ForegroundColor Cyan
    if (-not $Quiet)
    {
        Write-Host ($output.TrimEnd() -split "`n" | Where-Object { $_ -match 'spike-task|error|warning|Restored|App host=|loopback|notes\.|frontend:|MSB30' } | Out-String).TrimEnd()
    }

    if ($process.ExitCode -ne 0 -and -not $AllowFailure) { Write-Host $output; throw "$FileName failed with exit code $($process.ExitCode)" }
    [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Seconds = $seconds }
}

function Invoke-Dotnet([string[]] $Arguments, [string] $WorkingDirectory, [switch] $AllowFailure, [switch] $Quiet)
{
    Invoke-Tool 'dotnet' $Arguments $WorkingDirectory -AllowFailure:$AllowFailure -Quiet:$Quiet
}

function Assert-Output($Result, [string] $Pattern, [string] $What)
{
    if ($Result.Output -notmatch $Pattern) { Write-Host $Result.Output; throw "Expected $What (pattern '$Pattern')." }
    Write-Host "  ok: $What" -ForegroundColor Green
}

function Set-SdkVersion([string] $Project, [string] $From, [string] $To)
{
    $text = [IO.File]::ReadAllText($Project).Replace("Spike.Sdk/$From", "Spike.Sdk/$To")
    [IO.File]::WriteAllText($Project, $text)
}

function Stop-StartedProcesses
{
    # Everything descended from a process this script started, except the shared compiler server.
    $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CreationDate
    $scriptStart = (Get-Process -Id $PID).StartTime
    $mine = [Collections.Generic.HashSet[int]]::new()
    foreach ($id in $started) { [void]$mine.Add($id) }
    do
    {
        $added = 0
        foreach ($process in $all)
        {
            if ($mine.Contains([int]$process.ParentProcessId) -and $process.CreationDate -ge $scriptStart -and $mine.Add([int]$process.ProcessId)) { $added++ }
        }
    } while ($added -gt 0)

    foreach ($process in $all | Where-Object { $mine.Contains([int]$_.ProcessId) -and $_.Name -ne 'VBCSCompiler.exe' })
    {
        Write-Host "  stopping $($process.Name) $($process.ProcessId)"
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

try
{
    # 1. Packages
    if (-not $SkipPack)
    {
        Remove-Item -Recurse -Force (Join-Path $feed 'Spike.*.nupkg') -ErrorAction SilentlyContinue
        $pack = Invoke-Dotnet @('pack', 'PackageConsumer.slnx', '-c', 'Release', '-p:Version=0.1.0') $spike
        $results['pack 0.1.0 (fresh MSBuild server), s'] = $pack.Seconds
        $pack = Invoke-Dotnet @('pack', 'PackageConsumer.slnx', '-c', 'Release', '-p:Version=0.1.1') $spike
        $results['pack 0.1.1 (warm), s'] = $pack.Seconds
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($package in Get-ChildItem $feed -Filter 'Spike.*.0.1.0.nupkg')
    {
        Write-Host "$($package.Name): $($package.Length) bytes"
        $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        $zip.Entries | Where-Object { $_.FullName -notmatch '^(_rels|package/|\[Content_Types\])' } |
            ForEach-Object { Write-Host ("  {0,10} {1}" -f $_.Length, $_.FullName) }
        $zip.Dispose()
        $results["$($package.Name) bytes"] = $package.Length
    }

    # 2. Isolated consumer area: no repository ancestor, own nuget.config, package folder, HTTP cache and template hive.
    New-Item -ItemType Directory -Force $root, $apps, $hive | Out-Null
    for ($dir = Get-Item $root; $dir; $dir = $dir.Parent)
    {
        $inherited = Get-ChildItem -Force $dir.FullName -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(\.git|Directory\.(Build|Packages)\.(props|targets)|global\.json|nuget\.config)$' }
        if ($inherited) { throw "The temp area inherits $($inherited.FullName -join ', ')." }
    }

    $env:MSBUILDNODEHANDSHAKESALT = "spar-pc-$stamp"
    $env:NUGET_PACKAGES = Join-Path $root 'packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $root 'http-cache'
    if ($SlimRestore) { $env:DisableTransitiveFrameworkReferenceDownloads = 'true' }
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spar-local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="spar-local">
      <package pattern="Spike.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content (Join-Path $apps 'nuget.config')
    Write-Host "consumer area: $root"

    # 3. Template, in an isolated hive only.
    $install = Invoke-Dotnet @('new', 'install', (Join-Path $feed 'Spike.Templates.0.1.0.nupkg'), '--debug:custom-hive', $hive) $apps
    $results['template install, s'] = $install.Seconds
    $new = Invoke-Dotnet @('new', 'spar-app', '-n', 'NotesApp', '--debug:custom-hive', $hive) $apps
    $results['dotnet new spar-app, s'] = $new.Seconds
    $app = Join-Path $apps 'NotesApp'
    $project = Join-Path $app 'NotesApp.csproj'
    foreach ($file in 'NotesApp.csproj', 'Program.cs', 'NotesService.cs', 'Properties/launchSettings.json', 'frontend/index.html')
    {
        if (-not (Test-Path (Join-Path $app $file))) { throw "Template did not create $file." }
    }

    # 4. Cold restore and build, then no-op builds.
    $restore = Invoke-Dotnet @('restore') $app
    $results['cold restore (empty package folder and HTTP cache), s'] = $restore.Seconds
    $build = Invoke-Dotnet @('build', '--no-restore') $app
    Assert-Output $build 'spike-task client 0\.1\.0: 1 contract' 'the client task from package 0.1.0'
    $results['first build (fresh MSBuild server), s'] = $build.Seconds
    $serverPid = [regex]::Match($build.Output, 'spike-task client .*? pid (\d+)').Groups[1].Value
    foreach ($i in 1..3)
    {
        $noop = Invoke-Dotnet @('build') $app
        if ($noop.Output -match 'spike-task') { throw 'A no-op build ran a Spar task.' }
        $results["no-op build $i, s"] = $noop.Seconds
    }

    $packages = Get-ChildItem $env:NUGET_PACKAGES -Directory | ForEach-Object {
        "{0} {1}: {2:N0} bytes" -f $_.Name, ((Get-ChildItem $_.FullName -Directory).Name -join ','),
            (Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum
    }
    Write-Host ($packages | Out-String)

    # 5. dotnet run with both launch profiles.
    $run = Invoke-Dotnet @('run') $app
    Assert-Output $run 'host=desktop runtime=0\.1\.0 generator=0\.1\.0' 'the Desktop profile by default'
    $run = Invoke-Dotnet @('run', '-lp', 'Loopback') $app
    Assert-Output $run 'loopback GET /index.html -> \d+ chars' 'a loopback request served from the embedded frontend'
    $results['dotnet run -lp Loopback (up to date), s'] = $run.Seconds

    # 6. Generated TypeScript client under obj/.
    $client = Join-Path $app 'obj/Debug/net11.0/win-x64/spar/client'
    $header = Get-Content (Join-Path $client 'notes.ts') -TotalCount 1
    if ($header -ne '// Generated by Spar 0.1.0. Do not edit.') { throw "Unexpected client header '$header'." }
    Write-Host "  ok: client files: $((Get-ChildItem $client -Recurse -File).FullName.Substring($client.Length + 1) -join ', ')" -ForegroundColor Green

    # 7. Generator diagnostic for an invalid contract, then back.
    $service = Join-Path $app 'NotesService.cs'
    $original = [IO.File]::ReadAllText($service)
    [IO.File]::WriteAllText($service, $original.Replace(
        "    [SparCommand]`n    public ValueTask<int> CountAsync()",
        "    [SparCommand]`n    public Task<int> BadAsync(Dictionary<string, int> map) => Task.FromResult(map.Count);`n`n    [SparCommand]`n    public ValueTask<int> CountAsync()"))
    $bad = Invoke-Dotnet @('build', '--no-restore') $app -AllowFailure
    if ($bad.ExitCode -eq 0) { throw 'The invalid contract built.' }
    Assert-Output $bad 'NotesService\.cs\(25,55\): error SPIKE003' 'SPIKE003 at file:line'
    [IO.File]::WriteAllText($service, $original)
    Invoke-Dotnet @('build', '--no-restore') $app | Out-Null

    # 8. Design-time compile, as an IDE runs it: no Spar task, the package's generator is passed to the compiler.
    $clientStamp = Join-Path $app 'obj/Debug/net11.0/win-x64/spar/client.stamp'
    $before = (Get-Item $clientStamp).LastWriteTimeUtc
    $designTime = @('msbuild', '-t:Compile', '-p:DesignTimeBuild=true', '-p:BuildingInsideVisualStudio=true',
        '-p:SkipCompilerExecution=true', '-p:ProvideCommandLineArgs=true', '-p:BuildProjectReferences=false',
        '-getItem:CscCommandLineArgs', '-getTargetResult:Compile')
    $dt = Invoke-Dotnet $designTime $app -Quiet
    Assert-Output $dt 'spike\.sdk\\\\0\.1\.0\\\\tools\\\\Spike\.Generator\.dll' 'the 0.1.0 generator as an analyzer at design time'
    if ((Get-Item $clientStamp).LastWriteTimeUtc -ne $before) { throw 'The design-time build ran the client step.' }
    $results['design-time compile (0.1.0), s'] = $dt.Seconds

    # 9. NativeAOT publish with warnings as errors.
    $publish = Invoke-Dotnet @('publish', '-c', 'Release') $app
    if ($publish.Output -match 'warning') { throw 'Publish raised warnings.' }
    $exe = Join-Path $app 'bin/Release/net11.0/win-x64/publish/NotesApp.exe'
    $results['publish NativeAOT, s'] = $publish.Seconds
    $results['NotesApp.exe bytes'] = (Get-Item $exe).Length
    $native = Invoke-Tool $exe @('--host', 'loopback') $app
    Assert-Output $native 'loopback GET /index.html' 'the AOT exe serving over loopback'

    # 10. Update to 0.1.1 while the MSBuild server is warm.
    Set-SdkVersion $project '0.1.0' '0.1.1'
    $update = Invoke-Dotnet @('build') $app
    Assert-Output $update 'spike-task client 0\.1\.1: .*spike\.sdk\\0\.1\.1\\tools' 'the 0.1.1 task loaded from its own package folder'
    if ($update.Output -match 'MSB3027|MSB3021|MSB3026') { throw 'A file lock was reported.' }
    $updatePid = [regex]::Match($update.Output, 'spike-task client .*? pid (\d+)').Groups[1].Value
    Write-Host "  task process before $serverPid, after update $updatePid (same process: $($serverPid -eq $updatePid))"
    $results['update build to 0.1.1 (warm), s'] = $update.Seconds
    $results['update ran in the same MSBuild process'] = $serverPid -eq $updatePid
    $run = Invoke-Dotnet @('run', '--no-build') $app
    Assert-Output $run 'runtime=0\.1\.1 generator=0\.1\.1' 'runtime and generator 0.1.1 in the app'
    $dt = Invoke-Dotnet $designTime $app -Quiet
    Assert-Output $dt 'spike\.sdk\\\\0\.1\.1\\\\tools\\\\Spike\.Generator\.dll' 'the 0.1.1 generator at design time'
    $results['design-time compile (0.1.1), s'] = $dt.Seconds

    # 11. Back to 0.1.0 while warm: both versions stay loadable side by side.
    Set-SdkVersion $project '0.1.1' '0.1.0'
    $down = Invoke-Dotnet @('build') $app
    Assert-Output $down 'spike-task client 0\.1\.0: .* 177 reused' 'the 0.1.0 task and its warm cache again'

    # 12. global.json msbuild-sdks instead of a versioned Sdk attribute.
    Invoke-Dotnet @('new', 'spar-app', '-n', 'GlobalJsonApp', '--debug:custom-hive', $hive) $apps | Out-Null
    $global = Join-Path $apps 'GlobalJsonApp'
    $globalProject = Join-Path $global 'GlobalJsonApp.csproj'
    [IO.File]::WriteAllText($globalProject, [IO.File]::ReadAllText($globalProject).Replace('Sdk="Spike.Sdk/0.1.0"', 'Sdk="Spike.Sdk"'))
    Set-Content (Join-Path $global 'global.json') '{ "msbuild-sdks": { "Spike.Sdk": "0.1.0" } }'
    $g = Invoke-Dotnet @('build') $global
    Assert-Output $g 'spike-task client 0\.1\.0' 'global.json resolving 0.1.0'
    Set-Content (Join-Path $global 'global.json') '{ "msbuild-sdks": { "Spike.Sdk": "0.1.1" } }'
    $g = Invoke-Dotnet @('build') $global
    Assert-Output $g 'spike-task client 0\.1\.1' 'a global.json edit switching to 0.1.1 while warm'

    # 13. Visual Studio's MSBuild.exe (.NET Framework): the Runtime="NET" task runs in the .NET task host.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vsMSBuild = if (Test-Path $vswhere) { & $vswhere -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
    if ($vsMSBuild)
    {
        (Get-Item $service).LastWriteTime = Get-Date
        $vs = Invoke-Tool $vsMSBuild @('-restore', '-nologo', '-nr:false', '-v:m') $app
        Assert-Output $vs 'spike-task client 0\.1\.0' 'the task under Visual Studio MSBuild'
        $results['VS MSBuild.exe build, s'] = $vs.Seconds
    }
    else
    {
        Write-Host '  skipped: Visual Studio MSBuild.exe not found' -ForegroundColor Yellow
    }

    # 14. Optional: the design-time path both IDEs use (Roslyn MSBuildWorkspace), outside the repository.
    if ($Probe)
    {
        $probeDir = Join-Path $root 'probe'
        New-Item -ItemType Directory -Force $probeDir | Out-Null
        Copy-Item (Join-Path $spike 'ide-probe.cs') $probeDir
        $ide = Invoke-Dotnet @('run', 'ide-probe.cs', '--', $project) $probeDir -Quiet
        Write-Host $ide.Output
        Assert-Output $ide 'client\.stamp touched by design-time load: False' 'no Spar task at design time'
        Assert-Output $ide 'binding has ping: True' 'an unsaved edit regenerating the binding'
        Assert-Output $ide 'generator: SPIKE003 at .*NotesService\.cs\(25,59\)' 'SPIKE003 from the packaged generator'
    }

    Write-Host ''
    $results.GetEnumerator() | ForEach-Object { Write-Host ("{0,-55} {1}" -f $_.Key, $_.Value) }
}
finally
{
    Stop-StartedProcesses
    if (-not $Keep -and (Test-Path $root))
    {
        Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
        Write-Host "removed $root (exists afterwards: $(Test-Path $root))"
    }
}
