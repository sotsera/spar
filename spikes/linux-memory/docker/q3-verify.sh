#!/usr/bin/env bash
# Usage (in the container, as uid 1000, from /work/spikes/linux-memory, with Xvfb :99 and openbox running):
#   dbus-run-session -- bash docker/q3-verify.sh <rounds>
# Question 3, 5 of 5: baseline (move and resize), the fix (reset the view's controllers right after begin_move or
# begin_resize), the shutdown-time reset, insensitive-before-destroy, a page click, and GTK3 as the control.
set -u
rounds=${1:-5}
for r in $(seq 1 "$rounds"); do
  bash wsl/q3.sh gtk4 move none "v-g4-move-none-$r"
  bash wsl/q3.sh gtk4 resize none "v-g4-resize-none-$r"
  HOST_EXTRA=--reset-after-begin bash wsl/q3.sh gtk4 move none "v-g4-move-rab-$r"
  HOST_EXTRA=--reset-after-begin bash wsl/q3.sh gtk4 resize none "v-g4-resize-rab-$r"
  bash wsl/q3.sh gtk4 move reset "v-g4-move-reset-$r"
  bash wsl/q3.sh gtk4 resize insensitive "v-g4-resize-insensitive-$r"
  bash wsl/q3.sh gtk4 move none "v-g4-move-click-$r" click
  bash wsl/q3.sh gtk3 move none "v-g3-move-none-$r"
  bash wsl/q3.sh gtk3 resize none "v-g3-resize-none-$r"
done
