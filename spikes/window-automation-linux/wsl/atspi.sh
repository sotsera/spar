#!/usr/bin/env bash
# Usage: bash wsl/atspi.sh <run> (inside WSL). The tree verb holds three page states; atspi.py reads each over AT-SPI in
# the same private D-Bus session, then probes/compare-trees.mjs compares the two views.
set -uo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
run=$1
isolate "$run"
mkdir -p "$base/logs" "$base/out"
xvfb_start 97
hold=$base/out/hold-$run
rm -f "$hold"-*
log=$base/logs/$run.log
dbus-run-session -- bash -c "
  '$base/aot/Harness' tree --hold '$hold' --page '$work/page/dist' --out '$base/out' >'$log' 2>&1 &
  python3 -I '$spike/probes/atspi.py' '$log' '$hold' '$base/out' >'$base/logs/$run-atspi.log' 2>&1
  wait
  echo \"RUN exit=\$?\" >>'$log'"
xvfb_stop
cleanup_runtime
grep -E "TREE|HOLD|FATAL|RUN" "$log"
cat "$base/logs/$run-atspi.log"
