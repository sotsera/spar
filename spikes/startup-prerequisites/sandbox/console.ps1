# Runs inside Windows Sandbox: starts the GUI-subsystem app from a real console (cmd.exe in its own console window,
# no redirection), so the app must attach to the parent's console to write stderr. A batch file runs the app twice:
# directly (an interactive cmd would not wait for a GUI app; a batch file does) and with "start /wait". Screenshots
# the console, then closes the cmd window it started.
param([Parameter(Mandatory)][string]$Bin, [string]$Out = 'console')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$app = Join-Path $Bin 'app.exe'
$flag = Join-Path $outDir 'cmd-exit.txt'
$batch = Join-Path $outDir 'run.cmd'
Remove-Item $flag -ErrorAction SilentlyContinue
@"
@echo off
echo [batch] running the app directly
"$app" --ui none --min-runtime 999.0.0.0 --log "$outDir\direct.jsonl"
echo [batch] direct errorlevel=%errorlevel%
echo direct=%errorlevel% > "$flag.tmp"
start "" /wait "$app" --ui none --min-runtime 999.0.0.0 --log "$outDir\wait.jsonl"
echo [batch] start /wait errorlevel=%errorlevel%
echo wait=%errorlevel% >> "$flag.tmp"
move /y "$flag.tmp" "$flag" > nul
"@ | Set-Content -Encoding Ascii $batch
$p = Start-Process cmd.exe -ArgumentList '/k', "`"$batch`"" -PassThru
for ($i = 0; $i -lt 50 -and -not (Test-Path $flag); $i++) { Start-Sleep -Milliseconds 200 }
Start-Sleep -Seconds 1
& (Join-Path $Bin 'probe.exe') shot (Join-Path $outDir 'console.png')
Get-Content $flag -ErrorAction SilentlyContinue
$p | Stop-Process -Force
