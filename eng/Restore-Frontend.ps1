#requires -Version 7.4
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Frontend,
  [Parameter(Mandatory)][string]$StateFile,
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $Frontend).Path
$statePath = [IO.Path]::GetFullPath($StateFile)
$modules = Join-Path $directory 'node_modules'

function Invoke-Npm([string[]]$Arguments, [switch]$Capture) {
  $start = [Diagnostics.ProcessStartInfo]::new('cmd.exe')
  $start.WorkingDirectory = $directory
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  foreach ($argument in @('/d','/c','npm') + $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::Start($start)
  try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $output = $stdout.GetAwaiter().GetResult()
    $errorOutput = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode) { throw "npm failed ($($process.ExitCode)). $errorOutput" }
    if ($Capture) { return $output }
    Write-Output $output.TrimEnd()
    if ($errorOutput) { Write-Output $errorOutput.TrimEnd() }
  } finally { $process.Dispose() }
}

function Get-TreeHash {
  if (!(Test-Path -LiteralPath $modules -PathType Container)) { return $null }
  $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
  try {
    foreach ($entry in Get-ChildItem -LiteralPath $modules -Force -Recurse | Sort-Object FullName) {
      # Linked/workspace installations retain the original clean-install behavior; never follow links.
      if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $null }
      if (!$entry.PSIsContainer) {
        $relative = [IO.Path]::GetRelativePath($modules, $entry.FullName)
        $hash.AppendData([Text.Encoding]::UTF8.GetBytes($relative + "`n" + (Get-FileHash -LiteralPath $entry.FullName).Hash + "`n"))
      }
    }
    return [Convert]::ToHexString($hash.GetHashAndReset())
  } finally { $hash.Dispose() }
}

if ((Test-Path -LiteralPath $modules) -and
    ((Get-Item -LiteralPath $modules -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
  throw 'Frontend restore requires an ordinary node_modules directory; refusing to replace a junction or symbolic link.'
}

# Effective npm configuration can contain credentials. Hash in memory; never persist or print it.
$configuration = Invoke-Npm @('config','list','--json','--ignore-scripts','--include=optional') -Capture
$config = $configuration | ConvertFrom-Json -AsHashtable
$node = & node -p 'process.version + "/" + process.execPath'
if ($LASTEXITCODE) { throw 'Cannot identify the Node installation.' }
$inputs = [Collections.Generic.List[string]]::new()
$inputs.Add($directory)
$inputs.Add($node)
$inputs.Add($configuration)
$inputs.Add((Get-FileHash -LiteralPath $PSCommandPath).Hash)
foreach ($name in @('package.json','package-lock.json','npm-shrinkwrap.json')) {
  $path = Join-Path $directory $name
  $inputs.Add($(if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash } else { 'absent:' + $name }))
}
foreach ($name in @('userconfig','globalconfig')) {
  $path = $config[$name]
  if ($path -and (Test-Path -LiteralPath $path -PathType Leaf)) { $inputs.Add((Get-FileHash -LiteralPath $path).Hash) }
}
for ($parent = [IO.DirectoryInfo]::new($directory); $null -ne $parent; $parent = $parent.Parent) {
  $path = Join-Path $parent.FullName '.npmrc'
  $inputs.Add($(if (Test-Path -LiteralPath $path -PathType Leaf) { (Get-FileHash -LiteralPath $path).Hash } else { 'absent:' + $path }))
}
foreach ($variable in Get-ChildItem Env: | Where-Object { $_.Name -like 'NPM_CONFIG_*' -or $_.Name -in @('NODE_ENV','NODE_OPTIONS') } | Sort-Object Name) {
  $inputs.Add($variable.Name + '=' + $variable.Value)
}
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($inputs -join "`n")))
$previous = $null
if (!$Force -and (Test-Path -LiteralPath $statePath)) {
  try { $previous = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } catch { }
}
if ($previous -and $previous.inputs -eq $fingerprint -and $previous.tree) {
  $tree = Get-TreeHash
  if ($tree -and $tree -eq $previous.tree) {
    Write-Output 'Frontend dependencies unchanged; reusing verified installation.'
    return
  }
}

if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath }
Invoke-Npm @('ci','--ignore-scripts','--include=optional')
$tree = Get-TreeHash
[IO.Directory]::CreateDirectory((Split-Path $statePath)) | Out-Null
@{ inputs = $fingerprint; tree = $tree } | ConvertTo-Json | Set-Content -LiteralPath $statePath
