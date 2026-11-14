#!/usr/bin/env bash
# Usage (from spikes/desktop-origin): scripts/with-listener.sh <label> <command ...>
# Runs the loopback leak listener (and the LNA HTTP server on port 47811) only while <command> runs.
# Its log goes to artifacts/desktop-origin/listener-<label>.log.
set -u
label=$1; shift
out=../../artifacts/desktop-origin
mkdir -p "$out"
node tools/listener.mjs "$out/listener-$label.log" --lna-port 47811 > /dev/null &
listener=$!
until grep -q 'lna http' "$out/listener-$label.log" 2>/dev/null; do node -e 'setTimeout(()=>{},100)'; done
"$@"
status=$?
node -e 'setTimeout(()=>{},2500)' # late connections (and attribution) after the host exits
kill $listener
exit $status
