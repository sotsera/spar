#!/usr/bin/env bash
# Usage (in the container, from this folder): run.sh <gtk3|gtk4> <label> [app args...]
# Runs the published app on the container's Xvfb display with its WebKit data under the repo's artifacts/, and
# checks that no WebKit process outlives the app. The log goes to artifacts/browser-telemetry/linux/<label>.log.
set -u
variant=$1; label=$2; shift 2
dir=Linux.App/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Linux.App/bin/gtk4/Release/net11.0/linux-x64/publish
out=../../../artifacts/browser-telemetry/linux
mkdir -p "$out"
log="$out/$label.log"; : >"$log"
start=$(date +%s%N)
"$dir/Linux.App" --data-dir "../../../artifacts/webview2-profiles/browser-telemetry-linux-$variant" "$@" >"$log" 2>&1
code=$?
total=$(( ($(date +%s%N) - start) / 1000000 ))
left=$(pgrep -x 'WebKitWebProces|WebKitNetworkPr' | wc -l)
echo "SPAR_RUN $variant $label exit=$code total=${total}ms webkit-processes-left=$left" | tee -a "$log"