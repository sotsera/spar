#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/placement.sh <gtk3|gtk4> <wayland|x11>
# Question 4: save size, position and maximized state on quit, restore them at the next start. The Windows-side
# rectangle of the WSLg window (win/wincap.ps1) is the ground truth for where the window really is.
set -u
source wsl/lib.sh
variant=$1; backend=$2
export GDK_BACKEND=$backend
file=$ART/placement-$variant-$backend.json; rm -f "$file"
win() { echo "  WINDOWS $1: $(wincap "$TITLE" | head -1 | sed 's/.*rect=/rect=/; s/ visible.*zoomed=/ zoomed=/')"; }
facts() { send facts; sleep 0.3; echo "  HOST $1: $(grep "facts:" "$LOG" | tail -1 | grep -oE "(maximized|size|default-size|position|x11-root)=[^ ]+" | tr '\n' ' ')"; }
echo "== run 1: resize to 640x420, move to 300,200, quit"
start_host "$variant" "place1-$variant-$backend" --save-placement "$file" --frame titlebar; wait_mark ui-ready 15000 >/dev/null; sleep 0.3
win start; send "resize 640 420"; sleep 0.5; send "move 300 200"; sleep 0.6; grep -E " move " "$LOG" | tail -1 | sed -E 's/^M [0-9.]+ +[0-9.]+ /  /'; win changed; facts changed
stop_host >/dev/null; echo "  SAVED $(cat "$file")"
echo "== run 2: restore"
start_host "$variant" "place2-$variant-$backend" --save-placement "$file" --restore-placement "$file" --frame titlebar; wait_mark ui-ready 15000 >/dev/null; sleep 0.5
win restored; facts restored
send maximize; sleep 0.8; win maximized; stop_host >/dev/null; echo "  SAVED $(cat "$file")"
echo "== run 3: restore maximized, then unmaximize"
start_host "$variant" "place3-$variant-$backend" --restore-placement "$file" --frame titlebar; wait_mark ui-ready 15000 >/dev/null; sleep 0.5
win restored; facts restored; send unmaximize; sleep 0.8; win unmaximized; facts unmaximized
stop_host >/dev/null
