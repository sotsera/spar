#!/usr/bin/env bash
# Usage (Git Bash, in spikes/resource-usage): bash scripts/run-win.sh [memory|disk|shared|all] [runs]
# Runs the Windows host's scenarios. Logs go to ../../artifacts/resource-usage/win/ and are copied to results/win/.
# Every user-data folder is under ../../artifacts/webview2-profiles/resource-usage/ and is deleted only after the
# host that used it has waited for its browser to exit.
set -u
what=${1:-all}
runs=${2:-5}
spike=$(pwd)
exe="$spike/Res.Win/bin/publish/Res.Win.exe"
logs="$spike/../../artifacts/resource-usage/win"
profiles="$spike/../../artifacts/webview2-profiles/resource-usage"
mkdir -p "$logs" "$spike/results/win"

fresh() { rm -rf "$profiles/$1"; }

# Task Manager's "Memory (private working set)" is the "Working Set - Private" performance counter; read it with the
# matching process ids, plus Private Bytes and Working Set, for every msedgewebview2 instance.
counters() {
  powershell.exe -NoProfile -NonInteractive -Command '
    $s = (Get-Counter -ErrorAction SilentlyContinue -Counter "\Process(msedgewebview2*)\ID Process","\Process(msedgewebview2*)\Working Set - Private","\Process(msedgewebview2*)\Private Bytes","\Process(msedgewebview2*)\Working Set").CounterSamples
    $by = @{}
    foreach ($c in $s) { $i = [regex]::Match($c.Path, "\(([^)]*)\)").Groups[1].Value; if (-not $by[$i]) { $by[$i] = @{} }; $by[$i][($c.Path -split "\\")[-1]] = $c.CookedValue }
    foreach ($i in $by.Keys) { $v = $by[$i]; "COUNTER pid={0} wsPrivateKB={1} privateBytesKB={2} workingSetKB={3}" -f [int]$v["id process"], [long]($v["working set - private"]/1024), [long]($v["private bytes"]/1024), [long]($v["working set"]/1024) }
  ' | tr -d '\r'
}

memory() {
  for i in $(seq 1 "$runs"); do
    local name="mem-$i" log="$logs/memory-$i.log" go="$logs/memory.go"
    fresh "$name"
    rm -f "$go"
    "$exe" host --scenario memory --udf "$name" --go-file "$(cygpath -w "$go")" --wait-exit >"$log" 2>&1 &
    local pid=$!
    for _ in $(seq 1 600); do grep -q '^READY' "$log" && break; grep -q '^HOST-' "$log" && break; sleep 0.2; done
    counters >"$logs/memory-$i.counters.txt"
    touch "$go"
    wait "$pid"
    echo "memory run $i: exit $? ($(grep -c '' "$log") lines)"
    rm -f "$go"
    fresh "$name"
  done
}

disk() {
  node tools/serve.mjs www 0 >"$logs/serve.log" 2>&1 &
  local server=$!
  for _ in $(seq 1 50); do grep -q LISTENING "$logs/serve.log" && break; sleep 0.1; done
  local port; port=$(awk '/LISTENING/{print $2}' "$logs/serve.log")
  local -A fills=(
    [realistic]="ls=1024&idb=8&cache=4&http=40&size=100"
    [large]="ls=4096&idb=64&cache=32&http=400&size=100"
  )
  for i in $(seq 1 "$runs"); do
    for kind in realistic large; do
      local name="disk-$kind-$i"
      fresh "$name"
      "$exe" host --scenario disk --udf "$name" --server "http://127.0.0.1:$port" --fill "${fills[$kind]}" --iterations 10 >"$logs/disk-$kind-$i.log" 2>&1
      echo "disk $kind run $i: exit $?"
      fresh "$name"
    done
  done
  kill "$server"
}

shared() {
  for i in $(seq 1 "$runs"); do
    fresh shared-x; fresh shared-y
    "$exe" host --scenario shared --udf shared-x --instance a --stay 14000 --every 1000 --wait-exit >"$logs/shared-$i-a.log" 2>&1 &
    local a=$!
    sleep 4
    "$exe" host --scenario shared --udf shared-x --instance b --stay 5000 --every 1000 --wait-exit >"$logs/shared-$i-b.log" 2>&1 &
    local b=$!
    "$exe" host --scenario shared --udf shared-y --instance c --stay 5000 --every 1000 --wait-exit >"$logs/shared-$i-c.log" 2>&1 &
    local c=$!
    wait "$a" "$b" "$c"
    echo "shared run $i done"
    fresh shared-x; fresh shared-y
  done
}

case "$what" in
  memory) memory ;;
  disk) disk ;;
  shared) shared ;;
  all) memory; disk; shared ;;
esac
cp "$logs"/*.log "$logs"/*.txt "$spike/results/win/" 2>/dev/null
rm -f "$spike/results/win/serve.log"
