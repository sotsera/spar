#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-titlebar/run-theme.sh [warm runs]
# Start-up frames (PrintWindow, about 60/s, offscreen window, no focus change) per configuration and theme: one cold
# run (new profile) and warm runs; then live theme switching. Output: artifacts/windows-titlebar/theme/
set -u
warm=${1:-3}
exe=spikes/windows-titlebar/Titlebar.Desktop/bin/Release/net11.0/win-x64/publish/Titlebar.Desktop.exe
out=artifacts/windows-titlebar/theme
run() { local label=$1; shift; timeout 90 "$exe" --out "$out" --label "$label" "$@" >/dev/null 2>&1 || echo "EXIT $? $label"; }
series() {
  local name=$1; shift
  run "$name-cold" --profile "theme-$name" --fresh --capture 2500 "$@"
  for i in $(seq 1 "$warm"); do run "$name-warm$i" --profile "theme-$name" --capture 2500 "$@"; done
}
for theme in dark light; do
  series "windowed-standard-$theme" --hosting windowed --frame standard --theme $theme
  series "windowed-page-$theme"     --hosting windowed --buttons page --theme $theme
  series "windowed-system-$theme"   --hosting windowed --buttons system --theme $theme
  series "visual-page-$theme"       --hosting visual --buttons page --theme $theme
done
for cfg in "windowed --buttons page" "visual --buttons page" "windowed --buttons system"; do
  name=$(echo "$cfg" | tr -d '-' | tr ' ' '-')
  run "live-$name" --profile "theme-live" --capture 7000 --mode live --live 8 --hosting $cfg --theme dark
done
