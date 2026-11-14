#!/usr/bin/env bash
# Usage (from spikes/desktop-origin): scripts/host.sh <variant> <log name> <host args ...>
# Runs the published host of <variant> (v1, v2) and writes its output to artifacts/desktop-origin/<log name>.log.
set -u
variant=$1; name=$2; shift 2
out=../../artifacts/desktop-origin
timeout 180 Origin.Win/bin/$variant/Release/net11.0/win-x64/publish/Origin.Win.exe "$@" > "$out/$name.log" 2>&1
echo "$name exit=$?"
