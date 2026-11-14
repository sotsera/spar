#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-memory): bash wsl/electron-runs.sh <rounds>
# The Electron reference, measured like the WebKitGTK host: small page (ready, idle30, heavy) and the empty page
# (ready, idle30), Wayland and X11, each run in its own private D-Bus session.
set -u
source wsl/env.sh
for round in $(seq 1 "${1:-3}"); do
  for backend in wayland x11; do
    echo "== electron-$backend-def-r$round $(date +%T)"
    GDK_BACKEND=$backend bash wsl/session.sh electron "electron-$backend-def-r$round" ready,idle30,heavy
    echo "== electron-$backend-empty-r$round $(date +%T)"
    GDK_BACKEND=$backend SPAR_PAGE=empty bash wsl/session.sh electron "electron-$backend-empty-r$round" ready,idle30
  done
done
