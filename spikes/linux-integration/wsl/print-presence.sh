#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-integration): bash wsl/print-presence.sh <gtk3|gtk4>
# Question 8: print the page to a PDF without a dialog (webkit_print_operation_print with "Print to File").
# Question 9: presence checks, read-only, in the user's session (no auto-start, nothing sent) and in a private session
# with a private portals.conf; the Unity launcher signal is emitted only inside the private session.
set -u
variant=$1
source wsl/lib.sh
echo "== presence in the user's session (read-only)"
"$(bash wsl/exe.sh "$variant")" --mode presence | sed -E 's/^M [0-9.]+ +[0-9.]+ /  /' | grep -v process-start
before=$(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf|xdg-desktop-portal|xdg-document-portal|xdg-permission-store" | tr '\n' ' ')
home=$ART/portal-config/config; mkdir -p "$home/xdg-desktop-portal"
printf '[preferred]\ndefault=gtk\n' >"$home/xdg-desktop-portal/portals.conf"
XDG_CONFIG_HOME=$home XDG_CURRENT_DESKTOP=GNOME NO_AT_BRIDGE=1 GTK_A11Y=none dbus-run-session -- bash -c '
  source wsl/lib.sh
  variant='"$variant"'
  exe=$(bash wsl/exe.sh $variant)
  # Activate the portal the way an app would (GTK does at startup), then check what it offers.
  gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.DBus.Peer.Ping >/dev/null 2>&1
  echo "== presence in a private session with portals.conf"
  "$exe" --mode presence | sed -E "s/^M [0-9.]+ +[0-9.]+ /  /" | grep -v process-start
  dbus-monitor --session "interface=com.canonical.Unity.LauncherEntry" >"$ART/launcher-$variant.log" 2>&1 & mon=$!
  export GDK_BACKEND=wayland
  start_host $variant print-$variant
  wait_mark ui-ready 20000 >/dev/null
  rm -f "$ART/print-$variant.pdf"
  send "print $ART/print-$variant.pdf"
  wait_mark "print (finished|failed)" 15000 | sed -E "s/^M [0-9.]+ +[0-9.]+ /  /"
  echo "  WINDOWS print dialog: $(wincap "Print" | head -1)"
  send "launcher 0.42 3"; sleep 0.5
  stop_host >/dev/null
  kill $mon
  f=$ART/print-$variant.pdf
  [ -f "$f" ] && echo "  pdf: $(stat -c %s "$f") bytes, header $(head -c 8 "$f"), pages $(grep -ac "/Type /Page[^s]" "$f")"
  echo "  launcher signal seen by a listener: $(grep -c "member=Update" "$ART/launcher-$variant.log") ($(grep -oE "\"(progress|count)\"" "$ART/launcher-$variant.log" | tr "\n" " "))"
'
for pid in $(pgrep -u "$(id -u)" -f "at-spi|accessibility.conf|xdg-desktop-portal|xdg-document-portal|xdg-permission-store"); do
  case " $before " in *" $pid "*) ;; *) kill "$pid" 2>/dev/null && echo "  stopped leftover $pid";; esac
done
echo "== libraries (presence only, nothing installed)"
for lib in libayatana-appindicator3.so.1 libayatana-appindicator-glib.so.0 libappindicator3.so.1 libdbusmenu-glib.so.4 libdbusmenu-gtk3.so.4 libportal.so.1 libnotify.so.4; do
  echo "  $lib: $(ldconfig -p | grep -q "$lib" && echo present || echo absent)"
done
echo "  packages: $(dpkg -l 2>/dev/null | awk '/^ii/ && $2 ~ /appindicator|dbusmenu|libportal|libnotify/ {print $2}' | tr '\n' ' ')"
