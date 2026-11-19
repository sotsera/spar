#!/usr/bin/env bash
# Usage (Windows, repository root): wsl -d Ubuntu-24.04 --exec bash spikes/linux-gpu-csp/wsl/gpu.sh <series|gdk> [rounds] [cold]
# series: every detection method as separate processes (cold runs after dropping the libraries' page cache, then
#         interleaved warm rounds), with no display variables at all (EGL never reaches WSLg).
# gdk:    GDK's own GL signal and the GSK renderer, under an Xvfb of our own, inside a private D-Bus session.
# Results: results/wsl/<mode>.jsonl in the spike folder.
set -euo pipefail
here=$(cd "$(dirname "$0")/.." && pwd)
source "$here/wsl/common.sh"
mode=${1:-series}; rounds=${2:-25}; cold=${3:-5}
out=$here/results/wsl
mkdir -p "$out"
probe=$base/aot/GpuProbe
isolate "gpu-$mode"
export NO_AT_BRIDGE=1 GTK_A11Y=none
case "$mode" in
  series)
    unset DISPLAY WAYLAND_DISPLAY
    rm -f "$out/series.jsonl"
    python3 -I "$here/tools/series.py" "$probe" "$out/series.jsonl" wsl-user "$rounds" "$cold" \
      none node combined devices egl+--platform+surfaceless+--no-context egl+--platform+surfaceless \
      egl+--platform+device egl+--platform+default
    ;;
  gdk)
    xvfb_start 91
    rm -f "$out/gdk.jsonl"
    dbus-run-session -- python3 -I "$here/tools/series.py" "$probe" "$out/gdk.jsonl" wsl-user-xvfb "$rounds" "$cold" \
      gdk gdk+--window gdk+--window+--gsk-after-init+cairo gdk+--pre-egl+--window
    xvfb_stop
    ;;
esac
cleanup_runtime
