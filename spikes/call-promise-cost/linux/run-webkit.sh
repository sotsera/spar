#!/usr/bin/env bash
# Runs the page benchmark in WebKitGTK 6.0 under WSL. Build first on Windows (node tools/build.ts), then, from the
# repository root:
#   wsl -d Ubuntu-24.04 -- bash spikes/call-promise-cost/linux/run-webkit.sh [full|quick|memory|order|all] [runs]
# full: 3 benchmark page loads; memory: one fresh process per (variant, style) RSS reading, 3 rounds; order: the
# same-variant settle-order check; all: every mode. Everything the run creates outside the repository stays in ~/spar-spikes/call-promise-cost/:
# HOME, every XDG_*_HOME and XDG_RUNTIME_DIR point there (the display socket is reached through a symlink), the
# network session is ephemeral and no inspector server is enabled. Reports are copied to results/raw/webkitgtk-<run>.json.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
spike="$(cd "$here/.." && pwd)"
mode="${1:-all}"
runs="${2:-3}"
work="$HOME/spar-spikes/call-promise-cost"
private="$work/home"
real_runtime="${XDG_RUNTIME_DIR:?XDG_RUNTIME_DIR must be set}"
mkdir -p "$work/page" "$work/out" "$work/run" "$private/.config" "$private/.local/share" "$private/.cache" "$private/.local/state"
chmod 700 "$work/run"
cp "$spike/obj/bundle/bench.html" "$spike/obj/bundle/bench-page.js" "$work/page/"
cp "$here/webkit_bench.py" "$work/"
if [ -n "${WAYLAND_DISPLAY:-}" ] && [ -e "$real_runtime/$WAYLAND_DISPLAY" ] && [ ! -e "$work/run/$WAYLAND_DISPLAY" ]; then
  ln -s "$real_runtime/$WAYLAND_DISPLAY" "$work/run/$WAYLAND_DISPLAY"
fi

export HOME="$private"
export XDG_CONFIG_HOME="$private/.config"
export XDG_DATA_HOME="$private/.local/share"
export XDG_CACHE_HOME="$private/.cache"
export XDG_STATE_HOME="$private/.local/state"
export XDG_RUNTIME_DIR="$work/run"
export NO_AT_BRIDGE=1
unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER

mkdir -p "$spike/results/raw/memory"
if [ "$mode" = full ] || [ "$mode" = quick ] || [ "$mode" = all ]; then
  for run in $(seq 1 "$runs"); do
    if [ "$mode" = quick ]; then name="webkitgtk-quick-$run"; arg="quick=1"; else name="webkitgtk-$run"; arg=""; fi
    python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/$name.json" $arg
    cp "$work/out/$name.json" "$spike/results/raw/$name.json"
  done
fi

if [ "$mode" = memory ] || [ "$mode" = all ]; then
  for run in $(seq 1 "$runs"); do
    python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/memory-control-$run.json" "mode=memory&style=control&count=100000"
    for variant in native formC subclass of noSpecies ctor; do
      for style in none await match unwrap; do
        name="memory-webkitgtk-$variant-$style-$run"
        python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/$name.json" "mode=memory&variant=$variant&style=$style&count=100000" >/dev/null
      done
    done
    mv "$work/out/memory-control-$run.json" "$work/out/memory-webkitgtk-control-control-$run.json"
  done
  cp "$work"/out/memory-webkitgtk-*.json "$spike/results/raw/memory/"
fi

if [ "$mode" = order ] || [ "$mode" = all ]; then
  mkdir -p "$spike/results/raw/order"
  python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/order-webkitgtk.json" "mode=order"
  cp "$work/out/order-webkitgtk.json" "$spike/results/raw/order/order-webkitgtk.json"
fi

echo "created outside the repository:"
find "$work" -maxdepth 3 -not -path "$work/home/*" | sort
