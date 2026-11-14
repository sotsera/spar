#!/usr/bin/env bash
# Runs the WebAssembly probe matrix in WebKitGTK 6.0 under WSL. From the repository root:
#   wsl -d Ubuntu-24.04 -- bash spikes/webkit-wasm-off/linux/run.sh [matrix|timing|features|one <label> <host args...>]
# Everything the run creates outside the repository stays in ~/spar-spikes/webkit-wasm-off/: HOME, every
# XDG_*_HOME and XDG_RUNTIME_DIR point there (the display socket is reached through a symlink), the network
# session is ephemeral and no inspector server is enabled. Reports and logs are copied to results/.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
spike="$(cd "$here/.." && pwd)"
mode="${1:-matrix}"
work="$HOME/spar-spikes/webkit-wasm-off"
private="$work/home"
real_runtime="${XDG_RUNTIME_DIR:?XDG_RUNTIME_DIR must be set}"
mkdir -p "$work/pages" "$work/out" "$work/run" "$work/ext" "$private/.config" "$private/.local/share" "$private/.cache" "$private/.local/state"
chmod 700 "$work/run"
cp "$spike"/pages/* "$work/pages/"
cp "$here/probe_host.py" "$work/"
if [ -n "${WAYLAND_DISPLAY:-}" ] && [ -e "$real_runtime/$WAYLAND_DISPLAY" ] && [ ! -e "$work/run/$WAYLAND_DISPLAY" ]; then
  ln -s "$real_runtime/$WAYLAND_DISPLAY" "$work/run/$WAYLAND_DISPLAY"
fi
# The extension is rebuilt only when its source changes.
if [ ! -f "$work/ext/libwasmoff.so" ] || ! cmp -s "$here/ext/wasm_off_ext.c" "$work/ext.c"; then
  cp "$here/ext/wasm_off_ext.c" "$work/ext.c"
  cc -shared -fPIC -O2 -Wall -o "$work/ext/libwasmoff.so" "$work/ext.c" $(pkg-config --cflags --libs webkitgtk-web-process-extension-6.0)
fi

export HOME="$private"
export XDG_CONFIG_HOME="$private/.config"
export XDG_DATA_HOME="$private/.local/share"
export XDG_CACHE_HOME="$private/.cache"
export XDG_STATE_HOME="$private/.local/state"
export XDG_RUNTIME_DIR="$work/run"
export NO_AT_BRIDGE=1
unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER
# Start from an environment without inherited JSC options, so each run sets only its own.
for v in $(env | grep -o '^JSC_[^=]*' || true); do unset "$v"; done

mkdir -p "$spike/results/runs" "$spike/results/logs"

one() {
  local label="$1"; shift
  local code=0
  python3 -I "$work/probe_host.py" "$work/pages" "$work/out/$label.json" --label "$label" "$@" \
    >"$work/out/$label.log" 2>&1 || code=$?
  echo "$label exit=$code"
  cp "$work/out/$label.log" "$spike/results/logs/$label.log"
  if [ -f "$work/out/$label.json" ]; then cp "$work/out/$label.json" "$spike/results/runs/$label.json"; fi
}

case "$mode" in
  features)
    python3 -I "$work/probe_host.py" "$spike/results/features.json" --dump-features
    ;;
  one)
    shift; one "$@"
    ;;
  matrix)
    # Q1: CSP behaviour, no JSC options
    one csp-none --profile none
    one csp-base --profile base
    one csp-wasm-unsafe-eval --profile wasm-unsafe-eval
    one csp-unsafe-eval --profile unsafe-eval
    one csp-base-http --profile base --origin http
    # Q2: JSC options in the UI process environment
    one env-useWasm-false-none --profile none --setenv JSC_useWasm=false
    one env-useWasm-false-base --profile base --setenv JSC_useWasm=false
    one env-useWasm-false-wasm-unsafe-eval --profile wasm-unsafe-eval --setenv JSC_useWasm=false
    one env-useWebAssembly-false --profile none --setenv JSC_useWebAssembly=false
    one env-failToCompileWasmCode --profile none --setenv JSC_failToCompileWasmCode=true
    one env-noIPInt-noBBQ --profile none --setenv JSC_useWasmIPInt=false --setenv JSC_useBBQJIT=false
    one env-useJIT-false --profile none --setenv JSC_useJIT=false
    # Q3: other ways
    one jsc-ui-useWasm-false --profile none --jsc-ui useWasm=false
    one ext-off-in-signal --profile none --ext-dir "$work/ext" --ext-mode off
    one ext-report --profile none --ext-dir "$work/ext" --ext-mode report --ext-early
    one ext-off-none --profile none --ext-dir "$work/ext" --ext-mode off --ext-early
    one ext-off-base --profile base --ext-dir "$work/ext" --ext-mode off --ext-early
    one ext-report-env --profile none --ext-dir "$work/ext" --ext-mode report --ext-early --setenv JSC_useWasm=false
    one us-delete-none --profile none --user-script delete
    one us-delete-base --profile base --user-script delete
    one us-lock-base --profile base --user-script lock
    one us-lock-none --profile none --user-script lock
    ;;
  timing)
    runs="${2:-5}"
    for i in $(seq 1 "$runs"); do
      one "timing-default-$i" --profile base
      one "timing-useWasm-false-$i" --profile base --setenv JSC_useWasm=false
      one "timing-ext-off-$i" --profile base --ext-dir "$work/ext" --ext-mode off --ext-early
    done
    ;;
esac

echo "created outside the repository:"
find "$work" -maxdepth 2 -not -path "$work/home/*" -not -path "$work/out/*" | sort
