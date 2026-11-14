#!/usr/bin/env bash
# Runs the page's micro benchmark in WebKitGTK under WSL, three times. From the repository root on Windows:
#   wsl -d Ubuntu-24.04 -- bash spikes/bridge-encoding/linux/run-webkit.sh
# Needs artifacts/bridge-encoding/corpus.js (Bridge.Bench corpus). Everything the run creates outside the repository
# stays in ~/spar-spikes/bridge-encoding/ (HOME and every XDG_*_HOME point there; the web context is ephemeral).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
spike="$(cd "$here/.." && pwd)"
repo="$(cd "$spike/../.." && pwd)"
work="$HOME/spar-spikes/bridge-encoding"
private="$work/home"
mkdir -p "$work/page" "$work/out" "$private/.config" "$private/.local/share" "$private/.cache" "$private/.local/state"
cp "$spike/page/bench.html" "$spike/page/bench.js" "$repo/artifacts/bridge-encoding/corpus.js" "$work/page/"
cp "$here/webkit_micro.py" "$work/"

export HOME="$private"
export XDG_CONFIG_HOME="$private/.config"
export XDG_DATA_HOME="$private/.local/share"
export XDG_CACHE_HOME="$private/.cache"
export XDG_STATE_HOME="$private/.local/state"
export NO_AT_BRIDGE=1
unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER

for run in ${RUNS:-1 2 3}; do
  python3 -I "$work/webkit_micro.py" "$work/page/bench.html" "$work/out/micro-A-$run.json"
done

mkdir -p "$repo/artifacts/bridge-encoding/webkitgtk"
cp "$work"/out/micro-A-*.json "$repo/artifacts/bridge-encoding/webkitgtk/"
echo "created outside the repository:"
find "$work" -maxdepth 3 | sort
