#!/usr/bin/env bash
# M1d step 2's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1d-loopback.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-loopback/wsl.sh <step>...
# Steps: loopback loopbackterm nodisplay desktop. They run on the copy ../m1c-blackbox/wsl.sh keeps in
# /var/tmp/spar-m1c-blackbox/repo, after its `sync client appmodel build` steps (the Debug Notebook and the Debug
# Notebook.EndToEndTests, whose Playwright package and headless Chromium the loopback runs use; never WSLg's display,
# never a browser of the user's). DISPLAY and WAYLAND_DISPLAY are unset first; `desktop` runs on a private Xvfb (:98,
# an abstract socket only) with a private D-Bus session. Each run's HOME and XDG folders are under the copy's
# artifacts/linux-homes/m1d-loopback/<step>/.
set -uo pipefail
# No core dumps (decision "Linux test runs write no core dumps"): a limit of exactly 1 byte; children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
repo=/var/tmp/spar-m1c-blackbox/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1d-loopback
logs=$source_repo/artifacts/linux-runs/m1d-loopback
mkdir -p "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY GSK_RENDERER GTK_DEBUG SPAR_AUTOMATED DBUS_SESSION_BUS_ADDRESS CI BROWSER
for v in $(env | grep -o '^\(JSC_\|SPAR_\|WEBKIT_\)[^=]*' || true); do unset "$v"; done

package=$(find "$repo/samples/Notebook.EndToEndTests/bin/Development/Debug" -path '*/.playwright/package' -type d | head -1)
node_bin=$(command -v node || echo "$package/../node/linux-x64/node")

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

home_for() {
  local root=$homes/$1
  rm -rf "$root" && mkdir -p "$root"/{home,data,config,cache,state} && mkdir -p "/dev/shm/spar-m1d-$1" && chmod 700 "/dev/shm/spar-m1d-$1"
  echo "HOME=$root/home XDG_DATA_HOME=$root/data XDG_CONFIG_HOME=$root/config XDG_CACHE_HOME=$root/cache XDG_STATE_HOME=$root/state XDG_RUNTIME_DIR=/dev/shm/spar-m1d-$1"
}

step() {
  case $1 in
    loopback | loopbackterm)
      local signal=()
      [ "$1" = loopbackterm ] && signal=(--signal TERM)
      (cd "$repo" && timed "$node_bin" "$source_repo/docs/evidence/m1d-loopback/loopback-run.mjs" --project samples/Notebook \
        --root "$homes/$1" --log "$logs/$1.run.log" --playwright "$package" "${signal[@]}")
      grep -E "PASS|FAIL|failures" "$logs/$1.run.log" ;;
    nodisplay)
      # The Desktop profile with no display: the app stops (exit 6) and names the Loopback profile; no fallback.
      (cd "$repo" && export $(home_for nodisplay) && timed dotnet run --no-build --project samples/Notebook -lp Desktop)
      echo "exit code above; expected 6" ;;
    desktop)
      # The Desktop profile on a private Xvfb: the window's page reports ui-ready, then SIGTERM ends the app.
      LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" \
        --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" :98 -screen 0 1600x1000x24 -nolisten tcp -nolisten unix \
        >"$logs/xvfb.log" 2>&1 &
      local xvfb=$!
      for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X98\$" /proc/net/unix && break; sleep 0.05; done
      local out=$logs/desktop.out.log
      (cd "$repo" && env $(home_for desktop) DISPLAY=:98 GDK_BACKEND=x11 dbus-run-session -- \
        dotnet run --no-build --project samples/Notebook -lp Desktop >"$out" 2>&1) &
      local runner=$!
      for _ in $(seq 1 600); do grep -q "ui-ready" "$out" && break; sleep 0.1; done
      grep -E "Using launch|window shown|ui-ready|loopback" "$out"
      local app
      app=$(pgrep -f "Development/Debug/net11.0/linux-x64/Notebook" | head -1)
      [ -n "$app" ] && kill -TERM "$app"
      wait "$runner"; echo "dotnet run exit $?"
      grep -E "exit [0-9]+ at" "$out" | tail -1
      # The development server the run started outlives it by design; end it by its record.
      local vite
      vite=$(grep -o '"pid":[0-9]*' "$repo/samples/Notebook/obj/spar-dev-server.json" 2>/dev/null | cut -d: -f2)
      [ -n "$vite" ] && kill "$vite" 2>/dev/null && echo "vite $vite ended"
      kill "$xvfb" 2>/dev/null; wait "$xvfb" 2>/dev/null
      rm -rf /dev/shm/spar-m1d-desktop ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null), node $("$node_bin" --version)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
rm -rf /dev/shm/spar-m1d-nodisplay
exit $status
