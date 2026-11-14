#!/usr/bin/env bash
# Runs one probe with the compiled extension (in the work folder) and extra host arguments.
# Usage: bash linux/ext-run.sh <label> <off|report> [host args...]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
label="$1"; mode="$2"; shift 2
exec bash "$here/run.sh" one "$label" --ext-dir "$HOME/spar-spikes/webkit-wasm-off/ext" --ext-mode "$mode" "$@"
