#requires -Version 7.4
param(
  [string]$EvidenceDirectory = 'artifacts/compatibility/acceptance',
  [switch]$VerifyRender
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$fixture = Join-Path $evidence ('fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
# Copy source files only. No shared outputs, junctions, generated clients, or dependency caches.
$paths = @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'build', 'eng/Build-AssetBundle.ps1',
  'eng/Write-PublishReceipt.ps1', 'eng/Test-InspectionBoundary.ps1', 'eng/Test-ProductionBoundary.ps1',
  'eng/compatibility-probe', 'src', 'fixtures/Spar.NativeSample', 'fixtures/Spar.NativeSample.Contracts',
  'tools/Spar.ContractGenerator', 'tools/Spar.Dev/frontend')
$files = @(git -C $repository ls-files --cached --others --exclude-standard -- @paths)
if ($LASTEXITCODE) { throw 'Cannot enumerate fixture source.' }
foreach ($file in $files) {
  $destination = Join-Path $fixture $file
  New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
  Copy-Item -LiteralPath (Join-Path $repository $file) -Destination $destination
}
$project = Join-Path $fixture 'fixtures/Spar.NativeSample/Spar.NativeSample.csproj'
$backend = Join-Path $fixture 'fixtures/Spar.NativeSample/SampleBackend.cs'
$originalProject = [IO.File]::ReadAllText($project)
$originalBackend = [IO.File]::ReadAllText($backend)
$selection = @'
  <PropertyGroup>
    <SparDeliveryMode>CoreCLR</SparDeliveryMode>
    <SparCompatibilityDependency>Spar.DynamicDependency (repository fixture)</SparCompatibilityDependency>
    <SparCompatibilityReason>IL3050: RuntimeGreeting.Create emits and executes DynamicMethod IL.</SparCompatibilityReason>
  </PropertyGroup>

'@
$selectedProject = $originalProject.Replace('  <Import Project="../../build/Spar.Delivery.targets" />',
  $selection + '  <Import Project="../../build/Spar.Delivery.targets" />')
$dependentProject = $selectedProject.Replace('</Project>', @'
  <ItemGroup>
    <ProjectReference Include="../../eng/compatibility-probe/Spar.DynamicDependency.csproj"
                      GlobalPropertiesToRemove="RuntimeIdentifier;PublishAot;SelfContained" />
  </ItemGroup>
</Project>
'@)
$dependentBackend = $originalBackend.Replace('"Hello, " + request.Name', 'CreateRuntimeGreeting(request.Name)')
$dependentBackend = $dependentBackend.Replace('    public void NavigationDenied()', @'
    private string CreateRuntimeGreeting(string name)
    {
        var greeting = Spar.DynamicDependency.RuntimeGreeting.Create(name);
        log("runtime-generated-greeting:" + greeting);
        return greeting;
    }

    public void NavigationDenied()
'@)
[IO.File]::WriteAllText($project, $dependentProject)
[IO.File]::WriteAllText($backend, $dependentBackend)
$files | ForEach-Object {
  (Get-FileHash -LiteralPath (Join-Path $fixture $_)).Hash + '  ' + $_
} | Set-Content -LiteralPath (Join-Path $evidence 'fixture.sha256')
Copy-Item -LiteralPath $project -Destination (Join-Path $evidence 'fixture.csproj')
Copy-Item -LiteralPath $backend -Destination (Join-Path $evidence 'fixture-backend.cs')

$results = [Collections.Generic.List[object]]::new()
$allPassed = $false
function Invoke-Case([string]$Name, [string[]]$Arguments, [bool]$Success, [string]$Diagnostic = '') {
  $log = Join-Path $evidence "$Name.log"
  $watch = [Diagnostics.Stopwatch]::StartNew()
  & dotnet @Arguments *> $log
  $code = $LASTEXITCODE
  $text = Get-Content -LiteralPath $log -Raw
  $passed = (($code -eq 0) -eq $Success) -and (!$Diagnostic -or $text -match $Diagnostic)
  $results.Add(@{ name = $Name; arguments = $Arguments; exitCode = $code; expectedSuccess = $Success
    expectedDiagnostic = $Diagnostic; passed = $passed; elapsedMs = $watch.Elapsed.TotalMilliseconds; log = $log })
  if (!$passed) { throw "$Name failed expectation. See $log" }
  Write-Host "Passed $Name (exit $code)"
}
function Publish-Case([string]$Name, [bool]$Success, [string]$Diagnostic = '', [string[]]$Properties = @()) {
  $output = Join-Path $evidence "publish-$Name"
  if (Test-Path -LiteralPath $output) { throw "Use a fresh evidence directory; output already exists: $output" }
  Invoke-Case $Name (@('publish', $project, '-c', 'Release', '-o', $output, '--disable-build-servers') + $Properties) $Success $Diagnostic
  if (!$Success -and (Test-Path -LiteralPath (Join-Path $output 'spar-publish.json'))) { throw "$Name produced a success receipt." }
  return $output
}
try {
  Invoke-Case 'development-diagnostic' @('build', $project, '-c', 'Debug', '--disable-build-servers') $true 'warning IL3050.*RuntimeGreeting.Create'
  $null = Publish-Case 'strict-dependency' $false 'error IL3050.*RuntimeGreeting.Create' @('-p:SparStrictAot=true')
  $coreclr = Publish-Case 'coreclr' $true 'warning IL3050.*RuntimeGreeting.Create'
  $receipt = Get-Content -LiteralPath (Join-Path $coreclr 'spar-publish.json') -Raw | ConvertFrom-Json
  if ($receipt.mode -ne 'CoreCLR' -or $receipt.strictAot -or !$receipt.compatibility.dependency -or !$receipt.compatibility.reason) {
    throw 'Compatibility receipt did not retain selection and attribution.'
  }
  & (Join-Path $PSScriptRoot 'Test-Production.ps1') -PublishDirectory $coreclr -ExpectedMode CoreCLR -Samples 1 `
    -EvidenceDirectory (Join-Path $evidence 'coreclr-run') -VerifyRender:$VerifyRender
  if ($LASTEXITCODE) { throw 'CoreCLR production execution failed.' }
  $events = Get-Content -LiteralPath (Join-Path $evidence 'coreclr-run/run-1.jsonl') | ConvertFrom-Json
  if (!($events | Where-Object Stage -eq 'runtime-generated-greeting:Hello from generated code, Spar')) {
    throw 'The actual unsupported dependency path was not executed by the typed greeting.'
  }
  Push-Location $fixture
  try {
    & pwsh -NoProfile -File eng/Test-ProductionBoundary.ps1 *> (Join-Path $evidence 'coreclr-boundary.log')
    if ($LASTEXITCODE) { throw 'CoreCLR production boundary failed.' }
  } finally { Pop-Location }

  # A persisted compatibility choice does not repair a syntax error.
  [IO.File]::WriteAllText($backend, $dependentBackend + "`nthis is not valid C#;`n")
  try { $null = Publish-Case 'syntax-error' $false 'error CS' } finally { [IO.File]::WriteAllText($backend, $dependentBackend) }

  $null = Publish-Case 'conflicting-aot' $false 'SPAR1004' @('-p:SparStrictAot=true', '-p:PublishAot=false')
  $null = Publish-Case 'missing-attribution' $false 'SPAR1003' @('-p:SparCompatibilityReason=')
  $null = Publish-Case 'invalid-mode' $false 'SPAR1001' @('-p:SparDeliveryMode=Automatic')

  # Remove the dependency, its call and selection. No incompatibility cache survives this edit.
  [IO.File]::WriteAllText($backend, $originalBackend)
  [IO.File]::WriteAllText($project, $originalProject)
  $aot = Publish-Case 'dependency-removed' $true
  $receipt = Get-Content -LiteralPath (Join-Path $aot 'spar-publish.json') -Raw | ConvertFrom-Json
  if ($receipt.mode -ne 'NativeAOT' -or $receipt.requestedMode -ne 'NativeAOT') { throw 'Dependency removal did not restore default AOT.' }
  & (Join-Path $PSScriptRoot 'Test-Production.ps1') -PublishDirectory $aot -Samples 1 `
    -EvidenceDirectory (Join-Path $evidence 'aot-run') -VerifyRender:$VerifyRender
  if ($LASTEXITCODE) { throw 'Reevaluated AOT production execution failed.' }

  # Strict CI still selects AOT when the persisted selection is CoreCLR.
  [IO.File]::WriteAllText($project, $selectedProject)
  $strict = Publish-Case 'strict-override' $true '' @('-p:SparStrictAot=true')
  $receipt = Get-Content -LiteralPath (Join-Path $strict 'spar-publish.json') -Raw | ConvertFrom-Json
  if ($receipt.mode -ne 'NativeAOT' -or $receipt.requestedMode -ne 'CoreCLR' -or !$receipt.strictAot) { throw 'Strict override was not recorded.' }

  # A real framework compile-time AOT regression must fail even in compatibility mode.
  $framework = Join-Path $fixture 'src/Sotsera.Spar.Windows/SparAssetBundle.cs'
  $frameworkSource = [IO.File]::ReadAllText($framework)
  $brokenFramework = $frameworkSource.Replace('        ArgumentNullException.ThrowIfNull(stream);',
    '        _ = new System.Reflection.Emit.DynamicMethod("FrameworkRegression", typeof(void), Type.EmptyTypes);' + "`n        ArgumentNullException.ThrowIfNull(stream);")
  [IO.File]::WriteAllText($framework, $brokenFramework)
  try {
    $null = Publish-Case 'framework-regression-coreclr' $false 'SparAssetBundle.cs.*error IL3050'
    $null = Publish-Case 'framework-regression-aot' $false 'SparAssetBundle.cs.*error IL3050' @('-p:SparStrictAot=true')
  } finally { [IO.File]::WriteAllText($framework, $frameworkSource) }

  # With compatible source, fail actual native tool invocation and actual native linking.
  [IO.File]::WriteAllText($project, $originalProject)
  $missingTool = Join-Path $fixture 'absent-native-compiler'
  $null = Publish-Case 'missing-toolchain' $false 'error MSB3073.*absent-native-compiler' @("-p:IlcToolsPath=$missingTool/", '-t:Rebuild,Publish')
  $linkProject = $originalProject.Replace('</Project>', '<ItemGroup><LinkerArg Include="/INCLUDE:SparDeliberatelyMissingNativeSymbol" /></ItemGroup></Project>')
  [IO.File]::WriteAllText($project, $linkProject)
  try { $null = Publish-Case 'native-link-error' $false 'LNK2001|LNK1120' @('-t:Rebuild,Publish') }
  finally { [IO.File]::WriteAllText($project, $originalProject) }
  $allPassed = $true
} finally {
  @{ allPassed = $allPassed; observedAt = [DateTimeOffset]::UtcNow; sourceRevision = (git -C $repository rev-parse HEAD)
    dotnet = (dotnet --version); powershell = $PSVersionTable.PSVersion.ToString(); fixture = $fixture
    renderRequested = [bool]$VerifyRender; cases = $results.ToArray()
  } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'receipt.json')
}
Write-Output "Compatibility acceptance passed. Evidence: $evidence"
exit 0
