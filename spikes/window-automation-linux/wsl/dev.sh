#!/usr/bin/env bash
# Usage: bash wsl/dev.sh <run> <verb> [args...] (inside WSL): sync, publish, then one AOT run on display :97; prints the log.
here=$(cd "$(dirname "$0")/.." && pwd)
bash "$here/wsl/sync.sh" >/dev/null && bash "$here/wsl/publish.sh" | grep -v "^$" || exit 1
run=$1; shift
bash "$here/wsl/run.sh" "$run" 97 aot "$@" >/dev/null
grep -v -E "libEGL warning" /var/tmp/spar-linux/window-automation-linux/logs/$run.log
