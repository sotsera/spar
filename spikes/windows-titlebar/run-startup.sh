#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-titlebar/run-startup.sh [rounds]
# Start-up time per configuration, alternating configurations in each round (warm profiles, offscreen window, no
# capture). Output: artifacts/windows-titlebar/startup/<config>-<round>.jsonl
set -u
rounds=${1:-10}
exe=spikes/windows-titlebar/Titlebar.Desktop/bin/Release/net11.0/win-x64/publish/Titlebar.Desktop.exe
out=artifacts/windows-titlebar/startup
run() { local label=$1; shift; timeout 90 "$exe" --out "$out" --label "$label" --after-ready 200 "$@" >/dev/null 2>&1 || echo "EXIT $? $label"; }
for r in $(seq 0 "$rounds"); do
  run "standard-$r" --profile startup-standard --hosting windowed --frame standard
  run "custom-sink-$r" --profile startup-custom --hosting windowed --buttons page --sink direct
  run "visual-$r" --profile startup-visual --hosting visual --buttons page --ncr host
done
