#!/usr/bin/env bash
# Usage (in the container, from this folder): run.sh <gtk3|gtk4> <label> [app args...]
# Runs the published app on the container's Xvfb display with its WebKit data under the repo's artifacts/.
# Prints the wall time from launch to the UI-ready mark and checks that no WebKit process outlives the app.
set -u
variant=$1; label=$2; shift 2
dir=Linux.App/bin/Release/net11.0/linux-x64/publish
[ "$variant" = gtk4 ] && dir=Linux.App/bin/gtk4/Release/net11.0/linux-x64/publish
out=../../artifacts/linux-desktop
mkdir -p "$out"
log="$out/$label.log"; : >"$log"
start=$(date +%s%N)
"$dir/Linux.App" --data-dir "../../artifacts/webview2-profiles/linux-webkit-$variant" "$@" >"$log" 2>&1 &
pid=$!
ready=""
while kill -0 $pid 2>/dev/null; do
  if [ -z "$ready" ] && grep -q 'SPAR_MARK ui-ready' "$log"; then ready=$(( ($(date +%s%N) - start) / 1000000 )); fi
  sleep 0.005
done
wait $pid; code=$?
total=$(( ($(date +%s%N) - start) / 1000000 ))
left=$(pgrep -f 'WebKit(Web|Network|GPU)Process' | wc -l)
echo "SPAR_RUN $variant $label exit=$code launch->ui-ready(wall)=${ready:-none}ms total=${total}ms webkit-processes-left=$left" | tee -a "$log"
