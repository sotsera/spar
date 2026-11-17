#!/usr/bin/env bash
# Every scenario of docs/evidence/m1b-hangs.md, in order. Needs SPAR_REPO and SPAR_PROBE (see run.sh) and the three
# builds: the Notebook in Debug with SparDevServer=false, its NativeAOT Production publish in artifacts/m1b-hangs/notebook,
# and the copy that keeps the stall probe in artifacts/m1b-hangs/notebook-kept-publish. Button ids: 200 Wait, 201 Reload
# page, 202 Close anyway, 300 Keep waiting, 301 Exit now. Usage: scenarios.sh [name...] (all when none).
set -u
H="$(cd "$(dirname "$0")" && pwd)"
run() { echo "== $*"; bash "$H/run.sh" "$@" | grep -E '>' | sed 's/^/   /'; }
SELECTED=("$@")
w() { [ ${#SELECTED[@]} -eq 0 ] && return 0; local n; for n in "${SELECTED[@]}"; do [ "$n" = "$1" ] && return 0; done; return 1; }

# No false alarm: an idle page for 60 s, then finite work of 3 s and 10 s (3 runs each).
if w idle; then run aot idle -- wait:ui-ready:30 no-dialog:60 close exit:30; fi
if w busy; then
  for i in 1 2 3; do
    run dev busy3-$i --env SPAR_DEV_PAGE_HANG=3000:3000 -- "wait:busy for:30" no-dialog:30 close exit:30
    run dev busy10-$i --env SPAR_DEV_PAGE_HANG=3000:10000 -- "wait:busy for:30" no-dialog:30 close exit:30
  done
fi

# An endless hang, at five phases against the 5 s ticks: time to the alarm, then "Reload page" (renderer ended, one reload).
if w endless; then
  for delay in 3000 4200 5400 6600 7800; do
    run dev endless-$delay --env SPAR_DEV_PAGE_HANG=$delay -- "wait:the page hangs from now:30" "wait:The page is not responding:40" \
      dialog:10 sleep:1000 press:201 gone:10 wait:ui-ready:20 sleep:2000 close exit:30
  done
fi

# Work of 25 s: the alarm, then the page answers and the dialog closes by itself.
if w recover; then
  run dev busy25 --env SPAR_DEV_PAGE_HANG=3000:25000 -- "wait:busy for:30" "wait:The page is not responding:40" dialog:10 \
    "gone:20" "wait:answered again:5" sleep:6000 close exit:30
fi

# The Wait schedule, on a 500 ms heartbeat (Development override): 20/40/80/160/160 s become 2/4/8/16/16 s.
if w schedule; then
  run dev schedule --env SPAR_DEV_HEARTBEAT_MS=500 --env SPAR_DEV_PAGE_HANG=2000 -- "wait:the page hangs from now:30" \
    "wait:The page is not responding:20" dialog:5 press:200 \
    "wait:asking again:10" dialog:5 press:200 "wait:asking again:10" dialog:5 press:200 \
    "wait:asking again:20" dialog:5 press:200 "wait:asking again:30" dialog:5 press:200 \
    "wait:asking again:30" dialog:5 press:201 gone:10 wait:ui-ready:20 close exit:30
fi

# Closing a page that stops answering: silent for one interval (5 s) before it is found hung, then Wait (the dialog
# comes back after 20 s), then Close anyway; and a page already hung, whose open dialog becomes the close one.
if w close; then
  run dev close-silent --env SPAR_DEV_PAGE_HANG=3000 -- "wait:the page hangs from now:30" sleep:1000 close \
    "wait:did not answer the close request:10" dialog:5 press:200 "wait:asking again:40" dialog:5 press:202 exit:30
  run dev close-hung --env SPAR_DEV_PAGE_HANG=3000 -- "wait:the page hangs from now:30" "wait:The page is not responding:40" \
    dialog:5 sleep:1000 close "wait:Wait / Close anyway:5" sleep:500 dialog:5 press:202 exit:30
fi

# The shutdown prompt: a service whose disposal takes 120 s, Keep waiting, then Exit now (exit 10); and one of 15 s,
# whose prompt goes away when the shutdown finishes (exit 0).
if w shutdown; then
  run dev shutdown-exit-now --env SPAR_DEV_SLOW_DISPOSE_MS=120000 -- wait:ui-ready:30 close "wait:shutdown prompt shown:20" \
    dialog:5 press:300 "wait:shutdown prompt shown:40" dialog:5 press:301 exit:10
  run dev shutdown-finishes --env SPAR_DEV_SLOW_DISPOSE_MS=15000 -- wait:ui-ready:30 close "wait:shutdown prompt shown:20" \
    dialog:5 gone:10 exit:10
fi

# The automated mode: no dialog and no prompt; a close of a hung page closes anyway; a long shutdown is only logged.
if w automated; then
  run dev automated-hang --env SPAR_AUTOMATED=1 --env SPAR_DEV_PAGE_HANG=3000 -- "wait:The page is not responding:40" \
    no-dialog:3 close "wait:closes a page that does not answer:10" exit:30
  run dev automated-shutdown --env SPAR_AUTOMATED=1 --env SPAR_DEV_SLOW_DISPOSE_MS=13000 -- wait:ui-ready:30 close \
    "wait:without finishing:20" no-dialog:2 exit:10
fi

# The stall probe: a Development stall in a named callback, a stall from outside (the UI thread suspended); the
# Production build without the probe; the copy that keeps it.
if w stall; then
  run dev stall --env SPAR_DEV_UI_STALL_MS=300 -- wait:ui-ready:30 "wait:development stall trigger:5" sleep:500 stall-ui:200 \
    "wait:outside Spar's callbacks:5" close exit:30
  run aot stall-absent -- wait:ui-ready:30 stall-ui:300 "absent:stalled:5" close exit:30
  run kept stall-kept -- "wait:stall probe on:10" wait:ui-ready:30 stall-ui:300 "wait:outside Spar's callbacks:5" close exit:30
fi

# Production, a renderer suspended from outside: the alarm, then the page answering again when resumed; "Reload
# page" on a suspended renderer; and 30 s with the dialog open, to see whether WebView2 adds RenderProcessUnresponsive.
if w production; then
  run aot suspend-recover -- wait:ui-ready:30 sleep:2000 suspend-renderer "wait:The page is not responding:40" dialog:5 \
    sleep:2000 resume-renderer gone:10 "wait:answered again:5" close exit:30
  run aot suspend-reload -- wait:ui-ready:30 sleep:2000 suspend-renderer "wait:The page is not responding:40" dialog:5 \
    sleep:1000 press:201 gone:10 wait:ui-ready:20 close exit:30
  run aot engine-merge -- wait:ui-ready:30 sleep:2000 suspend-renderer "wait:The page is not responding:40" dialog:5 \
    sleep:30000 press:200 sleep:2000 resume-renderer gone:5 close exit:30
  run kept hook -- wait:ui-ready:30 sleep:2000 suspend-renderer "wait:hook: main Unresponsive:40" dialog:5 sleep:1000 \
    press:201 "wait:hook: main Reloaded:5" wait:ui-ready:20 sleep:2000 suspend-renderer "wait:hook: main Unresponsive:40" \
    dialog:5 resume-renderer "wait:hook: main Recovered:10" sleep:1000 suspend-renderer \
    "wait:hook: main Unresponsive:40" close "wait:Wait / Close anyway:5" sleep:500 dialog:5 press:202 "wait:hook: main ClosedAnyway:5" exit:30
fi
