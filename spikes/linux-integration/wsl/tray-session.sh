#!/usr/bin/env bash
# The body of wsl/tray.sh, run inside the private D-Bus session.
set -u
source wsl/lib.sh
variant=$1; backend=$2
export GDK_BACKEND=$backend
start_host "$variant" "tray-$variant-$backend" --tray --close-to-tray --frame titlebar
FCTL=$OUT/fake.fifo; mkfifo "$FCTL"
step() { echo "STEP $(mono) $*" >>"$LOG"; echo "== $*"; }
fake() { timeout 2 bash -c 'echo "$1" >"$2"' _ "$1" "$FCTL" || echo "FAKE SEND FAILED: $1"; }
start_fake() { python3 -I wsl/fakesni.py "$FCTL" >>"$OUT/fake.log" 2>&1 & FAKE_PID=$!; }
win() { echo "WINDOWS-SIDE $1: $(wincap "$TITLE" | head -1 | sed 's/.*rect=/rect=/')" | tee -a "$LOG"; }
wait_mark "ui-ready" 20000 >/dev/null || { stop_host; exit 1; }
step "no watcher yet"; wait_mark "tray (unavailable|exported)" 3000; sleep 0.5; wait_mark "tray unavailable" 1000
step "start the watcher"; start_fake; wait_mark "tray registered" 5000; sleep 0.5
step "status text update"; send "tray-status Spar: copying 42%"; sleep 0.6
step "attention status"; send "tray-attention"; sleep 0.6
step "icon change"; send "tray-icon spar-tray-busy"; sleep 0.6
step "context menu call and menu layout"; fake contextmenu; sleep 0.3; fake menu; sleep 0.5
step "set page state"; send "eval set-kept kept-before-hide"; sleep 0.3
step "close to tray"; send close; sleep 0.4; send facts; sleep 0.2; win hidden
sleep 2.0; echo "processes while hidden: $(left_processes)" | tee -a "$LOG"
step "left click (Activate) restores"; fake activate; sleep 1.2; send facts; sleep 0.2; win restored; send "eval report"; sleep 0.3
step "close to tray again"; send close; sleep 1.5
step "menu Restore"; fake "click Restore"; sleep 1.2; win restored-menu; send "eval report"; sleep 0.3
step "watcher restart"; fake quit; sleep 1.0; start_fake; wait_mark "tray registered after.*" 5000; sleep 0.8
step "menu Quit"; fake "click Quit"
for _ in $(seq 1 500); do kill -0 "$HOST_PID" 2>/dev/null || break; sleep 0.02; done
stop_host
fake quit; sleep 0.3; kill "$FAKE_PID" 2>/dev/null
echo "--- host"
grep -E "STEP|WINDOWS-SIDE|processes while|tray|close-to-tray|hidden|present|page-frame|visibility|report|quit|children|facts:" "$LOG" | grep -v "tray call .*GetAll" | cut -c1-260
echo "--- fake host"
cut -c1-300 "$OUT/fake.log"
