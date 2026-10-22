#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$PublishDirectory,
  [Parameter(Mandatory)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$copyPath = Join-Path ([IO.Path]::GetTempPath()) ('spar-native-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $copyPath | Out-Null
Get-ChildItem -LiteralPath $PublishDirectory -File |
  Where-Object Extension -ne '.pdb' | Copy-Item -Destination $copyPath
$payload = @(Get-ChildItem -LiteralPath $copyPath -File | Sort-Object Name | Select-Object -ExpandProperty Name)
$expected = @('Spar.NativeSample.exe', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt', 'WebView2Loader.dll') | Sort-Object
if (Compare-Object $payload $expected) { throw 'Unexpected AOT payload: build tools/managed runtime must not ship.' }
& (Join-Path $PSScriptRoot '../Test-NativeHost.ps1') -Executable (Join-Path $copyPath 'Spar.NativeSample.exe') `
  -ExpectedMode NativeAOT -EvidenceDirectory (Join-Path $EvidenceDirectory 'native')
& (Join-Path $PSScriptRoot '../Test-Bridge.ps1') -Executable (Join-Path $copyPath 'Spar.NativeSample.exe') `
  -ExpectedMode NativeAOT -EvidenceDirectory (Join-Path $EvidenceDirectory 'bridge')
