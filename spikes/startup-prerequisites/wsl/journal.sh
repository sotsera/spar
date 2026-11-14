#!/usr/bin/env bash
# Usage (Git Bash, from the repository root):
#   wsl.exe -d Ubuntu-24.04 -- bash "$(wsl.exe -d Ubuntu-24.04 wslpath -a "$(pwd -W)")/spikes/startup-prerequisites/wsl/journal.sh"
# Runs the Linux AOT executable in the WSL distribution with DISPLAY and WAYLAND_DISPLAY removed, so it opens no
# window on the Windows desktop: the no-display case with the real systemd-journald. Works in
# ~/spar-spikes/startup-prerequisites with a private HOME and XDG folders; copies the outputs back to
# artifacts/startup-prerequisites/wsl/.
set -u
spike=$(cd "$(dirname "$0")/.." && pwd)
repo=$(cd "$spike/../.." && pwd)
work=$HOME/spar-spikes/startup-prerequisites
mkdir -p "$work/home" "$work/out" "$work/run"
chmod 700 "$work/run"
cp "$spike/Startup.Linux/bin/Release/net11.0/linux-x64/publish/Startup.Linux" "$work/"
cd "$work"
since=$(date '+%Y-%m-%d %H:%M:%S')
for r in 1 2 3; do
  start=$(date +%s%N)
  env -u DISPLAY -u WAYLAND_DISPLAY HOME="$work/home" XDG_CONFIG_HOME="$work/home/.config" XDG_CACHE_HOME="$work/home/.cache" \
    XDG_DATA_HOME="$work/home/.local/share" XDG_STATE_HOME="$work/home/.local/state" \
    ./Startup.Linux --label "wsl-nodisplay-$r" --log "$work/out/wsl-nodisplay.jsonl" 2> "$work/out/stderr-$r.txt"
  echo "round $r exit=$? wall=$(( ($(date +%s%N) - start) / 1000000 )) ms" | tee -a "$work/out/runs.txt"
done
sleep 1
journalctl --since "$since" -t spar-sample -o verbose --no-pager > "$work/out/journal-verbose.txt" 2>&1
journalctl --since "$since" -t spar-sample --no-pager > "$work/out/journal.txt" 2>&1
mkdir -p "$repo/artifacts/startup-prerequisites/wsl"
cp "$work/out/"* "$repo/artifacts/startup-prerequisites/wsl/"
cat "$work/out/journal.txt"
