#!/usr/bin/env bash
# Usage: tools/run-desktop.sh <label> <profile> <page> [host args...]
# Runs the published WebView2 host offscreen on the probe pages (and the built React app), with its log in
# artifacts/csp-navigation/windows/<label>.log. Run from anywhere; paths are relative to this script.
set -u
export MSYS_NO_PATHCONV=1
here=$(cd "$(dirname "$0")/.." && pwd)
label=$1; profile=$2; page=$3; shift 3
out="$here/../../artifacts/csp-navigation/windows"
mkdir -p "$out"
dist="frontend/dist"
[ -n "${SPAR_DIST:-}" ] && dist="frontend/$SPAR_DIST"
cd "$here"
"Csp.Windows/bin/Release/net11.0/win-x64/publish/Csp.Windows.exe" --offscreen --label "$label" --profile "$profile" --page "$page" \
  --root "$dist" --root pages "$@" >"$out/$label.log" 2>&1
echo "exit=$? log=artifacts/csp-navigation/windows/$label.log lines=$(wc -l <"$out/$label.log")"
