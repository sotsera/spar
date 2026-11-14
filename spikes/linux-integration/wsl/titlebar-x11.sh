#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/titlebar-x11.sh <gtk3|gtk4> <titlebar|none|native> <threshold|press> [label suffix]
# Question 1 on the X11 backend (XWayland): the page-drawn title bar driven with XTEST. A root watcher (xtool watch)
# records the window-manager requests GTK sends (_NET_WM_MOVERESIZE, _NET_WM_STATE) and the frames' ConfigureNotify.
set -u
source wsl/lib.sh
variant=$1; frame=$2; drag=$3; suffix=${4:-}
export GDK_BACKEND=x11
extra=(); [ "$frame" = none ] && extra=(--edges)
start_host "$variant" "tb-x11-$variant-$frame-$drag$suffix" --frame "$frame" --drag "$drag" "${extra[@]}" ${HOST_EXTRA:-}
wait_mark "ui-ready" 20000 >/dev/null || { stop_host; exit 1; }
sleep 0.3; send facts; wait_mark "facts:" >/dev/null
wsl/xtool watch 16000 >"$OUT/xwatch.log" &
watcher=$!
sleep 0.2
step() { echo "STEP $(mono) $*" >>"$LOG"; echo "== $*"; }
pause() { sleep "${1:-0.6}"; }

wsl/xtool find "$TITLE" | tee -a "$LOG"
if [ "$frame" = native ]; then
  step "control: drag the window manager's own title bar (frame, 14 px above the client) by 120,60"
  wsl/xtool drag "$TITLE" 200 -14 120 60 12 16 | tee -a "$LOG"; pause 0.8
fi
step "click the search box (interactive element inside the bar)"
wsl/xtool click "$TITLE" $(rect search) 1 1; pause
step "drag the bar by 120,60"
wsl/xtool drag "$TITLE" $(rect spot) 120 60 12 16 | tee -a "$LOG"; pause 0.8
send facts; pause 0.3
step "double-click the bar"
wsl/xtool click "$TITLE" $(rect spot) 1 2; pause 1.0
send facts; pause 0.3
step "double-click the bar again"
wsl/xtool click "$TITLE" $(rect spot) 1 2; pause 1.0
step "right-click the bar"
wsl/xtool click "$TITLE" $(rect spot) 3 1; pause 0.8
step "maximize from outside (_NET_WM_STATE add, as a pager or keyboard shortcut would)"
wsl/xtool wmstate "$TITLE" add; pause 1.0
step "restore from outside"
wsl/xtool wmstate "$TITLE" remove; pause 1.0
send facts; pause 0.3
step "click the page's maximize button"
wsl/xtool click "$TITLE" $(rect max) 1 1; pause 1.0
send unmaximize; pause 1.0
step "click the page's minimize button"
send facts; pause 0.2
wsl/xtool click "$TITLE" $(rect min) 1 1; pause 1.0
echo "WINDOWS-SIDE after minimize: $(wincap "$TITLE" | head -1)" | tee -a "$LOG"
send facts; pause 0.3
step "unminimize from the host"
send present; pause 1.0
echo "WINDOWS-SIDE after present: $(wincap "$TITLE" | head -1)" | tee -a "$LOG"
send facts; pause 0.3
if [ "$frame" = none ]; then
  step "page-side resize edge: drag the bottom-right corner by 60,40"
  read -r w h < <(python3 -I -c 'import json,sys; [print(r["w"], r["h"]) for r in [json.loads(l.split(" ui-ready ",1)[1]) for l in open(sys.argv[1]) if " ui-ready " in l]]' "$LOG")
  wsl/xtool drag "$TITLE" $((w - 2)) $((h - 2)) 60 40 8 16 | tee -a "$LOG"; pause 0.8
  send facts; pause 0.3
fi
send "eval css-support"; pause 0.3
step "close through the window manager's close message (WM_DELETE_WINDOW)"
wsl/xtool close "$TITLE"
for _ in $(seq 1 300); do kill -0 "$HOST_PID" 2>/dev/null || break; sleep 0.02; done
stop_host
kill "$watcher" 2>/dev/null; wait "$watcher" 2>/dev/null
echo "--- summary $LABEL"
grep -E "css-support|STEP|WINDOWS-SIDE|\"type\":\"(mousedown|focus)\"|press \(|begin-move|begin-resize|toggle-maximize|window-menu|window-state|state-seen|minimize|close|quit|children|facts:|\"type\":\"focus\"|XT .* (before|after|press at|release at)" "$LOG" | cut -c1-260
echo "--- root watch"
grep -E "client-message|configure" "$OUT/xwatch.log" | head -40 | cut -c1-200
