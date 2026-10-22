#requires -Version 7.4
# Diagnostic attribution for the pinned SDK's verbose output, never a readiness decision.
function Read-WatcherTimings {
  param([Parameter(Mandatory)][string]$Path)
  $commands = @{}
  $restore = [Collections.Generic.List[int]]::new()
  $build = [Collections.Generic.List[int]]::new()
  $graph = [Collections.Generic.List[double]]::new()
  $design = [Collections.Generic.List[double]]::new()
  $projects = $null
  $ready = $false
  if (Test-Path -LiteralPath $Path) {
    foreach ($line in [IO.File]::ReadLines($Path)) {
      if ($line -match "arguments '(restore|build) .+': process id (\d+)$") {
        $commands[$Matches[2]] = $Matches[1]
      } elseif ($line -match 'Process id (\d+) ran for (\d+)ms and exited with exit code 0\.') {
        $role = $commands[$Matches[1]]
        if ($role -eq 'restore') { $restore.Add([int]$Matches[2]) }
        if ($role -eq 'build') { $build.Add([int]$Matches[2]) }
      } elseif ($line -match 'Project graph loaded in ([\d.,]+)s\.') {
        $graph.Add(1000 * [double]::Parse($Matches[1].Replace(',','.'), [Globalization.CultureInfo]::InvariantCulture))
      } elseif ($line -match 'Design-time build completed in ([\d.,]+)s\.') {
        $design.Add(1000 * [double]::Parse($Matches[1].Replace(',','.'), [Globalization.CultureInfo]::InvariantCulture))
      } elseif ($line -match 'Loaded (\d+) project\(s\) in ') {
        $projects = [int]$Matches[1]
      } elseif ($line -match '^dotnet watch : Waiting for changes\s*$') {
        $ready = $true
        break
      }
    }
  }
  # Empty arrays/null mean unobserved, not zero. Do not return raw launch arguments or pipe names.
  return @{
    log = $Path; firstReadyObserved = $ready
    complete = ($ready -and $restore.Count -gt 0 -and $build.Count -gt 0 -and
      $graph.Count -gt 0 -and $design.Count -gt 0 -and $null -ne $projects)
    restoreProcessMs = $restore.ToArray(); buildProcessMs = $build.ToArray()
    graphLoadApproxMs = $graph.ToArray(); designTimeBuildApproxMs = $design.ToArray()
    projectCount = $projects
  }
}
