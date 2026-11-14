#!/usr/bin/env bash
# Usage (on the development machine): linux/rebuild.sh   Syncs the sources into the container and republishes both variants.
export MSYS_NO_PATHCONV=1
docker exec spar-csp-linux bash /src/spikes/csp-navigation/linux/sync.sh
docker exec -w /work/spikes/csp-navigation spar-csp-linux bash -c 'linux/publish.sh gtk3 2>&1 | tail -1; linux/publish.sh gtk4 2>&1 | tail -1'
