#!/usr/bin/env bash
# Usage (from spikes/asset-packing): scripts/make-large.sh
# Builds the synthetic "large" frontend (real libraries, minified by Vite) into obj/inputs/large/.
set -eu
cd large
[ -d node_modules ] || npm ci --no-audit --no-fund
npx vite build
