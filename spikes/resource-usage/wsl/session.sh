#!/usr/bin/env bash
# Usage: bash wsl/session.sh <exe arguments...> (copied from spikes/webview-profiles; GTK 4 only)
# Runs the spike executable in its own D-Bus session on a private GTK Broadway display, so no window reaches the
# Windows desktop (WSLg) and nothing takes focus. Stops only the broadwayd and session daemons this run started.
set -u
source wsl/env.sh

exe="$PWD/Res.Linux/bin/publish/Res.Linux"
display=":$(( 40 + ($$ % 40) ))"
daemon=gtk4-broadwayd
"$daemon" "$display" >/dev/null 2>&1 &
broadway=$!
sleep 0.3
export GDK_BACKEND=broadway BROADWAY_DISPLAY=$display
dbus-run-session -- "$exe" "$@"
status=$?
sleep 0.3
# Session daemons this run activated inherit SPAR_WORK, this spike's own folder (another spike's processes carry a
# different one), so only processes started from here are ever signalled.
for dir in /proc/[0-9]*; do
  pid=${dir#/proc/}
  [ "$pid" = "$$" ] && continue
  if [ -O "$dir" ] && { tr '\0' '\n' < "$dir/environ"; } 2>/dev/null | grep -qx "SPAR_WORK=$SPAR_WORK"; then
    case "$(cat "$dir/comm" 2>/dev/null)" in
      at-spi-bus-laun|at-spi2-registr|dbus-daemon|xdg-desktop-por|xdg-document-po|xdg-permission-|gvfsd|dconf-service) kill "$pid" 2>/dev/null ;;
    esac
  fi
done
kill "$broadway" 2>/dev/null
exit $status
