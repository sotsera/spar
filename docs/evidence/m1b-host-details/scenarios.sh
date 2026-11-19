#!/usr/bin/env bash
# The desktop scenarios of docs/evidence/m1b-host-details.md, against the SecurityProbe app (tests/apps/SecurityProbe)
# published as NativeAOT: the current host, and a baseline published from the host before step 6 (commit d94c48a) in
# artifacts/m1b-host-details/baseline/. Runs HostRun.cs from a copy outside the repository (a file-based program inside
# it would take the repository's build settings). Each app run gets LOCALAPPDATA in its own folder under
# artifacts/webview2-profiles/m1b-host-details/. No run activates a window or sends input; all run in the automated
# mode, so nothing the host would ask reaches the desktop.
#   RUNNER_DIR=<folder outside the repository> docs/evidence/m1b-host-details/scenarios.sh [all|visibility|close] [label]
set -u
repo=$(cd "$(dirname "$0")/../../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
cp "$repo/docs/evidence/m1b-host-details/HostRun.cs" "$runner/"
current=$repo/tests/apps/SecurityProbe/bin/Production/Release/net11.0/win-x64/publish/SecurityProbe.exe
baseline=$repo/artifacts/m1b-host-details/baseline/SecurityProbe.exe
logs=$repo/artifacts/m1b-host-details/logs
profiles=$repo/artifacts/webview2-profiles/m1b-host-details
label=${2:-1}

run() {
  local name=$1 exe=$2 mode=$3 actions=$4
  rm -rf "${profiles:?}/${name:?}"
  (cd "$runner" && dotnet run HostRun.cs -- --exe "$exe" --root "$profiles/$name" --log "$logs/$name-$label.log" \
    --until "done $mode" --timeout 40 --actions "$actions" --env SECURITY_PROBE_MODE=$mode --env SPAR_AUTOMATED=1)
  echo "$name-$label: $?"
}

states="wait:3000;handles;minimize;wait:8000;restore;wait:3000;hide;wait:8000;show;wait:3000;move:0,40;wait:300;move:0,40;wait:2000;handles;close"
case "${1:-all}" in
  all|visibility)
    run baseline-visibility "$baseline" visibility "$states"
    run current-visibility "$current" visibility "$states"
    ;;&
  all|close)
    run baseline-close-minimized "$baseline" idle "wait:2000;minimize;wait:3000;close"
    run current-close-minimized "$current" idle "wait:2000;minimize;wait:3000;close"
    run current-close-hidden "$current" idle "wait:2000;hide;wait:3000;close"
    ;;&
esac
