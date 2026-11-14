#!/usr/bin/env bash
# Usage (in the container, as root): bash /src/spikes/theme-startup/linux/sync.sh
# Copies the repository's root build files and this spike's Linux sources and built frontend from the read-only /src
# mount into /work (bin and obj are not copied; earlier bin/obj in /work are kept), then builds the capture tool.
set -eu
mkdir -p /work/spikes/theme-startup /work/home
cp /src/global.json /src/Directory.Build.props /src/Directory.Packages.props /src/.editorconfig /work/
cd /src/spikes/theme-startup
tar cf - --exclude=bin --exclude=obj Theme.Linux linux frontend/dist | (cd /work/spikes/theme-startup && tar xf -)
clang -O2 -Wall -o /work/spikes/theme-startup/linux/xcap /work/spikes/theme-startup/linux/xcap.c -lX11 -lz
chown -R 1000:1000 /work/home
echo "synced; xcap built"
