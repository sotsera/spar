#!/usr/bin/env bash
# M1c step 2's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1c-startup.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-startup/wsl.sh <step>...
# Steps: sync client appmodel build test probe startup probeaot notebook egl. Like ../m1c-host/wsl.sh (whose `tools` step unpacks
# the Xvfb used here, into /var/tmp/spar-m1c-host/tools), the working copy is the Windows tree as it is, copied to
# /var/tmp/spar-m1c-startup/repo without bin, obj, node_modules, artifacts and spikes. HOME and the XDG folders are
# under the copy's artifacts/linux-homes/m1c-startup/ (each app run has its own below artifacts/linux-homes/host-tests/);
# XDG_RUNTIME_DIR is in /dev/shm (WebKit's sandbox refuses one under /var/tmp). Windows come up on an Xvfb of this
# script's own, never on WSLg's display; every test run has a private D-Bus session.
set -uo pipefail
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-startup
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-startup
logs=$source_repo/artifacts/linux-runs/m1c-startup
mkdir -p "$base" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER GSK_RENDERER JSC_SIGNAL_FOR_GC
unset WEBKIT_DISABLE_DMABUF_RENDERER XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN
for v in $(env | grep -o '^JSC_[^=]*' || true); do unset "$v"; done

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-startup
  chmod 700 /dev/shm/spar-m1c-startup
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-startup
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

# The start-up tests, the host tests (no regression) and the Linux unit tests, against the probe in SPAR_LINUX_HOST_APP.
linux_tests() {
  (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
    --filter-class "Spar.Tests.LinuxStartupTests" --filter-class "Spar.Tests.LinuxStartupUnitTests" \
    --filter-class "Spar.Tests.LinuxHostTests" --filter-class "Spar.Tests.LinuxHostUnitTests" "$@" --output Detailed)
}

step() {
  case $1 in
    sync)
      [ -d "$repo/.git" ] || git -c safe.directory='*' clone --quiet --no-hardlinks "$source_repo" "$repo"
      rsync -a --delete --exclude .git --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude /artifacts/ --exclude /spikes/ \
        "$source_repo/" "$repo/"
      echo "synced $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run check && timed npm test && timed npm run build) ;;
    appmodel) (cd "$repo/tests/AppModel" && timed npm ci --ignore-scripts) ;;
    build) (cd "$repo" && timed dotnet build Spar.slnx -c Debug) ;;
    test) (cd "$repo" && timed dotnet test --solution Spar.slnx -c Debug --no-build --filter-not-trait "Surface=Window" --filter-not-trait "Surface=BlackBox") ;;
    probe) (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) ;;
    startup|hosttests)
      rm -f "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      xvfb_start 96 || return 1
      export SPAR_LINUX_HOST_APP=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
      [ -x "$base/notebook/Notebook" ] && export SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook
      linux_tests
      cp "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt" "$logs/linux-startup-development.txt" 2>/dev/null
      unset SPAR_LINUX_NOTEBOOK
      xvfb_stop ;;
    probeaot)
      # The probe as a Production NativeAOT executable: the same tests; the one that needs the Development build's
      # variable (the raised WebKitGTK minimum) skips.
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbe -c Release -r linux-x64 -o "$base/probe-aot") || return 1
      rm -f "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      xvfb_start 97 || return 1
      export SPAR_LINUX_HOST_APP=$base/probe-aot/BridgeProbe
      linux_tests
      cp "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt" "$logs/linux-startup-production.txt" 2>/dev/null
      xvfb_stop ;;
    notebook)
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook") || return 1
      ls -l "$base/notebook"
      rm -f "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      xvfb_start 98 || return 1
      export SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook SPAR_LINUX_NOTEBOOK_LABEL=startup-default
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
        --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded" \
        --filter-method "*The_Notebooks_window_has_the_saved_schemes_colour_from_its_first_frame" --output Detailed | tail -8)
      cat "$repo/artifacts/test-results/Spar.Tests/linux-host-startup-default.txt" "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      cp "$repo/artifacts/test-results/Spar.Tests/linux-host-startup-default.txt" "$logs/" 2>/dev/null
      cp "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt" "$logs/linux-startup-notebook.txt" 2>/dev/null
      xvfb_stop ;;
    egl)
      # The EGL half of the GPU check in the test process: as the system has it (llvmpipe), then with Mesa's D3D12
      # driver forced, which reaches the GPU through /dev/dxg (the spike's hardware control).
      rm -f "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      for driver in "" d3d12; do
        (cd "$repo" && timed env ${driver:+GALLIUM_DRIVER=$driver} dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
          --filter-method "*The_EGL_check_names_the_renderer_and_tears_down" --output Detailed | grep -E -A12 "^(failed|passed) ")
      done
      cat "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt"
      cp "$repo/artifacts/test-results/Spar.Tests/linux-startup.txt" "$logs/linux-startup-egl.txt" ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

isolate
status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
