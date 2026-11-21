#!/usr/bin/env bash
# Usage (Windows, repository root): wsl -d Ubuntu-24.04 --exec bash spikes/linux-gpu-csp/wsl/csp.sh <csp|accel>
# csp:   the connect-src probes on the distribution's own WebKitGTK, under an Xvfb of our own and a private D-Bus
#        session ("app" does not resolve here, so the app:port probes only show the CSP side).
# accel: webkit://gpu, GSK renderer and memory, defaults against the software-GPU default.
set -uo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
source "$here/wsl/common.sh"
task=${1:-csp}
isolate "$task"
export NO_AT_BRIDGE=1 GTK_A11Y=none
xvfb_start 92
out=$here/results/wsl
mkdir -p "$out/$task"
case "$task" in
  csp)
    for profile in spar self none; do
      for i in 1 2; do
        dbus-run-session -- python3 -I "$here/csp/csp_host.py" "$here/csp/pages" "$out/csp/$profile-$i.json" \
          --profile "$profile" --label "wsl-$profile-$i" >"$out/csp/$profile-$i.log" 2>&1
        echo "csp $profile-$i exit=$?"
      done
    done ;;
  accel)
    run() {
      local name=$1 policy=$2; shift 2
      for i in 1 2 3; do
        env "$@" dbus-run-session -- python3 -I "$here/csp/accel_host.py" "$out/accel/$name-$i.json" --policy "$policy" \
          --label "$name-$i" >"$out/accel/$name-$i.log" 2>&1
        echo "accel $name-$i exit=$?"
      done
    }
    run defaults default
    run never-cairo never GSK_RENDERER=cairo
    run dmabuf-off-cairo default GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1
    run never-only never ;;
esac
xvfb_stop
cleanup_runtime
