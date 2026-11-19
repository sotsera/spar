#!/usr/bin/env bash
# When do WebView2 windows of one environment share a renderer? Runs spikes/webview-profiles' published host (its "multi"
# scenario: every window navigates to https://app.localhost, one after the other) with four windows in one user-data
# folder, with and without --renderer-process-limit=1, on distinct InPrivate profiles (as Spar's test windows), on one
# InPrivate profile, and on the default profile (as an app's windows). The host's output is buffered to its exit, so the
# renderer processes of that folder are polled every 0.5 s for 6 s while the windows stay open (--stay 8000), and the
# largest count seen is kept. Git Bash, from the repository root. Needs spikes/webview-profiles/Profiles.Win/bin/publish.
set -u
exe=spikes/webview-profiles/Profiles.Win/bin/publish/Profiles.Win.exe
out=spikes/renderer-sharing/results/process-limit.txt
: > "$out"
scan() {
  pwsh -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Where-Object { \$_.CommandLine -match 'webview-profiles.$1.EBWebView' -and \$_.CommandLine -match '--type=renderer' } | ForEach-Object { \$_.ProcessId }" | tr -d '\r' | sort -n | tr '\n' ' '
}
run() {
  local label=$1 windows=$2 args=$3 udf=$4
  "$exe" harness clean --udf "$udf" > /dev/null
  local log; log=$(mktemp)
  if [ -n "$args" ]; then
    "$exe" host --reputation off --scenario multi --windows "$windows" --stay 8000 --exit wait --browser-args "$args" > "$log" 2>&1 &
  else
    "$exe" host --reputation off --scenario multi --windows "$windows" --stay 8000 --exit wait > "$log" 2>&1 &
  fi
  local pid=$! best="" bestCount=0
  for _ in $(seq 1 12); do
    sleep 0.5
    local found; found=$(scan "$udf")
    local count; count=$(echo "$found" | wc -w)
    if [ "$count" -gt "$bestCount" ]; then bestCount=$count; best=$found; fi
  done
  wait $pid
  echo "$label | args=[${args:-none}] | most renderers seen while open: $bestCount [$best] | ready=$(grep -c '^READY' "$log") results=$(grep -c '^RESULT' "$log")" >> "$out"
  rm -f "$log"
  "$exe" harness clean --udf "$udf" > /dev/null
}
for round in 1 2 3; do
  run "4 InPrivate, distinct names (Spar's test windows)" "rs/~private:a,rs/~private:b,rs/~private:c,rs/~private:d" "" rs
  run "4 InPrivate, distinct names (Spar's test windows)" "rs/~private:a,rs/~private:b,rs/~private:c,rs/~private:d" "--renderer-process-limit=1" rs
  run "4 InPrivate, one name" "rs/~private:a,rs/~private:a,rs/~private:a,rs/~private:a" "" rs
  run "4 InPrivate, one name" "rs/~private:a,rs/~private:a,rs/~private:a,rs/~private:a" "--renderer-process-limit=1" rs
  run "4 default profile (an app's windows)" "rs/default,rs/default,rs/default,rs/default" "" rs
  run "4 default profile (an app's windows)" "rs/default,rs/default,rs/default,rs/default" "--renderer-process-limit=1" rs
done
cat "$out"
