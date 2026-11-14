#!/usr/bin/env bash
# Usage (in the container): bash /src/spikes/csp-navigation/linux/sync.sh
# Copies the repository's root build files and this spike from the read-only /src mount into /work
# (no bin, obj or node_modules; the built frontend dist folders are included).
set -eu
mkdir -p /work/spikes/csp-navigation
cp /src/global.json /src/Directory.Build.props /src/Directory.Packages.props /src/.editorconfig /work/
cd /src/spikes/csp-navigation
tar cf - --exclude=bin --exclude=obj --exclude=node_modules Directory.Build.props Directory.Packages.props Csp.Shared Csp.Linux pages linux \
  frontend/dist frontend/dist-sri frontend/dist-inline | (cd /work/spikes/csp-navigation && tar xf -)
