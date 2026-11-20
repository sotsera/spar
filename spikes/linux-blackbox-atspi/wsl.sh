#!/usr/bin/env bash
# The linux-blackbox-atspi spike's runs in WSL (Ubuntu-24.04, the normal user, no root): RESULTS.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/spikes/linux-blackbox-atspi/wsl.sh <step> [args...]
# Steps: sync client probe reader packages, then `run <bus> <verb> [args...]` (the reader, under an Xvfb of this
# script's own on display :63 and a private D-Bus session of the kind <bus> names: `standard` (dbus-run-session, with
# the distribution's session services, so the accessibility bus is activated on demand), `bare` (a session bus with no
# activatable services at all: a system without at-spi2-core) or `none` (no session bus)), and `collect`.
# Like docs/evidence/m1c-security-defaults/wsl.sh: the working copy is the Windows tree as it is, copied to
# /var/tmp/spar-linux-blackbox-atspi/repo without bin, obj, node_modules, artifacts and spikes (this spike's folder is
# copied in on its own); the SDK and caches are eng/wsl's under /var/tmp/spar-linux/cache; Xvfb is the one
# docs/evidence/m1c-host/wsl.sh `tools` unpacked into /var/tmp/spar-m1c-host/tools. HOME and the XDG folders are under
# the copy's artifacts/linux-homes/<run>/; XDG_RUNTIME_DIR is in /dev/shm (WebKit's sandbox refuses one under /var/tmp).
# Every inherited JSC_ and WEBKIT_ inspector or sandbox variable is cleared; nothing ever opens on WSLg.
set -uo pipefail
source_repo=$(cd "$(dirname "$0")/../.." && pwd)
spike_src=$source_repo/spikes/linux-blackbox-atspi
base=/var/tmp/spar-linux-blackbox-atspi
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
logs=$base/logs
display=63
mkdir -p "$base" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS
unset XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED PULSE_SERVER BROWSER DBUS_SESSION_BUS_ADDRESS
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\|BRIDGE_PROBE_\)[^=]*' || true); do unset "$v"; done

isolate() {
  local homes=$repo/artifacts/linux-homes/blackbox-atspi-$1
  rm -rf "$homes"
  mkdir -p "$homes"/{home,cache,config,data,state} "/dev/shm/spar-lba-$1"
  chmod 700 "/dev/shm/spar-lba-$1"
  export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
  export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-lba-$1 SPIKE_RUN=$1 SPIKE_HOMES=$homes
}
cleanup_runtime() { case "${XDG_RUNTIME_DIR:-}" in /dev/shm/spar-lba-*) rm -rf "$XDG_RUNTIME_DIR" ;; esac; }

# Xvfb in a bubblewrap mount namespace whose /usr/bin also holds the unpacked xkbcomp (not installed system-wide).
xvfb_start() {
  [ -x "$tools/root/usr/bin/Xvfb" ] || { echo "run docs/evidence/m1c-host/wsl.sh tools first"; return 1; }
  if grep -q "@/tmp/.X11-unix/X$display\$" /proc/net/unix; then echo "display :$display is taken"; return 1; fi
  LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- \
    "$tools/root/usr/bin/Xvfb" ":$display" -screen 0 1280x1024x24 -nolisten tcp -nolisten unix >"$base/xvfb-$display.log" 2>&1 &
  xvfb_pid=$!
  # WSLg's /tmp/.X11-unix is read-only, so Xvfb listens on the abstract socket only.
  for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X$display\$" /proc/net/unix && break; sleep 0.05; done
  export DISPLAY=":$display" GDK_BACKEND=x11
}
xvfb_stop() { [ -n "${xvfb_pid:-}" ] && { pkill -TERM -P "$xvfb_pid" 2>/dev/null; kill "$xvfb_pid" 2>/dev/null; wait "$xvfb_pid" 2>/dev/null; }; true; }

# A session bus with no activatable service at all (a system without at-spi2-core, as far as D-Bus can tell).
bare_config() {
  mkdir -p "$base/bus/empty-services"
  cat >"$base/bus/bare-session.conf" <<EOF
<!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
<busconfig>
  <type>session</type>
  <listen>unix:tmpdir=/tmp</listen>
  <auth>EXTERNAL</auth>
  <servicedir>$base/bus/empty-services</servicedir>
  <policy context="default"><allow send_destination="*" eavesdrop="true"/><allow eavesdrop="true"/><allow own="*"/></policy>
</busconfig>
EOF
}

# Every process of this run (by its XDG_RUNTIME_DIR), with its name; for the evidence after each run.
census() {
  for p in /proc/[0-9]*; do
    if { tr '\0' '\n' <"$p/environ"; } 2>/dev/null | grep -qx "XDG_RUNTIME_DIR=$XDG_RUNTIME_DIR"; then
      echo "  left: ${p#/proc/} $({ tr '\0' ' ' <"$p/cmdline"; } 2>/dev/null | cut -c1-120)"
    fi
  done
}

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

step() {
  case $1 in
    sync)
      mkdir -p "$repo"
      rsync -a --delete --exclude .git --exclude bin/ --exclude obj/ --exclude node_modules/ --exclude /artifacts/ --exclude /spikes/ \
        --exclude /tests/apps/BridgeProbeWasm/ "$source_repo/" "$repo/"
      mkdir -p "$repo/spikes"
      rsync -a --delete --exclude bin/ --exclude obj/ --exclude results/ "$spike_src/" "$repo/spikes/linux-blackbox-atspi/"
      echo "synced $(cd "$repo" && find src tests samples spikes -name '*.cs' | wc -l) C# files" ;;
    spike)
      rsync -a --delete --exclude bin/ --exclude obj/ --exclude results/ "$spike_src/" "$repo/spikes/linux-blackbox-atspi/" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run build) ;;
    probe)
      # The bridge probe as a Production NativeAOT executable, as the black box runs it.
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbe -c Release -r linux-x64 -o "$base/probe-aot") || return 1
      ls -l "$base/probe-aot/BridgeProbe" ;;
    reader)
      rsync -a --delete --exclude bin/ --exclude obj/ --exclude results/ "$spike_src/" "$repo/spikes/linux-blackbox-atspi/"
      (cd "$repo/spikes/linux-blackbox-atspi/Reader" && rm -rf bin obj "$base/reader" && timed dotnet publish -c Release -r linux-x64 -o "$base/reader" 2>&1 | tee "$logs/reader-publish.log")
      grep -oE "warning [A-Z]+[0-9]+[^[]*" "$logs/reader-publish.log" | sort -u | wc -l | sed 's/^/warning lines: /'
      ls -l "$base/reader/Reader"; ldd "$base/reader/Reader" ;;
    packages)
      for p in at-spi2-core at-spi2-common libatspi2.0-0t64 gsettings-desktop-schemas libxtst6 dbus dbus-daemon dbus-bin \
               dbus-session-bus-common libdbus-1-3 libatk-bridge2.0-0t64 libatk1.0-0t64 gir1.2-atspi-2.0 util-linux; do
        apt-cache show --no-all-versions "$p" 2>/dev/null | awk -v p="$p" '/^Version:/{v=$2} /^Installed-Size:/{i=$2} /^Size:/{s=$2} END{printf "%-28s %-22s download %7.1f KB  installed %6d KB\n", p, v, s/1024, i}'
      done
      echo "--- does WebKitGTK 6.0 (with GTK 4) pull any AT-SPI package? (recursive Depends only)"
      apt-cache depends --recurse --no-recommends --no-suggests --no-conflicts --no-breaks --no-replaces --no-enhances \
        libwebkitgtk-6.0-4 2>/dev/null | grep -E "^[a-z]" | sort -u >"$logs/webkit-closure.txt"
      wc -l <"$logs/webkit-closure.txt" | sed 's/^/packages in the closure: /'
      grep -E "spi|atk|dbus|xtst|libx11-6|util-linux|libglib" "$logs/webkit-closure.txt"
      echo "--- recommends of libgtk-4-1 and libwebkitgtk-6.0-4"
      apt-cache depends libgtk-4-1 libwebkitgtk-6.0-4 | grep -E "Recommends|Suggests"
      echo "--- what links what"
      for l in /usr/lib/x86_64-linux-gnu/libgtk-4.so.1 /usr/lib/x86_64-linux-gnu/libwebkitgtk-6.0.so.4 /usr/lib/x86_64-linux-gnu/libatspi.so.0; do
        echo "$l: $(ldd "$l" | grep -oE "lib(atspi|atk[^ ]*|dbus-1|gio-2.0|X11|Xtst)\.so[^ ]*" | sort -u | tr '\n' ' ')"
      done ;;
    run)
      shift
      local bus=$1 verb=$2
      shift 2
      isolate "$verb-$bus"
      xvfb_start || return 1
      export SPIKE_PROBE=$base/probe-aot/BridgeProbe SPIKE_BUS=$bus SPIKE_OUT=$logs
      local status
      case $bus in
        standard) timed dbus-run-session -- "$base/reader/Reader" "$verb" "$@"; status=$? ;;
        bare) bare_config; timed dbus-run-session --config-file="$base/bus/bare-session.conf" -- "$base/reader/Reader" "$verb" "$@"; status=$? ;;
        none) timed "$base/reader/Reader" "$verb" "$@"; status=$? ;;
        *) echo "unknown bus $bus"; status=2 ;;
      esac
      xvfb_stop
      sleep 0.5
      echo "census after the run (processes with this run's XDG_RUNTIME_DIR; none expected):"; census
      cleanup_runtime
      return $status ;;
    collect)
      mkdir -p "$spike_src/results"
      for f in "$logs"/*.log "$logs"/*.txt "$logs"/*.json; do
        [ -f "$f" ] || continue
        # The Windows profile folder (long and short names), the WSL user's name and any Windows SID.
        sed -e 's|C:\\Users\\[^\\ ]*|C:\\Users\\<user>|g' -e 's|[A-Z]\{1,6\}~1|<user>|g' -e "s|\b$(id -un)\b|<user>|g" \
          -e 's|S-1-5-21-[0-9-]*|<sid>|g' "$f" >"$spike_src/results/$(basename "$f")"
      done
      ls -l "$spike_src/results" ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

if [ "${1:-}" = run ]; then
  label=$(echo "$*" | tr ' /:' '___' | cut -c1-80)
  { echo "STEP $* on $(uname -r), $(date -u +%FT%TZ)"; step "$@"; } 2>&1 | tee "$logs/$label.log"
  exit "${PIPESTATUS[0]}"
fi

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
