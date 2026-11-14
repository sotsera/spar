# Runs inside Windows Sandbox: each case starts Startup.Win through the probe, which times launch to dialog, captures
# the screen and the dialog, dumps its UIA tree and dismisses it through UIA. -Only takes a regex on case labels.
# Output: C:\spar\out\<Out>\<label>-r<round>*.
param([Parameter(Mandatory)][string]$Bin, [string]$Out = 'cases', [string]$Only = '.', [int]$Rounds = 1)
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$app = Join-Path $Bin 'app.exe'
$bare = Join-Path $Bin 'app-nomanifest.exe'
$probe = Join-Path $Bin 'probe.exe'
$full = 'shot,dump,expand,shot,dump,dismiss:Close'
$cases = @(
  # label, probe actions, exe, app arguments
  @('evergreen-default', $full, $app, @('--stage', 'controller')),
  @('missing-fixed', $full, $app, @('--runtime', 'C:\rt\empty')),
  @('missing-noclient', 'shot,dump,dismiss:Close', $app, @('--runtime', 'C:\rt\broken-noclient')),
  @('tooold', $full, $app, @('--runtime', 'C:\rt\fixed-ok', '--min-runtime', '999.0.0.0')),
  @('os', 'shot,dump,dismiss:Close', $app, @('--min-os-build', '99999')),
  @('ok-controller', 'none', $app, @('--runtime', 'C:\rt\fixed-ok', '--stage', 'controller')),
  @('broken-noexe', $full, $app, @('--runtime', 'C:\rt\broken-noexe', '--stage', 'controller')),
  @('broken-fakeexe', 'shot,dump,dismiss:Close', $app, @('--runtime', 'C:\rt\broken-fakeexe', '--stage', 'controller')),
  @('broken-killed', 'shot,dump,dismiss:Close', $app, @('--runtime', 'C:\rt\fixed-ok', '--stage', 'controller', '--kill-browser')),
  @('mb-missing', 'shot,dump,dismiss:No', $app, @('--runtime', 'C:\rt\empty', '--ui', 'messagebox')),
  @('mb-os', 'shot,dump,dismiss:OK', $app, @('--min-os-build', '99999', '--ui', 'messagebox')),
  @('nomanifest-auto', 'shot,dump,dismiss:No', $bare, @('--runtime', 'C:\rt\empty')),
  @('nomanifest-taskdialog', 'none', $bare, @('--runtime', 'C:\rt\empty', '--ui', 'taskdialog')),
  @('eventlog', 'dismiss:Close', $app, @('--runtime', 'C:\rt\empty', '--eventlog')),
  @('link', 'link,wait:5000,shot,dismiss:Close', $app, @('--runtime', 'C:\rt\empty')),
  @('button', 'button:Download the WebView2 Runtime,wait:5000,shot,dismiss:Close', $app, @('--runtime', 'C:\rt\empty')),
  @('mb-yes', 'dismiss:Yes,wait:5000,shot', $app, @('--runtime', 'C:\rt\empty', '--ui', 'messagebox'))
)
foreach ($round in 1..$Rounds) {
  foreach ($c in $cases) {
    $label = $c[0]
    if ($label -notmatch $Only) { continue }
    $run = "$label-r$round"
    $argv = @($c[3]) + @('--app-name', 'Spar Sample', '--label', $run, '--log', (Join-Path $outDir "$run.jsonl"),
                         '--profile', "C:\spar\profiles\$run")
    & $probe run $outDir $run $c[1] $c[2] @argv
    # Browser windows opened by the link cases would cover later screenshots.
    Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  }
}
if ('eventlog' -match $Only) {
  # Get-WinEvent finds no provider metadata for an unregistered source; the classic reader lists it.
  Get-EventLog -LogName Application -Source 'Spar Sample' -Newest 3 -ErrorAction SilentlyContinue |
    Format-List TimeGenerated, EventID, EntryType, Source, Message | Out-File (Join-Path $outDir 'eventlog.txt') -Width 300
}
