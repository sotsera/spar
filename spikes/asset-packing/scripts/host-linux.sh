#!/usr/bin/env bash
# Usage (in the container, DISPLAY set, from /work/spikes/asset-packing): scripts/host-linux.sh <label> [runs] [combo ...]
# Loads the page <runs> times per decode mode with each published Pack.Linux; WebKit data under artifacts/webview2-profiles/.
set -u
label=$1; runs=${2:-5}; shift 2 || true
combos=${*:-"raw-react brotli-react separate-react zip-react raw-large brotli-large separate-large zip-large"}
out=../../artifacts/asset-packing/$label
mkdir -p "$out"
for combo in $combos; do
  exe=Pack.Linux/bin/$combo/Release/net11.0/linux-x64/publish/Pack.Linux
  format=${combo%-*}
  modes="lazy"; case $format in brotli|separate) modes="lazy eager background passthrough";; zip) modes="lazy eager background";; esac
  for mode in $modes; do
    n=$runs; [ "$mode" = passthrough ] && n=1
    for i in $(seq 1 "$n"); do
      start=$(date +%s%N)
      line=$("$exe" --decode $mode --data-dir ../../artifacts/webview2-profiles/asset-packing-linux 2>>"$out/stderr.log")
      end=$(date +%s%N)
      echo "RUN $combo wall=$(( (end - start) / 1000000 )) $line" | tee -a "$out/host.log"
    done
  done
done
