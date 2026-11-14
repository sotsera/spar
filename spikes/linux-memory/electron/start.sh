#!/usr/bin/env bash
# Usage (from spikes/linux-memory, after wsl/env.sh): bash electron/start.sh LABEL FIFO [electron switches...]
# Starts the Electron reference as a child (run.sh measures the electron process tree under this shell).
label=$1; fifo=$2; shift 2
platform=$([ "${GDK_BACKEND:-wayland}" = x11 ] && echo x11 || echo wayland)
art=$(cd ../.. && pwd)/artifacts
profile=$art/linux-memory/electron-profiles/$label
mkdir -p "$profile"
# NSS and NSPR are not installed in this WSL distribution; install.sh extracts them into the working folder.
export LD_LIBRARY_PATH=$SPAR_WORK/electron-libs/root/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
electron/node_modules/electron/dist/electron --ozone-platform="$platform" "$@" electron/ "$label" "$fifo" "$profile" "$(pwd)/web"
