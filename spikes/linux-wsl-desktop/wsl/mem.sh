#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/mem.sh <gtk3|gtk3sb|gtk4> <label> [app args...]
# Starts the host, waits for ui-ready plus 1 s, then sums PSS and RSS (smaps_rollup) of the host and its descendants.
set -u
variant=$1; label=$2; shift 2
log=../../artifacts/linux-wsl-desktop/runs/$label/run.log
bash wsl/run.sh "$variant" "$label" --after-ready 2500 "$@" >/dev/null &
for _ in $(seq 1 200); do grep -q SPAR_READY "$log" 2>/dev/null && break; sleep 0.05; done
sleep 1
host=$(pgrep -n -x Wsl.Linux)
pids=$host; frontier=$host
while [ -n "$frontier" ]; do next=""; for p in $frontier; do next="$next $(pgrep -P "$p" | tr '\n' ' ')"; done; frontier=$(echo $next); pids="$pids $frontier"; done
total_pss=0; total_rss=0; parts=""
for p in $pids; do
  [ -r /proc/$p/smaps_rollup ] || continue
  pss=$(awk '/^Pss:/ {print $2}' /proc/$p/smaps_rollup); rss=$(awk '/^Rss:/ {print $2}' /proc/$p/smaps_rollup)
  total_pss=$((total_pss + pss)); total_rss=$((total_rss + rss)); parts="$parts $(cat /proc/$p/comm)=$((pss / 1024))"
done
wait
echo "MEM $label total PSS $((total_pss / 1024)) MB, RSS $((total_rss / 1024)) MB; PSS by process (MB):$parts"
