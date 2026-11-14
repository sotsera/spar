#!/usr/bin/env bash
# Usage (in the container as uid 1000, from spikes/csp-navigation): linux/run.sh <gtk3|gtk4> <label> <profile> <page> [host args]
# Runs the published host on Xvfb (:99) with its WebKit data under the repo's artifacts/; the log goes to
# artifacts/csp-navigation/linux/<variant>/<label>.log.
set -u
variant=$1; label=$2; profile=$3; page=$4; shift 4
dir=Csp.Linux/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Csp.Linux/bin/gtk4/Release/net11.0/linux-x64/publish
out=../../artifacts/csp-navigation/linux/$variant
mkdir -p "$out"
dist=frontend/dist
[ -n "${SPAR_DIST:-}" ] && dist=frontend/$SPAR_DIST
"$dir/Csp.Linux" --label "$label" --profile "$profile" --page "$page" --root "$dist" --root pages \
  --data-dir "../../artifacts/webview2-profiles/csp-navigation-linux-$variant" "$@" >"$out/$label.log" 2>&1
echo "exit=$? $variant $label lines=$(wc -l <"$out/$label.log")"
