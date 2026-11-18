#!/usr/bin/env bash
# Usage: bash wsl/sandbox-paths.sh (inside WSL). Shows that WebKitGTK 6.0's bubblewrap sandbox aborts when
# XDG_RUNTIME_DIR lies under /var/tmp, and runs when only that folder moves to /dev/shm (common.sh's layout).
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
try() {
  local label=$1 runtime_under_var_tmp=$2
  isolate "paths-$label"
  if [ "$runtime_under_var_tmp" = yes ]; then
    cleanup_runtime
    export XDG_RUNTIME_DIR=$work/artifacts/linux-homes/paths-$label/runtime
    mkdir -p "$XDG_RUNTIME_DIR" && chmod 700 "$XDG_RUNTIME_DIR"
  fi
  xvfb_start 97
  dbus-run-session -- "$base/aot/Harness" journey --runs 1 --page "$work/page/dist" --out "$base/out" >"$base/logs/paths-$label.log" 2>&1
  echo "LAYOUT $label exit=$? HOME=$HOME XDG_RUNTIME_DIR=$XDG_RUNTIME_DIR: $(grep -m1 -E 'bwrap:|RESULT' "$base/logs/paths-$label.log")"
  xvfb_stop
  cleanup_runtime
}
try runtime-under-var-tmp yes
try runtime-in-dev-shm no
