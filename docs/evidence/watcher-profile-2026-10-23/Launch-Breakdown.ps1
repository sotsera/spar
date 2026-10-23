$ErrorActionPreference='Stop'
. ./eng/integration/WatcherTimings.ps1
$rows=[Collections.Generic.List[object]]::new()
foreach($name in @('control-immediate','traced-worker','idle-ten-seconds','control-worker','package-immediate')){
  $root="artifacts/watcher-profile/$name"
  if(!(Test-Path "$root/receipt.json")){continue}
  $receipt=Get-Content "$root/receipt.json" -Raw|ConvertFrom-Json
  if(!$receipt.passed){continue}
  $fixture=if($name -eq 'package-immediate'){(Get-Content artifacts/watcher-profile/consumer-path.txt -Raw).Trim()}else{@(Get-ChildItem $root -Directory -Filter 'profile-*')[0].FullName}
  $events=@(Get-Content (Join-Path $fixture 'run/coordinator.jsonl')|ConvertFrom-Json)
  $directory=($events|Where-Object kind -eq session-started|Select-Object -First 1).detail
  foreach($generation in 1,2){
    $runPid=$null
    foreach($line in [IO.File]::ReadLines((Join-Path $directory "watcher-$generation.stderr.log"))){
      if($line -match "arguments 'run --no-build .+': process id (\d+)$"){$runPid=[int]$Matches[1]}
    }
    $launcher=$events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'watcher' -and $_.generation -eq $generation}|Select-Object -First 1
    $run=$events|Where-Object{$_.processId -eq $runPid -and $_.processStartedAtUtc}|Select-Object -First 1
    $application=$events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'application' -and $_.generation -eq $generation}|Select-Object -First 1
    $ready=$events|Where-Object{$_.kind -eq 'application-ready' -and $_.generation -eq $generation}|Select-Object -First 1
    if(!$launcher -or !$run -or !$application -or !$ready){throw "Missing retained launch identity: $name/$generation"}
    $timing=Read-WatcherTimings (Join-Path $directory "watcher-$generation.stderr.log")
    $timing.Remove('log')
    $rows.Add(@{name=$name;generation=$generation;launcherProcessId=$launcher.processId;launcherCreated=$launcher.processStartedAtUtc;runCliProcessId=$runPid;runCliCreated=$run.processStartedAtUtc;nativeProcessId=$application.processId;nativeCreated=$application.processStartedAtUtc;admitted=$application.observedAt;domReady=$ready.observedAt
      sdkBeforeRunMs=([DateTimeOffset]$run.processStartedAtUtc-[DateTimeOffset]$launcher.processStartedAtUtc).TotalMilliseconds
      runCliToNativeMs=([DateTimeOffset]$application.processStartedAtUtc-[DateTimeOffset]$run.processStartedAtUtc).TotalMilliseconds
      nativeToAdmissionMs=([DateTimeOffset]$application.observedAt-[DateTimeOffset]$application.processStartedAtUtc).TotalMilliseconds
      admissionToDomMs=([DateTimeOffset]$ready.observedAt-[DateTimeOffset]$application.observedAt).TotalMilliseconds
      sdk=$timing})
  }
}
$rows.ToArray()|ConvertTo-Json -Depth 7|Set-Content artifacts/watcher-profile/launch-breakdown.json
$rows.ToArray()|ForEach-Object{[pscustomobject]$_}|Format-Table name,generation,sdkBeforeRunMs,runCliToNativeMs,nativeToAdmissionMs,admissionToDomMs -AutoSize
