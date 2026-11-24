#!/usr/bin/env bash
# M1d's exit on Windows (docs/evidence/m1d-exit.md): every budget of plan "Provisional budgets" on the working tree's
# code, in Release where the budget says so, with the method of decisions "M1b budgets measured at the exit" and "M1c
# budgets measured at the exit". From Git Bash at the repository's root:
#   bash docs/evidence/m1d-exit/win.sh <step>...
# Steps, in this order (each one alone, so the PC is otherwise idle while it measures):
#   budgets      Spar.WindowTests' BudgetTests in Release on the CoreCLR test lane (launch, round trip, window test,
#                working set); the test windows are offscreen and the launched app is on the black box's hidden desktop
#   budgetsaot   the same tests published with NativeAOT (SparTestLane=Aot) and run
#   m1a          WireShapes' BudgetTests in Release: the command test's start on both lanes, the browser test and the
#                loopback round trip (headless Chromium)
#   notebook     the Notebook's Production NativeAOT executable: published, its size and folder listed
#   noop         the Notebook's no-op Debug build with its frontend steps: 2 warm-up builds, then 10 timed, with the
#                MSBuild server and node reuse (the developer's case, as the 6b record measured it); then one detailed
#                build that shows no Spar step ran
#   hotreload    a method body under `dotnet watch -lp Loopback` (../m1d-hot-reload/watch-run.mjs, headless Chromium,
#                SPAR_LOOPBACK_BROWSER=none): 5 edits and their 5 restores, each applied by Hot Reload
#   firstedit    three more `dotnet watch` sessions with one body edit each: the first edit of a session, the slowest
#   templatefirstedit  the first body edit in a new app from packages (after prepare-user-checks.sh), three sessions
#   harness      the console harness's Production NativeAOT executable, published for its size
#   buildtests   Spar.BuildTests: the console harness's budget and output, the isolated no-op build, the production
#                boundary (its numbers go to timings.txt; the harness's size is asserted, not kept)
# Logs and copied result files go to artifacts/m1d-exit/windows/. Nothing here opens a window on the monitor.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
logs=$repo/artifacts/m1d-exit/windows
results=$repo/artifacts/test-results
mkdir -p "$logs"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
unset CI SPAR_TEST_RESULTS

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

keep() {
  [ -f "$1" ] && cp "$1" "$logs/$2" && echo "kept $2" && cat "$1"
}

# The WebView2 processes whose command line points into this repository, and the black box's leftover roots.
census() {
  powershell.exe -NoProfile -Command "\$r = (Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Where-Object { \$_.CommandLine -like '*spar.v7*' }).Count; \"webview2 processes of this repository left: \$r\"" | tr -d '\r'
}

step() {
  case $1 in
    budgets)
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release --filter-class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-coreclr.txt
      census ;;
    budgetsaot)
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests) || return 1
      (cd "$repo" && timed artifacts/aot/Spar.WindowTests/Spar.WindowTests.exe -class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-aot.txt
      census ;;
    m1a)
      rm -f "$results/WireShapes.Tests/budgets.txt" "$results/WireShapes.EndToEndTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/WireShapes.Tests -c Release -- --filter-class "WireShapes.Tests.BudgetTests")
      keep "$results/WireShapes.Tests/budgets.txt" m1a-command-coreclr.txt
      rm -f "$results/WireShapes.Tests/budgets.txt"
      (cd "$repo" && timed dotnet publish tests/WireShapes.Tests -c Release -p:SparTestLane=Aot -o artifacts/aot/WireShapes.Tests) || return 1
      (cd "$repo" && timed artifacts/aot/WireShapes.Tests/WireShapes.Tests.exe -class "WireShapes.Tests.BudgetTests")
      keep "$results/WireShapes.Tests/budgets.txt" m1a-command-aot.txt
      (cd "$repo" && timed dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests")
      keep "$results/WireShapes.EndToEndTests/budgets.txt" m1a-browser.txt ;;
    notebook)
      local out=$repo/artifacts/m1d-exit/notebook-win
      rm -rf "$out"
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -o "$out") || return 1
      (cd "$out" && ls -l && stat -c '%n %s B' Notebook.exe) ;;
    noop)
      (cd "$repo" && timed dotnet build samples/Notebook -nologo -v:q) || return 1
      for i in 1 2; do (cd "$repo" && dotnet build samples/Notebook -nologo -v:q >/dev/null); done
      for i in $(seq 1 10); do
        local start=$(date +%s%N)
        (cd "$repo" && dotnet build samples/Notebook -nologo -v:q >/dev/null) || return 1
        echo "no-op build $i: $(( ($(date +%s%N) - start) / 1000000 )) ms"
      done
      (cd "$repo" && dotnet build samples/Notebook -nologo -v:d) > "$logs/noop-detailed.log"
      for t in SparClient SparFrontendInstall SparFrontendTypeCheck SparFrontendBuild SparFrontendPack; do
        echo "$t: $(grep -c "Skipping target \"$t\" because all output files are up-to-date" "$logs/noop-detailed.log") skipped as up to date, $(grep -c "Building target \"$t\" completely\|Target \"$t\" .*: *Building" "$logs/noop-detailed.log") run"
      done
      grep -E "^(CoreCompile|_CopyFilesMarkedCopyLocal)|Skipping target \"CoreCompile\"" "$logs/noop-detailed.log" | head -3 ;;
    hotreload)
      local package
      package=$(cd "$repo" && find samples/Notebook.EndToEndTests/bin/Development/Debug -path '*/.playwright/package' -type d | head -1)
      (cd "$repo" && timed dotnet build samples/Notebook -c Debug -nologo -v:q) || return 1
      (cd "$repo" && timed node docs/evidence/m1d-hot-reload/watch-run.mjs --project samples/Notebook \
        --root "$(cygpath -w "$repo/artifacts/test-host/m1d-exit/hot-reload")" --log "$(cygpath -w "$logs/hotreload.run.log")" \
        --playwright "$package" --edits body,body,body,body,body --verbose true)
      grep -E "PASS|FAIL|failures" "$logs/hotreload.run.log"
      (cd "$repo" && git status --porcelain samples/) ;;
    firstedit)
      local package
      package=$(cd "$repo" && find samples/Notebook.EndToEndTests/bin/Development/Debug -path '*/.playwright/package' -type d | head -1)
      for i in 1 2 3; do
        (cd "$repo" && timed node docs/evidence/m1d-hot-reload/watch-run.mjs --project samples/Notebook \
          --root "$(cygpath -w "$repo/artifacts/test-host/m1d-exit/hot-reload-$i")" --log "$(cygpath -w "$logs/firstedit-$i.run.log")" \
          --playwright "$package" --edits body --verbose true)
        grep -E "FAIL|applied it|failures" "$logs/firstedit-$i.run.log"
      done
      (cd "$repo" && git status --porcelain samples/) ;;
    templatefirstedit)
      # The first body edit of a `dotnet watch` session in a new app from packages (prepare-user-checks.sh made it in
      # %TEMP%\spar-guide-m1d6b\work\Notes), written as soon as its window is at ui-ready, three sessions; its window
      # on a desktop of the run's own (../m1d-hot-reload/WatchDesktop.cs), its data under artifacts/webview2-profiles/.
      local G app shared
      G=$(cygpath -u "$TEMP")/spar-guide-m1d6b
      app=$G/work/Notes/Notes
      shared=$(cygpath -u "$TEMP")/spar-package-tests
      [ -d "$app" ] || { echo "run prepare-user-checks.sh first"; return 1; }
      rm -f "$logs/template-firstedit.txt"
      for i in 1 2 3; do
        (export NUGET_PACKAGES=$(cygpath -w "$shared/nuget-packages") npm_config_cache=$(cygpath -w "$shared/npm-cache") MSBUILDDISABLENODEREUSE=1
         export LOCALAPPDATA=$(cygpath -w "$repo/artifacts/webview2-profiles/m1d-exit/template-$i/local")
         timed dotnet run "$(cygpath -w "$repo/docs/evidence/m1d-hot-reload/WatchDesktop.cs")" -- --project "$(cygpath -w "$app")" \
           --root "$(cygpath -w "$repo/artifacts/webview2-profiles/m1d-exit/template-$i")" --log "$(cygpath -w "$logs/template-firstedit-$i.log")" \
           --command "dotnet watch" --steps "until:ui-ready;edit:Greetings/GreetingService.cs|Hello, |Hi, ;until:Hot Reload applied;sleep:500;ctrlc;sleep:3000")
        local written applied host
        written=$(grep -m1 "edit written" "$logs/template-firstedit-$i.log" | awk '{print $1}')
        applied=$(grep -m1 "changes applied" "$logs/template-firstedit-$i.log" | awk '{print $1}')
        host=$(grep -m1 "Hot Reload applied revision" "$logs/template-firstedit-$i.log" | awk '{print $1}')
        echo "session $i: edit at $written ms, dotnet watch's 'changes applied' at $applied ms, the host's line at $host ms" | tee -a "$logs/template-firstedit.txt"
      done ;;
    harness)
      # The console harness's Production NativeAOT executable, published as Spar.BuildTests publishes it, for its size
      # (the build tests assert the budget but keep no number).
      local out=$repo/artifacts/m1d-exit/harness-win
      rm -rf "$out"
      (cd "$repo" && timed dotnet publish tests/apps/Spar.Harness -c Release -o "$out") || return 1
      stat -c '%n %s B' "$out/Spar.Harness.exe" ;;
    buildtests)
      (cd "$repo" && timed dotnet test --project tests/Spar.BuildTests -- --output Detailed)
      local latest
      latest=$(ls -td "$repo"/artifacts/build-tests/*/ | head -1)
      keep "$latest/logs/timings.txt" buildtests-timings.txt ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -s), dotnet $(dotnet --version 2>/dev/null), $(date -u +%FT%TZ)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
