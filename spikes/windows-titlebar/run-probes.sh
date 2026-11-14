#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-titlebar/run-probes.sh
# Host-safe probes: each configuration opens its own window left of the virtual screen (no focus change), sends
# WM_NCHITTEST probes to its own windows and closes. Output: artifacts/windows-titlebar/probes/<label>.jsonl
set -u
exe=spikes/windows-titlebar/Titlebar.Desktop/bin/Release/net11.0/win-x64/publish/Titlebar.Desktop.exe
out=artifacts/windows-titlebar/probes
run() { local label=$1; shift; timeout 90 "$exe" --out "$out" --label "$label" --profile probe --mode probe "$@" >/dev/null 2>&1 || echo "EXIT $? $label"; }
run windowed-standard        --hosting windowed --frame standard
run windowed-page            --hosting windowed --buttons page
run windowed-page-sinkT      --hosting windowed --buttons page --sink transparent
run windowed-page-sinkD      --hosting windowed --buttons page --sink direct
run windowed-page-capdrag     --hosting windowed --buttons page --capdrag
run windowed-page-nc-off     --hosting windowed --buttons page --nc off
run windowed-system          --hosting windowed --buttons system
run windowed-system-sinkT    --hosting windowed --buttons system --sink transparent
run visual-standard          --hosting visual --frame standard
run visual-page              --hosting visual --buttons page
run visual-system            --hosting visual --buttons system
