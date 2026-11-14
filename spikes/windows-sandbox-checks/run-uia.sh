#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/windows-sandbox-checks/run-uia.sh <out folder> <label> [args...]
# Host-safe: opens the spike window offscreen without activation, holds it, and dumps its UI Automation tree with
# the native client (control view, then raw view; read-only). No input, no display or system change.
set -u
exe=spikes/windows-sandbox-checks/Checks.Desktop/bin/Release/net11.0/win-x64/publish/Checks.Desktop.exe
out=$1; label=$2; shift 2
mkdir -p "$out"
rm -f "${out:?}/${label:?}.hold"
timeout 90 "$exe" --out "$out" --label "$label" --profile uia --hold 6000 --after-ready 1500 "$@" >/dev/null 2>&1 &
for _ in $(seq 1 150); do [ -f "$out/$label.hold" ] && break; sleep 0.2; done
hwnd=$(cut -d' ' -f2 "$out/$label.hold")
"$exe" --uia-dump "$hwnd" "$out/$label-control.txt"
"$exe" --uia-dump "$hwnd" "$out/$label-raw.txt" raw
wait
