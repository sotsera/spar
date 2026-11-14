#!/usr/bin/env bash
# Runs inside spar-webkit-wasm-off:fedora43 as uid 1000: the probe subset on Xvfb, reports to /out.
set -uo pipefail
work=/tmp/w
mkdir -p "$work/pages" "$work/ext" "$work/out" /tmp/h /tmp/run
chmod 700 /tmp/run
export HOME=/tmp/h XDG_RUNTIME_DIR=/tmp/run XDG_CONFIG_HOME=/tmp/h/.config XDG_CACHE_HOME=/tmp/h/.cache XDG_DATA_HOME=/tmp/h/.local/share
export NO_AT_BRIDGE=1 GDK_BACKEND=x11 DISPLAY=:99
cp /spike/pages/* "$work/pages/"
cp /spike/linux/probe_host.py "$work/"
clang -shared -fPIC -O2 -Wall -o "$work/ext/libwasmoff.so" /spike/linux/ext/wasm_off_ext.c $(pkg-config --cflags --libs webkitgtk-web-process-extension-6.0)
Xvfb :99 -screen 0 1280x800x24 -nolisten tcp >/tmp/xvfb.log 2>&1 &
for _ in $(seq 1 50); do [ -e /tmp/.X11-unix/X99 ] && break; sleep 0.1; done
rpm -q webkitgtk6.0 gtk4 python3-gobject bubblewrap > /out/versions.txt
mkdir -p /out/runs /out/logs
one() {
  local label="$1"; shift
  local code=0
  dbus-run-session -- python3 -I "$work/probe_host.py" "$work/pages" "$work/out/$label.json" --label "$label" "$@" >"/out/logs/$label.log" 2>&1 || code=$?
  echo "$label exit=$code"
  [ -f "$work/out/$label.json" ] && cp "$work/out/$label.json" "/out/runs/$label.json"
}
one csp-none --profile none
one csp-base --profile base
one csp-wasm-unsafe-eval --profile wasm-unsafe-eval
one csp-unsafe-eval --profile unsafe-eval
one env-useWasm-false-base --profile base --setenv JSC_useWasm=false
one ext-off-base --profile base --ext-dir "$work/ext" --ext-mode off --ext-early
one us-delete-base --profile base --user-script delete
