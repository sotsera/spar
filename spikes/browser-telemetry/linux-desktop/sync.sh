#!/usr/bin/env bash
# Usage (in the container): sync.sh   Copies the repository's root build files and this spike's sources from the
# read-only /src mount into /work (bin, obj and node_modules are not copied; earlier bin/obj in /work are kept).
set -eu
mkdir -p /work/spikes/browser-telemetry
cp /src/global.json /src/Directory.Build.props /src/Directory.Packages.props /src/.editorconfig /work/
cd /src/spikes/browser-telemetry
tar cf - --exclude=bin --exclude=obj --exclude=node_modules d1-pipeline linux-desktop react/React.Telemetry react/React.App/SparDiagnostics.cs react/Directory.Packages.props \
  | (cd /work/spikes/browser-telemetry && tar xf -)