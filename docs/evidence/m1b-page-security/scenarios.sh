#!/usr/bin/env bash
# Every scenario of docs/evidence/m1b-page-security.md, against the SecurityProbe app (tests/apps/SecurityProbe) and the
# Notebook. Runs SecurityRun.cs from a copy outside the repository (a file-based program inside it would take the
# repository's build settings). Each app run gets LOCALAPPDATA in its own folder under artifacts/webview2-profiles/.
set -u
repo=$(cd "$(dirname "$0")/../../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
cp "$repo/docs/evidence/m1b-page-security/SecurityRun.cs" "$runner/"
aot=$repo/tests/apps/SecurityProbe/bin/Production/Release/net11.0/win-x64/publish/SecurityProbe.exe
dev=$repo/tests/apps/SecurityProbe/bin/Development/Debug/net11.0/win-x64/SecurityProbe.exe
notebook=$repo/samples/Notebook/bin/Production/Release/net11.0/win-x64/publish/Notebook.exe
# Published with -p:SecurityProbeListenerOff=true -o artifacts/m1b-page-security/probe-listener-off.
off=$repo/artifacts/m1b-page-security/probe-listener-off/SecurityProbe.exe
logs=$repo/artifacts/m1b-page-security/logs
profiles=$repo/artifacts/webview2-profiles/m1b-page-security

run() {
  local name=$1 exe=$2 until=$3 timeout=$4 after=$5
  shift 5
  rm -rf "$profiles/$name"
  (cd "$runner" && dotnet run SecurityRun.cs -- --exe "$exe" --root "$profiles/$name" --log "$logs/$name.log" \
    --until "$until" --timeout "$timeout" --after "$after" "$@")
  echo "$name: $?"
}

case "${1:-all}" in
  all|aot)
    run aot-idle "$aot" "done idle" 30 1000 --env SECURITY_PROBE_MODE=idle
    run aot-nonce "$aot" "done nonce" 30 1000 --env SECURITY_PROBE_MODE=nonce
    run aot-worker "$aot" "done worker" 30 3000 --env SECURITY_PROBE_MODE=worker
    run aot-navigate "$aot" "done navigate" 30 2000 --env SECURITY_PROBE_MODE=navigate
    run aot-permissions "$aot" "done permissions" 30 1000 --env SECURITY_PROBE_MODE=permissions
    run aot-flood "$aot" "done flood:100000" 60 12000 --env SECURITY_PROBE_MODE=flood:100000
    run aot-distinct "$aot" "done distinct:1000" 60 12000 --env SECURITY_PROBE_MODE=distinct:1000
    ;;&
  all|dev)
    run dev-flood "$dev" "done flood:100000" 60 12000 --env SECURITY_PROBE_MODE=flood:100000 --env SPAR_AUTOMATED=1
    run dev-distinct "$dev" "done distinct:1000" 60 12000 --env SECURITY_PROBE_MODE=distinct:1000 --env SPAR_AUTOMATED=1
    run dev-distinct-100k "$dev" "done distinct:100000" 120 12000 --env SECURITY_PROBE_MODE=distinct:100000 --env SPAR_AUTOMATED=1
    ;;&
  all|hang)
    for i in 1 2 3; do
      run dev-hang-early-$i "$dev" "The page is not responding" 40 1000 --env SECURITY_PROBE_MODE=hang-early --env SPAR_AUTOMATED=1
    done
    ;;&
  off)
    # A Production page busy for 20 s raises the hung-page dialog, by design, on the desktop it runs on: run these only
    # where that is acceptable (they ran once, 2026-11-17, before the listener stopped at a bound).
    for i in 1 2 3; do
      run off-distinct-100k-$i "$off" "done distinct:100000" 120 1000 --env SECURITY_PROBE_MODE=distinct:100000
      run aot-distinct-100k-$i "$aot" "done distinct:100000" 120 1000 --env SECURITY_PROBE_MODE=distinct:100000
    done
    ;;&
  all|notebook)
    run notebook "$notebook" "ui-ready" 30 8000
    ;;
esac
