#!/usr/bin/env bash
# M1c step 6's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1c-security-defaults.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-security-defaults/wsl.sh <step>...
# Steps, in this order: sync client appmodel build test probe probewasm defaults regress probeaot defaultsaot regressaot
# notebook boundary. Like ../m1c-wasm/wsl.sh (the Xvfb is the one ../m1c-host/wsl.sh's `tools` step unpacks into
# /var/tmp/spar-m1c-host/tools), the working copy is the Windows tree as it is, copied to
# /var/tmp/spar-m1c-security-defaults/repo without bin, obj, node_modules, artifacts and spikes. HOME and the XDG folders
# are under the copy's artifacts/linux-homes/m1c-security-defaults/ (each app run has its own below
# artifacts/linux-homes/host-tests/); XDG_RUNTIME_DIR is in /dev/shm (WebKit's sandbox refuses one under /var/tmp).
# Windows come up on an Xvfb of this script's own, never on WSLg's display; every test run has a private D-Bus session;
# every inherited JSC_ and WEBKIT_ inspector or sandbox variable is cleared (the tests set their own).
# `boundary` runs the build tests' production-boundary test on Linux: it lists files through git, so the copy for it
# also carries the repository's .git (read only: nothing is committed or changed there).
# `explore <plan> [seconds] [variables...]` runs the Development probe once with that plan and NAME=value variables
# and prints its log, for looking around.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"): WSL and Docker Desktop pipe a
# crashed process's core to Windows' %TEMP%\wsl-crashes, about 6 GB for each of our apps, and a piped core_pattern
# ignores a limit of 0; a limit of exactly 1 byte turns it off. Children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-security-defaults
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-security-defaults
logs=$source_repo/artifacts/linux-runs/m1c-security-defaults
results=$repo/artifacts/test-results/Spar.Tests
dev_probe=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
dev_wasm=$repo/tests/apps/BridgeProbeWasm/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
mkdir -p "$base" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE __NV_DISABLE_EXPLICIT_SYNC
unset XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED PULSE_SERVER BROWSER
unset SPAR_LINUX_HOST_APP SPAR_LINUX_WASM_APP SPAR_LINUX_NOTEBOOK
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\)[^=]*' || true); do unset "$v"; done

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-security-defaults
  chmod 700 /dev/shm/spar-m1c-security-defaults
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-security-defaults
}

# Xvfb in a bubblewrap mount namespace whose /usr/bin also holds the unpacked xkbcomp (not installed system-wide).
xvfb_start() {
  [ -x "$tools/root/usr/bin/Xvfb" ] || { echo "run docs/evidence/m1c-host/wsl.sh tools first"; return 1; }
  LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- \
    "$tools/root/usr/bin/Xvfb" ":$1" -screen 0 1280x1024x24 -nolisten tcp -nolisten unix >"$base/xvfb-$1.log" 2>&1 &
  xvfb_pid=$!
  # WSLg's /tmp/.X11-unix is read-only, so Xvfb listens on the abstract socket only.
  for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X$1\$" /proc/net/unix && break; sleep 0.05; done
  export SPAR_LINUX_TEST_DISPLAY=":$1"
}
xvfb_stop() { [ -n "${xvfb_pid:-}" ] && { pkill -TERM -P "$xvfb_pid" 2>/dev/null; kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }; true; }

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

linux_tests() {
  (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- "$@" --output Detailed)
}

# Copies the apps' logs written since marker $1 to the label's folder.
keep_logs() {
  mkdir -p "$logs/apps-$1" && find "$repo/artifacts/linux-homes/host-tests" -name app.log -newer "$base/marker-$1" \
    -exec sh -c 'cp "$1" "$2/$(basename "$(dirname "$(dirname "$(dirname "$(dirname "$1")")")")").log"' _ {} "$logs/apps-$1" \;
}

# This step's tests (the overrides, the inspectors and GTK_DEBUG, the keys, downloads, the picker, the menu, history),
# the host's unit tests and step 5's WebAssembly tests (one filter kind only: xUnit joins different kinds with AND),
# against probe $2 and its WebAssembly build $3, on display $1; results and logs kept under label $4.
defaults() {
  rm -f "$results/linux-security-defaults.txt" "$results/linux-page-security.txt"
  xvfb_start "$1" || return 1
  export SPAR_LINUX_HOST_APP=$2 SPAR_LINUX_WASM_APP=$3
  touch "$base/marker-$4"
  linux_tests --filter-method "*browser_environment_overrides*" --filter-method "*inspector*" --filter-method "*GTK_DEBUG*" \
    --filter-method "*browser_keys*" --filter-method "*Downloads_the_picker*" --filter-method "*History_navigation*" \
    --filter-method "*WebAssembly*" --filter-method "Spar.Tests.LinuxHostUnitTests.*"
  local status=$?
  cp "$results/linux-security-defaults.txt" "$logs/linux-security-defaults-$4.txt" 2>/dev/null
  cp "$results/linux-page-security.txt" "$logs/linux-wasm-$4.txt" 2>/dev/null
  keep_logs "$4"
  unset SPAR_LINUX_WASM_APP
  xvfb_stop
  return $status
}

# Steps 1 to 5's Linux tests on probe $2, display $1, label $3.
regress() {
  rm -f "$results/linux-page-security.txt"
  xvfb_start "$1" || return 1
  export SPAR_LINUX_HOST_APP=$2 SPAR_LINUX_WASM_APP=${4:-}
  touch "$base/marker-$3"
  linux_tests --filter-class "Spar.Tests.LinuxStartupTests" --filter-class "Spar.Tests.LinuxStartupUnitTests" \
    --filter-class "Spar.Tests.LinuxHostTests" --filter-class "Spar.Tests.LinuxHostUnitTests" --filter-class "Spar.Tests.LinuxHangTests" \
    --filter-class "Spar.Tests.LinuxPageSecurityTests"
  local status=$?
  cp "$results/linux-page-security.txt" "$logs/linux-page-security-$3.txt" 2>/dev/null
  cp "$results/linux-security-defaults.txt" "$logs/linux-security-defaults-regress-$3.txt" 2>/dev/null
  keep_logs "$3"
  unset SPAR_LINUX_WASM_APP
  xvfb_stop
  return $status
}

step() {
  case $1 in
    sync)
      # A plain copy, without .git: the tests read no history.
      mkdir -p "$repo"
      rsync -a --delete --exclude .git --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude /artifacts/ --exclude /spikes/ \
        --exclude /tests/apps/BridgeProbeWasm/ "$source_repo/" "$repo/"
      echo "synced $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run check && timed npm test && timed npm run build) ;;
    appmodel) (cd "$repo/tests/AppModel" && timed npm ci --ignore-scripts) ;;
    build) (cd "$repo" && timed dotnet build Spar.slnx -c Debug) ;;
    test) (cd "$repo" && timed dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox") ;;
    probe) (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) ;;
    probewasm)
      # The probe with csp.AllowWebAssembly(), from a folder of its own (../m1c-wasm/wsl.sh).
      rsync -a --delete --exclude /bin/ --exclude /obj/ "$repo/tests/apps/BridgeProbe/" "$repo/tests/apps/BridgeProbeWasm/"
      (cd "$repo" && timed dotnet build tests/apps/BridgeProbeWasm -c Debug -p:SparDevServer=false -p:BridgeProbeWebAssembly=true) ;;
    defaults) defaults 81 "$dev_probe" "$dev_wasm" development ;;
    regress) regress 82 "$dev_probe" development "$dev_wasm" ;;
    probeaot)
      # Both probes as Production NativeAOT executables.
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbe -c Release -r linux-x64 -o "$base/probe-aot") || return 1
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbeWasm -c Release -r linux-x64 -p:BridgeProbeWebAssembly=true -o "$base/probe-wasm-aot") || return 1
      ls -l "$base/probe-aot/BridgeProbe" "$base/probe-wasm-aot/BridgeProbe" ;;
    defaultsaot) defaults 83 "$base/probe-aot/BridgeProbe" "$base/probe-wasm-aot/BridgeProbe" production ;;
    regressaot) regress 84 "$base/probe-aot/BridgeProbe" production "$base/probe-wasm-aot/BridgeProbe" ;;
    notebook)
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook") || return 1
      ls -l "$base/notebook"
      xvfb_start 85 || return 1
      touch "$base/marker-notebook"
      export SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook SPAR_LINUX_NOTEBOOK_LABEL=security-defaults
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
        --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded" --output Detailed | tail -8)
      cat "$results/linux-host-security-defaults.txt"
      cp "$results/linux-host-security-defaults.txt" "$logs/" 2>/dev/null
      log=$(find "$repo/artifacts/linux-homes/host-tests" -path '*Notebook*' -name app.log -newer "$base/marker-notebook" | head -1)
      echo "Notebook log: $log"
      grep -c "Content Security Policy:" "$log" | sed 's/^/violation lines: /'
      grep -E "Browser settings|opt-ins|ignored|in force|WebAssembly|Content-Security-Policy:" "$log"
      xvfb_stop ;;
    boundary)
      # The production-boundary test of the build tests (tests/Spar.BuildTests), on Linux: it copies the files git lists
      # into a clean folder of its own and publishes the Notebook there. The copy gets the source's .git for that.
      rsync -a --delete "$source_repo/.git/" "$repo/.git/"
      (cd "$repo" && timed dotnet test --project tests/Spar.BuildTests -- \
        --filter-method "*The_production_executable_carries_its_page_and_no_test_or_loopback_code" --output Detailed) ;;
    explore)
      shift
      xvfb_start 86 || return 1
      run=$homes/explore-$(date +%s)
      mkdir -p "$run"/{home,cache,config,data,state}
      local plan=$1 seconds=${2:-12}
      shift 2 2>/dev/null || shift $#
      (export HOME=$run/home XDG_CACHE_HOME=$run/cache XDG_CONFIG_HOME=$run/config XDG_DATA_HOME=$run/data XDG_STATE_HOME=$run/state
       export DISPLAY=:86 GDK_BACKEND=x11 BRIDGE_PROBE_PLAN=$plan SPAR_AUTOMATED=1
       for assignment in "$@"; do export "$assignment"; done
       (sleep $((seconds / 2)); ss -ltnp > "$run/ports.txt" 2>&1) &
       timeout "$seconds" dbus-run-session -- "$dev_probe")
      cat "$run"/data/*/logs/app.log
      echo "--- listening TCP sockets halfway through ---"
      cat "$run/ports.txt"
      echo "--- files in the run's home ---"
      find "$run/home" -type f -not -path '*/.cache/*' | sed "s|$run/||"
      xvfb_stop ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

isolate
if [ "${1:-}" = explore ]; then
  step "$@" 2>&1 | tee "$logs/explore.log"
  exit "${PIPESTATUS[0]}"
fi

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
