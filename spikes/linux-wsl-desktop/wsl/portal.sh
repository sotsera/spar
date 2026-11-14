#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/portal.sh <gtk3|gtk4>
# Checks the XDG settings portal's color-scheme and its SettingChanged signal without touching the user's session or
# settings: a private D-Bus session (dbus-run-session), XDG_CURRENT_DESKTOP=GNOME so xdg-desktop-portal uses
# gtk.portal (UseIn=gnome) for Settings, and a private XDG_CONFIG_HOME so the dconf writes below go to a disposable
# folder under artifacts/. The host starts with the system theme, then the script flips the GNOME color-scheme twice.
set -u
variant=$1
home=$(mkdir -p ../../artifacts/linux-wsl-desktop/portal-home && cd ../../artifacts/linux-wsl-desktop/portal-home && pwd)
rm -rf "$home/config"; mkdir -p "$home/config/xdg-desktop-portal"
# Without this file the 1.18 portal exported no Settings interface, even with XDG_CURRENT_DESKTOP=GNOME (see RESULTS.md).
[ "${PORTALS_CONF:-1}" = 1 ] && printf "[preferred]
default=gtk
" >"$home/config/xdg-desktop-portal/portals.conf"
export XDG_CONFIG_HOME=$home/config XDG_CURRENT_DESKTOP=GNOME
dbus-run-session -- bash -c '
  set -u
  label=portal-'"$variant"'
  gsettings set org.gnome.desktop.interface color-scheme prefer-dark
  echo "PORTAL_READ_BEFORE $(gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.portal.Settings.ReadOne org.freedesktop.appearance color-scheme 2>&1)"
  bash wsl/run.sh '"$variant"' $label --order load-first --layers css,prefer-dark,wv-bg --after-ready 3000 &
  app=$!
  for _ in $(seq 1 200); do grep -q SPAR_READY ../../artifacts/linux-wsl-desktop/runs/$label/run.log 2>/dev/null && break; sleep 0.05; done
  sleep 0.8; echo "PORTAL_FLIP prefer-light at $(date +%s.%N)"; gsettings set org.gnome.desktop.interface color-scheme prefer-light
  sleep 1.0; echo "PORTAL_FLIP prefer-dark at $(date +%s.%N)"; gsettings set org.gnome.desktop.interface color-scheme prefer-dark
  wait $app
  grep -E "portal|theme-appl|scheme-change|ui-ready" ../../artifacts/linux-wsl-desktop/runs/$label/run.log | cut -c1-200
  ps -o pid=,comm= --ppid 1 -u $(id -u) | grep -E "xdg|dconf" | sed "s/^/IN-SESSION /"
'
sleep 0.5
echo "LEFT AFTER SESSION: $(ps -u "$(id -u)" -o pid=,etimes=,comm= | awk '$2 < 30 && $3 ~ /(xdg|dconf)/' | tr '\n' ' ')"
echo "user setting (real config): $(env -u XDG_CONFIG_HOME gsettings get org.gnome.desktop.interface color-scheme); user dconf file: $(ls ~/.config/dconf/user 2>&1)"
