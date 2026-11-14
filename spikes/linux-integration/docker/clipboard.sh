#!/usr/bin/env bash
# Usage (in the container, as uid 1000, from /work/spikes/linux-integration, inside dbus-run-session):
#   bash docker/clipboard.sh <gtk3|gtk4> <label> [host args...]
# Question 6 on the container's own Xvfb, so the user's clipboard is never touched: the host clipboard (set, read)
# and the page's navigator.clipboard under the strict CSP, with trusted clicks (XTEST) as the user gesture.
set -u
variant=$1; label=$2; shift 2
source wsl/lib.sh
export DISPLAY=:99 GDK_BACKEND=x11
start_host "$variant" "clip-$variant-$label" --frame titlebar "$@"
wait_mark ui-ready 20000 >/dev/null; sleep 0.4; send facts; sleep 0.3
send "clip-set spar-clip-host-1"; sleep 0.3; send clip-get; sleep 0.5
wsl/xtool click "$TITLE" $(rect paste) 1 1 >/dev/null; sleep 1.0
wsl/xtool click "$TITLE" $(rect copy) 1 1 >/dev/null; sleep 0.8
send clip-get; sleep 0.5
send "eval click paste"; sleep 1.0
stop_host >/dev/null
echo "--- $LABEL ($*)"
grep -E "clip|permission-request" "$LOG" | grep -v "command clip" | sed -E 's/^M [0-9.]+ +[0-9.]+ /  /' | cut -c1-200
