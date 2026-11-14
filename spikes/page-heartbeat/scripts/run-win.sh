#!/usr/bin/env bash
# Usage (Git Bash, from spikes/page-heartbeat): scripts/run-win.sh <case|group> [runs]
# Runs the published host (Beat.Win/bin/publish) on this machine: offscreen (-32000,-32000), never activated, no input,
# no dialogs (the host refuses on-screen placement, window-state changes and dialogs outside Windows Sandbox). Each run
# gets a fresh user-data folder under artifacts/webview2-profiles/page-heartbeat/, deleted after the browser exited.
# Logs: results/win/<case>-<n>.log. Hangs start at a random offset of 5-10 s after page ready, so their phase against
# the 5 s ping ticks varies between runs.
set -u
export MSYS_NO_PATHCONV=1  # Git Bash would rewrite /loadhang.html and nav:/index.html into Windows paths
cd "$(dirname "$0")/.."
exe=Beat.Win/bin/publish/Beat.Win.exe
out=results/win
profiles=../../artifacts/webview2-profiles/page-heartbeat
mkdir -p "$out" "$profiles"
what=${1:?case}
runs=${2:-5}

run() { # run <case> <n> <args...>
  local name=$1 n=$2; shift 2
  local profile="$name-$n"
  rm -rf "${profiles:?}/$profile"
  "$exe" --profile "$profile" --log "$out/$name-$n.log" "$@" >/dev/null 2>&1
  echo "$name $n exit=$? $(date +%H:%M:%S)" | tee -a "$out/runs.txt"
  sleep 1
  rm -rf "${profiles:?}/$profile"
}

at() { echo $((5000 + RANDOM % 5000)); }

one() { # one <case> <n>
  local c=$1 n=$2 t; t=$(at)
  case $c in
    baseline) run "$c" "$n" --duration 60000 ;;
    idle) run "$c" "$n" --actions "$t:hang-idle" --duration $((t + 30000)) ;;
    handler) run "$c" "$n" --actions "$t:hang-handler" --duration $((t + 30000)) ;;
    microtask) run "$c" "$n" --actions "$t:hang-microtask" --duration $((t + 30000)) ;;
    busy3) run "$c" "$n" --actions "$t:busy:3000" --duration $((t + 15000)) ;;
    busy10) run "$c" "$n" --actions "$t:busy:10000" --duration $((t + 25000)) ;;
    busy20) run "$c" "$n" --actions "$t:busy:20000" --duration $((t + 35000)) ;;
    worker) run "$c" "$n" --actions "$t:hang-worker" --duration $((t + 30000)) ;;
    # Hang during load, as the first document: armed by the injected script's doc message, by ContentLoading, or only
    # after NavigationCompleted (never comes). The actions never start (no ready), so the timeout ends the run.
    load-doc) run "$c" "$n" --start /loadhang.html --arm doc --end-after 30000 ;;
    load-contentloading) run "$c" "$n" --start /loadhang.html --arm content-loading --end-after 30000 ;;
    load-navcompleted) run "$c" "$n" --start /loadhang.html --arm completed-ok --end-after 30000 ;;
    # A healthy page whose document has no injected handler: every ping is lost.
    noinject) run "$c" "$n" --no-inject --arm completed-ok --end-after 30000 ;;
    anim) run "$c" "$n" --actions "2000:load:anim" --duration 60000 ;;
    tasks100) run "$c" "$n" --actions "2000:load:tasks100" --duration 60000 ;;
    tasks1000) run "$c" "$n" --actions "2000:load:tasks1000" --duration 60000 ;;
    # ProcessFailed(RenderProcessUnresponsive) for an idle hang, 70 s, with and without any host-to-page traffic.
    pf-pings) run "$c" "$n" --actions "3000:hang-idle" --duration 75000 --timeout 120 ;;
    pf-quiet) run "$c" "$n" --actions "3000:hang-idle" --duration 75000 --timeout 120 --no-ping --probe-interval 0 ;;
    # Posting to a hung page: 1,000 messages of 1 KiB per second for 30 s, memory each second, pump probe throughout.
    stress-hung) run "$c" "$n" --actions "3000:memory:before,4000:hang-idle,6000:stress-on:1000/1024,36000:stress-off,37000:memory:after,45000:memory:later" --duration 46000 --timeout 90 ;;
    stress-healthy) run "$c" "$n" --actions "3000:memory:before,6000:stress-on:1000/1024,36000:stress-off,37000:memory:after,45000:memory:later" --duration 46000 --timeout 90 ;;
    # Recovery of a hung page, 20 s into the hang.
    reload) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:reload" --duration 70000 --timeout 90 ;;
    kill) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:kill" --duration 45000 --timeout 90 ;;
    renav) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:/index.html" --duration 70000 --timeout 90 ;;
    stop-reload) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:stop,24100:reload" --duration 70000 --timeout 90 ;;
    xsite) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:https://other.localhost/index.html,34000:nav:/index.html" --duration 50000 --timeout 90 ;;
    blank) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:about:blank,26000:nav:/index.html" --duration 45000 --timeout 90 ;;
    edge-kill) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:edge://kill" --duration 40000 --timeout 90 ;;
    reload-load) run "$c" "$n" --start /loadhang.html --arm content-loading --on-unresponsive reload --end-after 40000 ;;
    kill-load) run "$c" "$n" --start /loadhang.html --arm content-loading --on-unresponsive kill --end-after 40000 ;;
    healthy-reload) run "$c" "$n" --actions "3000:state,6000:reload" --duration 15000 ;;
    healthy-kill) run "$c" "$n" --actions "3000:state,6000:kill" --duration 15000 ;;
    # WebView-level visibility on the offscreen window (no change on the desktop): controller hidden, TrySuspend,
    # MemoryUsageTargetLevel low. Gate off, so pings keep going and their round trips show any throttling.
    ctl-hidden) run "$c" "$n" --gate none --actions "5000:hide-controller,65000:show-controller" --duration 75000 --timeout 120 ;;
    ctl-hidden-long) run "$c" "$n" --gate none --actions "5000:hide-controller,425000:show-controller" --duration 435000 --timeout 500 ;;
    ctl-suspend) run "$c" "$n" --gate none --actions "5000:hide-controller,6000:suspend,36000:resume,37000:show-controller" --duration 50000 --timeout 120 ;;
    mem-low) run "$c" "$n" --gate none --actions "5000:mem:low,35000:mem:normal" --duration 45000 --timeout 120 ;;
    *) echo "unknown case $c"; return 1 ;;
  esac
}

group() {
  case $1 in
    detect) echo baseline idle handler microtask busy3 busy10 busy20 worker ;;
    load) echo load-doc load-contentloading load-navcompleted noinject ;;
    healthy) echo anim tasks100 tasks1000 ;;
    pf) echo pf-pings pf-quiet ;;
    stress) echo stress-hung stress-healthy ;;
    recover) echo reload kill renav stop-reload xsite blank healthy-reload healthy-kill reload-load kill-load ;;
    webview) echo ctl-hidden ctl-suspend mem-low ;;
    *) echo "$1" ;;
  esac
}

for c in $(group "$what"); do
  for n in $(seq 1 "$runs"); do one "$c" "$n"; done
done
