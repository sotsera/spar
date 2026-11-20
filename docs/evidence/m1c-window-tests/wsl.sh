#!/usr/bin/env bash
# M1c step 7's Linux window tests in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1c-window-tests.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-window-tests/wsl.sh <step>...
# Steps, in this order: sync client appmodel build test window windowaot linuxhost probe probeaot hostaot.
# The working copy is the Windows tree as it is, copied to /var/tmp/spar-m1c-window-tests/repo without bin, obj,
# node_modules, artifacts and spikes. The Xvfb is the one ../m1c-host/wsl.sh's `tools` step unpacks into
# /var/tmp/spar-m1c-host/tools (not installed system-wide), put on PATH as a wrapper that runs it in a bubblewrap mount
# namespace whose /usr/bin also holds the unpacked xkbcomp; the window tests start it themselves, one private Xvfb per
# test process on an abstract socket only, and their own D-Bus session, HOME and XDG folders (under the copy's
# artifacts/linux-homes/test-host/). DISPLAY and WAYLAND_DISPLAY are unset, so nothing ever opens on WSLg's display.
# The Linux host tests left in Spar.Tests (their own processes) still use this script's Xvfb and dbus-run-session.
set -uo pipefail
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-window-tests
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-window-tests
logs=$source_repo/artifacts/linux-runs/m1c-window-tests
dev_probe=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
mkdir -p "$base/bin" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE __NV_DISABLE_EXPLICIT_SYNC GTK_DEBUG
unset XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED PULSE_SERVER BROWSER SPAR_TEST_DISPLAY CI
unset SPAR_LINUX_HOST_APP SPAR_LINUX_WASM_APP SPAR_LINUX_NOTEBOOK DBUS_SESSION_BUS_ADDRESS
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\)[^=]*' || true); do unset "$v"; done

# The wrapper the window tests find on PATH as Xvfb: the unpacked server, with xkbcomp where it looks for it, ended with
# its bubblewrap parent (the tests tether it to their process).
cat > "$base/bin/Xvfb" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
exec bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" "\$@"
EOF
chmod +x "$base/bin/Xvfb"

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-window-tests
  chmod 700 /dev/shm/spar-m1c-window-tests
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-window-tests
}

# This script's own Xvfb, for the tests that launch an app process of its own (Spar.Tests); never WSLg's display.
xvfb_start() {
  Xvfb ":$1" -screen 0 1280x1024x24 -nolisten tcp -nolisten unix >"$base/xvfb-$1.log" 2>&1 &
  xvfb_pid=$!
  for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X$1\$" /proc/net/unix && break; sleep 0.05; done
  export SPAR_LINUX_TEST_DISPLAY=":$1"
}
xvfb_stop() { [ -n "${xvfb_pid:-}" ] && { kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }; unset xvfb_pid; true; }

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

# The processes this run's window tests left (Xvfb, dbus-daemon, WebKit's), by their environment's runtime folder.
census() {
  local left=0
  for p in /proc/[0-9]*; do
    local environ
    environ=$( { tr '\0' '\n' < "$p/environ"; } 2>/dev/null) || continue
    grep -q "^XDG_RUNTIME_DIR=/dev/shm/spar-test-" <<<"$environ" || continue
    grep -q "^HOME=$repo/" <<<"$environ" || continue
    echo "left: $(basename "$p") $(tr '\0' ' ' < "$p/cmdline" | cut -c1-120)"; left=$((left + 1))
  done
  echo "processes left by the window tests: $left"
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
        "$source_repo/" "$repo/"
      echo "synced $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run build) ;;
    appmodel) (cd "$repo/tests/AppModel" && timed npm ci --ignore-scripts) ;;
    build) (cd "$repo" && timed dotnet build Spar.slnx -c Debug) ;;
    test) (cd "$repo" && timed dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox") ;;
    window)
      # The window tests on CoreCLR, as CI's window step runs them; each test process starts its own Xvfb and D-Bus.
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Debug --no-build -- "${@:2}")
      census ;;
    windowaot)
      (cd "$repo" && timed dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests) || return 1
      aot_run Spar.WindowTests "${@:2}"
      census ;;
    linuxhost)
      # The Linux host tests left in Spar.Tests (an app process of their own each), on this script's Xvfb.
      xvfb_start 151 || return 1
      export SPAR_LINUX_HOST_APP=$dev_probe
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
        --filter-class "Spar.Tests.LinuxStartupTests" --filter-class "Spar.Tests.LinuxStartupUnitTests" \
        --filter-class "Spar.Tests.LinuxHostTests" --filter-class "Spar.Tests.LinuxHostUnitTests" --filter-class "Spar.Tests.LinuxHangTests" \
        --filter-class "Spar.Tests.LinuxPageSecurityTests" --output Detailed)
      local status=$?
      xvfb_stop
      return $status ;;
    probe) (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

isolate
if [ "${1:-}" = window ] || [ "${1:-}" = windowaot ]; then
  # One step with xUnit options after it: wsl.sh window --filter-class Spar.WindowTests.SmokeTests
  { echo "STEP $1 on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$@"; } 2>&1 | tee "$logs/$1.log"
  exit "${PIPESTATUS[0]}"
fi

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
