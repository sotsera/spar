#requires -Version 7.4
param(
  [Parameter(Mandatory)][string]$PublishDirectory,
  [Parameter(Mandatory)][string]$Archive,
  [Parameter(Mandatory)][string]$Configuration,
  [Parameter(Mandatory)][string]$RuntimeIdentifier,
  [string]$PublishAot,
  [ValidateSet('NativeAOT', 'CoreCLR')][string]$RequestedMode,
  [ValidateSet('true', 'false')][string]$StrictAot,
  [Parameter(Mandatory)][string]$AttributionDirectory
)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$revision = git -C $root rev-parse HEAD
if ($LASTEXITCODE) { throw 'Cannot identify publication source.' }
$status = @(git -C $root status --porcelain)
$sources = @(git -C $root ls-files --cached --others --exclude-standard)
$sourceHashes = @($sources | ForEach-Object {
  $path = Join-Path $root $_
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    @{ path = $_; sha256 = (Get-FileHash -LiteralPath $path).Hash }
  }
})
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
  $entry = $zip.GetEntry('spar-contract.txt')
  if (!$entry) { throw 'Published archive has no contract identity.' }
  $reader = [IO.StreamReader]::new($entry.Open())
  try { $fingerprint = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
$receipt = [ordered]@{
  schema = 2; createdAt = [DateTimeOffset]::UtcNow
  mode = $(if ($PublishAot -eq 'true') { 'NativeAOT' } else { 'CoreCLR' })
  requestedMode = $RequestedMode; strictAot = $StrictAot -eq 'true'; selfContained = $true
  compatibility = @{
    dependency = (Get-Content -LiteralPath (Join-Path $AttributionDirectory 'spar-compatibility-dependency.txt') -Raw)?.Trim()
    reason = (Get-Content -LiteralPath (Join-Path $AttributionDirectory 'spar-compatibility-reason.txt') -Raw)?.Trim()
  }
  configuration = $Configuration; runtimeIdentifier = $RuntimeIdentifier
  revision = $revision; workingTreeDirty = $status.Count -gt 0; sourceHashes = $sourceHashes
  dotnet = (dotnet --version); node = (node --version); npm = (npm --version); powershell = $PSVersionTable.PSVersion.ToString()
  assetArchiveSha256 = (Get-FileHash -LiteralPath $Archive).Hash
  contractFingerprint = $fingerprint
  payload = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object FullName -ne (Join-Path $directory 'spar-publish.json') | ForEach-Object {
    @{ name = [IO.Path]::GetRelativePath($directory, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
  })
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $directory 'spar-publish.json')
