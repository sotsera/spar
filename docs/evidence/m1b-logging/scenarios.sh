#!/usr/bin/env bash
# Every scenario of docs/evidence/m1b-logging.md, against the SecurityProbe app (tests/apps/SecurityProbe) and the
# Notebook. Runs the page security run's SecurityRun.cs (docs/evidence/m1b-page-security/) from a copy outside the
# repository: windows shown without activation and moved offscreen, standard error read with the run's clock. Each app
# run gets LOCALAPPDATA in its own folder under artifacts/webview2-profiles/; its app.log is copied beside the run's log.
# Long floods use the Development build with SPAR_AUTOMATED=1, so no hung-page dialog can reach the desktop.
set -u
repo=$(cd "$(dirname "$0")/../../.." && pwd)
runner=${RUNNER_DIR:?set RUNNER_DIR to a folder outside the repository}
cp "$repo/docs/evidence/m1b-page-security/SecurityRun.cs" "$runner/"
aot=$repo/tests/apps/SecurityProbe/bin/Production/Release/net11.0/win-x64/publish/SecurityProbe.exe
dev=$repo/tests/apps/SecurityProbe/bin/Development/Debug/net11.0/win-x64/SecurityProbe.exe
notebook=$repo/samples/Notebook/bin/Production/Release/net11.0/win-x64/publish/Notebook.exe
logs=$repo/artifacts/m1b-logging/logs
profiles=$repo/artifacts/webview2-profiles/m1b-logging

# Runs one scenario; $before, when set, names a function that prepares the run's folder first.
run() {
  local name=$1 exe=$2 until=$3 timeout=$4 after=$5
  shift 5
  rm -rf "$profiles/$name"
  ${before:-true} "$profiles/$name"
  (cd "$runner" && dotnet run SecurityRun.cs -- --exe "$exe" --root "$profiles/$name" --log "$logs/$name.log" \
    --until "$until" --timeout "$timeout" --after "$after" "$@")
  echo "$name: $?"
  find "$profiles/$name" -name 'app*.log' -path '*logs*' -exec cp {} "$logs/$name.app.log" \;
}

# The Notebook's own lines: a page file that is not a page (the store's warning), and a page file another process holds
# open without sharing, so listing the pages fails: the bridge logs the command's exception, and the page's SDK reports
# the failure no handler took with console.error, which reaches the log as a page error.
notebook_pages() {
  local pages="$1/com.example.Notebook/data/pages"
  mkdir -p "$pages"
  printf -- '---\ntitle: Ideas\nrevision: 1\nupdated: 2026-11-17T10:00:00.0000000+00:00\n---\n# Ideas\n' > "$pages/ideas.md"
  printf -- '# Scratch, written by hand\n' > "$pages/draft.md"
  cp "$pages/ideas.md" "$pages/locked.md"
  local locked
  locked=$(cygpath -w "$pages/locked.md")
  pwsh -NoProfile -Command "\$s = [IO.File]::Open('$locked', 'Open', 'Read', 'None'); Start-Sleep -Seconds 20; \$s.Dispose()" &
  sleep 2
}

case "${1:-all}" in
  smoke)
    run aot-page-errors "$aot" "done page-errors" 30 2000 --env SECURITY_PROBE_MODE=page-errors
    ;;
  all|aot)
    run aot-page-errors "$aot" "done page-errors" 30 2000 --env SECURITY_PROBE_MODE=page-errors
    for mode in console distinct-console uncaught distinct-uncaught rejections distinct-rejections; do
      run "aot-$mode-1000" "$aot" "done $mode:1000" 30 2000 --env "SECURITY_PROBE_MODE=$mode:1000"
    done
    ;;&
  all|dev)
    run dev-page-errors "$dev" "done page-errors" 30 2000 --env SECURITY_PROBE_MODE=page-errors --env SPAR_AUTOMATED=1
    for mode in console distinct-console uncaught distinct-uncaught rejections distinct-rejections; do
      run "dev-$mode-100k" "$dev" "done $mode:100000" 120 12000 --env "SECURITY_PROBE_MODE=$mode:100000" --env SPAR_AUTOMATED=1
    done
    ;;&
  all|notebook)
    before=notebook_pages run notebook "$notebook" "ui-ready" 30 3000
    wait
    ;;
esac
