#!/usr/bin/env bash
# Runs one in-container task (docker/in-container.sh) on Docker Desktop's engine (Git Bash on the development machine).
# Usage: bash tools/container.sh <ubuntu|fedora> <nodri|dxg-norender|dxg-render> <task> [args]
#   nodri         no GPU device at all
#   dxg-norender  /dev/dri and /dev/dxg plus the WSL D3D12 libraries, user NOT in the render node's group (as WSL now)
#   dxg-render    the same, user in the render node's group (only inside this container: --group-add)
# The container runs as uid 1000 with seccomp=unconfined so bubblewrap can build the web-process sandbox.
set -euo pipefail
spike_unix="$(cd "$(dirname "$0")/.." && pwd)"
spike="$(cd "$spike_unix" && pwd -W)"
repo="$(cd "$spike_unix/../.." && pwd -W)"
export MSYS_NO_PATHCONV=1
image=$1 env=$2 task=$3; shift 3
out="$spike_unix/results/$image-$env"
mkdir -p "$out"
flags=()
case "$env" in
  nodri) ;;
  dxg-norender|dxg-render)
    flags+=(--device /dev/dri --device /dev/dxg -v /usr/lib/wsl:/usr/lib/wsl:ro -e LD_LIBRARY_PATH=/usr/lib/wsl/lib)
    if [ "$env" = dxg-render ]; then
      gid=$(docker run --rm --device /dev/dri "spar-spike-linux-gpu-csp:$( [ "$image" = ubuntu ] && echo ubuntu24.04 || echo fedora43)" stat -c %g /dev/dri/renderD128)
      flags+=(--group-add "$gid")
    fi ;;
  *) echo "unknown env $env"; exit 2 ;;
esac
tag=$( [ "$image" = ubuntu ] && echo ubuntu24.04 || echo fedora43)
docker run --rm --init --shm-size=1g --security-opt seccomp=unconfined -u 1000 \
  --name "spar-spike-linux-gpu-csp-$image-$env-$task" "${flags[@]}" -e "LABEL=$image-$env" \
  -v "$spike:/spike:ro" -v "$repo/artifacts/linux-gpu-csp:/probe:ro" -v "$(cd "$out" && pwd -W):/out" \
  "spar-spike-linux-gpu-csp:$tag" bash /spike/docker/in-container.sh "$task" "$@"
