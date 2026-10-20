#requires -Version 7.4
param(
  [string]$Feed = 'artifacts/packages',
  [string]$EvidenceDirectory = 'artifacts/external-consumer/acceptance',
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$externalEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $externalEvidence -Force | Out-Null
$consumer = Join-Path ([IO.Path]::GetTempPath()) ('spar-consumer-' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'New-PackageConsumer.ps1') -Directory $consumer -Feed $Feed
$consumer | Set-Content -LiteralPath (Join-Path $externalEvidence 'consumer-path.txt')
$project = Join-Path $consumer 'backend/Spar.NativeSample.csproj'
$backend = Join-Path $consumer 'backend/SampleBackend.cs'
$frontend = Join-Path $consumer 'backend/frontend'
$externalResults = [Collections.Generic.List[object]]::new()
$allPassed = $false
function Invoke-ExternalBuild([string]$Name, [string[]]$Arguments, [bool]$ExpectedSuccess = $true, [string]$Diagnostic = '') {
  $log = Join-Path $externalEvidence "$Name.log"
  Push-Location $consumer
  try { & dotnet @Arguments *> $log; $code = $LASTEXITCODE } finally { Pop-Location }
  $text = Get-Content -LiteralPath $log -Raw
  $passed = (($code -eq 0) -eq $ExpectedSuccess) -and (!$Diagnostic -or $text -match $Diagnostic)
  $externalResults.Add(@{ name = $Name; arguments = $Arguments; exitCode = $code; expectedSuccess = $ExpectedSuccess; passed = $passed })
  if (!$passed) { throw "$Name failed expectation: $log" }
}
try {
  # Build the application directly on the first restore: the solution must not mask a missing contract dependency.
  Invoke-ExternalBuild 'fresh-build' @('build', $project, '--disable-build-servers')
  foreach ($assetsPath in @('backend/obj/project.assets.json', 'contracts/obj/project.assets.json')) {
    $assets = Get-Content -LiteralPath (Join-Path $consumer $assetsPath) -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
      if ($library.Value.type -eq 'project' -and $library.Name -notlike 'Spar.NativeSample.Contracts/*') {
        throw "Unexpected source-project dependency: $($library.Name)"
      }
    }
    if ((Get-Content -LiteralPath (Join-Path $consumer $assetsPath) -Raw).Contains($sourceRoot.Replace('\', '\\'))) {
      throw 'Consumer restore references the framework checkout.'
    }
  }
  Push-Location $frontend
  try { npm ci --ignore-scripts --include=optional *> (Join-Path $externalEvidence 'npm.log'); if ($LASTEXITCODE) { throw 'Frontend restore failed.' } }
  finally { Pop-Location }
  $externalCoordinator = Join-Path $consumer '.tools/Spar.Dev.exe'
  $externalApplication = Join-Path $consumer 'backend/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
  . (Join-Path $PSScriptRoot 'Test-DevelopmentSession.ps1') -Coordinator $externalCoordinator -Application $externalApplication `
    -EvidenceDirectory (Join-Path $externalEvidence 'development') -VerifyRender:$VerifyRender -LibraryOnly
  $settings = Get-Content -LiteralPath (Join-Path $consumer 'spar.dev.json') -Raw | ConvertFrom-Json -AsHashtable
  $settings.sessionDirectory = Join-Path $externalEvidence 'development/sessions'
  $fixture = [pscustomobject]@{ Name = 'external-package'; Directory = (Join-Path $externalEvidence 'development');
    Frontend = $frontend; Settings = $settings; Config = (Join-Path $consumer 'spar.dev.json') }
  $unrelatedStart = [Diagnostics.ProcessStartInfo]::new('node')
  $unrelatedStart.UseShellExecute = $false
  $unrelatedStart.CreateNoWindow = $true
  $unrelatedStart.ArgumentList.Add('-e')
  $unrelatedStart.ArgumentList.Add('setInterval(() => {}, 1000)')
  $unrelated = [Diagnostics.Process]::Start($unrelatedStart)
  $null = $unrelated.Handle
  $session = Start-Session $fixture $true
  $originalBackend = [IO.File]::ReadAllText($backend)
  $css = Join-Path $frontend 'src/styles.css'
  $originalCss = [IO.File]::ReadAllText($css)
  try {
    Wait-Until $session { $null -ne (Latest-Snapshot $session) } 'external package typed readiness' 120
    $initial = Latest-Snapshot $session
    Send-Command $session 'probe' 'seed'
    Wait-Until $session { (Latest-Snapshot $session).counter -eq 2 -and (Latest-Snapshot $session).local -eq 7 } 'external state seed'
    [IO.File]::WriteAllText($css, $originalCss.Replace('#70e3bd', '#e9b44c'))
    Wait-Until $session { (Latest-Snapshot $session).color -eq 'rgb(233, 180, 76)' } 'external frontend HMR' 60
    [IO.File]::WriteAllText($backend, $originalBackend.Replace('"Hello, "', '"Hello from packages, "'))
    Wait-Until $session { (Latest-Snapshot $session).greeting -eq 'Hello from packages, Spar' } 'external backend Hot Reload' 120
    $patched = Latest-Snapshot $session
    Assert-Condition ($patched.document -eq $initial.document -and $patched.counter -eq 2 -and $patched.local -eq 7) 'External patch lost document or state.'
    $patched | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $externalEvidence 'patched-snapshot.json')
    if ($VerifyRender) {
      $window = [IntPtr]($session.Events | Where-Object kind -eq 'window-created' | Select-Object -Last 1).window
      Wait-Until $session { [NativeProbeDesktop]::CaptureColor($window, (Join-Path $externalEvidence 'patched.bmp'), 1000, 233, 180, 76) -gt 3000 } 'external rendered patch'
    }
    Send-Command $session 'restart'
    Wait-Until $session { $null -ne (Latest-Snapshot $session 2) } 'external coordinated rebuild/restart' 120
    Assert-Condition ((Latest-Snapshot $session 2).counter -eq 0) 'External restart did not reset backend state.'
    Send-Command $session 'stop'
    Confirm-Exit $session
  } finally {
    if (!$session.Process.HasExited) {
      Send-Command $session 'stop' '' $true
      Confirm-Exit $session
    }
    foreach ($handle in $session.Handles.Values) { $handle.Dispose() }
    $session.Process.Dispose()
    if (!$unrelated.HasExited) { $unrelated.Kill(); $unrelated.WaitForExit() }
    $unrelated.Dispose()
    [IO.File]::WriteAllText($backend, $originalBackend)
    [IO.File]::WriteAllText($css, $originalCss)
  }
  $results.ToArray() | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $externalEvidence 'development.json')

  $aot = Join-Path $consumer 'artifacts/aot'
  Invoke-ExternalBuild 'publish-aot' @('publish', $project, '-c', 'Release', '-o', $aot, '--disable-build-servers')
  & (Join-Path $PSScriptRoot 'Test-ProductionBoundary.ps1') `
    -Assembly (Join-Path $consumer 'backend/bin/Release/net10.0-windows/win-x64/Spar.NativeSample.dll') `
    -HostAssembly (Join-Path $consumer 'backend/bin/Release/net10.0-windows/win-x64/Sotsera.Spar.Windows.dll') *> (Join-Path $externalEvidence 'production-boundary.log')
  & (Join-Path $PSScriptRoot 'Test-Production.ps1') -PublishDirectory $aot -Samples 1 -VerifyRender:$VerifyRender `
    -EvidenceDirectory (Join-Path $externalEvidence 'aot-run')
  if ($LASTEXITCODE) { throw 'Copied external AOT application failed.' }

  # Add an application-owned dependency and persist the public compatibility configuration.
  $dependency = Join-Path $consumer 'dependency'
  New-Item -ItemType Directory -Path $dependency | Out-Null
  Copy-Item -Path (Join-Path $sourceRoot 'eng/compatibility-probe/*') -Destination $dependency -Exclude bin,obj
  $projectText = [IO.File]::ReadAllText($project)
  $compatibility = @'
  <PropertyGroup>
    <SparDeliveryMode>CoreCLR</SparDeliveryMode>
    <SparCompatibilityDependency>Spar.DynamicDependency (consumer fixture)</SparCompatibilityDependency>
    <SparCompatibilityReason>IL3050: RuntimeGreeting emits and executes DynamicMethod IL.</SparCompatibilityReason>
  </PropertyGroup>
'@
  $projectText = $projectText.Replace('  <PropertyGroup>', $compatibility + "`n  <PropertyGroup>")
  $projectText = $projectText.Replace('</Project>', '<ItemGroup><ProjectReference Include="../dependency/Spar.DynamicDependency.csproj" /></ItemGroup></Project>')
  [IO.File]::WriteAllText($project, $projectText)
  [IO.File]::WriteAllText($backend, $originalBackend.Replace('"Hello, " + request.Name', 'Spar.DynamicDependency.RuntimeGreeting.Create(request.Name)'))
  $strict = Join-Path $consumer 'artifacts/strict'
  Invoke-ExternalBuild 'strict-dependency' @('publish', $project, '-c', 'Release', '-p:SparStrictAot=true', '-o', $strict, '--disable-build-servers') $false 'error IL3050.*RuntimeGreeting.Create'
  if (Test-Path -LiteralPath (Join-Path $strict 'spar-publish.json')) { throw 'Failed strict publication wrote a success receipt.' }
  $coreclr = Join-Path $consumer 'artifacts/coreclr'
  Invoke-ExternalBuild 'publish-coreclr' @('publish', $project, '-c', 'Release', '-o', $coreclr, '--disable-build-servers')
  & (Join-Path $PSScriptRoot 'Test-Production.ps1') -PublishDirectory $coreclr -ExpectedMode CoreCLR -Samples 1 `
    -VerifyRender:$VerifyRender -EvidenceDirectory (Join-Path $externalEvidence 'coreclr-run')
  if ($LASTEXITCODE) { throw 'Copied external compatibility application failed.' }
  foreach ($mode in @('aot', 'coreclr')) {
    Copy-Item -LiteralPath (Join-Path $consumer "artifacts/$mode/spar-publish.json") -Destination (Join-Path $externalEvidence "$mode-publication.json")
  }
  $allPassed = $true
} finally {
  @{ allPassed = $allPassed; observedAt = [DateTimeOffset]::UtcNow; consumer = $consumer; cases = $externalResults.ToArray()
    sourceRevision = (git -C $sourceRoot rev-parse HEAD); rendering = [bool]$VerifyRender
    packages = @(Get-ChildItem -LiteralPath (Join-Path $consumer '.feed') -Filter '*.nupkg' | ForEach-Object {
      @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    })
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $externalEvidence 'receipt.json')
}
Write-Output "External package acceptance passed: $consumer"
exit 0
