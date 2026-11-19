#!/usr/bin/env bash
# Sourced by the other scripts (inside WSL Ubuntu-24.04). The working copy, its isolated homes and the tools live under
# /var/tmp/spar-linux/window-automation-linux; the SDK and NuGet packages are eng/wsl's shared cache. Nothing goes to the
# user's WSL home, and nothing needs root.
base=/var/tmp/spar-linux/window-automation-linux
work=$base/work
spike=$work/spikes/window-automation-linux
tools=$base/tools
cache=/var/tmp/spar-linux/cache
export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$tools/root/usr/bin:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
unset JSC_SIGNAL_FOR_GC WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER

# isolate <run>: HOME and the XDG folders under the working copy's artifacts/linux-homes/<run>/ (AGENTS.md). The one
# exception is XDG_RUNTIME_DIR: WebKitGTK's sandbox symlinks /var/tmp to /tmp inside bubblewrap and aborts ("Can't make
# symlink at /var/tmp: destination exists") when the runtime folder it binds lies under /var/tmp (wsl/sandbox-paths.sh).
# It goes to a per-run tmpfs folder, /dev/shm/spar-wal-<run> (0700), removed by cleanup_runtime.
isolate() {
  local homes=$work/artifacts/linux-homes/$1
  mkdir -p "$homes"/{home,cache,config,data,state} "/dev/shm/spar-wal-$1"
  chmod 700 "/dev/shm/spar-wal-$1"
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-wal-$1
}

cleanup_runtime() { case "$XDG_RUNTIME_DIR" in /dev/shm/spar-wal-*) rm -rf "$XDG_RUNTIME_DIR" ;; esac; }

# xvfb_start <display>: an Xvfb of our own (never WSLg's :0 or Wayland). xkbcomp is not installed system-wide, and Xvfb
# runs it from /usr/bin, so Xvfb runs in a bubblewrap mount namespace where /usr/bin also holds the unpacked xkbcomp.
xvfb_start() {
  unset WAYLAND_DISPLAY DISPLAY
  bwrap --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- \
    "$tools/root/usr/bin/Xvfb" ":$1" -screen 0 1280x1024x24 -nolisten tcp -nolisten unix >"$base/xvfb-$1.log" 2>&1 &
  xvfb_pid=$!
  for _ in $(seq 1 100); do DISPLAY=":$1" xdotool getmouselocation >/dev/null 2>&1 && break; sleep 0.05; done
  export DISPLAY=":$1" GDK_BACKEND=x11
}

xvfb_stop() { pkill -TERM -P "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }
