#!/usr/bin/env bash
# Usage (from spikes/asset-packing): scripts/publish-bench.sh [combo ...]
# Strict NativeAOT publish of Pack.Bench for each format-input combination; prints the executable size.
set -eu
combos=${*:-"none-react raw-react brotli-react separate-react zip-react raw-large brotli-large separate-large zip-large"}
for combo in $combos; do
  format=${combo%-*}; input=${combo#*-}
  dotnet publish Pack.Bench/Pack.Bench.csproj -c Release -p:PackFormat=$format -p:PackInput=$input -nologo -v:q -clp:ErrorsOnly
  exe=$(ls Pack.Bench/bin/$combo/Release/net11.0/*/publish/Pack.Bench Pack.Bench/bin/$combo/Release/net11.0/*/publish/Pack.Bench.exe 2>/dev/null | head -1)
  echo "SIZE $combo $(stat -c %s "$exe") $exe"
done
