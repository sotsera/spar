#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-titlebar/run-uia.sh [label args...]
# For each configuration: open the spike window offscreen, hold it, dump its UI Automation tree twice (at once and
# 3 s later; read-only), close. Without arguments it runs the standard set.
set -u
exe=spikes/windows-titlebar/Titlebar.Desktop/bin/Release/net11.0/win-x64/publish/Titlebar.Desktop.exe
out=artifacts/windows-titlebar/uia
mkdir -p "$out"
run() {
  local label=$1; shift
  rm -f "${out:?}/${label:?}.hold"
  timeout 90 "$exe" --out "$out" --label "$label" --profile probe --hold 9000 --after-ready 1000 "$@" >/dev/null 2>&1 &
  for _ in $(seq 1 100); do [ -f "$out/$label.hold" ] && break; sleep 0.2; done
  local hwnd; hwnd=$(cut -d' ' -f2 "$out/$label.hold")
  powershell -NoProfile -ExecutionPolicy Bypass -File spikes/windows-titlebar/tools/uia-dump.ps1 -Hwnd "$hwnd" -Out "$out/$label-1.txt"
  sleep 3
  powershell -NoProfile -ExecutionPolicy Bypass -File spikes/windows-titlebar/tools/uia-dump.ps1 -Hwnd "$hwnd" -Out "$out/$label-2.txt"
  wait
}
if [ $# -gt 0 ]; then run "$@"; exit; fi
run windowed-standard   --hosting windowed --frame standard
run windowed-page       --hosting windowed --buttons page
run windowed-page-sinkD --hosting windowed --buttons page --sink direct
run windowed-system     --hosting windowed --buttons system
run visual-standard     --hosting visual --frame standard
run visual-page         --hosting visual --buttons page
run visual-system       --hosting visual --buttons system
