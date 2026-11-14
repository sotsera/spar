#!/usr/bin/env bash
# Usage (in the container): sync.sh   Copies this spike's sources from the read-only /src mount into /work (bin/obj kept).
set -eu
cd /src/spikes/linux-desktop && tar cf - --exclude=bin --exclude=obj . | (cd /work/spikes/linux-desktop && tar xf -)
