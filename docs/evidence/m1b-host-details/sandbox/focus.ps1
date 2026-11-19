# Runs inside Windows Sandbox as the signed-in user (docs/evidence/m1b-host-details.md): the SecurityProbe app in its
# focus mode, driven with real input through SendInput, step by step. Each step is marked in driver.txt with the
# number of standard-error lines before it, so the page's reports (stderr.txt) can be split by step afterwards.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Helper)
$ErrorActionPreference = 'Stop'
$out = Join-Path 'C:\spar\out' $Label
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -Path $Helper -ReferencedAssemblies System.Drawing
$clock = [Diagnostics.Stopwatch]::StartNew()
$driver = Join-Path $out 'driver.txt'
$stderr = Join-Path $out 'stderr.txt'
function Note($text) { ('{0,9:0.0} | {1}' -f $clock.Elapsed.TotalMilliseconds, $text) | Out-File -Append -Encoding utf8 $driver }
function Lines { if (Test-Path $stderr) { @(Get-Content $stderr -Encoding utf8) } else { @() } }
function Mark($name) { Note "STEP $name (stderr line $(@(Lines).Count))" }
function WaitFor($pattern, $timeout, $from = 0) {
  $end = $clock.ElapsedMilliseconds + $timeout
  while ($clock.ElapsedMilliseconds -lt $end) {
    $lines = Lines
    for ($i = $from; $i -lt $lines.Count; $i++) { if ($lines[$i] -match $pattern) { return $i } }
    Start-Sleep -Milliseconds 100
  }
  Note "TIMEOUT waiting for '$pattern'"
  return -1
}
function Foreground { [Guest]::Describe([Guest]::GetForegroundWindow()) }
function State { [Guest]::Tap(0x77); Start-Sleep -Milliseconds 400 }
function AltTab { [Guest]::Key(0x12, $false); [Guest]::Tap(0x09); [Guest]::Key(0x12, $true) }
# A click on the title bar activates the window without a click into the page.
function ClickTitle($why) { $r = [Guest]::WindowRect($window); [Guest]::Click($r.Left + 300, $r.Top + 12); Start-Sleep -Milliseconds 300; Note "title bar clicked ($why); foreground $(Foreground)" }
function BackToApp($why) { if ([Guest]::GetForegroundWindow() -ne $window -and -not [Guest]::IsIconic($window)) { ClickTitle $why } }

# Leftovers that would take part in Alt+Tab: browser windows, and the shut-down dialog of an Alt+F4 that once reached
# the guest's desktop.
Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($shell in @(Get-Process explorer -ErrorAction SilentlyContinue)) {
  $stray = [Guest]::Dialog($shell.Id)
  if ($stray -ne [IntPtr]::Zero) { Note "closing $([Guest]::Describe($stray))"; [Guest]::Close($stray) }
}

$work = "C:\spar-work\$Label"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
$env:LOCALAPPDATA = $work
$env:SECURITY_PROBE_MODE = 'focus'
Note "start $Exe"
$app = Start-Process -FilePath $Exe -PassThru -RedirectStandardError $stderr
if ((WaitFor 'focus: ready' 30000) -lt 0) { Note 'the page never got ready'; Stop-Process -Id $app.Id -Force; exit 1 }
$window = [Guest]::MainWindow($app.Id)
Note "window $([Guest]::Describe($window)); foreground $(Foreground)"
Start-Sleep -Milliseconds 500

Mark 'A: typing right after the window is activated by a click on its title bar'
ClickTitle 'activation'
[Guest]::Type('abc')
State
[Guest]::Shot((Join-Path $out 'a-typing.png'))

Mark 'B: Tab five times from wherever focus is, then t'
foreach ($i in 1..5) { [Guest]::Tap(0x09); Start-Sleep -Milliseconds 250 }
[Guest]::Type('t')
State

Mark 'C: Shift+Tab, then s'
[Guest]::Combo(0x10, 0x09)
Start-Sleep -Milliseconds 300
[Guest]::Type('s')
State

Mark 'D: another window takes the foreground, then Alt+Tab back, then r'
# The guest has no Notepad: a plain form of another process stands in.
# Its console is hidden, and a hidden start also hides the process's first ShowWindow (pitfall "Windows Sandbox"):
# a first Show and Hide use it up.
$form = 'Add-Type -AssemblyName System.Windows.Forms; $f = New-Object Windows.Forms.Form; $f.Text = ''Other window''; $f.Show(); $f.Hide(); [void]$f.ShowDialog()'
$notepad = Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command', "`"$form`"" -PassThru
Start-Sleep -Milliseconds 3000
Note "foreground with the other window $(Foreground)"
AltTab
Start-Sleep -Milliseconds 1000
Note "foreground after Alt+Tab $(Foreground)"
BackToApp 'Alt+Tab did not reach the app'
[Guest]::Type('r')
State

Mark 'E: minimized (SC_MINIMIZE), restored by Alt+Tab, then m'
[Guest]::SystemCommand($window, 0xF020)
Start-Sleep -Milliseconds 1500
Note "minimized $([Guest]::IsIconic($window)); foreground $(Foreground)"
AltTab
Start-Sleep -Milliseconds 1200
Note "minimized $([Guest]::IsIconic($window)); foreground $(Foreground)"
if ([Guest]::IsIconic($window)) {
  [Guest]::SystemCommand($window, 0xF120)
  Start-Sleep -Milliseconds 1200
  Note "Alt+Tab did not restore it; SC_RESTORE posted; minimized $([Guest]::IsIconic($window)); foreground $(Foreground)"
}
[Guest]::Type('m')
State

Mark 'F: the page hangs (F9); the dialog goes away by itself when the page answers; then h'
$from = @(Lines).Count
[Guest]::Tap(0x78)
$dialog = [IntPtr]::Zero
$end = $clock.ElapsedMilliseconds + 30000
while ($dialog -eq [IntPtr]::Zero -and $clock.ElapsedMilliseconds -lt $end) { Start-Sleep -Milliseconds 200; $dialog = [Guest]::Dialog($app.Id) }
Note "dialog $([Guest]::Describe($dialog)); foreground $(Foreground)"
[Guest]::Shot((Join-Path $out 'f-dialog.png'))
[void](WaitFor 'hang ended' 40000 $from)
$end = $clock.ElapsedMilliseconds + 10000
while ([Guest]::Dialog($app.Id) -ne [IntPtr]::Zero -and $clock.ElapsedMilliseconds -lt $end) { Start-Sleep -Milliseconds 100 }
Start-Sleep -Milliseconds 800
Note "dialog gone; foreground $(Foreground)"
[Guest]::Type('h')
State

Mark 'G: the page hangs (F9); Enter on the dialog (Wait); then k'
$from = @(Lines).Count
[Guest]::Tap(0x78)
$dialog = [IntPtr]::Zero
$end = $clock.ElapsedMilliseconds + 30000
while ($dialog -eq [IntPtr]::Zero -and $clock.ElapsedMilliseconds -lt $end) { Start-Sleep -Milliseconds 200; $dialog = [Guest]::Dialog($app.Id) }
Start-Sleep -Milliseconds 500
Note "dialog $([Guest]::Describe($dialog)); foreground $(Foreground)"
[Guest]::Tap(0x0D)
Start-Sleep -Milliseconds 800
Note "after Enter: dialog $([Guest]::Describe([Guest]::Dialog($app.Id))); foreground $(Foreground)"
[void](WaitFor 'hang ended' 40000 $from)
Start-Sleep -Milliseconds 800
[Guest]::Type('k')
State

Mark 'H: the select, opened by a click, then dragged with the window and opened by Alt+Down, then by a click'
BackToApp 'before the select'
$origin = [Guest]::ClientOrigin($window)
Note "client origin $($origin -join ',')"
[Guest]::Click($origin[0] + 120, $origin[1] + 160)
Start-Sleep -Milliseconds 1000
Note "popups, clicked before the move: $([Guest]::Popups())"
[Guest]::Shot((Join-Path $out 'h-select-before.png'))
[Guest]::Tap(0x1B)
Start-Sleep -Milliseconds 500
$rect = [Guest]::WindowRect($window)
[Guest]::Drag($rect.Left + 300, $rect.Top + 12, $rect.Left + 450, $rect.Top + 112)
Start-Sleep -Milliseconds 800
$origin = [Guest]::ClientOrigin($window)
Note "client origin after the drag $($origin -join ','); foreground $(Foreground)"
[Guest]::Combo(0x12, 0x28)
Start-Sleep -Milliseconds 1000
Note "popups, Alt+Down after the move: $([Guest]::Popups())"
[Guest]::Shot((Join-Path $out 'h-select-keyboard.png'))
[Guest]::Tap(0x1B)
Start-Sleep -Milliseconds 500
[Guest]::Click($origin[0] + 120, $origin[1] + 160)
Start-Sleep -Milliseconds 1000
Note "popups, clicked after the move: $([Guest]::Popups())"
[Guest]::Shot((Join-Path $out 'h-select-after.png'))
[Guest]::Tap(0x28)
[Guest]::Tap(0x0D)
Start-Sleep -Milliseconds 500
State

Mark 'I: Alt+F4 when the app has the foreground, otherwise WM_CLOSE'
BackToApp 'before Alt+F4'
# Alt+F4 only to the app: sent to the desktop it opens the guest's shut-down dialog.
if ([Guest]::GetForegroundWindow() -eq $window) { [Guest]::Combo(0x12, 0x73) } else { Note 'not the foreground; WM_CLOSE'; [Guest]::Close($window) }
if (-not $app.WaitForExit(20000)) { Note 'still running 20 s after Alt+F4; WM_CLOSE'; [Guest]::Close($window); [void]$app.WaitForExit(20000) }
Note "exit $($app.ExitCode)"
Stop-Process -Id $notepad.Id -Force -ErrorAction SilentlyContinue
# The app's log: app.log since M1b step 7 (host.log before it, as the baseline build writes).
Get-ChildItem $work -Recurse -Include app.log, host.log | ForEach-Object { Copy-Item $_.FullName (Join-Path $out $_.Name) }
Note 'done'
