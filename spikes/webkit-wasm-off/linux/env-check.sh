#!/usr/bin/env bash
# Prints the WSL distro's WebKitGTK, GTK, compiler and sandbox tooling versions (read-only).
set -u
. /etc/os-release; echo "distro: $PRETTY_NAME"; uname -r
dpkg-query -W -f '${Package} ${Version}\n' 'libwebkitgtk-6.0*' 'libjavascriptcoregtk-6.0*' 'libgtk-4*' 'gir1.2-webkit-6.0' 'gir1.2-javascriptcoregtk-6.0' 'libwebkitgtk-6.0-dev' 'bubblewrap' 'xdg-dbus-proxy' 2>/dev/null
for t in gcc cc pkg-config bwrap python3 Xvfb weston; do printf '%s: ' "$t"; command -v "$t" || echo missing; done
pkg-config --modversion webkitgtk-web-process-extension-6.0 2>&1 | head -1
ls /usr/lib/x86_64-linux-gnu/webkitgtk-6.0/ 2>&1 | head
echo "WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-} DISPLAY=${DISPLAY:-} XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-}"
