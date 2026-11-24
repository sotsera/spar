#!/usr/bin/env bash
# M1d's exit in WSL (Ubuntu-24.04, the normal user, no root): every budget of plan "Provisional budgets" on the working
# tree's code, the M1c exit's runner (../m1c-exit/wsl.sh) with M1d's two budgets and the M1a lines added;
# docs/evidence/m1d-exit.md. From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the path):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-exit/wsl.sh <step>...
# Steps, in this order: sync budgets launch budgetsaot notebook m1a noop hotreload firstedit harness buildtests.
#   sync        ../m1c-blackbox/wsl.sh's sync, client and build (the copy in /var/tmp/spar-m1c-blackbox/repo)
#   budgets     Spar.WindowTests' BudgetTests in Release, CoreCLR test lane
#   launch      the Production launch test alone (PSS without the test process's own WebKit)
#   budgetsaot  the same tests on the NativeAOT test lane
#   notebook    the Notebook's Production executable for linux-x64: its size and folder
#   m1a         WireShapes' BudgetTests in Release: command start on both lanes, the browser test, the loopback round trip
#   noop        the Notebook's no-op Debug build with its frontend steps: 2 warm-ups, 10 timed, with the MSBuild server
#               and node reuse; then one detailed build that shows no Spar step ran
#   hotreload   ../m1d-hot-reload/wsl.sh watch with five method-body edits and their restores (loopback, headless
#               Chromium), its log as artifacts/linux-runs/m1d-hot-reload/exit-hotreload.run.log
#   firstedit   three more sessions with one body edit each: the first edit of a session, the slowest
#   harness     the console harness's Production NativeAOT executable, published for its size
#   buildtests  Spar.BuildTests (the console harness's budget and output, the isolated no-op, the production boundary;
#               the harness's size is asserted, not kept); the copy gets the source's .git for it
# The environment is the M1c exit's: the Xvfb unpacked by ../m1c-host/wsl.sh (not installed), each test process its own
# Xvfb on abstract sockets, DISPLAY and WAYLAND_DISPLAY unset, so nothing opens on WSLg's display; HOME and every XDG
# folder under the copy's artifacts/linux-homes/m1d-exit/. Nothing here touches another run's processes.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"); children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1d-exit
logs=$source_repo/artifacts/linux-runs/m1d-exit
results=$repo/artifacts/test-results
mkdir -p "$base/bin" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE __NV_DISABLE_EXPLICIT_SYNC GTK_DEBUG
unset GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED
unset PULSE_SERVER BROWSER SPAR_TEST_DISPLAY CI DBUS_SESSION_BUS_ADDRESS SPAR_TEST_RESULTS
unset SPAR_LINUX_HOST_APP SPAR_LINUX_WASM_APP SPAR_LINUX_NOTEBOOK SPAR_LINUX_FLOOD SPAR_LINUX_NOTEBOOK_LABEL
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\|BRIDGE_PROBE_\)[^=]*' || true); do unset "$v"; done

# The Xvfb wrapper of ../m1c-blackbox/wsl.sh.
cat > "$base/bin/Xvfb" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
exec bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" "\$@"
EOF
chmod +x "$base/bin/Xvfb"

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1d-exit
  chmod 700 /dev/shm/spar-m1d-exit
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1d-exit
}

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

census() {
  local left=0
  for p in /proc/[0-9]*; do
    local environ
    environ=$( { tr '\0' '\n' < "$p/environ"; } 2>/dev/null) || continue
    grep -q "^XDG_RUNTIME_DIR=/dev/shm/spar-\(test\|bb\)-" <<<"$environ" || continue
    grep -q "^HOME=$repo/" <<<"$environ" || continue
    echo "left: $(basename "$p") $(tr '\0' ' ' < "$p/cmdline" | cut -c1-120)"; left=$((left + 1))
  done
  echo "processes left by the tests: $left"
}

keep() {
  [ -f "$1" ] && cp "$1" "$logs/$2" && echo "kept $2" && cat "$1"
}

step() {
  case $1 in
    sync) bash "$source_repo/docs/evidence/m1c-blackbox/wsl.sh" sync client build ;;
    budgets)
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release --filter-class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-coreclr.txt
      census ;;
    launch)
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release -- \
        --filter-method "*The_Production_app_starts_to_ui_ready_and_holds_its_working_set")
      keep "$results/Spar.WindowTests/budgets.txt" "budgets-launch-$(date +%H%M%S).txt"
      census ;;
    budgetsaot)
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests) || return 1
      (cd "$repo" && timed artifacts/aot/Spar.WindowTests/Spar.WindowTests -class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-aot.txt
      census ;;
    notebook)
      rm -rf "$base/notebook-m1d-exit"
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook-m1d-exit") || return 1
      (cd "$base/notebook-m1d-exit" && ls -l && stat -c '%n %s B' Notebook && file Notebook) ;;
    m1a)
      rm -f "$results/WireShapes.Tests/budgets.txt" "$results/WireShapes.EndToEndTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/WireShapes.Tests -c Release -- --filter-class "WireShapes.Tests.BudgetTests")
      keep "$results/WireShapes.Tests/budgets.txt" m1a-command-coreclr.txt
      rm -f "$results/WireShapes.Tests/budgets.txt"
      (cd "$repo" && timed dotnet publish tests/WireShapes.Tests -c Release -p:SparTestLane=Aot -o artifacts/aot/WireShapes.Tests) || return 1
      (cd "$repo" && timed artifacts/aot/WireShapes.Tests/WireShapes.Tests -class "WireShapes.Tests.BudgetTests")
      keep "$results/WireShapes.Tests/budgets.txt" m1a-command-aot.txt
      (cd "$repo" && timed dotnet test --project tests/WireShapes.EndToEndTests -c Release -- --filter-class "WireShapes.EndToEndTests.BudgetTests")
      keep "$results/WireShapes.EndToEndTests/budgets.txt" m1a-browser.txt ;;
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
        echo "$t: $(grep -c "Skipping target \"$t\" because all output files are up-to-date" "$logs/noop-detailed.log") skipped as up to date, $(grep -c "Building target \"$t\" completely" "$logs/noop-detailed.log") run"
      done ;;
    hotreload)
      EDITS=body,body,body,body,body LOGNAME_WATCH=exit-hotreload bash "$source_repo/docs/evidence/m1d-hot-reload/wsl.sh" watch
      cp "$source_repo/artifacts/linux-runs/m1d-hot-reload/exit-hotreload.run.log" "$logs/hotreload.run.log" ;;
    firstedit)
      for i in 1 2 3; do
        EDITS=body LOGNAME_WATCH=exit-firstedit-$i bash "$source_repo/docs/evidence/m1d-hot-reload/wsl.sh" watch
        cp "$source_repo/artifacts/linux-runs/m1d-hot-reload/exit-firstedit-$i.run.log" "$logs/firstedit-$i.run.log"
      done ;;
    harness)
      # The console harness's Production NativeAOT executable, published as Spar.BuildTests publishes it, for its size.
      rm -rf "$base/harness-m1d-exit"
      (cd "$repo" && timed dotnet publish tests/apps/Spar.Harness -c Release -o "$base/harness-m1d-exit") || return 1
      stat -c '%n %s B' "$base/harness-m1d-exit/Spar.Harness" ;;
    buildtests)
      rsync -a --delete "$source_repo/.git/" "$repo/.git/"
      (cd "$repo" && timed dotnet test --project tests/Spar.BuildTests -- --output Detailed)
      local latest
      latest=$(ls -td "$repo"/artifacts/build-tests/*/ | head -1)
      keep "$latest/logs/timings.txt" buildtests-timings.txt ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

isolate
status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null), WebKitGTK $(pkg-config --modversion webkitgtk-6.0 2>/dev/null), $(date -u +%FT%TZ)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
