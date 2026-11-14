#!/usr/bin/env bash
# Usage (from spikes/desktop-origin): scripts/load-times.sh <label> [rounds]
# Loads the React bundle from each candidate origin, alternating the names in every round. Round 0 warms each
# profile and is reported apart. Each name has its own profile (artifacts/webview2-profiles/desktop-origin-load-*).
# "app.localhost+rules" is app.localhost with --host-resolver-rules mapping the name to NOTFOUND.
# Output: artifacts/desktop-origin/<label>.log (one TIMING line per run, prefixed with the round and name).
set -u
label=$1; rounds=${2:-10}
out=../../artifacts/desktop-origin/$label.log
exe=Origin.Win/bin/v1/Release/net11.0/win-x64/publish/Origin.Win.exe
: > "$out"
for round in $(seq 0 "$rounds"); do
  for name in app.localhost app.spar.invalid app.example app.spar.local app.localhost+rules; do
    origin=${name%+rules}
    extra=()
    [ "$name" != "$origin" ] && extra=(--browser-args "--host-resolver-rules=\"MAP $origin ~NOTFOUND\"")
    line=$(timeout 60 "$exe" --origin "https://$origin" --profile "desktop-origin-load-${name/+/-}" --timeout 30 "${extra[@]}" | grep -E '^(TIMING|TIMEOUT|HOST-FAILURE)')
    echo "round=$round name=$name $line" | tee -a "$out"
  done
done
