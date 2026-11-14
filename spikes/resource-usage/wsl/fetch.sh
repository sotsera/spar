#!/usr/bin/env bash
# Usage (inside WSL, from the repository's spikes/resource-usage folder): bash wsl/fetch.sh
# Copies the working copy's results/linux/ back into this spike's results/linux/.
set -eu
spike=$(cd "$(dirname "$0")/.." && pwd)
source "$spike/wsl/env.sh"
mkdir -p "$spike/results/linux"
cp "$SPAR_WORK/repo/spikes/resource-usage/results/linux/"* "$spike/results/linux/"
echo "fetched $(ls "$spike/results/linux" | wc -l) files"
