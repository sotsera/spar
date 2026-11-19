#!/usr/bin/env bash
# Usage: bash wsl/run.sh <run> <display> <aot|jit> <verb> [args...] (inside WSL). One isolated run: private HOME and XDG
# folders, a private D-Bus session (dbus-run-session), its own Xvfb; the harness's output goes to $base/logs/<run>.log.
set -uo pipefail
source "$(cd "$(dirname "$0")" && pwd)/common.sh"
run=$1 display=$2 flavour=$3
shift 3
census() {
  # Only this spike's processes: they inherit XDG_RUNTIME_DIR=/dev/shm/spar-wal-<run> (other spikes may share the distro).
  for p in $(grep -l -a "XDG_RUNTIME_DIR=/dev/shm/spar-wal-" /proc/[0-9]*/environ 2>/dev/null | cut -d/ -f3); do
    [ "$p" = "$$" ] || cat "/proc/$p/comm" 2>/dev/null
  done | grep -v -x -E "bash|grep|cut|cat|sleep|sort|uniq|tr" | sort | uniq -c | tr -s " " | tr "\n" ";"
}
before=$(census)
isolate "$run"
mkdir -p "$base/logs" "$base/out"
xvfb_start "$display"
start=$(date +%s%N)
dbus-run-session -- "$base/$flavour/Harness" "$@" --page "$work/page/dist" --out "$base/out" >"$base/logs/$run.log" 2>&1
code=$?
echo "RUN exit=$code wall=$(( ($(date +%s%N) - start) / 1000000 ))ms" >>"$base/logs/$run.log"
xvfb_stop
cleanup_runtime
sleep 0.5
echo "PROCESSES before=[$before] after=[$(census)]" >>"$base/logs/$run.log"
tail -3 "$base/logs/$run.log"
exit $code
