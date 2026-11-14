#!/usr/bin/env bash
# Q2: relaunch at once on the same UDF, and a second instance while the first runs.
set -u
source "$(dirname "$0")/common.sh"
out="$results/relaunch.txt"
: > "$out"
rel() {
  local name="$1"; shift
  echo "== relaunch $name" | tee -a "$out"
  "$exe" harness relaunch --variant "$name" --udf "q2-$name" "$@" | grep -A3 '^SUMMARY' >> "$out"
  "$exe" harness clean --udf "q2-$name" > /dev/null
}
rel young-close          --runs 30
rel reputation-off       --runs 30 --host "--reputation off"
rel alt-args-young       --runs 10 --alt-args "--disable-features=msSmartScreenProtection"
rel alt-args-rep-off     --runs 10 --alt-args "--disable-features=msSmartScreenProtection" --host "--reputation off"
for variant in same args language exclusiveB exclusiveA profile; do
  echo "== concurrent $variant" | tee -a "$out"
  "$exe" harness concurrent --variant "$variant" --udf "q2-conc" --runs 3 >> "$out"
  "$exe" harness clean --udf "q2-conc" > /dev/null
done
