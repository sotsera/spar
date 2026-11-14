#!/usr/bin/env bash
# Usage: sandbox/cases.sh <group|case> [runs] — prints one guest configuration per line ("label args...", no spaces
# inside an argument) for sandbox/run.ps1. Everything here changes window state, uses real input or shows the native
# dialog, so it runs only inside Windows Sandbox (the host refuses these actions elsewhere).
set -u
what=${1:?group}
runs=${2:-3}
at() { echo $((5000 + RANDOM % 5000)); }

one() {
  local c=$1 n=$2 t; t=$(at)
  local l="$c-$n"
  case $c in
    # Reference: on screen, healthy, 60 s.
    onscreen) echo "$l --duration 60000" ;;
    anim) echo "$l --actions 2000:load:anim --duration 60000" ;;
    idle) echo "$l --actions $t:hang-idle --duration $((t + 30000))" ;;
    # Window states with the gate off: pings keep going, so round trips, ticks and visibility show any throttling,
    # and the detector reports what an ungated heartbeat would.
    minimize) echo "$l --gate none --actions 5000:minimize,65000:restore --duration 75000 --timeout 150" ;;
    cover) echo "$l --gate none --actions 5000:cover,65000:uncover --duration 75000 --timeout 150" ;;
    offscreen) echo "$l --gate none --actions 5000:offscreen,65000:onscreen --duration 75000 --timeout 150" ;;
    # Beyond Chromium's 5-minute mark for intensive timer throttling of hidden pages: 7 min minimised.
    minimize-long) echo "$l --gate none --actions 5000:minimize,425000:restore --duration 435000 --timeout 500" ;;
    ctl-hidden) echo "$l --gate none --actions 5000:hide-controller,65000:show-controller --duration 75000 --timeout 150" ;;
    suspend) echo "$l --gate none --actions 5000:hide-controller,6000:suspend,36000:resume,37000:show-controller --duration 50000 --timeout 150" ;;
    mem-low) echo "$l --gate none --actions 5000:mem:low,35000:mem:normal --duration 45000 --timeout 150" ;;
    # The design's gate (window state) with a hang that starts while minimised, then restore.
    gate-minimize-hang) echo "$l --gate window --actions 5000:minimize,10000:hang-idle,45000:restore --duration 70000 --timeout 150" ;;
    # A hang while covered (not gated: the window is not minimised).
    cover-hang) echo "$l --gate window --actions 5000:cover,$((t + 5000)):hang-idle --duration $((t + 40000)) --timeout 150" ;;
    # Machine under CPU load (2 spinning threads per guest CPU in the app process).
    cpu-idle) echo "$l --actions 3000:burn-on:0,63000:burn-off --duration 65000 --timeout 150" ;;
    cpu-tasks100) echo "$l --actions 2000:load:tasks100,3000:burn-on:0,63000:burn-off --duration 65000 --timeout 150" ;;
    cpu-hang) echo "$l --actions 3000:burn-on:0,$t:hang-idle --duration $((t + 30000)) --timeout 150" ;;
    # ProcessFailed(RenderProcessUnresponsive) with real input into a hung page: a click 5 s into the hang, with and
    # without heartbeat traffic; and a key press.
    input-click) echo "$l --actions 3000:hang-idle,8000:click --duration 75000 --timeout 150" ;;
    input-click-quiet) echo "$l --no-ping --probe-interval 0 --actions 3000:hang-idle,8000:click --duration 75000 --timeout 150" ;;
    input-key) echo "$l --actions 3000:hang-idle,8000:click,9000:key --duration 75000 --timeout 150" ;;
    # The default native dialog: answered "Reload page" (terminate the renderer, then reload) after 3 s; a finite
    # 25 s task that recovers while the dialog is open (auto-close); and "Wait".
    dialog-reload) echo "$l --on-unresponsive dialog --dialog-answer reload --dialog-after 3000 --actions 3000:state,4000:hang-idle --duration 40000 --timeout 150" ;;
    dialog-recover) echo "$l --on-unresponsive dialog --actions 4000:busy:25000 --duration 40000 --timeout 150" ;;
    dialog-wait) echo "$l --on-unresponsive dialog --dialog-answer wait --dialog-after 3000 --actions 4000:hang-idle --duration 40000 --timeout 150" ;;
    *) echo "unknown case $c" >&2; return 1 ;;
  esac
}

group() {
  case $1 in
    base) echo onscreen anim idle ;;
    states) echo minimize cover offscreen ctl-hidden suspend mem-low ;;
    gated) echo gate-minimize-hang cover-hang ;;
    long) echo minimize-long ;;
    cpu) echo cpu-idle cpu-tasks100 cpu-hang ;;
    input) echo input-click input-click-quiet input-key ;;
    dialog) echo dialog-reload dialog-recover dialog-wait ;;
    *) echo "$1" ;;
  esac
}

for c in $(group "$what"); do
  for n in $(seq 1 "$runs"); do one "$c" "$n"; done
done
