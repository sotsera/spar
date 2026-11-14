#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/input.sh <gtk3|gtk4> <label> [app args...]
# X11 backend: starts the host, waits for ui-ready, then sends trusted input through XTEST (wsl/xtest): Tab, Tab, and
# a click on the page's button (its centre, from the page's report). The page logs each event with isTrusted.
set -u
variant=$1; label=$2; shift 2
log=../../artifacts/linux-wsl-desktop/runs/$label/run.log
GDK_BACKEND=x11 bash wsl/run.sh "$variant" "$label" --after-ready 2500 "$@" &
app=$!
for _ in $(seq 1 200); do grep -q SPAR_READY "$log" 2>/dev/null && break; sleep 0.05; done
sleep 0.3
title="Spar WSL $label"
wsl/xtest "$title" key Tab
sleep 0.2
wsl/xtest "$title" key Tab
sleep 0.2
read -r bx by < <(grep -o '"button":\[[0-9]*,[0-9]*\]' "$log" | head -1 | tr -dc '0-9,' | tr ',' ' ')
wsl/xtest "$title" click "$bx" "$by"
wait $app
grep -E "page:input|page:window-(focus|blur)|is-active|hasFocus\":(true|false)" "$log" | grep -o -E 'is-active=[01]|"n":"[^"]*"|"trusted":(true|false)|"key":"[^"]*"|"target":"[^"]*"|"hasFocus":(true|false)' | paste -sd' ' | sed 's/"n":/\n  /g'
