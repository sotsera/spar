#!/usr/bin/env bash
# Usage (Git Bash on Windows, from spikes/linux-wsl-desktop):
#   win/capture-run.sh "<wincap options>" "<environment for the app>" <gtk3|gtk3sb|gtk4> <label> [app args...]
# Starts win/wincap.ps1 for the window titled "Spar WSL <label>", waits until it is ready, runs the app in WSL through
# wsl/run.sh (in the WSL working copy), then waits for the capture. Capture output: artifacts/linux-wsl-desktop/win/<label>/.
set -u
capture=$1; environment=$2; variant=$3; label=$4; shift 4
distro=${SPAR_WSL_DISTRO:-Ubuntu-24.04}
out=../../artifacts/linux-wsl-desktop/win/$label
rm -rf "$out"; mkdir -p "$out"
# shellcheck disable=SC2086
pwsh -NoProfile -File win/wincap.ps1 -Title "Spar WSL $label" -Out "$out" $capture >"$out/wincap.log" 2>&1 &
cap=$!
for _ in $(seq 1 200); do [ -f "$out/wincap.ready" ] && break; sleep 0.05; done
MSYS_NO_PATHCONV=1 wsl -d "$distro" -e bash -c "cd ~/spar-wsl-desktop/spikes/linux-wsl-desktop && env $environment bash wsl/run.sh $variant $label $*"
wait $cap
echo "SPAR_CAPTURE $label exit=$? $(grep -c '"i":' "$out/wincap.jsonl" 2>/dev/null) frames, $(ls "$out"/*.png 2>/dev/null | wc -l) png"
