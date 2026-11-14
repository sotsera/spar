#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/pickers.sh <gtk3|gtk4> <wayland|x11> <plain|portalconf> [env...]
# Question 5 in a private D-Bus session: open the GTK file picker, cancel it from code after 700 ms, and record whether
# it went through xdg-desktop-portal (dbus-monitor) and which window appeared on the Windows side. "portalconf" gives
# the session a private XDG_CONFIG_HOME with xdg-desktop-portal/portals.conf ([preferred] default=gtk) and
# XDG_CURRENT_DESKTOP=GNOME; the user's own configuration is never used.
set -u
variant=$1; backend=$2; conf=$3; shift 3
source wsl/lib.sh
label="pick-$variant-$backend-$conf${1:+-$(echo "$1" | tr '=' '-')}"
before=$(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf|xdg-desktop-portal|xdg-document-portal|xdg-permission-store" | tr '\n' ' ')
if [ "$conf" = portalconf ]; then
  home=$ART/portal-config/config; mkdir -p "$home/xdg-desktop-portal"
  printf '[preferred]\ndefault=gtk\n' >"$home/xdg-desktop-portal/portals.conf"
  export XDG_CONFIG_HOME=$home XDG_CURRENT_DESKTOP=GNOME
fi
export GDK_BACKEND=$backend NO_AT_BRIDGE=1 GTK_A11Y=none LABEL_OVERRIDE=$label "$@"
dbus-run-session -- bash -c '
  source wsl/lib.sh
  start_host '"$variant"' "$LABEL_OVERRIDE"
  dbus-monitor --session "interface=org.freedesktop.portal.FileChooser" "interface=org.freedesktop.portal.Request" "interface=org.freedesktop.impl.portal.FileChooser" >"$OUT/dbus.log" 2>&1 &
  mon=$!
  wait_mark ui-ready 20000 >/dev/null
  sleep 0.5
  send "pick 700"
  for t in 2 4 6; do sleep 0.2; echo "WINDOWS picker at ~${t}00ms: $(wincap "Spar picker probe" | head -1 | sed "s/.*rect=/rect=/")" >>"$LOG"; done
  sleep 2.2
  echo "WINDOWS picker after cancel: $(wincap "Spar picker probe" | head -1)" >>"$LOG"
  stop_host >/dev/null
  kill $mon
  echo "--- $LABEL_OVERRIDE"
  grep -E "pick|WINDOWS" "$LOG" | sed -E "s/^M [0-9.]+ +//" | cut -c1-200
  echo "  portal calls: $(grep -cE "member=(OpenFile|Close)|member=Response" "$OUT/dbus.log") ($(grep -oE "member=(OpenFile|Close|Response)" "$OUT/dbus.log" | sort | uniq -c | tr "\n" " "))"
'
for pid in $(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf|xdg-desktop-portal|xdg-document-portal|xdg-permission-store"); do
  case " $before " in *" $pid "*) ;; *) kill "$pid" 2>/dev/null && echo "  stopped leftover $pid";; esac
done
