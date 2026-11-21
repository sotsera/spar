#!/usr/bin/env bash
# M1c step 4's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1c-page-security.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-page-security/wsl.sh <step>...
# Steps, in this order: sync client appmodel build test probe security regress probeaot flood notebook. Like
# ../m1c-hangs/wsl.sh (the Xvfb is the one ../m1c-host/wsl.sh's `tools` step unpacks into /var/tmp/spar-m1c-host/tools),
# the working copy is the Windows tree as it is, copied to /var/tmp/spar-m1c-page-security/repo without bin, obj,
# node_modules, artifacts and spikes. HOME and the XDG folders are under the copy's
# artifacts/linux-homes/m1c-page-security/ (each app run has its own below artifacts/linux-homes/host-tests/);
# XDG_RUNTIME_DIR is in /dev/shm (WebKit's sandbox refuses one under /var/tmp). Windows come up on an Xvfb of this
# script's own, never on WSLg's display; every test run has a private D-Bus session. The external-link test opens its
# address with a stub handler it writes into the app run's own XDG folders, and first checks that GIO picks the stub.
# `explore <plan> [seconds]` runs the Development probe once with that plan and prints its log, for looking around.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"): WSL and Docker Desktop pipe a
# crashed process's core to Windows' %TEMP%\wsl-crashes, about 6 GB for each of our apps, and a piped core_pattern
# ignores a limit of 0; a limit of exactly 1 byte turns it off. Children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-page-security
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-page-security
logs=$source_repo/artifacts/linux-runs/m1c-page-security
results=$repo/artifacts/test-results/Spar.Tests
mkdir -p "$base" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER GSK_RENDERER JSC_SIGNAL_FOR_GC
unset WEBKIT_DISABLE_DMABUF_RENDERER XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED
unset PULSE_SERVER BROWSER
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\)[^=]*' || true); do unset "$v"; done

isolate() {
  mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-page-security
  chmod 700 /dev/shm/spar-m1c-page-security
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-page-security
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

# The page-security tests against the probe in $2, on display $1; results and the apps' logs kept under label $3.
security() {
  rm -f "$results/linux-page-security.txt"
  xvfb_start "$1" || return 1
  export SPAR_LINUX_HOST_APP=$2
  touch "$base/marker-$3"
  linux_tests ${SECURITY_FILTER:---filter-class Spar.Tests.LinuxPageSecurityTests}
  cp "$results/linux-page-security.txt" "$logs/linux-page-security-$3.txt" 2>/dev/null
  mkdir -p "$logs/apps-$3" && find "$repo/artifacts/linux-homes/host-tests" -name app.log -newer "$base/marker-$3" \
    -exec sh -c 'cp "$1" "$2/$(basename "$(dirname "$(dirname "$(dirname "$(dirname "$1")")")")").log"' _ {} "$logs/apps-$3" \;
  xvfb_stop
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
    security) security 96 "$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe" development ;;
    regress)
      # Steps 1 to 3's tests on this step's host: the lock, the relay's listener and the folders must change none of them.
      xvfb_start 97 || return 1
      export SPAR_LINUX_HOST_APP=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
      linux_tests --filter-class "Spar.Tests.LinuxStartupTests" --filter-class "Spar.Tests.LinuxStartupUnitTests" \
        --filter-class "Spar.Tests.LinuxHostTests" --filter-class "Spar.Tests.LinuxHostUnitTests" --filter-class "Spar.Tests.LinuxHangTests"
      xvfb_stop ;;
    probeaot)
      # The probe as a Production NativeAOT executable: the same tests; those that need the Development build skip.
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbe -c Release -r linux-x64 -o "$base/probe-aot") || return 1
      security 98 "$base/probe-aot/BridgeProbe" production ;;
    flood)
      # The flood numbers (docs/evidence/m1c-page-security.md): distinct violations in Development, automated so a long
      # busy page shows no dialog, and 1,000 in Production; each run's log kept.
      xvfb_start 99 || return 1
      export SPAR_LINUX_HOST_APP=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
      SPAR_LINUX_FLOOD="1000 10000 100000" linux_tests --filter-method "*The_flood_numbers_are_recorded"
      cp "$results/linux-csp-flood.txt" "$logs/linux-csp-flood-development.txt" 2>/dev/null
      export SPAR_LINUX_HOST_APP=$base/probe-aot/BridgeProbe
      SPAR_LINUX_FLOOD="1000 10000" linux_tests --filter-method "*The_flood_numbers_are_recorded"
      cp "$results/linux-csp-flood.txt" "$logs/linux-csp-flood-production.txt" 2>/dev/null
      xvfb_stop ;;
    notebook)
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook") || return 1
      ls -l "$base/notebook"
      xvfb_start 95 || return 1
      touch "$base/marker-notebook"
      export SPAR_LINUX_NOTEBOOK=$base/notebook/Notebook SPAR_LINUX_NOTEBOOK_LABEL=page-security
      (cd "$repo" && timed dbus-run-session -- dotnet test --project tests/Spar.Tests -c Debug --no-build -- \
        --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded" --output Detailed | tail -8)
      cat "$results/linux-host-page-security.txt"
      cp "$results/linux-host-page-security.txt" "$logs/" 2>/dev/null
      # The Notebook's own page under the policy: 0 violations in its log, and its folders.
      log=$(find "$repo/artifacts/linux-homes/host-tests" -path '*Notebook*' -name app.log -newer "$base/marker-notebook" | head -1)
      echo "Notebook log: $log"
      grep -c "Content Security Policy:" "$log" | sed 's/^/violation lines: /'
      grep -E "Browser data folder|Permissions-Policy" "$log"
      # The Production boundary: type names are UTF-8 in the image, string literals UTF-16.
      for marker in CspAdvice PageSecurityMismatch DevelopmentTriggers; do
        printf '%s (UTF-8): %s\n' "$marker" "$(LC_ALL=C grep -c -a -F "$marker" "$base/notebook/Notebook")"
      done
      for marker in "Allow it with csp." SPAR_DEV_; do
        utf16=$(printf '%s' "$marker" | sed 's/./&\\x00/g')
        printf '%s (UTF-16): %s\n' "$marker" "$(LC_ALL=C grep -c -a -P "$utf16" "$base/notebook/Notebook")"
      done
      xvfb_stop ;;
    explore)
      shift
      xvfb_start 94 || return 1
      run=$homes/explore-$(date +%s)
      mkdir -p "$run"/{home,cache,config,data,state}
      (export HOME=$run/home XDG_CACHE_HOME=$run/cache XDG_CONFIG_HOME=$run/config XDG_DATA_HOME=$run/data XDG_STATE_HOME=$run/state
       export DISPLAY=:94 GDK_BACKEND=x11 BRIDGE_PROBE_PLAN=$1 SPAR_AUTOMATED=1
       if [ -n "${3:-}" ]; then mkdir -p "$run/site" && echo "<p>site</p>" > "$run/site/frame.html"; (cd "$run/site" && exec python3 -m http.server --bind 127.0.0.1 8765 >"$run/site.log" 2>&1) & export BRIDGE_PROBE_FRAME=$3; fi
       timeout "${2:-12}" dbus-run-session -- "$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe")
      cat "$run"/data/*/logs/app.log; [ -f "$run/site.log" ] && { cat "$run/site.log"; pkill -f "http.server --bind 127.0.0.1 8765"; }
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
