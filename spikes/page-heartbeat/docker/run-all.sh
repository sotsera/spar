#!/usr/bin/env bash
# Usage (Git Bash, from spikes/page-heartbeat): docker/run-all.sh — every Linux group except cpu, one container each.
set -u
cd "$(dirname "$0")/.."
for g in "detect 5" "load 5" "healthy 3" "wk 3" "stress 3" "recover 5" "states 3"; do bash docker/run.sh $g; done
echo ALL-DONE
