#!/usr/bin/env bash
# Usage: bash wsl/collect.sh (inside WSL). Copies the logs, trees and PNGs of the runs into the spike's results/,
# with the repository path and the user name replaced by placeholders, and the GLib/portal noise of the private
# D-Bus session kept (it is part of what a run prints).
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
source "$here/wsl/common.sh"
results=$here/results
mkdir -p "$results/linux"
repo=$(cd "$here/../.." && pwd)
user=$(id -un)
redact() { sed -e "s#$repo#<repo>#g" -e "s#/home/$user#<home>#g" -e "s#\b$user\b#<user>#g" -e "s#$base#<work>#g"; }
for f in "$base"/logs/*.log; do redact <"$f" >"$results/linux/$(basename "$f")"; done
cp "$base"/out/*.png "$results/linux/" 2>/dev/null || true
for f in "$base"/out/tree-*.json; do redact <"$f" >"$results/linux/$(basename "$f")"; done
grep -l -i -E "$user|C:\\\\Users|ALESSA" "$results"/linux/* 2>/dev/null && echo "WARNING: identifying text left in the files above" || echo "collected into results/linux (no user name or profile path found)"
