#!/usr/bin/env bash
# Runs each published WebView2 host N times (offscreen window, warm profile after the first run) and prints the
# startup marks, the page's fetch/postMessage medians and the CDP timings of every run.
set -u
cd "$(dirname "$0")"
runs=${1:-5}
for binding in Com WinRT; do
  exe="WebView2.App/bin/publish/$binding/WebView2.App.exe"
  for i in $(seq 1 "$runs"); do
    output=$(timeout 90 "$exe" 2>&1)
    status=$?
    echo "$binding run=$i exit=$status"
    echo "$output" | grep -E "process start|SPIKE_MARKS|PAGE|CDP Runtime|failure" | sed 's/^/    /'
  done
done
