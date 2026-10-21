#requires -Version 7.4
param(
  [string]$CodeCommand = 'code',
  [string]$Coordinator = 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe',
  [string]$ExtensionDirectory = "$PSScriptRoot/../tools/Spar.VSCode",
  [string]$EvidenceDirectory = 'artifacts/vscode-startup',
  [switch]$PauseForVisualReview
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $EvidenceDirectory ('startup-' + [guid]::NewGuid().ToString('N'))))
$workspace = Join-Path $root 'workspace'
$profile = Join-Path $root 'profile'
New-Item -ItemType Directory -Force $workspace, (Join-Path $profile 'User') | Out-Null
@{ 'extensions.autoUpdate' = $false; 'window.confirmBeforeClose' = 'never' } | ConvertTo-Json |
  Set-Content (Join-Path $profile 'User/settings.json')
$coordinator = (Resolve-Path -LiteralPath $Coordinator).Path
$extension = (Resolve-Path -LiteralPath $ExtensionDirectory).Path
'{' | Set-Content (Join-Path $workspace 'malformed.json')
'This is a file, not a directory.' | Set-Content (Join-Path $workspace 'session-file')
@{ project = "$workspace/app.csproj"; application = "$workspace/app.exe"; contractFile = "$workspace/contract.cs"
  sessionDirectory = "$workspace/session-file"; frontend = @{ workingDirectory = $workspace } } | ConvertTo-Json -Depth 5 |
  Set-Content (Join-Path $workspace 'blocked.json')
@{ coordinator = $coordinator; missing = "$workspace/absent.json"; malformed = "$workspace/malformed.json"
  blocked = "$workspace/blocked.json"; pauseForVisualReview = $PauseForVisualReview.IsPresent } | ConvertTo-Json |
  Set-Content (Join-Path $workspace 'probe.json')
$codeScript = (Get-Command $CodeCommand).Source
$codeExecutable = [IO.Path]::GetFullPath((Join-Path (Split-Path $codeScript) '../Code.exe'))
$start = [Diagnostics.ProcessStartInfo]::new($codeExecutable)
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('--new-window','--wait','--skip-welcome','--skip-release-notes','--disable-workspace-trust',
  '--user-data-dir',$profile,'--extensions-dir',(Join-Path $env:USERPROFILE '.vscode/extensions'),
  '--extensionDevelopmentPath',$extension,
  '--extensionTestsPath',(Join-Path $PSScriptRoot 'vscode-development/startup.cjs'),$workspace)) {
  $start.ArgumentList.Add($argument)
}
$editor = [Diagnostics.Process]::Start($start)
$null = $editor.Handle
$output = $editor.StandardOutput.ReadToEndAsync()
$errors = $editor.StandardError.ReadToEndAsync()
Write-Output "VS Code startup evidence: $workspace"
try {
  if (!$editor.WaitForExit(420000)) { throw 'Isolated startup editor did not complete.' }
  $receipt = Get-Content (Join-Path $workspace 'receipt.json') -Raw | ConvertFrom-Json
  if ($editor.ExitCode -ne 0 -or $receipt.outcome -ne 'passed') { throw 'Actual VS Code startup checks failed.' }
  $receipt | ConvertTo-Json -Depth 6
} finally {
  if (!$editor.HasExited) { $editor.Kill($true); $editor.WaitForExit() }
  $output.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'editor.stdout.log')
  $errors.GetAwaiter().GetResult() | Set-Content (Join-Path $root 'editor.stderr.log')
  $editor.Dispose()
}
