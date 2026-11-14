#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/page-heartbeat/sandbox/run-all.sh [groups...]
# Runs the guest groups one after another in the running sandbox (sandbox/start.sh and connect.sh first).
set -u
groups=${*:-base states gated input dialog}
for g in $groups; do bash spikes/page-heartbeat/sandbox/guest.sh "$g" 3; echo "group $g done $(date +%H:%M:%S)"; done
echo ALL-DONE
