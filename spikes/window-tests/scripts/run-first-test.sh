#!/usr/bin/env bash
# Question 1: how often the first test of a fresh test process stalls (each run is a new process on a new folder).
set -u
exe=spikes/window-tests/WindowTests.Win/bin/publish/WindowTests.Win.exe
out=artifacts/window-tests/first
mkdir -p "$out"
runs=${RUNS:-10}
for mode in inprivate profile-clear udf; do
  for i in $(seq 1 "$runs"); do
    "$exe" profiles --mode "$mode" --count 2 --root "first-$mode-$i" > "$out/$mode-$i.log" 2>&1
    grep -h '^TEST' "$out/$mode-$i.log" | sed "s/^/run=$i /"
  done
done
