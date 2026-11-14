#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/resource-usage): bash wsl/run-linux.sh <memory|disk|all> [runs]
# Each run is a fresh process with fresh data and cache folders under ../../artifacts/resource-usage/linux/, on a
# private Broadway display and D-Bus session (wsl/session.sh). Logs go to results/linux/.
set -u
source wsl/env.sh
what=${1:-all}
runs=${2:-5}
out=results/linux
data=../../artifacts/resource-usage/linux
mkdir -p "$out" "$data"

# Other WebKitGTK processes in this distribution share library pages and lower PSS; record them for every run.
foreign() { echo "FOREIGN $(pgrep -a -f 'WebKit(Web|Network|GPU)Process' | grep -v "$1" | wc -l) $(date -u +%H:%M:%S)"; }

memory() {
  for i in $(seq 1 "$runs"); do
    rm -rf "$data/mem-$i"
    bash wsl/session.sh host --scenario memory --data "mem-$i" >"$out/memory-$i.log" 2>&1
    echo "memory run $i: exit $? $(foreign "$data/mem-$i")" | tee -a "$out/runs.txt"
    rm -rf "$data/mem-$i"
  done
}

disk() {
  node tools/serve.mjs www 0 >"$out/serve.out" 2>&1 &
  local server=$!
  for _ in $(seq 1 50); do grep -q LISTENING "$out/serve.out" && break; sleep 0.1; done
  local port; port=$(awk '/LISTENING/{print $2}' "$out/serve.out")
  for i in $(seq 1 "$runs"); do
    for kind in realistic large; do
      local fill="ls=1024&idb=8&cache=4&http=40&size=100"
      [ "$kind" = large ] && fill="ls=4096&idb=64&cache=32&http=400&size=100"
      rm -rf "$data/disk-$kind-$i"
      bash wsl/session.sh host --scenario disk --data "disk-$kind-$i" --server "http://127.0.0.1:$port" --fill "$fill" --iterations 10 >"$out/disk-$kind-$i.log" 2>&1
      echo "disk $kind run $i: exit $? $(foreign x)" | tee -a "$out/runs.txt"
      rm -rf "$data/disk-$kind-$i"
    done
  done
  kill "$server"
  rm -f "$out/serve.out"
}

case "$what" in
  memory) memory ;;
  disk) disk ;;
  all) memory; disk ;;
esac
