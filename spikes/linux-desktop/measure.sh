#!/usr/bin/env bash
# Usage (in the container, from this folder): measure.sh <gtk3|gtk4> <label> <runs> [app args...]
# Repeats run.sh and prints the marks that matter for startup, journey, memory and shutdown, one line per run.
set -u
variant=$1; label=$2; runs=$3; shift 3
for i in $(seq 1 "$runs"); do
  ./run.sh "$variant" "$label-$i" --selftest "$@" >/dev/null
  log=../../artifacts/linux-desktop/$label-$i.log
  mark() { grep -m1 "SPAR_MARK $1" "$log" | awk '{print $NF}'; }
  journey=$(grep -m1 SPAR_JOURNEY "$log" | awk '{print $2}')
  mem=$(grep -m1 'SPAR_MEMORY after-journey' "$log" | sed -E 's/.*app rss=([0-9.]+) pss=([0-9.]+).*total rss=([0-9.]+) pss=([0-9.]+)/app-rss=\1 app-pss=\2 total-rss=\3 total-pss=\4/')
  run=$(grep -m1 SPAR_RUN "$log" | sed -E 's/.*(exit=[0-9]+).*ui-ready\(wall\)=([0-9a-z]+)ms.*left=([0-9]+)/\1 wall-ui-ready=\2 left=\3/')
  echo "$label-$i: host->window=$(mark window-shown) load-started=$(mark load-started) load-finished=$(mark load-finished) ui-ready=$(mark ui-ready) journey=$journey $mem close->children-exited=$(mark children-exited) $run"
done
