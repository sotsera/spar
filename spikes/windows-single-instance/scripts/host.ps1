# Usage (from the repository root): pwsh spikes/windows-single-instance/scripts/host.ps1 [-Cases a,b,...] [-ShortRoot <dir>]
# Runs the host cases with the published probe. Every data folder and log is under artifacts/windows-single-instance/host/,
# except the 8.3 case, whose data folder is under -ShortRoot (a folder reached through a short name). Pipes exist only
# while the probes run; nothing is registered.
param([string]$Cases = 'normal,burst,hung,exiting,killed,squat,alias,long', [string]$ShortRoot = '')
$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$exe = "$root\spikes\windows-single-instance\Single.Win\bin\Release\net11.0\win-x64\publish\Single.Win.exe"
$base = "$root\artifacts\windows-single-instance\host"

function Start-Probe([string]$case, [string[]]$arguments) {
  Start-Process $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
}

function Wait-Log([string]$logs, [string]$pattern, [int]$timeoutMs = 5000) {
  $watch = [Diagnostics.Stopwatch]::StartNew()
  while ($watch.ElapsedMilliseconds -lt $timeoutMs) {
    if (Test-Path $logs) {
      if (Select-String -Path "$logs\*.log" -Pattern $pattern -Quiet) { return }
    }
    Start-Sleep -Milliseconds 20
  }
  throw "timed out waiting for '$pattern' in $logs"
}

function New-Case([string]$name) {
  $dir = Join-Path $base $name
  Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force $dir | Out-Null
  return $dir
}

foreach ($case in ($Cases -split ',')) {
  Write-Host "== $case"
  switch ($case) {
    'normal' {
      # One primary, 20 secondaries one after another.
      $dir = New-Case $case
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '9000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary serving'
      & $exe spawn --log "$dir\logs" --count 20 --gap-ms 100 -- run @common -- open 'file-{i}.sql' | Out-Null
      $primary.WaitForExit()
    }
    'burst' {
      # One primary, 10 secondaries started together.
      $dir = New-Case $case
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '5000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary serving'
      & $exe spawn --log "$dir\logs" --count 10 --parallel -- run @common -- open 'burst-{i}.sql' | Out-Null
      $primary.WaitForExit()
    }
    'hung' {
      # The primary is suspended: one secondary alone, then two at once; the primary is resumed after each.
      $dir = New-Case $case
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '40000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary serving'
      for ($i = 0; $i -lt 5; $i++) {
        & $exe suspend --log "$dir\logs" --pid $primary.Id | Out-Null
        & $exe spawn --log "$dir\logs" --count 1 -- run @common -- open "hung-$i.sql" | Out-Null
        & $exe resume --log "$dir\logs" --pid $primary.Id | Out-Null
        Start-Sleep -Milliseconds 300
      }
      & $exe suspend --log "$dir\logs" --pid $primary.Id | Out-Null
      & $exe spawn --log "$dir\logs" --count 2 --parallel -- run @common -- open 'hung-pair-{i}.sql' | Out-Null
      & $exe resume --log "$dir\logs" --pid $primary.Id | Out-Null
      Start-Sleep -Milliseconds 500
      Stop-Process -Id $primary.Id
    }
    'exiting' {
      # The primary stops serving, then keeps the lock for 3 s (within the 5 s wait) or 7 s (beyond it).
      foreach ($slow in @(3000, 7000)) {
        $dir = New-Case "$case-$slow"
        $common = @('--data', "$dir\data", '--log', "$dir\logs")
        for ($i = 0; $i -lt 3; $i++) {
          $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '500', '--slow-exit-ms', "$slow", '--', "first-$i"))
          Wait-Log "$dir\logs" "pid=$($primary.Id) primary exiting slowly" 5000
          & $exe spawn --log "$dir\logs" --count 1 -- run @common --hold-ms 300 -- open "late-$i.sql" | Out-Null
          $primary.WaitForExit()
        }
      }
    }
    'killed' {
      # The primary is killed; the next launch must take the lock at once.
      $dir = New-Case $case
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      for ($i = 0; $i -lt 10; $i++) {
        $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '30000', '--', "doomed-$i"))
        Wait-Log "$dir\logs" "pid=$($primary.Id) primary serving"
        Stop-Process -Id $primary.Id -Force
        & $exe spawn --log "$dir\logs" --count 1 -- run @common --hold-ms 200 -- open "after-kill-$i.sql" | Out-Null
      }
    }
    'squat' {
      # Same user: a squatter takes the name before the primary, and a squatter tries after the primary.
      $dir = New-Case $case
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      $squatter = Start-Probe $case (@('squat') + $common + @('--hold-ms', '6000', '--accept'))
      Wait-Log "$dir\logs" 'squat: holding'
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '3000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary without a pipe|primary serving'
      & $exe spawn --log "$dir\logs" --count 1 -- run @common -- open 'to-squatter.sql' | Out-Null
      $primary.WaitForExit(); $squatter.WaitForExit()

      $dir = New-Case "$case-after"
      $common = @('--data', "$dir\data", '--log', "$dir\logs")
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '4000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary serving'
      & $exe squat @common --log "$dir\logs" --hold-ms 200 | Out-Null
      & $exe spawn --log "$dir\logs" --count 1 -- run @common -- open 'still-primary.sql' | Out-Null
      $primary.WaitForExit()
    }
    'alias' {
      # The secondary reaches the data folder through a junction, and (with -ShortRoot) through an 8.3 short name.
      foreach ($naming in @('path', 'fileid')) {
        $dir = New-Case "$case-$naming"
        New-Item -ItemType Directory -Force "$dir\data" | Out-Null
        New-Item -ItemType Junction -Path "$dir\alias" -Target "$dir\data" | Out-Null
        $primary = Start-Probe $case @('run', '--data', "$dir\data", '--log', "$dir\logs", '--naming', $naming, '--hold-ms', '7000', '--', 'first')
        Wait-Log "$dir\logs" 'primary serving'
        & $exe spawn --log "$dir\logs" --count 1 -- run --data "$dir\alias" --log "$dir\logs" --naming $naming -- open 'via-junction.sql' | Out-Null
        $primary.WaitForExit()
        (Get-Item "$dir\alias").Delete()

        if ($ShortRoot) {
          $long = Join-Path $ShortRoot "alias-$naming"
          Remove-Item -Recurse -Force $long -ErrorAction SilentlyContinue
          New-Item -ItemType Directory -Force $long | Out-Null
          $long = (Get-Item $long).FullName
          $short = (New-Object -ComObject Scripting.FileSystemObject).GetFolder($long).ShortPath
          "long=$long short=$short" | Out-File "$dir\paths.txt"
          $primary = Start-Probe $case @('run', '--data', $long, '--log', "$dir\logs-short", '--naming', $naming, '--hold-ms', '7000', '--', 'first')
          Wait-Log "$dir\logs-short" 'primary serving'
          & $exe spawn --log "$dir\logs-short" --count 1 -- run --data $short --log "$dir\logs-short" --naming $naming -- open 'via-short-name.sql' | Out-Null
          $primary.WaitForExit()
          Remove-Item -Recurse -Force $long
        }
      }
    }
    'long' {
      # A data folder path over 260 characters.
      $dir = New-Case $case
      $deep = "$dir\data\" + ((1..12 | ForEach-Object { 'a-rather-long-folder-name' }) -join '\')
      "length=$($deep.Length)" | Out-File "$dir\paths.txt"
      $common = @('--data', $deep, '--log', "$dir\logs")
      $primary = Start-Probe $case (@('run') + $common + @('--hold-ms', '3000', '--', 'first'))
      Wait-Log "$dir\logs" 'primary serving'
      & $exe spawn --log "$dir\logs" --count 3 -- run @common -- open 'deep-{i}.sql' | Out-Null
      $primary.WaitForExit()
    }
  }
}
