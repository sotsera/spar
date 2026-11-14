#!/usr/bin/env bash
# Usage (Git Bash, from spikes/page-heartbeat): scripts/run-win-all.sh — every host-side Windows group, in order.
set -u
cd "$(dirname "$0")/.."
bash scripts/run-win.sh detect 5
bash scripts/run-win.sh load 5
bash scripts/run-win.sh healthy 3
bash scripts/run-win.sh pf 3
bash scripts/run-win.sh stress 3
bash scripts/run-win.sh recover 5
bash scripts/run-win.sh edge-kill 2
bash scripts/run-win.sh webview 3
echo ALL-DONE
