#!/usr/bin/env bash
# Runs the probe subset on WebKitGTK 2.54.2 in the Fedora 43 image (build it first, see docker/Dockerfile).
# Usage (Git Bash on the development machine, from the spike folder): bash tools/fedora.sh
# The container runs as uid 1000 with seccomp=unconfined so bubblewrap can create the web process sandbox.
set -euo pipefail
spike="$(cd "$(dirname "$0")/.." && pwd -W)"
export MSYS_NO_PATHCONV=1
mkdir -p "$(dirname "$0")/../results/fedora-2.54"
docker run --rm --init --shm-size=1g --security-opt seccomp=unconfined -u 1000 \
  -v "$spike:/spike:ro" -v "$spike/results/fedora-2.54:/out" \
  spar-webkit-wasm-off:fedora43 bash /spike/docker/in-container.sh
