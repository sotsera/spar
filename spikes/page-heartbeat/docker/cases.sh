#!/usr/bin/env bash
# Runs inside the container as uid 1000 (started by docker/in-container.sh), from /spike:
#   bash docker/cases.sh <case|group> [runs]
# Same cases as scripts/run-win.sh where the host allows them, plus window states and WebKit's responsiveness property.
# The app runs on the container's Xvfb display under openbox; logs go to /out/<case>-<n>.log.
set -u
exe=/spike/Beat.Linux/bin/publish/Beat.Linux
out=/out
what=${1:?case}
runs=${2:-5}

run() { # run <case> <n> <args...>
  local name=$1 n=$2; shift 2
  local data=/tmp/u/data-$name-$n
  rm -rf "$data"
  "$exe" --www /spike/www/ --data "$data" --log "$out/$name-$n.log" "$@" >/dev/null 2>"$out/$name-$n.err"
  echo "$name $n exit=$? $(date +%H:%M:%S)" | tee -a "$out/runs.txt"
  [ -s "$out/$name-$n.err" ] || rm -f "$out/$name-$n.err"
  rm -rf "$data"
  sleep 0.5
}

at() { echo $((5000 + RANDOM % 5000)); }

one() {
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
    load-doc) run "$c" "$n" --start /loadhang.html --arm doc --end-after 30000 ;;
    load-contentloading) run "$c" "$n" --start /loadhang.html --arm content-loading --end-after 30000 ;;
    load-navcompleted) run "$c" "$n" --start /loadhang.html --arm completed-ok --end-after 30000 ;;
    noinject) run "$c" "$n" --no-inject --arm completed-ok --end-after 30000 ;;
    anim) run "$c" "$n" --actions "2000:load:anim" --duration 60000 ;;
    tasks100) run "$c" "$n" --actions "2000:load:tasks100" --duration 60000 ;;
    tasks1000) run "$c" "$n" --actions "2000:load:tasks1000" --duration 60000 ;;
    # WebKit's is-web-process-responsive for an idle hang, 70 s: with pings, with no traffic at all, with input
    # (an xdotool click into the page 5 s into the hang), and with a reload request 20 s into the hang.
    wk-pings) run "$c" "$n" --actions "3000:hang-idle" --duration 75000 --timeout 120 ;;
    wk-quiet) run "$c" "$n" --actions "3000:hang-idle" --duration 75000 --timeout 120 --no-ping --probe-interval 0 ;;
    wk-click) run "$c" "$n" --actions "3000:hang-idle,8000:sh:xdotool mousemove --window \$WID 200 200 click 1" --duration 45000 --timeout 120 --no-ping --probe-interval 0 ;;
    wk-busy) run "$c" "$n" --actions "3000:busy:20000,5000:sh:xdotool mousemove --window \$WID 200 200 click 1" --duration 35000 --timeout 120 ;;
    stress-hung) run "$c" "$n" --actions "3000:memory:before,4000:hang-idle,6000:stress-on:1000/1024,36000:stress-off,37000:memory:after,45000:memory:later" --duration 46000 --timeout 90 ;;
    stress-healthy) run "$c" "$n" --actions "3000:memory:before,6000:stress-on:1000/1024,36000:stress-off,37000:memory:after,45000:memory:later" --duration 46000 --timeout 90 ;;
    reload) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:reload" --duration 70000 --timeout 90 ;;
    kill) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:kill" --duration 45000 --timeout 90 ;;
    kill-sigkill) run "$c" "$n" --kill-how sigkill --actions "3000:state,4000:hang-idle,24000:kill" --duration 45000 --timeout 90 ;;
    renav) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:/index.html" --duration 70000 --timeout 90 ;;
    blank) run "$c" "$n" --actions "3000:state,4000:hang-idle,24000:nav:about:blank,26000:nav:/index.html" --duration 45000 --timeout 90 ;;
    healthy-reload) run "$c" "$n" --actions "3000:state,6000:reload" --duration 15000 ;;
    healthy-kill) run "$c" "$n" --actions "3000:state,6000:kill" --duration 15000 ;;
    reload-load) run "$c" "$n" --start /loadhang.html --arm content-loading --on-unresponsive reload --end-after 40000 ;;
    kill-load) run "$c" "$n" --start /loadhang.html --arm content-loading --on-unresponsive kill --end-after 40000 ;;
    # Window states, gate off so pings keep going: their round trips and the page's timer ticks show throttling.
    st-minimize) run "$c" "$n" --gate none --actions "5000:minimize,65000:restore" --duration 75000 --timeout 120 ;;
    st-minimize-long) run "$c" "$n" --gate none --actions "5000:minimize,425000:restore" --duration 435000 --timeout 500 ;;
    st-hide) run "$c" "$n" --gate none --actions "5000:hide,65000:show" --duration 75000 --timeout 120 ;;
    st-cover) run "$c" "$n" --gate none --actions "5000:cover,65000:uncover" --duration 75000 --timeout 120 ;;
    st-offscreen) run "$c" "$n" --gate none --actions "5000:sh:xdotool windowmove \$WID 3000 3000,65000:sh:xdotool windowmove \$WID 100 100" --duration 75000 --timeout 120 ;;
    # The same with the design's gate (window state), to count false alarms with a hang while minimised.
    gate-minimize-hang) run "$c" "$n" --gate window --actions "5000:minimize,10000:hang-idle,45000:restore" --duration 70000 --timeout 120 ;;
    # Machine under CPU load: 2 spinning threads per CPU in the app process, healthy page with 100 ms tasks.
    cpu-idle) run "$c" "$n" --actions "3000:burn-on:$(( $(nproc) * 2 )),63000:burn-off" --duration 65000 --timeout 120 ;;
    cpu-tasks100) run "$c" "$n" --actions "2000:load:tasks100,3000:burn-on:$(( $(nproc) * 2 )),63000:burn-off" --duration 65000 --timeout 120 ;;
    *) echo "unknown case $c"; return 1 ;;
  esac
}

group() {
  case $1 in
    detect) echo baseline idle handler microtask busy3 busy10 busy20 worker ;;
    load) echo load-doc load-contentloading load-navcompleted noinject ;;
    healthy) echo anim tasks100 tasks1000 ;;
    wk) echo wk-pings wk-quiet wk-click wk-busy ;;
    stress) echo stress-hung stress-healthy ;;
    recover) echo reload kill kill-sigkill renav blank healthy-reload healthy-kill reload-load kill-load ;;
    states) echo st-minimize st-hide st-cover st-offscreen gate-minimize-hang ;;
    cpu) echo cpu-idle cpu-tasks100 ;;
    *) echo "$1" ;;
  esac
}

for c in $(group "$what"); do
  for n in $(seq 1 "$runs"); do one "$c" "$n"; done
done
