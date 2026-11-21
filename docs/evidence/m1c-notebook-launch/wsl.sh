#!/usr/bin/env bash
# Why the Notebook's launch grew (docs/evidence/m1c-notebook-launch.md): an interleaved A/B of a Production NativeAOT
# executable under five D-Bus and accessibility conditions, in WSL (Ubuntu-24.04, the normal user, no root). From Git
# Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the path):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-notebook-launch/wsl.sh <step>...
# Steps:
#   old                       the Notebook of 97d26c5 (M1c step 6), from artifacts/linux-runs/m1c-notebook-launch/
#                             old-97d26c5.tar (git archive), built in /var/tmp/spar-nb-old
#   new                       today's Notebook, published from ../m1c-blackbox/wsl.sh's copy after its `sync`
#   ab <label> <exe> <app id> <rounds> <conditions>
#                             <rounds> rounds after one warm-up round; each round launches the app once per condition,
#                             in an order that rotates every round; conditions are letters of "abcdefg":
#     a  no session bus (DBUS_SESSION_BUS_ADDRESS unset), as the launcher of steps 1-6 ran the apps
#     b  a private session bus only (dbus-daemon, no DISPLAY), nothing of accessibility started first
#     c  the black box now: (b), with org.a11y.Bus and its registry started before the app
#     d  (c) with GTK_A11Y=none
#     e  (c) with WEBKIT_A11Y_BUS_ADDRESS set empty, which WebKitGTK 2.52 reads in place of asking for the bus
#        (UIProcess/glib/WebProcessPoolGLib.cpp), and with which the web process connects nowhere
#        (AccessibilityAtspi::connect returns for an empty address)
#     f  (c) with xdg-desktop-portal also started before the app, as a desktop session has it running: the web
#        process asks it for real-time threads (WTF RealTimeThreads, a synchronous proxy on its main thread)
#     g  (c) on a bus whose only service is org.a11y.Bus (no portal can be started, as in the CI image)
# Each condition has its own private Xvfb on an abstract socket (from :190), so nothing one condition publishes on a
# root window (an autolaunched bus, AT_SPI_BUS) reaches another; DISPLAY and WAYLAND_DISPLAY are unset here, so nothing
# opens on WSLg's display. Each launch gets a new data root and runtime folder, as both launchers did. Every process this
# starts carries SPAR_AB_RUN in its environment, and the end of the run ends those still alive; nothing else is touched.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"); children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
work=/var/tmp/spar-nb-launch
logs=$source_repo/artifacts/linux-runs/m1c-notebook-launch
run_id=ab-$$-$(date +%s)
mkdir -p "$work/bin" "$logs"
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS AT_SPI_BUS_ADDRESS GTK_A11Y NO_AT_BRIDGE WEBKIT_A11Y_BUS_ADDRESS

# The Xvfb wrapper of ../m1c-blackbox/wsl.sh.
cat > "$work/bin/Xvfb" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
exec bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" "\$@"
EOF
chmod +x "$work/bin/Xvfb"

# Condition g's session bus: the session bus's own policy, with org.a11y.Bus as its only activatable service.
mkdir -p "$work/a11y-only-services"
cp /usr/share/dbus-1/services/org.a11y.Bus.service "$work/a11y-only-services/"
cat > "$work/a11y-only.conf" <<EOF
<!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN"
 "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
<busconfig>
  <type>session</type>
  <keep_umask/>
  <listen>unix:tmpdir=/tmp</listen>
  <auth>EXTERNAL</auth>
  <servicedir>$work/a11y-only-services</servicedir>
  <policy context="default">
    <allow send_destination="*" eavesdrop="true"/>
    <allow eavesdrop="true"/>
    <allow own="*"/>
  </policy>
</busconfig>
EOF

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

dotnet_env() {
  export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
  export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
}

# The processes of this run (SPAR_AB_RUN in their environment), optionally only those of one more variable's value.
tagged() {
  # One grep over the environments (NUL-separated lines), then the second variable over what matched.
  local files
  files=$(grep -lsxz "SPAR_AB_RUN=$run_id" /proc/[0-9]*/environ 2>/dev/null)
  [ -n "$files" ] && [ -n "${1:-}" ] && files=$(grep -lsxz "$1" $files 2>/dev/null)
  for p in $files; do p=${p#/proc/}; echo "${p%/environ}"; done
}

end_tagged() {
  local ids
  ids=$(tagged "${1:-}")
  [ -n "$ids" ] && kill -9 $ids 2>/dev/null
  return 0
}

declare -A display_of
start_displays() {
  local conditions=$1 number=190 c
  for c in $(grep -o . <<<"$conditions"); do
    while [ -e "/tmp/.X$number-lock" ] || grep -q "@/tmp/.X11-unix/X$number\$" /proc/net/unix; do number=$((number + 1)); done
    env -i PATH=/usr/bin:/bin SPAR_AB_RUN=$run_id setsid "$work/bin/Xvfb" ":$number" -noreset -screen 0 3840x2160x24 \
      -nolisten tcp -nolisten unix > "$work/xvfb-$c.log" 2>&1 &
    local tries=0
    until grep -q "@/tmp/.X11-unix/X$number\$" /proc/net/unix || [ $tries -ge 100 ]; do sleep 0.05; tries=$((tries + 1)); done
    display_of[$c]=":$number"
    echo "condition $c on display :$number"
    number=$((number + 1))
  done
}

# One launch: $1 condition, $2 executable, $3 app id, $4 launch number, $5 round; prints one result line.
launch() {
  local c=$1 exe=$2 id=$3 n=$4 round=$5
  local root=$work/roots/$run_id/$n runtime=/dev/shm/spar-ab-$run_id-$n
  # Condition a keeps one runtime folder for all its launches, as the launcher of steps 1-6 passed the runner's own:
  # its autolaunched bus outlives a launch, and the accessibility bus it started keeps its socket there.
  [ "$c" = a ] && runtime=/dev/shm/spar-ab-$run_id-a
  mkdir -p "$root"/{home,data,config,cache,state,tmp} "$runtime" && chmod 700 "$runtime"
  local -a base=(PATH=/usr/local/bin:/usr/bin:/bin LANG=C.UTF-8 HOME=$root/home XDG_DATA_HOME=$root/data
    XDG_CONFIG_HOME=$root/config XDG_CACHE_HOME=$root/cache XDG_STATE_HOME=$root/state XDG_RUNTIME_DIR=$runtime
    TMPDIR=$root/tmp GDK_BACKEND=x11 SPAR_AB_RUN=$run_id)
  local -a app=("${base[@]}" DISPLAY=${display_of[$c]} SPAR_AB_LAUNCH=$n)
  local bus="" a11y="" before=0
  if [ "$c" != a ]; then
    # The black box's bus: dbus-daemon with the app's environment but no DISPLAY (LinuxAppProcess.Start).
    local config=(--session)
    [ "$c" = g ] && config=(--config-file="$work/a11y-only.conf")
    env -i "${base[@]}" SPAR_AB_BUS=$n dbus-daemon "${config[@]}" --nofork --nopidfile --print-address=1 > "$root/bus-address" 2> "$root/bus.err" &
    local tries=0
    until [ -s "$root/bus-address" ] || [ $tries -ge 200 ]; do sleep 0.01; tries=$((tries + 1)); done
    bus=$(head -1 "$root/bus-address")
    app+=(DBUS_SESSION_BUS_ADDRESS=$bus)
    if [ -n "${SPAR_AB_MONITOR:-}" ]; then
      # Every message on the launch's session bus, with its time, to see who starts what (dbus-monitor --monitor).
      env -i "${base[@]}" SPAR_AB_BUS=$n dbus-monitor --address "$bus" --monitor > "$logs/$label-$n-$c.bus.txt" 2>&1 &
      sleep 0.2
    fi
    if [ "$c" != b ]; then
      # LinuxDesktop.StartAccessibility: org.a11y.Bus.GetAddress (auto-starting the launcher), then the registry.
      local t0=$(date +%s%N)
      a11y=$(env -i "${base[@]}" DBUS_SESSION_BUS_ADDRESS=$bus gdbus call --session --dest org.a11y.Bus \
        --object-path /org/a11y/bus --method org.a11y.Bus.GetAddress 2>> "$root/bus.err" | sed -E "s/^\('(.*)',\)$/\1/")
      env -i "${base[@]}" gdbus call --address "$a11y" --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
        --method org.freedesktop.DBus.StartServiceByName org.a11y.atspi.Registry 0 > /dev/null 2>> "$root/bus.err"
      if [ "$c" = f ]; then
        env -i "${base[@]}" DBUS_SESSION_BUS_ADDRESS=$bus gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
          --method org.freedesktop.DBus.StartServiceByName org.freedesktop.portal.Desktop 0 > /dev/null 2>> "$root/bus.err"
      fi
      before=$(( ($(date +%s%N) - t0) / 1000000 ))
    fi
  fi
  [ "$c" = d ] && app+=(GTK_A11Y=none)
  [ "$c" = e ] && app+=(WEBKIT_A11Y_BUS_ADDRESS=)

  env -i "${app[@]}" "$exe" > "$root/app.out" 2>&1 &
  local pid=$!
  local log=$root/data/$id/logs/app.log tries=0
  until grep -q "ui-ready at" "$log" 2>/dev/null || [ $tries -ge 1500 ]; do sleep 0.02; tries=$((tries + 1)); done
  sleep 1
  local names
  names=$(for p in $(tagged "SPAR_AB_LAUNCH=$n") $( [ -n "$bus" ] && tagged "SPAR_AB_BUS=$n"); do cat "/proc/$p/comm" 2>/dev/null; done | sort | uniq -c | awk '{printf "%s%s×%s", sep, $2, $1; sep=","}')
  local apps="-"
  if [ -n "$a11y" ]; then
    apps=$(env -i "${base[@]}" gdbus call --address "$a11y" --dest org.a11y.atspi.Registry --object-path /org/a11y/atspi/accessible/root \
      --method org.freedesktop.DBus.Properties.Get org.a11y.atspi.Accessible ChildCount 2>/dev/null | grep -o '[0-9]\+' | head -1)
  fi
  kill -TERM "$pid" 2>/dev/null
  tries=0
  while kill -0 "$pid" 2>/dev/null && [ $tries -ge 0 ] && [ $tries -lt 400 ]; do sleep 0.02; tries=$((tries + 1)); done
  wait "$pid" 2>/dev/null
  local exit=$?
  # The app's own processes, then the launch's bus and what it started (condition a's autolaunched bus stays, as it
  # stayed for the launcher of steps 1-6, until the end of the run).
  local p comm
  for p in $(tagged "SPAR_AB_LAUNCH=$n"); do
    comm=$(cat "/proc/$p/comm" 2>/dev/null) || continue
    [ "$c" = a ] && [[ $comm =~ ^(dbus-daemon|dbus-launch|at-spi) ]] && continue
    kill -9 "$p" 2>/dev/null
  done
  if [ -n "$bus" ]; then
    # The bus first, so the services it started (the document portal's FUSE mount among them) see it go and exit.
    for p in $(tagged "SPAR_AB_BUS=$n"); do [ "$(cat "/proc/$p/comm" 2>/dev/null)" = dbus-daemon ] && kill -TERM "$p" 2>/dev/null; done
    sleep 0.5
    end_tagged "SPAR_AB_BUS=$n"
  fi
  if grep -q " $runtime/doc " /proc/mounts; then
    fusermount3 -u -z "$runtime/doc" && echo "note: unmounted the document portal's $runtime/doc"
  fi
  cp "$log" "$logs/$label-$n-$c.app.log" 2>/dev/null
  local started phases
  started=$(grep -o "Host started [0-9.]* ms after the process" "$log" | grep -o "[0-9.]\+" | head -1)
  phases=$(grep -oE "\] (single instance|prerequisites checked|gpu checked|theme read|services built|gtk ready|web view created|window shown|page committed|first bridge message|first bridge reply|ui-ready) at [0-9.]+ ms" "$log" \
    | sed -E 's/^\] (.*) at ([0-9.]+) ms$/\1=\2/; s/ /_/g' | paste -sd' ')
  echo "LAUNCH round=$round n=$n condition=$c exit=$exit host_started=${started:-?} a11y_before_ms=$before registry_children=${apps:-?} $phases processes=$names"
  [ "$c" = a ] || rm -rf "$runtime"
}

step() {
  case $1 in
    old)
      dotnet_env
      rm -rf /var/tmp/spar-nb-old && mkdir -p /var/tmp/spar-nb-old/repo
      tar -xf "$logs/old-97d26c5.tar" -C /var/tmp/spar-nb-old/repo || return 1
      (cd /var/tmp/spar-nb-old/repo/src/Spar.Client && timed npm ci --ignore-scripts && timed npm run build) || return 1
      (cd /var/tmp/spar-nb-old/repo && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o /var/tmp/spar-nb-old/notebook) || return 1
      mkdir -p "$work/apps/old" && cp /var/tmp/spar-nb-old/notebook/Notebook "$work/apps/old/Notebook" && ls -l "$work/apps/old" ;;
    new)
      dotnet_env
      bash "$source_repo/docs/evidence/m1c-blackbox/wsl.sh" sync client > /dev/null || return 1
      (cd /var/tmp/spar-m1c-blackbox/repo && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$work/publish-new") || return 1
      mkdir -p "$work/apps/new" && cp "$work/publish-new/Notebook" "$work/apps/new/Notebook" && ls -l "$work/apps/new" ;;
    ab)
      label=$2
      local exe=$3 id=$4 rounds=$5 conditions=$6
      start_displays "$conditions"
      local n=0 round c rotated
      for round in $(seq 0 "$rounds"); do
        rotated=${conditions:$((round % ${#conditions}))}${conditions:0:$((round % ${#conditions}))}
        for c in $(grep -o . <<<"$rotated"); do
          n=$((n + 1))
          launch "$c" "$exe" "$id" "$n" "$round"
        done
      done
      end_tagged
      sleep 0.5
      awk -v run="/dev/shm/spar-ab-$run_id-" 'index($2, run) == 1 && $2 ~ "/doc$" {print $2}' /proc/mounts | while read -r m; do fusermount3 -u -z "$m"; done
      rm -rf /dev/shm/spar-ab-$run_id-*
      echo "processes of this run left: $(tagged | wc -l)" ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

label=run
{ echo "STEP $* on $(uname -r), WebKitGTK $(pkg-config --modversion webkitgtk-6.0 2>/dev/null), GTK $(pkg-config --modversion gtk4 2>/dev/null)"; step "$@"; } 2>&1 | tee "$logs/${1}-${2:-}.log"
exit "${PIPESTATUS[0]}"
