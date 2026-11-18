#!/usr/bin/env bash
# Usage (inside WSL): bash matrix.sh. The Linux matrix of spikes/webview-restart-wait on Xvfb :91: every cell is one
# probe process with 10 restarts (cycles 1-10) after a first view (cycle 0).
here=$(cd "$(dirname "$0")" && pwd)
cells=()
for session in default ephemeral-new; do
  for gap in 0 100 250 500 1000 2000 3000 5000 8000; do
    cells+=("$session-$gap" --reps 10 --gap "$gap" --session "$session" --)
  done
done
for session in ephemeral-reuse persistent-new persistent-reuse; do
  for gap in 0 1000 5000; do
    cells+=("$session-$gap" --reps 10 --gap "$gap" --session "$session" --)
  done
done
cells+=(hidden-default-1000 --reps 10 --gap 1000 --window hidden --)
cells+=(dwell-default-0 --reps 10 --gap 0 --dwell 3000 --)
for i in $(seq 1 10); do cells+=("relaunch-default-$i" --reps 0 --); done
for i in $(seq 1 10); do cells+=("relaunch-ephemeral-$i" --reps 0 --session ephemeral-new --); done
bash "$here/run.sh" 91 "${cells[@]}"
