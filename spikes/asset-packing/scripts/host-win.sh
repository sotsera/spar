#!/usr/bin/env bash
# Usage (from spikes/asset-packing, Windows): scripts/host-win.sh <label> [runs] [combo ...]
# Publishes Pack.Win for each format-input combination, then loads the page <runs> times per decode mode in an
# offscreen window (profile artifacts/webview2-profiles/asset-packing). Logs go to artifacts/asset-packing/<label>/.
set -eu
label=$1; runs=${2:-5}; shift 2 || true
combos=${*:-"none-react raw-react brotli-react separate-react zip-react raw-large brotli-large separate-large zip-large"}
out=../../artifacts/asset-packing/$label
mkdir -p "$out"
for combo in $combos; do
  format=${combo%-*}; input=${combo#*-}
  dotnet publish Pack.Win/Pack.Win.csproj -c Release -p:PackFormat=$format -p:PackInput=$input -nologo -v:q -clp:ErrorsOnly
  exe=Pack.Win/bin/$combo/Release/net11.0/win-x64/publish/Pack.Win.exe
  echo "SIZE $combo $(stat -c %s $exe)" | tee -a "$out/sizes.log"
  [ "$format" = none ] && continue
  modes="lazy"; case $format in brotli|separate) modes="lazy eager background passthrough";; zip) modes="lazy eager background";; esac
  for mode in $modes; do
    n=$runs; [ "$mode" = passthrough ] && n=1
    for i in $(seq 1 "$n"); do
      echo "RUN $combo $(timeout 60 $exe --decode $mode --origin ${ORIGIN:-https://app.localhost})" | tee -a "$out/host.log"
    done
  done
done
