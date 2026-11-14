#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/titlebar-wayland.sh <gtk3|gtk4> <titlebar|none|native>
# Question 1 on the Wayland backend. There is no synthetic input on WSLg's Wayland, so the page's buttons are clicked
# from script (untrusted clicks, which still run the page -> host command path), and the Windows-side window state is
# read with win/wincap.ps1. WAYLAND_DEBUG records the xdg_toplevel requests GTK sends.
set -u
source wsl/lib.sh
variant=$1; frame=$2
export GDK_BACKEND=wayland
WAYLAND_DEBUG=client start_host "$variant" "tb-wl-$variant-$frame" --frame "$frame"
wait_mark "ui-ready" 20000 >/dev/null || { stop_host; exit 1; }
step() { echo "STEP $(mono) $*" >>"$LOG"; echo "== $*"; }
win() { echo "WINDOWS-SIDE $1: $(wincap "$TITLE" | head -1 | sed 's/.*rect=/rect=/')" | tee -a "$LOG"; }
sleep 0.3; send facts; win start
step "page maximize button (script click)"; send "eval click max"; sleep 1; win maximized; send facts; sleep 0.3
step "page maximize button again"; send "eval click max"; sleep 1; win restored
step "page minimize button"; send "eval click min"; sleep 1; win minimized; send facts; sleep 0.3
step "host present"; send present; sleep 1; win presented; send facts; sleep 0.3
step "host unminimize"; send unminimize; sleep 1; win unminimized
step "page close button"; send "eval click close"
for _ in $(seq 1 300); do kill -0 "$HOST_PID" 2>/dev/null || break; sleep 0.02; done
stop_host
echo "--- summary $LABEL"
grep -E "STEP|WINDOWS-SIDE|toggle-maximize|window-state|state-seen|minimize|present|close|quit|children|facts:" "$LOG" | cut -c1-230
echo "--- xdg_toplevel requests and configure states"
grep -E "xdg_toplevel@[0-9]+\.(set_maximized|unset_maximized|set_minimized|move|resize|show_window_menu|set_title)|xdg_toplevel@[0-9]+\.configure" "$LOG" | sed -E 's/^\[ *[0-9.]+\] *//' | uniq -c | head -30 | cut -c1-160
