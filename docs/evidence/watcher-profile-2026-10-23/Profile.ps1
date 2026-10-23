#requires -Version 7.4
param(
  [ValidatePattern('^[a-z0-9-]+$')][string]$Name = 'traced-immediate',
  [switch]$Trace,
  [ValidateRange(0,15000)][int]$IdleMs = 0,
  [string]$ConsumerDirectory
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path '.').Path
. ./eng/integration/Fixtures.ps1
. ./eng/integration/Sessions.ps1
. ./eng/integration/Processes.ps1
$root = Join-Path $repository "artifacts/watcher-profile/$Name"
if(Test-Path $root){throw 'Use a fresh evidence directory.'}
[IO.Directory]::CreateDirectory($root)|Out-Null
$coordinator = (Resolve-Path 'tools/Spar.Dev/bin/Debug/net10.0-windows/Spar.Dev.exe').Path
$application = (Resolve-Path 'fixtures/Spar.NativeSample/bin/Debug/net10.0-windows/win-x64/Spar.NativeSample.exe').Path
$owned = [Collections.Generic.List[object]]::new()
$tracers = [Collections.Generic.List[object]]::new()
$workers = [Collections.Generic.List[object]]::new()
$observations = [Collections.Generic.List[object]]::new()
$edits = [Collections.Generic.List[object]]::new()
$sdk = [Collections.Generic.List[object]]::new()
$state = @{generation=1; watcher=$null; counts=@{}; nextCpu=[DateTimeOffset]::MinValue; directory=$null}
$passed = $false
$cleanup = $null
$unrelated = $null
$session = $null
function Observe {
  Drain-Events $session
  if(!$state.directory){$state.directory=($session.Events|Where-Object kind -eq session-started|Select-Object -First 1).detail}
  if($state.directory){
    foreach($gen in 1..$state.generation){
      $path=Join-Path $state.directory "watcher-$gen.stderr.log"
      if(!(Test-Path $path)){continue}
      $lines=@((Read-Shared $path) -split "`n")
      $completeCount=[Math]::Max(0,$lines.Count-1)
      $count=if($state.counts.ContainsKey($gen)){$state.counts[$gen]}else{0}
      for($index=$count;$index -lt $completeCount;$index++){
        $line=$lines[$index].TrimEnd("`r")
        $phase=$null;$duration=$null;$child=$null
        if($line -match "arguments '(restore|build) .+': process id (\d+)$"){$phase="$($Matches[1])-started";$child=[int]$Matches[2]}
        elseif($line -match "arguments 'run --no-build .+': process id (\d+)$"){$phase='run-cli-started';$child=[int]$Matches[1]}
        elseif($line -match 'Process id (\d+) ran for (\d+)ms and exited with exit code (\d+)\.'){$phase='child-exited';$child=[int]$Matches[1];$duration=[int]$Matches[2]}
        elseif($line -match 'Project graph loaded in ([\d.,]+)s'){$phase='graph-loaded';$duration=1000*[double]::Parse($Matches[1].Replace(',','.'),[Globalization.CultureInfo]::InvariantCulture)}
        elseif($line -match 'Design-time build completed in ([\d.,]+)s'){$phase='design-time-built';$duration=1000*[double]::Parse($Matches[1].Replace(',','.'),[Globalization.CultureInfo]::InvariantCulture)}
        elseif($line -match 'C# and Razor changes applied in (\d+)ms'){$phase='sdk-update-complete';$duration=[int]$Matches[1]}
        elseif($line -match 'File change:.*SampleBackend.cs'){$phase='backend-file-observed'}
        elseif($line -match 'Waiting for changes'){$phase='watcher-ready'}
        elseif($line -match 'Hot reload session started'){$phase='hot-reload-session'}
        elseif($line -match 'Applying updates to module'){$phase='applying-module-update'}
        elseif($line -match 'Updates applied\.'){$phase='module-updated'}
        elseif($line -match 'Preparing compilation|Compilation prepared'){$phase='compilation-preparation'}
        if($phase){$sdk.Add(@{observedAtUtc=[DateTimeOffset]::UtcNow;generation=$gen;phase=$phase;durationMs=$duration;childProcessId=$child})}
      }
      $state.counts[$gen]=$completeCount
    }
  }
  if($state.watcher -and [DateTimeOffset]::UtcNow -ge $state.nextCpu){
    $p=$state.watcher
    $p.Refresh()
    if(!$p.HasExited){$observations.Add(@{observedAtUtc=[DateTimeOffset]::UtcNow;generation=$state.generation;processId=$p.Id;cpuMs=$p.TotalProcessorTime.TotalMilliseconds;workingSetBytes=$p.WorkingSet64})}
    $state.nextCpu=[DateTimeOffset]::UtcNow.AddMilliseconds(100)
  }
}
function Await([scriptblock]$Condition,[string]$Description,[int]$Seconds=120){
  $end=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
  do { Observe; if(&$Condition){return}; if($session.Process.HasExited){throw "Coordinator exited while waiting for $Description"}; Start-Sleep -Milliseconds 30 }while([DateTimeOffset]::UtcNow -lt $end)
  throw "Timed out: $Description; completion remains unproven."
}
function Begin-Trace([int]$Generation){
  $start=[Diagnostics.ProcessStartInfo]::new((Resolve-Path 'artifacts/coreclr-startup/tools/dotnet-trace.exe').Path)
  $start.UseShellExecute=$false;$start.CreateNoWindow=$true
  $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
  foreach($arg in @('collect','--process-id',"$($state.watcher.Id)",'--profile','dotnet-common,dotnet-sampled-thread-time','--buffersize','64','--duration','00:00:45','--output',(Join-Path $root "watcher-$Generation.nettrace"))){$start.ArgumentList.Add($arg)}
  $p=[Diagnostics.Process]::Start($start);$null=$p.Handle
  $item=@{process=$p;generation=$Generation;errors=$p.StandardError.ReadToEndAsync();output=$p.StandardOutput.ReadToEndAsync();stopped=$false;prefix=[Collections.Generic.List[string]]::new()}
  $tracers.Add($item)
  $end=[DateTimeOffset]::UtcNow.AddSeconds(20)
  do{
    Observe
    $file=Join-Path $root "watcher-$Generation.nettrace"
    if((Test-Path $file) -and (Get-Item $file).Length -gt 128){$item.streamingObservedAtUtc=[DateTimeOffset]::UtcNow;return $item}
    if($p.HasExited){throw 'Trace collector failed to attach.'}
    Start-Sleep -Milliseconds 30
  }while([DateTimeOffset]::UtcNow -lt $end)
  throw 'Trace readiness not established.'
}
function Find-Worker($Launcher){
  $all=@(Get-CimInstance Win32_Process)
  $parent=$all|Where-Object ProcessId -eq $Launcher.Id|Select-Object -First 1
  if(!$parent -or [Math]::Abs(($parent.CreationDate.ToUniversalTime()-$Launcher.StartTime.ToUniversalTime()).TotalMilliseconds) -gt 1){throw 'Launcher identity changed.'}
  foreach($candidate in @($all|Where-Object{$_.CommandLine -and $_.CommandLine -match 'DotnetTools[\\/]dotnet-watch.*dotnet-watch.dll' -and $_.CommandLine.Contains($fixture.Settings.project)})){
    $ancestor=$candidate
    $matched=$false
    foreach($depth in 1..8){
      $next=$all|Where-Object ProcessId -eq $ancestor.ParentProcessId|Select-Object -First 1
      if(!$next -or $ancestor.CreationDate -lt $next.CreationDate){break}
      if($next.ProcessId -eq $Launcher.Id){$matched=$true;break}
      $ancestor=$next
    }
    if(!$matched){continue}
    $worker=[Diagnostics.Process]::GetProcessById([int]$candidate.ProcessId);$null=$worker.Handle
    if([Math]::Abs(($worker.StartTime.ToUniversalTime()-$candidate.CreationDate.ToUniversalTime()).TotalMilliseconds) -gt 1){$worker.Dispose();throw 'SDK worker identity changed.'}
    $workers.Add($worker)
    return $worker
  }
  return $null
}
function End-Trace($Item){
  if(!$Item -or $Item.stopped){return}
  $p=$Item.process
  if(!$p.HasExited){$p.StandardInput.WriteLine();$p.StandardInput.Flush()}
  if(!$p.WaitForExit(55000)){throw 'Trace collector completion not observed.'}
  $Item.prefix.ToArray()+$Item.output.GetAwaiter().GetResult()|Set-Content (Join-Path $root "trace-$($Item.generation).log")
  $Item.errors.GetAwaiter().GetResult()|Set-Content (Join-Path $root "trace-$($Item.generation).stderr.log")
  if($p.ExitCode -ne 0){throw "Trace collector failed: $($p.ExitCode)"}
  $Item.stopped=$true
}
try{
  $unrelated=Start-UnrelatedProcess
  if($ConsumerDirectory){
    $consumer=(Resolve-Path $ConsumerDirectory).Path
    $settings=Get-Content (Join-Path $consumer 'spar.dev.json') -Raw|ConvertFrom-Json -AsHashtable
    foreach($key in @('project','application','generatedClient','contractFile')){$settings[$key]=[IO.Path]::GetFullPath($settings[$key],$consumer)}
    $settings.restartFiles=@($settings.restartFiles|ForEach-Object{[IO.Path]::GetFullPath($_,$consumer)})
    $settings.sessionDirectory=Join-Path $root 'sessions'
    $settings.startupTimeoutSeconds=90;$settings.shutdownTimeoutSeconds=3
    $frontend=Join-Path $consumer 'backend/frontend'
    $settings.frontend=@{fileName='cmd.exe';arguments=@('/d','/c','npm','run','dev');workingDirectory=$frontend;port=0;environment=@{}}
    $fixture=[pscustomobject]@{Directory=$consumer;Frontend=$frontend;Backend=(Join-Path $consumer 'backend');Settings=$settings}
    $coordinator=(Resolve-Path (Join-Path $consumer '.tools/Spar.Dev.exe')).Path
  }else{$fixture=New-BackendFixture 'profile' -Repository $repository -Application $application -EvidenceDirectory $root}
  $run=Join-Path $fixture.Directory 'run'
  [IO.Directory]::CreateDirectory($run)|Out-Null
  $runFixture=[pscustomobject]@{Name=$Name;Directory=$run;Settings=$fixture.Settings;Config=(Join-Path $run 'spar.dev.json')}
  $launched=[DateTimeOffset]::UtcNow
  $session=Start-Session $runFixture $true -Coordinator $coordinator -WorkingDirectory $repository
  $owned.Add($session)
  $oldGreeting='"Hello, " + request.Name'
  $oldColor='#70e3bd'
  foreach($generation in @(1,2)){
    $state.generation=$generation;$state.watcher=$null
    Await { @($session.Events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'watcher' -and $_.generation -eq $generation}).Count -gt 0 } 'retained watcher launch'
    $event=$session.Events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'watcher' -and $_.generation -eq $generation}|Select-Object -First 1
    $launcher=$session.Handles.Values|Where-Object Id -eq $event.processId|Select-Object -First 1
    if(!$launcher){throw 'Watcher launcher identity was not retained.'}
    Await { $state.watcher=Find-Worker $launcher; $null -ne $state.watcher } 'retained SDK worker'
    $activeTrace=if($Trace){Begin-Trace $generation}else{$null}
    Await { $null -ne (Latest-Snapshot $session $generation) -and @($session.Events|Where-Object{$_.kind -eq 'watcher-ready' -and $_.generation -eq $generation}).Count -gt 0 } 'typed DOM and watcher readiness'
    $initial=Latest-Snapshot $session $generation
    Send-Command $session 'probe' 'seed'
    Await { $v=Latest-Snapshot $session $generation; $v.counter -eq 2 -and $v.local -eq 7 } 'seeded state'
    if($IdleMs){$until=[DateTimeOffset]::UtcNow.AddMilliseconds($IdleMs);do{Observe;Start-Sleep -Milliseconds 30}while([DateTimeOffset]::UtcNow -lt $until)}
    foreach($edit in 1..3){
      $newColor=if($oldColor -eq '#70e3bd'){'#e9b44c'}else{'#70e3bd'}
      $rgb=if($newColor -eq '#e9b44c'){'rgb(233, 180, 76)'}else{'rgb(112, 227, 189)'}
      Replace-Source (Join-Path $fixture.Frontend 'src/styles.css') $oldColor $newColor
      Await { (Latest-Snapshot $session $generation).color -eq $rgb } 'CSS DOM update'
      $oldColor=$newColor
      $greeting="Hello profile $generation/$edit, "
      $newGreeting='"'+$greeting+'" + request.Name'
      $started=[DateTimeOffset]::UtcNow;$clock=[Diagnostics.Stopwatch]::StartNew()
      $cpu=$state.watcher.TotalProcessorTime.TotalMilliseconds
      Replace-Source (Join-Path $fixture.Backend 'SampleBackend.cs') $oldGreeting $newGreeting
      $saved=[DateTimeOffset]::UtcNow
      Await { (Latest-Snapshot $session $generation).greeting -eq ($greeting+'Spar') } 'changed C# read in DOM' 90
      $elapsed=$clock.Elapsed.TotalMilliseconds
      $snapshot=Latest-Snapshot $session $generation
      Assert-Condition ($snapshot.document -eq $initial.document -and $snapshot.mount -eq $initial.mount -and $snapshot.counter -eq 2 -and $snapshot.local -eq 7) 'Supported edit lost state.'
      Assert-Condition (@($session.Events|Where-Object kind -eq application-ready).Count -eq $generation) 'Supported edit restarted the app.'
      $updated=$session.Events|Where-Object{$_.kind -eq 'backend-updated' -and $_.generation -eq $generation}|Select-Object -Last 1
      $published=$session.Events|Where-Object{$_.kind -eq 'backend-update-published' -and $_.generation -eq $generation}|Select-Object -Last 1
      $ui=$session.Events|Where-Object{$_.kind -eq 'ui-snapshot' -and $_.generation -eq $generation}|Select-Object -Last 1
      $state.watcher.Refresh()
      $edits.Add(@{generation=$generation;edit=$edit;startedAtUtc=$started;savedAtUtc=$saved;elapsedMs=$elapsed;runtimeUpdateAt=$updated.observedAt;publishedAt=$published.observedAt;domEventAt=$ui.observedAt;watcherCpuMs=$state.watcher.TotalProcessorTime.TotalMilliseconds-$cpu})
      $oldGreeting=$newGreeting
    }
    End-Trace $activeTrace
    if($generation -eq 1){
      $oldApp=$session.Events|Where-Object{$_.kind -eq 'application-ready' -and $_.generation -eq 1}|Select-Object -First 1
      $oldHandle=$session.Handles.Values|Where-Object Id -eq $oldApp.processId|Select-Object -First 1
      Replace-Source (Join-Path $fixture.Backend 'Program.Development.cs') 'Title = "Spar development"' 'Title = "Spar profile replacement"'
      Await { @($session.Events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'watcher' -and $_.generation -eq 2}).Count -gt 0 } 'replacement watcher'
      Assert-Condition $oldHandle.HasExited 'Old app did not retire before replacement.'
    }
  }
  Send-Command $session 'stop'
  $cleanup=Confirm-Exit $session -Unrelated $unrelated
  $passed=$true
}finally{
  try{foreach($t in $tracers){if(!$t.stopped -and $t.output){End-Trace $t}}}finally{
    try{Close-TestSessions $owned}finally{
      if($unrelated){Stop-TestProcess $unrelated}
      foreach($t in $tracers){$p=$t.process;if(!$p.HasExited){$p.Kill($true);$null=$p.WaitForExit(10000)};if($p.HasExited){$p.Dispose()}}
      foreach($p in $workers){if(!$p.WaitForExit(10000)){throw 'SDK worker exit remains unproven.'};$p.Dispose()}
      @{source=(git rev-parse HEAD);passed=$passed;traceRequested=[bool]$Trace;idleMs=$IdleMs;coordinatorStartedAtUtc=$launched;edits=$edits.ToArray();sdk=$sdk.ToArray();watcherObservations=$observations.ToArray();events=@($session.Events|Where-Object{$_.kind -in @('session-started','process-started','session-phase','build-selected','watcher-ready','application-ready','backend-updated','backend-update-published','application-stopped','watcher-stopped','owned-processes-ended')}|Select-Object kind,role,generation,processId,processStartedAtUtc,code,observedAt,updateRevision);cleanup=$cleanup} | ConvertTo-Json -Depth 9 | Set-Content (Join-Path $root 'receipt.json')
    }
  }
}
Write-Output "Profile $Name passed with actual retained process exit."
