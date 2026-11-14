#!/usr/bin/env bash
# Every measured run, one after the other (never concurrently). From this folder, after npm install:
#   bash tools/run-all.sh
set -euo pipefail
cd "$(dirname "$0")/.."
npx tsc -p tsconfig.json
node tools/build.ts
for run in 1 2 3; do node --expose-gc obj/bundle/bench-node.mjs --out results/raw/node-$run.json; echo "node $run done"; done
for run in 1 2 3; do node --expose-gc --min-semi-space-size=64 obj/bundle/bench-node.mjs --alloc --out results/raw/alloc-node-$run.json; echo "alloc node $run done"; done
node tools/run-webview2.ts --runs 3
node tools/run-webview2.ts --runs 3 --query mode=alloc --name alloc-webview2 --semi 64
(cd ../.. && wsl -d Ubuntu-24.04 -- bash spikes/stream-result-cost/linux/run-webkit.sh all 3 2>&1 | grep -v "EGL\|MESA\|^$" | tail -3)
node tools/summarize.ts
