#!/usr/bin/env bash
# M1c step 1's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1c-host.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-host/wsl.sh <step>...
# Steps: tools sync client appmodel build test probe hosttests probeaot notebook. The working copy is the Windows tree as it is (not a
# clone of HEAD: it runs uncommitted changes), copied to /var/tmp/spar-m1c-host/repo without bin, obj, node_modules,
# artifacts and spikes. HOME and the XDG folders are under the copy's artifacts/linux-homes/m1c-host/; XDG_RUNTIME_DIR is
# in /dev/shm (WebKit's sandbox refuses one under /var/tmp). The SDK and NuGet packages are eng/wsl's shared cache.
# Windows come up on an Xvfb of this script's own, never on WSLg's display.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"): WSL and Docker Desktop pipe a
# crashed process's core to Windows' %TEMP%\wsl-crashes, about 6 GB for each of our apps, and a piped core_pattern
# ignores a limit of 0; a limit of exactly 1 byte turns it off. Children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-host
repo=$base/repo
tools=$base/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-host
logs=$source_repo/artifacts/linux-runs/m1c-host
mkdir -p "$base" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
# As eng/wsl/step.sh: the shared Playwright browsers, and the NSS libraries Chromium needs.
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER GSK_RENDERER JSC_SIGNAL_FOR_GC
for v in $(env | grep -o '^JSC_[^=]*' || true); do unset "$v"; done

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-host
  chmod 700 /dev/shm/spar-m1c-host
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-host
}

# Xvfb in a bubblewrap mount namespace whose /usr/bin also holds the unpacked xkbcomp (not installed system-wide).
xvfb_start() {
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

step() {
  case $1 in
    tools)
      mkdir -p "$tools/debs" "$tools/root"
      (cd "$tools/debs" && for pkg in xvfb xserver-common libxfont2 x11-xkb-utils xauth libfontenc1 libxkbfile1 xfonts-base xfonts-encodings libxcvt0; do
        dpkg -s "$pkg" >/dev/null 2>&1 || ls "$pkg"_*.deb >/dev/null 2>&1 || apt-get download "$pkg" 2>&1 | tail -1
      done; for deb in *.deb; do dpkg -x "$deb" "$tools/root"; done)
      mkdir -p "$tools/realbin" "$tools/usrbin"
      [ -e "$tools/usrbin/xkbcomp" ] || (cd "$tools/usrbin" && for f in /usr/bin/*; do ln -sfn "$tools/realbin/$(basename "$f")" "$(basename "$f")"; done && ln -sfn "$tools/root/usr/bin/xkbcomp" xkbcomp) ;;
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
    hosttests)
      xvfb_start 93
      export SPAR_LINUX_HOST_APP=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug -- \
        --filter-class "Spar.Tests.LinuxHostTests" --filter-class "Spar.Tests.LinuxHostUnitTests" --output Detailed)
      xvfb_stop ;;
    probeaot)
      # The probe as a Production NativeAOT executable: the same host tests but the exit-5 one, which needs the
      # Development build's automated mode (a Production app shows its message and waits).
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbe -c Release -r linux-x64 -o "$base/probe-aot") || return 1
      xvfb_start 95
      export SPAR_LINUX_HOST_APP=$base/probe-aot/BridgeProbe
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug -- \
        --filter-method "*The_page_calls_moves_to_a_new_document_and_closes_through_the_close_request" \
        --filter-method "*A_frame_of_another_origin_cannot_reach_the_bridge" \
        --filter-method "*A_lost_web_process_is_recovered_once_and_the_page_connects_again" --output Detailed)
      grep -h "echo mean" "$repo"/artifacts/linux-homes/host-tests/*/data/com.example.tests.BridgeProbe/logs/app.log
      xvfb_stop ;;
    notebook)
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook") || return 1
      ls -l "$base/notebook"
      xvfb_start 94
      export SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook
      # As built (GTK's GL renderer, WebKit's acceleration on llvmpipe), then as step 2's software-GPU default will run it.
      for label in default software; do
        if [ "$label" = software ]; then export GSK_RENDERER=cairo WEBKIT_DISABLE_DMABUF_RENDERER=1; fi
        SPAR_LINUX_NOTEBOOK_LABEL=$label
        export SPAR_LINUX_NOTEBOOK_LABEL
        (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug -- \
          --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded" --output Detailed | tail -5)
        cat "$repo/artifacts/test-results/Spar.Tests/linux-host-$label.txt"
      done
      unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER
      xvfb_stop ;;
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
