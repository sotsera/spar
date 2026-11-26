#!/usr/bin/env bash
# M2b's spar dev smoke on Windows (docs/evidence/m2b-spar-dev.md), from Git Bash at the repository's root, after
# dotnet build src/Spar.Tool -c Debug: smoke.sh against the Notebook with the built spar tool, the app on a desktop of
# its own (spar dev --private: no window reaches the monitor, takes the focus or gets input), its data folders under
# artifacts/webview2-profiles/m2b-spar-dev/<run>/ (LOCALAPPDATA), then the processes left that name the run's folder or
# the Notebook. The log, with the repository's and the user's paths replaced (redact.pl), goes to
# logs/windows-smoke.txt.
set -u
repo=$(pwd -W)
run=$(date -u +%Y%m%d-%H%M%S)
root="$repo/artifacts/webview2-profiles/m2b-spar-dev/$run"
mkdir -p "$root/local" artifacts/m2b-spar-dev docs/evidence/m2b-spar-dev/logs
raw="artifacts/m2b-spar-dev/windows-$run.txt"

LOCALAPPDATA="$root/local" bash docs/evidence/m2b-spar-dev/smoke.sh samples/Notebook "$repo/src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.exe" > "$raw" 2>&1
# Only the kinds of process spar dev starts (the app, WebView2, the build, npm and Vite): the shells running this
# script name the run's folder too.
query='Start-Sleep -Seconds 2; $kinds = "Notebook.exe", "msedgewebview2.exe", "dotnet.exe", "VBCSCompiler.exe", "node.exe", "cmd.exe"; @(Get-CimInstance Win32_Process | Where-Object { $kinds -contains $_.Name -and $_.CommandLine -and ($_.CommandLine.Contains("m2b-spar-" + "dev") -or $_.CommandLine.Contains("samples\Note" + "book")) }).Count'
{
  echo "# Processes whose command line names the run's folder or the Notebook's project, 2 s after spar dev --stop:"
  powershell -NoProfile -Command "$query"
} >> "$raw" 2>&1

REPO="$repo" perl docs/evidence/m2b-spar-dev/redact.pl < "$raw" > docs/evidence/m2b-spar-dev/logs/windows-smoke.txt
echo "wrote docs/evidence/m2b-spar-dev/logs/windows-smoke.txt"
