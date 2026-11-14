#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/tray.sh <gtk3|gtk4> <wayland|x11>
# Questions 2 and 3 in a private D-Bus session (dbus-run-session), so nothing touches the user's session bus: the
# host's StatusNotifierItem against wsl/fakesni.py (watcher + host), close to tray, restore, watcher restart, Quit.
set -u
# At-spi daemons that the private session starts can outlive it; only the ones that were not there before are stopped.
before=$(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf" | tr '\n' ' ')
export NO_AT_BRIDGE=1 GTK_A11Y=none
dbus-run-session -- bash "$(dirname "$0")/tray-session.sh" "$@"
for pid in $(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf"); do case " $before " in *" $pid "*) ;; *) kill "$pid" 2>/dev/null && echo "stopped leftover at-spi process $pid";; esac; done
