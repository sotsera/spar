#!/usr/bin/env bash
# Usage (inside WSL): bash run.sh <display> <cell> [probe options...] [-- <cell> [probe options...]]...
# Starts one Xvfb on :<display>, runs each cell as its own probe process with a fresh isolated HOME/XDG and a private
# D-Bus session, then stops Xvfb and reports any process of this spike left behind. Logs: $base/logs/<cell>.log.
# Cells named relaunch-<group>-<n> share one home per group (created by the group's first cell): a relaunched app on
# the same data folders.
set -uo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
display=$1
shift
mkdir -p "$base/logs"
xvfb_start "$display"
echo "Xvfb :$display ready=$(cat "$base/xvfb-$display.ready" 2>/dev/null)"
while [ $# -gt 0 ]; do
  cell=$1
  shift
  probe=()
  while [ $# -gt 0 ] && [ "$1" != "--" ]; do probe+=("$1"); shift; done
  [ $# -gt 0 ] && shift
  home=$cell
  if [[ $cell == relaunch-* ]]; then
    home=${cell%-*}
    if [ "${cell##*-}" = 1 ]; then isolate "$home"; else isolate_keep "$home"; fi
  else
    isolate "$home"
  fi
  start=$(date +%s%N)
  dbus-run-session -- "$base/bin/RestartWait.Linux" --data "$base/homes/$home/probe" --out "$base/logs/$cell.log" "${probe[@]}" >"$base/logs/$cell.stderr" 2>&1
  echo "$cell: exit=$? wall=$(( ($(date +%s%N) - start) / 1000000 ))ms $(grep -E 'summary|FAILED|cycle 0' "$base/logs/$cell.log" | cut -c1-200)"
  cleanup_runtime
done
xvfb_stop
sleep 0.5
echo "LEFT: $(leftovers | tr '\n' ' ')"
