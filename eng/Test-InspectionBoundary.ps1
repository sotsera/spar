#requires -Version 7.4
param([string]$Assembly = 'src/Sotsera.Spar.Windows/bin/Release/net10.0-windows/Sotsera.Spar.Windows.dll')

$ErrorActionPreference = 'Stop'
$path = (Resolve-Path -LiteralPath $Assembly).Path
$stream = [IO.File]::OpenRead($path)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
  $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
  $types = foreach ($handle in $metadata.TypeDefinitions) { $metadata.GetString($metadata.GetTypeDefinition($handle).Name) }
  $methods = foreach ($handle in $metadata.MethodDefinitions) { $metadata.GetString($metadata.GetMethodDefinition($handle).Name) }
  $resources = foreach ($handle in $metadata.ManifestResources) { $metadata.GetString($metadata.GetManifestResource($handle).Name) }
  if ($types -match 'Inspection|ProtocolCompleted|ScriptCompleted|ProtocolReceiver|ProtocolReceived|ProtocolEventArgs') { throw 'Inspection types exist in Release.' }
  if ($methods -contains 'InspectAsync' -or $methods -contains 'get_EnableDevelopmentInspection') { throw 'Inspection API exists in Release.' }
  if ($resources -contains 'Spar.Inspection.js') { throw 'Inspection script exists in Release.' }
  @{ assembly = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;
    inspectionApiAbsent = $true; inspectionTypesAbsent = $true; inspectionScriptAbsent = $true } | ConvertTo-Json
} finally {
  $pe.Dispose()
  $stream.Dispose()
}
