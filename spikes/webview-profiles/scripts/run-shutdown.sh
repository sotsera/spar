#!/usr/bin/env bash
# Q1: Close every controller, exit the host at once, and time every browser process of the UDF (30 runs per variant).
set -u
source "$(dirname "$0")/common.sh"
out="$results/shutdown.txt"
: > "$out"
run() {
  local name="$1"; shift
  echo "== $name" | tee -a "$out"
  "$exe" harness shutdown --variant "$name" --udf "q1-$name" --runs "${RUNS:-30}" "$@" | tee "$bulky/q1-$name.txt" | grep -A12 '^SUMMARY' >> "$out"
  "$exe" harness clean --udf "q1-$name" > /dev/null
}
run default
run reputation-off   --host "--reputation off"
run linger-2s        --linger 2000
run block-network    --host "--block-network"
run unload           --host "--reputation off" --page-extra "&unload=1"
run service-worker   --host "--reputation off" --page-extra "&sw=1"
run idb-stream       --host "--reputation off" --page-extra "&stream=1"
run kill-host        --host "--reputation off" --kill
run kill-host-young  --kill
run wait-exit        --host "--reputation off --exit wait"
run wait-exit-young  --host "--exit wait"
