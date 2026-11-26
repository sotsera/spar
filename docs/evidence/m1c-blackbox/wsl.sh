#!/usr/bin/env bash
# M1c step 7, part 2 in WSL (Ubuntu-24.04, the normal user, no root): the Linux black box and SIGTERM,
# docs/evidence/m1c-blackbox.md. From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the path):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-blackbox/wsl.sh <step>...
# Steps, in this order: sync client appmodel build test window windowaot devprobe wasmprobe notebookexe windowdev
# windowdevprobe notebook boundary packagetests; `windowsession` (WSLg's display, after asking the user) is left out of
# every run, as are `devtoolssmoke` (M2a) and `devsmoke` (M2b). `window`, `windowsession` and `windowaot` take xUnit
# options after them when run alone:
#   wsl.sh window --filter-class Spar.WindowTests.BlackBoxFixtureTests
# The working copy is the Windows tree as it is, copied to /var/tmp/spar-m1c-blackbox/repo without bin, obj,
# node_modules, artifacts and spikes. The Xvfb is the one ../m1c-host/wsl.sh's `tools` step unpacks into
# /var/tmp/spar-m1c-host/tools (not installed), put on PATH as a wrapper that runs it in a bubblewrap mount namespace
# whose /usr/bin also holds the unpacked xkbcomp. The tests start everything else themselves: each test process its
# own Xvfb (the Window surface's from :140, the black box's from :180, abstract sockets only), and the black box a
# private D-Bus session per launched app (at-spi2-core's accessibility bus activated through it), HOME and every XDG
# folder under its data root in the copy's artifacts/linux-homes/black-box/, XDG_RUNTIME_DIR in /dev/shm. DISPLAY and
# WAYLAND_DISPLAY are unset, so nothing ever opens on WSLg's display. Nothing here touches another run's processes.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"): WSL and Docker Desktop pipe a
# crashed process's core to Windows' %TEMP%\wsl-crashes, about 6 GB for each of our apps, and a piped core_pattern
# ignores a limit of 0; a limit of exactly 1 byte turns it off. Children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-blackbox
logs=$source_repo/artifacts/linux-runs/m1c-blackbox
results=$repo/artifacts/test-results/Spar.WindowTests
dev_probe=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
mkdir -p "$base/bin" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE __NV_DISABLE_EXPLICIT_SYNC GTK_DEBUG
unset GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED
unset PULSE_SERVER BROWSER SPAR_TEST_DISPLAY CI DBUS_SESSION_BUS_ADDRESS
unset SPAR_LINUX_HOST_APP SPAR_LINUX_WASM_APP SPAR_LINUX_NOTEBOOK SPAR_LINUX_FLOOD
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\|BRIDGE_PROBE_\)[^=]*' || true); do unset "$v"; done

# The wrapper the tests find on PATH as Xvfb: the unpacked server, with xkbcomp where it looks for it, ended with its
# bubblewrap parent (the tests tether it to their process).
cat > "$base/bin/Xvfb" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
exec bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" "\$@"
EOF
chmod +x "$base/bin/Xvfb"

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-blackbox
  chmod 700 /dev/shm/spar-m1c-blackbox
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-blackbox
}

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

# The processes this copy's tests left (Xvfb, dbus-daemon, the accessibility bus, the apps, WebKit's), by their HOME
# or runtime folder: the window tests' (/dev/shm/spar-test-*) and the black box's (/dev/shm/spar-bb-*).
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

aot_run() {
  local name=$1
  shift
  mkdir -p "$repo/artifacts/test-results/$name"
  (cd "$repo" && timed "artifacts/aot/$name/$name" -result-trx "artifacts/test-results/$name/aot-$(date -u +%Y-%m-%d_%H_%M_%S).trx" "$@")
}

step() {
  case $1 in
    sync)
      mkdir -p "$repo"
      rsync -a --delete --exclude .git --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude /artifacts/ --exclude /spikes/ \
        --exclude /tests/apps/BridgeProbeWasm/ "$source_repo/" "$repo/"
      echo "synced $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run build) ;;
    appmodel) (cd "$repo/tests/AppModel" && timed npm ci --ignore-scripts) ;;
    build) (cd "$repo" && timed dotnet build Spar.slnx -c Debug) ;;
    test) (cd "$repo" && timed dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox") ;;
    window)
      # The window and black-box tests on CoreCLR, as CI's window step runs them.
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Debug --no-build -- "${@:2}")
      census ;;
    windowsession)
      # The window tests on WSLg's own display (its XWayland under WSLg's compositor) instead of a private Xvfb: windows
      # open on the user's desktop, so only when the user allowed it. A watcher reports whether a WebKit process mapped
      # Mesa's D3D12 driver, which would show the page drawn on the GPU; in WSL it does not (WebKit's sandbox blocks
      # /dev/dxg; decision "The splash on WSLg's display; a GPU needs a real Linux machine").
      local found=$base/windowsession-gpu.txt
      rm -f "$found"
      (
        while :; do
          for p in $(pgrep -f 'WebKit(Web|GPU)Process' || true); do
            if grep -qE "d3d12|dxcore" "/proc/$p/maps" 2>/dev/null; then
              echo "gpu: $(tr '\0' ' ' < "/proc/$p/cmdline" | cut -c1-60) (pid $p) maps $(grep -oE '/[^ ]*(d3d12|dxcore)[^ ]*' "/proc/$p/maps" | sort -u | tr '\n' ' ')" > "$found"
              exit 0
            fi
          done
          sleep 0.5
        done
      ) &
      local watcher=$!
      (cd "$repo" && DISPLAY=:0 SPAR_TEST_DISPLAY=session timed dotnet test --project tests/Spar.WindowTests -c Debug --no-build -- "${@:2}")
      local exit=$?
      kill "$watcher" 2>/dev/null
      wait "$watcher" 2>/dev/null
      cat "$found" 2>/dev/null || echo "gpu: no WebKit process mapped the D3D12 driver"
      census
      return $exit ;;
    devtoolssmoke)
      # M2a's devtools (docs/evidence/m2a-devtools.md): the Notebook's Debug build on a private Xvfb (:171, the wrapper
      # above, abstract socket only) with a private D-Bus session, its HOME and XDG folders under the copy's
      # artifacts/linux-homes/m2a-devtools/ and its runtime folder in /dev/shm (WebKit's sandbox cannot bind one under
      # /var/tmp), then ../m2a-devtools/smoke.sh with the built spar tool; SIGTERM closes the app, which removes its
      # discovery file.
      local stamp
      stamp=$(date -u +%Y%m%d-%H%M%S)
      local home=$repo/artifacts/linux-homes/m2a-devtools/$stamp runtime=/dev/shm/spar-m2a-devtools-$stamp
      mkdir -p "$home"/{home,cache,config,data,state} "$runtime"
      chmod 700 "$runtime"
      Xvfb :171 -nolisten tcp -nolisten unix -screen 0 1280x800x24 & local xvfb=$!
      local bus
      bus=$(dbus-daemon --session --fork --print-address=1 --print-pid=1 | tr '\n' ' ')
      sleep 1
      (
        export DISPLAY=:171 GDK_BACKEND=x11 DBUS_SESSION_BUS_ADDRESS=${bus%% *} HOME=$home/home XDG_CACHE_HOME=$home/cache \
          XDG_CONFIG_HOME=$home/config XDG_DATA_HOME=$home/data XDG_STATE_HOME=$home/state XDG_RUNTIME_DIR=$runtime
        "$repo/samples/Notebook/bin/Development/Debug/net11.0/linux-x64/Notebook" > "$home/app.out" 2>&1 & local app=$!
        SMOKE_SHOT=$source_repo/docs/evidence/m2a-devtools/linux-notebook.png \
          timed bash "$source_repo/docs/evidence/m2a-devtools/smoke.sh" "$repo/samples/Notebook" dotnet "$repo/src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.dll"
        kill -TERM "$app"
        wait "$app"
        echo "app exit $?; discovery file left: $([ -f "$repo/samples/Notebook/obj/spar-devtools.json" ] && echo yes || echo no)"
      )
      [ -f "$repo/samples/Notebook/obj/spar-dev-server.json" ] && kill "$(sed 's/.*"pid":\([0-9]*\).*/\1/' "$repo/samples/Notebook/obj/spar-dev-server.json")" 2>/dev/null
      kill "${bus##* }" 2>/dev/null
      kill "$xvfb" 2>/dev/null
      wait "$xvfb" 2>/dev/null
      rm -rf "$runtime"
      census ;;
    devsmoke)
      # M2b's spar dev (docs/evidence/m2b-spar-dev.md): ../m2b-spar-dev/smoke.sh with the built spar tool against the
      # Notebook, spar dev --private starting its own Xvfb (the wrapper above, abstract socket only), with a private
      # D-Bus session, HOME and XDG folders under the copy's artifacts/linux-homes/m2b-spar-dev/ and the runtime folder in
      # /dev/shm; then the processes left that name the copy's Notebook.
      local stamp
      stamp=$(date -u +%Y%m%d-%H%M%S)
      local home=$repo/artifacts/linux-homes/m2b-spar-dev/$stamp runtime=/dev/shm/spar-m2b-spar-dev-$stamp
      mkdir -p "$home"/{home,cache,config,data,state} "$runtime"
      chmod 700 "$runtime"
      local bus
      bus=$(dbus-daemon --session --fork --print-address=1 --print-pid=1 | tr '\n' ' ')
      (
        export DBUS_SESSION_BUS_ADDRESS=${bus%% *} HOME=$home/home XDG_CACHE_HOME=$home/cache \
          XDG_CONFIG_HOME=$home/config XDG_DATA_HOME=$home/data XDG_STATE_HOME=$home/state XDG_RUNTIME_DIR=$runtime
        timed bash "$source_repo/docs/evidence/m2b-spar-dev/smoke.sh" "$repo/samples/Notebook" dotnet "$repo/src/Spar.Tool/bin/Debug/net11.0/Spar.Tool.dll"
      )
      echo "# Processes left that name the copy's Notebook: $(pgrep -fc "$repo/samples/Notebook" || true)"
      kill "${bus##* }" 2>/dev/null
      rm -rf "$runtime"
      census ;;
    windowaot)
      (cd "$repo" && timed dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests) || return 1
      aot_run Spar.WindowTests "${@:2}"
      census ;;
    devprobe) (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) ;;
    wasmprobe)
      # The probe with csp.AllowWebAssembly(), from a folder of its own, as a Production NativeAOT executable.
      rsync -a --delete --exclude /bin/ --exclude /obj/ "$repo/tests/apps/BridgeProbe/" "$repo/tests/apps/BridgeProbeWasm/"
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbeWasm -c Release -r linux-x64 -p:BridgeProbeWebAssembly=true -o "$base/probe-wasm-aot") || return 1
      ls -l "$base/probe-wasm-aot/BridgeProbe" ;;
    notebookexe)
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook") || return 1
      ls -l "$base/notebook/Notebook" ;;
    windowdev)
      # The probe tests with the runner's extra executables: the WebAssembly opt-in's build and the Notebook.
      export SPAR_LINUX_WASM_APP=$base/probe-wasm-aot/BridgeProbe SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Debug --no-build -- --filter-trait "App=BridgeProbe" "${@:2}")
      census ;;
    windowdevprobe)
      # The same tests on the Development build of the probe (its automated mode, triggers and scripted answers).
      export SPAR_LINUX_HOST_APP=$dev_probe SPAR_LINUX_WASM_APP=$base/probe-wasm-aot/BridgeProbe
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Debug --no-build -- --filter-trait "App=BridgeProbe" "${@:2}")
      census ;;
    notebook)
      # The Notebook's end-to-end project: its black-box tests (the Production executable) and its window journeys.
      (cd "$repo" && timed dotnet build samples/Notebook.EndToEndTests -c Debug) || return 1
      (cd "$repo" && timed dotnet test --project samples/Notebook.EndToEndTests -c Debug --no-build -- --filter-trait "Surface=BlackBox" "${@:2}")
      census ;;
    notebookwindow)
      # The Notebook's window journeys on CoreCLR (Surface=Window), which the black-box step above leaves out.
      (cd "$repo" && timed dotnet build samples/Notebook.EndToEndTests -c Debug) || return 1
      (cd "$repo" && timed dotnet test --project samples/Notebook.EndToEndTests -c Debug --no-build -- --filter-trait "Surface=Window" "${@:2}")
      census ;;
    packagetests)
      # Spar.PackageTests (M1d step 4, docs/evidence/m1d-template-tests.md): Spar packed into a feed of its own and the
      # app template's content built from it in /tmp, its fast, NativeAOT, page, browser, window and black-box tests
      # run there. The copy gets the source's .git: the fixture checks that packing changes no file of the tree.
      rsync -a --delete "$source_repo/.git/" "$repo/.git/"
      (cd "$repo" && timed dotnet test --project tests/Spar.PackageTests -- "${@:2}")
      census ;;
    boundary)
      # The production-boundary test of the build tests, on Linux: it copies the files git lists into a clean folder
      # of its own and publishes the Notebook there. The copy gets the source's .git for that.
      rsync -a --delete "$source_repo/.git/" "$repo/.git/"
      (cd "$repo" && timed dotnet test --project tests/Spar.BuildTests -- \
        --filter-method "*The_production_executable_carries_its_page_and_no_test_or_loopback_code" --output Detailed) ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

isolate
if [ "${1:-}" = window ] || [ "${1:-}" = windowsession ] || [ "${1:-}" = windowaot ] || [ "${1:-}" = windowdev ] || [ "${1:-}" = windowdevprobe ] || [ "${1:-}" = notebook ] || [ "${1:-}" = packagetests ]; then
  { echo "STEP $1 on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$@"; } 2>&1 | tee "$logs/$1.log"
  exit "${PIPESTATUS[0]}"
fi

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
