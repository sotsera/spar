#!/usr/bin/env bash
# Usage (in the container, from /work/spikes/asset-packing): scripts/publish-linux-host.sh [combo ...]
# Strict NativeAOT publish of Pack.Linux for each combination ("-probe" suffix: the Content-Encoding probe build).
set -eu
combos=${*:-"none-react raw-react brotli-react separate-react zip-react raw-large brotli-large separate-large zip-large"}
for combo in $combos; do
  probe=false; suffix=""
  case $combo in *-probe) probe=true; suffix=-probe; combo=${combo%-probe};; esac
  format=${combo%-*}; input=${combo#*-}
  dotnet publish Pack.Linux/Pack.Linux.csproj -c Release -p:PackFormat=$format -p:PackInput=$input -p:PackProbe=$probe -nologo -v:q -clp:ErrorsOnly
  dir=Pack.Linux/bin/$combo$suffix/Release/net11.0/linux-x64/publish
  echo "SIZE $combo$suffix $(stat -c %s $dir/Pack.Linux) sections=$(size -A $dir/Pack.Linux | awk '/^Total/ {print $2}') files: $(ls $dir | tr '\n' ' ')"
done
