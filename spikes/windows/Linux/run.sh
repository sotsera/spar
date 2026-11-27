#!/bin/bash
# Runs the Linux probe in Spar's Linux CI image, on a private Xvfb with a session bus of its own and a private home,
# with the core limit at 1 byte. "wm" starts openbox first (when installed in the container).
#   bash run.sh <label> [wm]
set -eu
label=$1
wm=${2:-}
here=$(cd "$(dirname "$0")" && pwd)
home=/home/spar/linux-homes/windows-spike-$label
rm -rf "$home" && mkdir -p "$home"/{data,cache,config,runtime}
chmod 700 "$home/runtime"
display=:$((90 + RANDOM % 9))
Xvfb "$display" -screen 0 1920x1080x24 -nolisten tcp >/dev/null 2>&1 &
xvfb=$!
trap 'kill $xvfb 2>/dev/null || true' EXIT
sleep 1
export DISPLAY=$display GDK_BACKEND=x11 HOME=$home XDG_DATA_HOME=$home/data XDG_CACHE_HOME=$home/cache \
  XDG_CONFIG_HOME=$home/config XDG_RUNTIME_DIR=$home/runtime LIBGL_ALWAYS_SOFTWARE=1
if [ "$wm" = wm ]; then
  openbox >/dev/null 2>&1 &
  export SPIKE_WM=openbox
  sleep 1
fi
mkdir -p "$here/results"
prlimit --core=1 dbus-run-session -- dotnet "$here/bin/Release/net11.0/Windows.Linux.dll" \
  "$here/results/linux-$label.json" "$home/webkit"
