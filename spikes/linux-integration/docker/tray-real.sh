#!/usr/bin/env bash
# Usage (in the container, as uid 1000, from /work/spikes/linux-integration, inside dbus-run-session):
#   bash docker/tray-real.sh <gtk3|gtk4>
# Questions 2 and 3 against a real tray host: xfce4-panel 4.18's Status Tray plugin on Xvfb, driven with xdotool
# (XTEST on the container's own X server). Screenshots go to artifacts/linux-integration/docker/.
set -u
variant=$1
source wsl/lib.sh
export DISPLAY=:99 GDK_BACKEND=x11
shots=$ART/docker; mkdir -p "$shots"
step() { echo "STEP $(mono) $*" >>"$LOG"; echo "== $*"; }
cap() { docker/xcap "$shots/$variant-$1.png" ${2:-0 0 1280 40} | tee -a "$LOG"; }
panel_up() { for _ in $(seq 1 100); do gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus --method org.freedesktop.DBus.NameHasOwner org.kde.StatusNotifierWatcher 2>/dev/null | grep -q true && return 0; sleep 0.05; done; return 1; }
icon() { docker/xcap /tmp/icon.png 0 0 1280 40 | grep -oE "icon-centre=[0-9-]+,[0-9-]+" | cut -d= -f2 | tr ',' ' '; }
start_host "$variant" "real-tray-$variant" --tray --close-to-tray --frame titlebar --size 480x320
wait_mark ui-ready 20000 >/dev/null || { stop_host; exit 1; }
step "no tray host yet"; sleep 0.3; wait_mark "tray unavailable" 1000
step "start xfce4-panel"; xfce4-panel >"$OUT/panel.log" 2>&1 & PANEL=$!
panel_up && echo "  watcher up" ; wait_mark "tray registered" 8000; sleep 1.5
cap 1-registered
read -r ix iy < <(icon); echo "  icon at $ix,$iy" | tee -a "$LOG"
step "hover for the tooltip"; xdotool mousemove "$ix" "$iy"; sleep 2.0; cap 2-tooltip "0 0 640 140"; xdotool mousemove 900 600; sleep 0.5
step "status text update"; send "tray-status Spar: copying 42%"; sleep 0.5; xdotool mousemove "$ix" "$iy"; sleep 2.0; cap 3-tooltip-updated "0 0 640 140"; xdotool mousemove 900 600; sleep 0.3
step "icon change"; send "tray-icon spar-tray-busy"; sleep 0.8; cap 4-icon-changed
step "close to tray"; send close; sleep 0.6; send facts; sleep 0.2
step "left click (Activate)"; xdotool mousemove "$ix" "$iy" click 1; sleep 1.2; send facts; sleep 0.2; cap 5-after-left-click "0 0 1280 800"
step "close to tray again"; send close; sleep 0.6
step "right click (menu)"; xdotool mousemove "$ix" "$iy" click 3; sleep 1.0; cap 6-menu "0 0 640 200"
step "menu: first item (Restore) with the keyboard"; xdotool key Down Return; sleep 1.2; send facts; sleep 0.2
step "attention status"; send "tray-attention"; sleep 0.8; cap 7-attention
step "panel restart"; kill "$PANEL"; wait "$PANEL" 2>/dev/null; sleep 0.8
xfce4-panel >>"$OUT/panel.log" 2>&1 & PANEL=$!
panel_up; sleep 1.5; cap 8-after-panel-restart
read -r ix iy < <(icon); echo "  icon at $ix,$iy" | tee -a "$LOG"
step "right click, then Quit"; xdotool mousemove "$ix" "$iy" click 3; sleep 1.0; cap 9-menu-before-quit "0 0 640 200"
xdotool key Up Return
for _ in $(seq 1 500); do kill -0 "$HOST_PID" 2>/dev/null || break; sleep 0.02; done
stop_host
kill "$PANEL" 2>/dev/null; wait "$PANEL" 2>/dev/null
echo "--- host"
grep -E "STEP|XCAP|icon at|tray (unavailable|registered|watcher|call|menu|status|icon)|close-to-tray|hidden|present|page-frame|facts:|quit|children" "$LOG" | grep -v "GetAll\|tray call org.freedesktop" | sed -E 's/^M [0-9.]+ +//' | cut -c1-230
