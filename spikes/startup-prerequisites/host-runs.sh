#!/usr/bin/env bash
# Usage (Git Bash, from the repository root): spikes/startup-prerequisites/host-runs.sh [rounds]
# Host-safe Windows cases: no window and no dialog (--ui none), so nothing appears on the desktop. Version-stage
# checks (runtime present, missing in an empty fixed-version folder, too old, OS too old) and environment-stage checks
# (the browser process starts but no window is created) with a broken copy of the runtime under artifacts/.
# stderr goes to a file (an inherited file handle). Logs: artifacts/startup-prerequisites/host/.
set -u
rounds=${1:-3}
A=artifacts/startup-prerequisites/host
P=artifacts/webview2-profiles/startup-prerequisites
R=artifacts/startup-prerequisites/runtimes   # empty/, fixed-ok/ and broken-*/ (see RESULTS.md, Method)
mkdir -p "$A" "$R/empty" "$P"
rm -f "${A:?}"/*.jsonl "${A:?}"/*.stderr.txt "${A:?}/runs.txt"
exe=spikes/startup-prerequisites/Startup.Win/bin/Release/net11.0/win-x64/publish/Startup.Win.exe
run() {
  local label=$1; shift
  local s; s=$(date +%s%N)
  "$exe" --label "$label" --log "$A/$label.jsonl" "$@" 2>"$A/$label.stderr.txt"
  local c=$?
  echo "$label exit=$c wall=$(( ($(date +%s%N) - s) / 1000000 ))ms" | tee -a "$A/runs.txt"
}
win() { cygpath -aw "$1"; }
for r in $(seq 1 "$rounds"); do
  run "ok-$r" --ui none
  run "missing-$r" --ui none --runtime "$(win "$R/empty")"
  run "tooold-$r" --ui none --min-runtime 999.0.0.0
  run "os-$r" --ui none --min-os-build 99999
  run "env-ok-$r" --ui none --stage environment --profile "$(win "$P/host-env")"
  for broken in "$R"/fixed-ok/ "$R"/broken-*/; do
    [ -d "$broken" ] || continue
    name=$(basename "$broken")
    run "env-$name-$r" --ui none --stage environment --runtime "$(win "$broken")" --profile "$(win "$P/host-$name")"
  done
done
