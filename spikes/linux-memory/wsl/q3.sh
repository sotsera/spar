#!/usr/bin/env bash
# Usage (inside WSL or the container, in spikes/linux-memory): bash wsl/q3.sh <gtk3|gtk4> <move|resize|off> <fix> <label> [extra steps]
# GTK4 web-process lifetime after a window-manager move or resize (X11 backend; XTEST against this spike's window only).
# Steps: wait for ready, XTEST-drag inside the view by 120,60 (the host starts the move/resize after 4 px), wait 0.8 s,
# optionally click the page once (extra step "click"), close through WM_DELETE_WINDOW, then read the host's marks:
# children-exited after N ms, or children-alive after 10 s.
set -u
[ -z "${SPAR_IN_CONTAINER:-}" ] && source wsl/env.sh
variant=$1; drag=$2; fix=$3; label=$4; extra=${5:-}
ART=${ART:-$(cd ../.. && pwd)/artifacts/linux-memory}
OUT=$ART/q3/$label; rm -rf "$OUT"; mkdir -p "$OUT"; LOG=$OUT/host.log; CTL=$OUT/control.fifo; mkfifo "$CTL"; : >"$LOG"
export GDK_BACKEND=x11
title="Spar mem $label"
timeout 60 "$(bash wsl/exe.sh "$variant")" --label "$label" --control "$CTL" --drag "$drag" --fix "$fix" --page small ${HOST_EXTRA:-} >>"$LOG" 2>&1 &
tpid=$!
for _ in $(seq 1 600); do grep -q SPAR_READY "$LOG" && break; sleep 0.05; done
sleep 0.5
if [ "$drag" != off ]; then wsl/xt drag "$title" 240 200 120 60 12 16 >>"$LOG" 2>&1; fi
sleep 0.8
if [ "$extra" = click ]; then wsl/xt click "$title" 300 250 >>"$LOG" 2>&1; sleep 0.5; fi
wsl/xt close "$title" >>"$LOG" 2>&1
for _ in $(seq 1 400); do kill -0 "$tpid" 2>/dev/null || break; sleep 0.05; done
kill -0 "$tpid" 2>/dev/null && { echo "host still running; stopping it" >>"$LOG"; kill "$tpid"; }
wait "$tpid" 2>/dev/null
result=$(grep -oE "children-exited after [0-9]+ ms|children-alive after [0-9]+ ms" "$LOG" | tail -1)
echo "Q3 $label: $result | $(grep -oE "begin-(move|resize)|XT [0-9.]+ window now [0-9 -]+" "$LOG" | tr '\n' ';')"
