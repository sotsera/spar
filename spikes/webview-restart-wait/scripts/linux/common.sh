#!/usr/bin/env bash
# Sourced by the other scripts (inside WSL Ubuntu-24.04). Everything of this spike lives under
# /var/tmp/spar-linux/webview-restart-wait: the working copy, the unpacked tools, the isolated homes and the logs. The
# .NET SDK and NuGet packages are eng/wsl's shared cache (/var/tmp/spar-linux/cache). Nothing goes to the user's WSL
# home and nothing needs root.
base=/var/tmp/spar-linux/webview-restart-wait
work=$base/work
tools=$base/tools
cache=/var/tmp/spar-linux/cache
export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset JSC_SIGNAL_FOR_GC WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER

# isolate <run>: a fresh HOME and XDG folders under $base/homes/<run>/. XDG_RUNTIME_DIR is the one exception: WebKitGTK's
# sandbox aborts when the runtime folder it binds lies under /var/tmp (found by spikes/window-automation-linux), so it
# is a per-run tmpfs folder /dev/shm/spar-wrw-<run> (0700), removed by cleanup_runtime.
isolate() {
  local homes=$base/homes/$1
  rm -rf "$homes" "/dev/shm/spar-wrw-$1"
  mkdir -p "$homes"/{home,cache,config,data,state} "/dev/shm/spar-wrw-$1"
  chmod 700 "/dev/shm/spar-wrw-$1"
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-wrw-$1
}

cleanup_runtime() { case "$XDG_RUNTIME_DIR" in /dev/shm/spar-wrw-*) rm -rf "$XDG_RUNTIME_DIR" ;; esac; }

# xvfb_start <display>: an Xvfb of our own (never WSLg's :0 or Wayland). Xvfb runs xkbcomp from /usr/bin, which is not
# installed, so it runs in a bubblewrap mount namespace whose /usr/bin also holds the unpacked xkbcomp.
xvfb_start() {
  unset WAYLAND_DISPLAY DISPLAY
  rm -f "$base/xvfb-$1.ready"
  bwrap --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- \
    env LD_LIBRARY_PATH="$tools/root/usr/lib/x86_64-linux-gnu" "$tools/root/usr/bin/Xvfb" ":$1" -screen 0 1280x1024x24 -nolisten tcp -displayfd 3 3>"$base/xvfb-$1.ready" >"$base/xvfb-$1.log" 2>&1 &
  xvfb_pid=$!
  for _ in $(seq 1 200); do [ -s "$base/xvfb-$1.ready" ] && break; sleep 0.05; done
  export DISPLAY=":$1" GDK_BACKEND=x11
}

xvfb_stop() { pkill -TERM -P "$xvfb_pid" 2>/dev/null; kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }

# Processes of this spike still alive (their environment names $base).
leftovers() { grep -l -s "$base" /proc/[0-9]*/environ 2>/dev/null | grep -v "/proc/$$/" | sed 's#/proc/\([0-9]*\)/environ#\1#' | while read -r p; do echo "$p:$(cat /proc/$p/comm 2>/dev/null)"; done | grep -E ':(WebKit|Xvfb|bwrap|xdg-|RestartWait|dbus)' || true; }

# isolate_keep <run>: as isolate, but keeps an existing home (a relaunch on the same data folders).
isolate_keep() {
  local homes=$base/homes/$1
  mkdir -p "$homes"/{home,cache,config,data,state} "/dev/shm/spar-wrw-$1"
  chmod 700 "/dev/shm/spar-wrw-$1"
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-wrw-$1
}
