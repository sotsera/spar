#requires -Version 7.4
param([Parameter(Mandatory)][string]$Frontend, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$frontendPath = (Resolve-Path -LiteralPath $Frontend).Path
$dist = Join-Path $frontendPath 'dist'
$client = Get-Content -LiteralPath (Join-Path $frontendPath 'src/generated/client.js') -Raw
if ($client -notmatch "const fingerprint = '([0-9a-f]{64})';") { throw 'No generated contract fingerprint.' }
$fingerprint = $Matches[1]
$outputPath = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$stream = [IO.File]::Create($outputPath)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
  $files = @(Get-ChildItem -LiteralPath $dist -Recurse -File | Sort-Object FullName)
  if (!$files -or !(Test-Path -LiteralPath (Join-Path $dist 'index.html'))) { throw 'Missing frontend build output.' }
  foreach ($file in $files) {
    $name = [IO.Path]::GetRelativePath($dist, $file.FullName).Replace('\', '/')
    if ($name -eq 'spar-contract.txt') { throw 'Reserved asset name.' }
    $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $destination = $entry.Open()
    $source = $file.OpenRead()
    try { $source.CopyTo($destination) } finally { $source.Dispose(); $destination.Dispose() }
  }
  $entry = $zip.CreateEntry('spar-contract.txt')
  $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
  $destination = $entry.Open()
  try { $destination.Write([Text.Encoding]::UTF8.GetBytes($fingerprint)) } finally { $destination.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
