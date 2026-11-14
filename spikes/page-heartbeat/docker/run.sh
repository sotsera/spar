#!/usr/bin/env bash
# Usage (Git Bash, from spikes/page-heartbeat): docker/run.sh <group|case> [runs]
# One throwaway container (spar-startup-prereq:webkit: Ubuntu 24.04, WebKitGTK 2.52.6 / 6.0, GTK 4.14.5, Xvfb, openbox,
# xdotool, x11-apps) per call, removed at the end. The spike folder is mounted read-only; logs land in
# results/linux/ (mounted as /out). seccomp=unconfined lets bubblewrap create the web process's namespaces.
set -eu
export MSYS_NO_PATHCONV=1
spike=$(pwd -W)
mkdir -p results/linux
docker run --rm --init --name "spar-page-heartbeat-$1" --security-opt seccomp=unconfined \
  -v "$spike:/spike:ro" -v "$spike/results/linux:/out" \
  spar-startup-prereq:webkit bash /spike/docker/in-container.sh "$@"
