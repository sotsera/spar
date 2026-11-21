#!/usr/bin/env bash
# M1d step 1's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1d-dev-server.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-dev-server/wsl.sh <step>...
# Steps: tools sync client notebook <scenario>... Scenarios: hmr reuse worker kill stdin embedded external nonode.
# The working copy is the Windows tree as it is (uncommitted changes included), copied to /var/tmp/spar-m1d-dev-server/repo
# without bin, obj, node_modules, artifacts and spikes; the frontend edits of the HMR runs happen in that copy and are
# restored. The Xvfb is the one ../m1c-host/wsl.sh's `tools` step unpacked (not installed), on a display of this
# script's own (:97), with xdotool unpacked beside it for the Retry key; DISPLAY and WAYLAND_DISPLAY are unset first, so
# nothing opens on WSLg's display. Each app run has HOME and every XDG folder under the copy's
# artifacts/linux-homes/m1d-dev-server/<scenario>/, a private D-Bus session, and XDG_RUNTIME_DIR in /dev/shm.
set -uo pipefail
# No core dumps (decision "Linux test runs write no core dumps"): a limit of exactly 1 byte; children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1d-dev-server
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1d-dev-server
logs=$source_repo/artifacts/linux-runs/m1d-dev-server
exe=$repo/samples/Notebook/bin/Development/Debug/net11.0/linux-x64/Notebook
mkdir -p "$base/bin" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER GSK_RENDERER GTK_DEBUG SPAR_AUTOMATED
unset DBUS_SESSION_BUS_ADDRESS SPAR_DEV_SERVER_URL CI
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\)[^=]*' || true); do unset "$v"; done

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

xvfb_start() {
  LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" \
    --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" :97 -screen 0 1600x1000x24 -nolisten tcp -nolisten unix \
    >"$base/xvfb.log" 2>&1 &
  xvfb_pid=$!
  for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X97\$" /proc/net/unix && break; sleep 0.05; done
}
xvfb_stop() { [ -n "${xvfb_pid:-}" ] && { pkill -TERM -P "$xvfb_pid" 2>/dev/null; kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }; true; }

# One run of the Debug Notebook: linux-run.mjs with a private D-Bus session, on the private display.
run() {
  local name=$1 steps=$2
  shift 2
  local args=()
  for e in "$@"; do args+=(--env "$e"); done
  mkdir -p /dev/shm/spar-m1d-$name && chmod 700 /dev/shm/spar-m1d-$name
  XDG_RUNTIME_DIR=/dev/shm/spar-m1d-$name DISPLAY=:97 GDK_BACKEND=x11 timed dbus-run-session -- node "$source_repo/docs/evidence/m1d-dev-server/linux-run.mjs" \
    --exe "$exe" --root "$homes/$name" --log "$logs/$name.run.log" --frontend "$repo/samples/Notebook/frontend" \
    --obj "$repo/samples/Notebook/obj" --steps "$steps" "${args[@]}" ${STDIN_PIPE:+--stdin-pipe true}
  rm -rf /dev/shm/spar-m1d-$name
  grep -E "run: |Development server|ui-ready at|error page|stopped|npm run dev|reused|__spar-hmr|styles.css\?t=|WARN|ERROR" "$logs/$name.run.log" \
    | grep -v " out: " | cut -c1-240
}

# Ends the server a previous run left (its record's Vite and the processes below it), so the next run starts one.
stop_vite() {
  local record=$repo/samples/Notebook/obj/spar-dev-server.json
  [ -f "$record" ] || return 0
  local pid
  pid=$(node -e 'console.log(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).pid)' "$record")
  pkill -KILL -P "$pid" 2>/dev/null; kill -KILL "$pid" 2>/dev/null; rm -f "$record"
  echo "ended the recorded development server (Vite pid $pid)"
}

hmr_steps() {
  local s="until:ui-ready;sleep:2000;marker:0;until:__spar-hmr/0;sleep:1500"
  for n in 1 2 3 4 5; do s="$s;marker:$n;until:__spar-hmr/$n;sleep:1500"; done
  for n in 1 2 3 4 5; do s="$s;css:$n;until:styles.css?t=;sleep:1500"; done
  echo "$s;close"
}

step() {
  case $1 in
    tools)
      # xdotool and its library, unpacked beside the Xvfb (not installed), for the error page's Retry key.
      mkdir -p "$base/debs" "$base/root"
      (cd "$base/debs" && for pkg in xdotool libxdo3; do ls "$pkg"_*.deb >/dev/null 2>&1 || apt-get download "$pkg" 2>&1 | tail -1; done
       for deb in *.deb; do dpkg -x "$deb" "$base/root"; done)
      printf '#!/bin/sh\nLD_LIBRARY_PATH=%s exec %s "$@"\n' "$base/root/usr/lib/x86_64-linux-gnu" "$base/root/usr/bin/xdotool" > "$base/bin/xdotool"
      chmod +x "$base/bin/xdotool"; xdotool version ;;
    sync)
      mkdir -p "$repo"
      rsync -a --delete --exclude .git --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude /artifacts/ --exclude /spikes/ \
        "$source_repo/" "$repo/"
      echo "synced $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run build) ;;
    notebook) (cd "$repo" && timed dotnet build samples/Notebook -c Debug && grep DevServer "$exe.runtimeconfig.json") ;;
    hmr) xvfb_start; run hmr "$(hmr_steps)"; xvfb_stop ;;
    worker) xvfb_start; run worker "until:ui-ready;sleep:1500;worker;until:__spar-worker/;sleep:1500;close"; xvfb_stop ;;
    reuse) xvfb_start; run reuse "until:ui-ready;sleep:1500;violation:1;sleep:3000;close"; xvfb_stop ;;
    kill)
      xvfb_start
      run kill "until:ui-ready;sleep:1500;kill-vite;until:Development error page shown;sleep:1500;key:Return;until:Development server ready;until:ui-ready;sleep:1500;close"
      xvfb_stop ;;
    stdin)
      # A server of its own: one a run without a piped input started never ends with this one's input.
      stop_vite
      xvfb_start; STDIN_PIPE=1 run stdin "until:ui-ready;sleep:1000;close-stdin;until:development server stopped;sleep:1000;close"; xvfb_stop ;;
    external)
      # A Vite the developer runs (port 5173, the plugin's own defaults), then the app with SPAR_DEV_SERVER_URL.
      (cd "$repo/samples/Notebook/frontend" && exec node node_modules/vite/bin/vite.js < <(sleep 600) > "$base/external-vite.log" 2>&1) &
      local vite=$!
      for _ in $(seq 1 100); do grep -q "ready in" "$base/external-vite.log" 2>/dev/null && break; sleep 0.1; done
      xvfb_start
      run external "until:ui-ready;sleep:1500;marker:0;until:__spar-hmr/0;sleep:1000;close" "SPAR_DEV_SERVER_URL=http://127.0.0.1:5173"
      pkill -TERM -P "$vite" 2>/dev/null; kill "$vite" 2>/dev/null; wait "$vite" 2>/dev/null
      run externaldown "until:Development error page shown;sleep:500;close" "SPAR_DEV_SERVER_URL=http://127.0.0.1:5173"
      xvfb_stop ;;
    embedded)
      (cd "$repo" && timed dotnet build samples/Notebook -c Debug -p:SparDevServer=false && grep DevServer "$exe.runtimeconfig.json")
      xvfb_start; run embedded "until:ui-ready;sleep:1000;close"; xvfb_stop
      (cd "$repo" && timed dotnet build samples/Notebook -c Debug >/dev/null) ;;
    nonode)
      # A PATH with everything of /usr/bin but Node and npm (the sandbox trial needs bwrap and true from it).
      rm -rf "$base/nonode-bin" && mkdir -p "$base/nonode-bin"
      for f in /usr/bin/*; do case $(basename "$f") in node|nodejs|npm|npx|corepack) ;; *) ln -s "$f" "$base/nonode-bin/";; esac; done
      xvfb_start; run nonode "until:Exit code 3;sleep:500" "PATH=$base/nonode-bin" "SPAR_AUTOMATED=1"; xvfb_stop ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null), node $(node --version)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
