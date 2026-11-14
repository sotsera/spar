#!/usr/bin/env bash
# Usage (from spikes/asset-packing): scripts/run-bench.sh <label> [runs] [combo ...]
# Runs the published Pack.Bench builds: eager and lazy (memory) <runs> times, decode once (30 repeats per asset),
# and the whole process in startup mode 21 times (wall time from this shell). Logs go to artifacts/asset-packing/<label>/.
set -eu
label=$1; runs=${2:-5}; shift 2 || true
combos=${*:-"none-react raw-react brotli-react separate-react zip-react raw-large brotli-large separate-large zip-large"}
out=../../artifacts/asset-packing/$label
mkdir -p "$out"
for combo in $combos; do
  exe=$(ls Pack.Bench/bin/$combo/Release/net11.0/*/publish/Pack.Bench Pack.Bench/bin/$combo/Release/net11.0/*/publish/Pack.Bench.exe 2>/dev/null | head -1)
  log="$out/$combo.log"; : >"$log"
  echo "SIZE $combo $(stat -c %s "$exe")" >>"$log"
  for i in $(seq 1 "$runs"); do "$exe" eager >>"$log"; "$exe" lazy >>"$log"; done
  "$exe" decode 30 >>"$log"
  for i in $(seq 1 21); do
    start=$(date +%s%N); "$exe" startup; end=$(date +%s%N)
    echo "STARTUP $combo $(( (end - start) / 1000 ))us" >>"$log"
  done
  echo "done $combo"
done
