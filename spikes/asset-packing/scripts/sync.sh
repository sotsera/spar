#!/usr/bin/env bash
# Usage (in the container): bash /src/spikes/asset-packing/scripts/sync.sh
# Copies the repository's root build files and this spike (sources, plus the built obj/inputs bundles) from the
# read-only /src mount into /work, so no Windows bin/ or obj/ is shared.
set -eu
mkdir -p /work/spikes/asset-packing/obj
cd /src && cp Directory.Build.props Directory.Packages.props global.json .editorconfig /work/
cd /src/spikes/asset-packing
tar cf - --exclude=bin --exclude=obj --exclude=node_modules . | (cd /work/spikes/asset-packing && tar xf -)
tar cf - obj/inputs | (cd /work/spikes/asset-packing && tar xf -)
