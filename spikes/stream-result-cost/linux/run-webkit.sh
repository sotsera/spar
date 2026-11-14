#!/usr/bin/env bash
# Runs the page benchmark in WebKitGTK 6.0 under WSL. Build first on Windows (node tools/build.ts), then, from the
# repository root:
#   wsl -d Ubuntu-24.04 -- bash spikes/stream-result-cost/linux/run-webkit.sh [full|quick|retained|all] [runs]
# full: <runs> benchmark page loads; retained: one fresh process per (combo, window) RSS reading, <runs> rounds.
# Everything the run creates outside the repository stays in ~/spar-spikes/stream-result-cost/: HOME, every
# XDG_*_HOME and XDG_RUNTIME_DIR point there (the display socket is reached through a symlink), the network session is
# ephemeral and no inspector server is enabled. Reports are copied to results/raw/.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
spike="$(cd "$here/.." && pwd)"
mode="${1:-all}"
runs="${2:-3}"
work="$HOME/spar-spikes/stream-result-cost"
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

mkdir -p "$spike/results/raw/retained"
if [ "$mode" = full ] || [ "$mode" = quick ] || [ "$mode" = all ]; then
  for run in $(seq 1 "$runs"); do
    if [ "$mode" = quick ]; then name="webkitgtk-quick-$run"; arg="quick=1"; else name="webkitgtk-$run"; arg=""; fi
    python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/$name.json" $arg
    cp "$work/out/$name.json" "$spike/results/raw/$name.json"
  done
fi

if [ "$mode" = retained ] || [ "$mode" = all ]; then
  for run in $(seq 1 "$runs"); do
    for window in 8 64; do
      for combo in plain resultMatch leanMatch eagerMatch batch tail genPlain genResult; do
        name="retained-webkitgtk-$combo-w$window-$run"
        python3 -I "$work/webkit_bench.py" "$work/page/bench.html" "$work/out/$name.json" "mode=retained&combo=$combo&window=$window" >/dev/null
      done
    done
  done
  cp "$work"/out/retained-webkitgtk-*.json "$spike/results/raw/retained/"
fi

echo "created outside the repository:"
find "$work" -maxdepth 3 -not -path "$work/home/*" | sort
