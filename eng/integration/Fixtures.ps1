#requires -Version 7.4
# Importing this file only defines helpers. Callers choose and retain every fixture.

function New-Fixture {
  param(
    [Parameter(Mandatory)][string]$Name,
    [string]$FrontendDirectory = 'frontend',
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$EvidenceDirectory
  )
  $directory = Join-Path $EvidenceDirectory ($Name + '-' + [guid]::NewGuid().ToString('N'))
  $frontend = Join-Path $directory $FrontendDirectory
  New-Item -ItemType Directory -Path $frontend -Force | Out-Null
  $original = Join-Path $repository 'samples/Spar.NativeSample/frontend'
  foreach ($file in @('package.json','package-lock.json','tsconfig.json','index.html')) {
    Copy-Item -LiteralPath (Join-Path $original $file) -Destination $frontend
  }
  Copy-Item -LiteralPath (Join-Path $original 'src') -Destination $frontend -Recurse
  New-Item -ItemType Junction -Path (Join-Path $frontend 'node_modules') -Target (Join-Path $original 'node_modules') | Out-Null
  Copy-Item -LiteralPath (Join-Path $repository 'tools/Spar.Dev/frontend/vite-session.mjs') -Destination $frontend
  $config = [IO.File]::ReadAllText((Join-Path $original 'vite.config.mjs')).Replace('../../../tools/Spar.Dev/frontend/vite-session.mjs', './vite-session.mjs')
  [IO.File]::WriteAllText((Join-Path $frontend 'vite.config.mjs'), $config)
  $settings = [ordered]@{
    project = Join-Path $repository 'samples/Spar.NativeSample/Spar.NativeSample.csproj'
    application = $Application
    generatedClient = Join-Path $frontend 'src/generated/client.js'
    sessionDirectory = Join-Path $directory 'sessions'
    startupTimeoutSeconds = 30; shutdownTimeoutSeconds = 3
    frontend = [ordered]@{ fileName = 'cmd.exe'; arguments = @('/d','/c','npm','run','dev'); workingDirectory = $frontend; port = 0; environment = @{} }
  }
  [pscustomobject]@{ Name = $Name; Directory = $directory; Frontend = $frontend; Settings = $settings; Config = (Join-Path $directory 'spar.dev.json') }
}

function New-BackendFixture {
  param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Application,
    [Parameter(Mandatory)][string]$EvidenceDirectory
  )
  $fixture = New-Fixture $Name 'backend/frontend' -Repository $Repository -Application $Application -EvidenceDirectory $EvidenceDirectory
  $backend = Join-Path $fixture.Directory 'backend'
  $original = Join-Path $repository 'samples/Spar.NativeSample'
  Get-ChildItem -LiteralPath $original -File | Where-Object Extension -in @('.cs','.csproj','.html','.js') |
    Copy-Item -Destination $backend
  $project = Join-Path $backend 'Spar.NativeSample.csproj'
  $contracts = Join-Path $fixture.Directory 'contracts'
  New-Item -ItemType Directory -Path $contracts | Out-Null
  $originalContracts = Join-Path $repository 'samples/Spar.NativeSample.Contracts'
  Copy-Item -Path (Join-Path $originalContracts '*.cs'), (Join-Path $originalContracts '*.csproj') -Destination $contracts
  $contractProject = Join-Path $contracts 'Spar.NativeSample.Contracts.csproj'
  $contractXml = [xml][IO.File]::ReadAllText($contractProject)
  foreach ($reference in $contractXml.Project.ItemGroup.ProjectReference) {
    if ($reference -is [Xml.XmlElement]) { $reference.Include = [IO.Path]::GetFullPath($reference.Include, $originalContracts) }
  }
  $contractXml.Project.Import.Project = [IO.Path]::GetFullPath($contractXml.Project.Import.Project, $originalContracts)
  $contractXml.SelectSingleNode('/Project/PropertyGroup/SparClientOutputDirectory').InnerText = Join-Path $fixture.Frontend 'src/generated'
  $contractXml.Save($contractProject)
  $xml = [xml][IO.File]::ReadAllText($project)
  foreach ($import in $xml.Project.Import) {
    $import.Project = [IO.Path]::GetFullPath($import.Project, $original)
  }
  foreach ($reference in $xml.Project.ItemGroup.ProjectReference) {
    if ($reference -is [Xml.XmlElement]) {
      $reference.Include = if ($reference.Include.Contains('Spar.NativeSample.Contracts')) { $contractProject }
        else { [IO.Path]::GetFullPath($reference.Include, $original) }
    }
  }
  $xml.Save($project)
  $source = Join-Path $backend 'SampleBackend.cs'
  $text = [IO.File]::ReadAllText($source).Replace(': ISampleBackend', ': InitialBackendBase, ISampleBackend')
  $text = $text.Replace('> 5000', '> 15000') # Isolated fixture only: keep work alive beyond the SDK's 5-second cleanup timeout.
  $text += "`npublic class InitialBackendBase;`npublic class ReplacementBackendBase;`n"
  [IO.File]::WriteAllText($source, $text)
  $fixture.Settings.project = $project
  $fixture.Settings.application = Join-Path $backend 'bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe'
  $fixture.Settings['watchBackend'] = $true
  $fixture.Settings['contractFile'] = Join-Path $contracts 'BackendContract.cs'
  $fixture.Settings['restartFiles'] = @((Join-Path $backend 'Program.cs'), (Join-Path $backend 'Program.Development.cs'))
  $fixture.Settings.startupTimeoutSeconds = 90
  $fixture | Add-Member -NotePropertyName Backend -NotePropertyValue $backend
  Get-ChildItem -LiteralPath $backend, $contracts -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($fixture.Directory,$_.FullName)
  } | Set-Content -LiteralPath (Join-Path $fixture.Directory 'sources.initial.sha256')
  return $fixture
}

function Replace-Source([string]$Path, [string]$Before, [string]$After) {
  $source = [IO.File]::ReadAllText($Path)
  if (!$source.Contains($Before)) { throw "Replacement input not found: $Before" }
  [IO.File]::WriteAllText($Path, $source.Replace($Before,$After))
}
