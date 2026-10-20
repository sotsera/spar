#requires -Version 7.4
param(
  [string]$Assembly = 'samples/Spar.NativeSample/bin/Release/net10.0-windows/win-x64/Spar.NativeSample.dll',
  [string]$HostAssembly = 'src/Sotsera.Spar.Windows/bin/Release/net10.0-windows/Sotsera.Spar.Windows.dll'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-InspectionBoundary.ps1') -Assembly $HostAssembly
$path = (Resolve-Path -LiteralPath $Assembly).Path
$stream = [IO.File]::OpenRead($path)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
  $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
  $types = @($metadata.TypeDefinitions | ForEach-Object { $metadata.GetString($metadata.GetTypeDefinition($_).Name) })
  $resources = @($metadata.ManifestResources | ForEach-Object { $metadata.GetString($metadata.GetManifestResource($_).Name) })
  $references = @($metadata.AssemblyReferences | ForEach-Object { $metadata.GetString($metadata.GetAssemblyReference($_).Name) })
  if ($types -contains 'SampleDocument' -or $types -contains 'NativeReceipt' -or $types -notcontains 'ProductionReceipt') { throw 'Wrong sample entry point.' }
  if ($resources.Count -ne 1 -or $resources[0] -ne 'Spar.Production.Assets.zip') { throw 'Probe resources shipped.' }
  if ($references -match 'Development|CodeAnalysis|ContractGenerator') { throw 'Development/build reference shipped.' }
  # Read the actual embedded archive from the PE resource table without loading application code.
  $resourceDirectory = $pe.PEHeaders.CorHeader.ResourcesDirectory
  $block = $pe.GetSectionData($resourceDirectory.RelativeVirtualAddress)
  $reader = $block.GetReader()
  $length = $reader.ReadInt32()
  $bytes = $reader.ReadBytes($length)
  $memory = [IO.MemoryStream]::new($bytes, $false)
  $archive = [IO.Compression.ZipArchive]::new($memory)
  try {
    $notice = $archive.GetEntry('frontend-LICENSES.txt')
    if (!$notice) { throw 'Embedded frontend notices are missing.' }
    $noticeStream = $notice.Open()
    try { $noticeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($noticeStream)) }
    finally { $noticeStream.Dispose() }
    $sidecar = Join-Path (Split-Path $path) 'frontend-LICENSES.txt'
    if ((Get-FileHash -LiteralPath $sidecar).Hash -ne $noticeHash) { throw 'Embedded and sidecar frontend notices differ.' }
    foreach ($entry in $archive.Entries | Where-Object FullName -Match '\.(js|html)$') {
      $textReader = [IO.StreamReader]::new($entry.Open())
      try { $text = $textReader.ReadToEnd() } finally { $textReader.Dispose() }
      if ($text -match 'spar:backend|spar:sample-probe|spar-probe|/@vite/client|Edit not applied|Retain this text through HMR') {
        throw 'Development frontend code shipped.'
      }
    }
  } finally { $archive.Dispose(); $memory.Dispose() }
  @{ assembly = $path; sha256 = (Get-FileHash -LiteralPath $path).Hash
    productionEntryPoint = $true; probeResourcesAbsent = $true; developmentReferencesAbsent = $true; frontendDevelopmentCodeAbsent = $true
    frontendNoticesSha256 = $noticeHash; embeddedNoticesMatchSidecar = $true } | ConvertTo-Json
} finally { $pe.Dispose(); $stream.Dispose() }
