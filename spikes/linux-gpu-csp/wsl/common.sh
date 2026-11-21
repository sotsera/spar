#!/usr/bin/env bash
# Sourced by the other WSL scripts (Ubuntu-24.04, as the normal user, no root). The working copy, its run homes and
# the tools live under /var/tmp/spar-linux/linux-gpu-csp; the SDK and NuGet packages are eng/wsl's shared cache.
# Nothing goes to the user's WSL home.
base=/var/tmp/spar-linux/linux-gpu-csp
work=$base/work
spike=$work/spikes/linux-gpu-csp
tools=$base/tools
cache=/var/tmp/spar-linux/cache
export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER LIBGL_ALWAYS_SOFTWARE GALLIUM_DRIVER
for v in $(env | grep -o '^JSC_[^=]*' || true); do unset "$v"; done

# isolate <run>: HOME and the XDG folders under the working copy's artifacts/linux-homes/<run>/ (AGENTS.md);
# XDG_RUNTIME_DIR in /dev/shm (WebKit's sandbox rejects one under /var/tmp, see the pitfalls).
isolate() {
  local homes=$work/artifacts/linux-homes/$1
  mkdir -p "$homes"/{home,cache,config,data,state} "/dev/shm/spar-lgc-$1"
  chmod 700 "/dev/shm/spar-lgc-$1"
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-lgc-$1
}
cleanup_runtime() { case "${XDG_RUNTIME_DIR:-}" in /dev/shm/spar-lgc-*) rm -rf "$XDG_RUNTIME_DIR" ;; esac; }

# Xvfb of our own (never WSLg's :0 or Wayland). xkbcomp is not installed system-wide and Xvfb runs it from /usr/bin,
# so Xvfb runs in a bubblewrap mount namespace whose /usr/bin also holds the unpacked xkbcomp (tools/ is a copy of
# the unpacked xvfb, x11-xkb-utils and libxfont2 packages).
xvfb_start() {
  unset WAYLAND_DISPLAY DISPLAY
  LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- \
    "$tools/root/usr/bin/Xvfb" ":$1" -screen 0 1280x1024x24 -nolisten tcp -nolisten unix >"$base/xvfb-$1.log" 2>&1 &
  xvfb_pid=$!
  # WSLg's /tmp/.X11-unix is read-only, so Xvfb listens on the abstract socket only.
  for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X$1\$" /proc/net/unix && break; sleep 0.05; done
  export DISPLAY=":$1" GDK_BACKEND=x11
}
xvfb_stop() { [ -n "${xvfb_pid:-}" ] && { pkill -TERM -P "$xvfb_pid" 2>/dev/null; kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }; true; }
