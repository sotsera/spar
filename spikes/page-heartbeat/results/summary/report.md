## Detection: hang start (page HANG message) to HB-UNRESPONSIVE, 5 s interval, 3 misses

| Host | Case | Runs | Alarmed | Detection s, median (min-max) | Responsive again after alarm, s |
|---|---|---|---|---|---|
| Windows host | blank | 5 | 5/5 | 16.0 (15.9-16.0) | - |
| Windows host | busy10 | 5 | 0/5 | - | - |
| Windows host | busy20 | 5 | 5/5 | 17.8 (16.0-19.2) | 2.2 (0.8-4.0) |
| Windows host | busy3 | 5 | 0/5 | - | - |
| Windows host | edge-kill | 2 | 2/2 | 16.0 (16.0-16.0) | - |
| Windows host | handler | 5 | 5/5 | 18.5 (15.9-19.6) | - |
| Windows host | idle | 5 | 5/5 | 15.8 (15.3-19.9) | - |
| Windows host | kill | 5 | 5/5 | 16.0 (16.0-16.0) | 5.0 (5.0-5.0) |
| Windows host | kill-load | 5 | 5/5 | 20.0 (20.0-20.0) | - |
| Windows host | load-contentloading | 5 | 5/5 | 20.0 (20.0-20.0) | - |
| Windows host | load-doc | 5 | 5/5 | 20.0 (20.0-20.0) | - |
| Windows host | load-navcompleted | 5 | 0/5 | - | - |
| Windows host | microtask | 5 | 5/5 | 17.4 (15.9-19.5) | 5.0 (5.0-10.0) |
| Windows host | pf-pings | 3 | 3/3 | 17.0 (17.0-17.0) | - |
| Windows host | pf-quiet | 3 | 0/3 | - | - |
| Windows host | reload | 5 | 5/5 | 16.0 (16.0-16.0) | - |
| Windows host | reload-load | 5 | 5/5 | 20.0 (19.9-20.0) | - |
| Windows host | renav | 5 | 5/5 | 16.0 (16.0-16.0) | - |
| Windows host | stop-reload | 5 | 5/5 | 16.0 (16.0-16.0) | - |
| Windows host | stress-hung | 3 | 3/3 | 16.0 (16.0-16.0) | - |
| Windows host | worker | 5 | 0/5 | - | - |
| Windows host | xsite | 5 | 5/5 | 16.0 (16.0-16.0) | 5.0 (5.0-5.0) |
| Linux container | blank | 5 | 5/5 | 15.9 (15.9-15.9) | - |
| Linux container | busy10 | 5 | 0/5 | - | - |
| Linux container | busy20 | 5 | 5/5 | 17.8 (15.5-19.1) | 2.2 (0.9-4.5) |
| Linux container | busy3 | 5 | 0/5 | - | - |
| Linux container | gate-minimize-hang | 3 | 3/3 | 54.9 (54.9-54.9) | - |
| Linux container | handler | 5 | 5/5 | 16.4 (15.4-19.8) | - |
| Linux container | idle | 5 | 5/5 | 17.2 (15.6-19.3) | - |
| Linux container | kill | 5 | 5/5 | 15.9 (15.9-15.9) | 5.0 (5.0-5.0) |
| Linux container | kill-load | 5 | 5/5 | 19.9 (19.9-19.9) | - |
| Linux container | kill-sigkill | 5 | 5/5 | 15.9 (15.9-15.9) | 5.0 (5.0-5.0) |
| Linux container | load-contentloading | 5 | 5/5 | 19.9 (19.9-19.9) | - |
| Linux container | load-doc | 5 | 5/5 | 19.9 (19.9-19.9) | - |
| Linux container | load-navcompleted | 5 | 0/5 | - | - |
| Linux container | microtask | 5 | 5/5 | 18.0 (15.2-19.7) | - |
| Linux container | reload | 5 | 5/5 | 15.9 (15.9-15.9) | - |
| Linux container | reload-load | 5 | 5/5 | 19.9 (19.9-19.9) | - |
| Linux container | renav | 5 | 5/5 | 15.9 (15.9-15.9) | - |
| Linux container | stress-hung | 3 | 3/3 | 15.9 (15.9-15.9) | - |
| Linux container | wk-busy | 3 | 3/3 | 16.9 (16.9-16.9) | 3.1 (3.1-3.1) |
| Linux container | wk-click | 3 | 0/3 | - | - |
| Linux container | wk-pings | 3 | 3/3 | 16.9 (16.9-16.9) | - |
| Linux container | wk-quiet | 3 | 0/3 | - | - |
| Linux container | worker | 5 | 0/5 | - | - |
| Windows Sandbox | idle | 3 | 3/3 | 16.9 (15.2-19.0) | - |
| Windows Sandbox | dialog-recover | 3 | 3/3 | 16.0 (16.0-16.0) | 9.0 (9.0-9.0) |
| Windows Sandbox | dialog-reload | 3 | 3/3 | 16.0 (16.0-16.0) | 5.0 (5.0-5.0) |
| Windows Sandbox | dialog-wait | 3 | 3/3 | 16.0 (16.0-16.0) | - |
| Windows Sandbox | cover-hang | 3 | 3/3 | 18.5 (16.4-19.1) | - |
| Windows Sandbox | gate-minimize-hang | 3 | 3/3 | 55.0 (55.0-55.0) | - |
| Windows Sandbox | input-click | 3 | 3/3 | 17.0 (17.0-17.0) | - |
| Windows Sandbox | input-click-quiet | 3 | 0/3 | - | - |
| Windows Sandbox | input-key | 3 | 3/3 | 17.0 (17.0-17.0) | - |

## Alarms in runs without an endless main-thread hang (healthy pages, window states, finite work)

| Host | Case | Runs | Runs with an alarm | Probe round trip ms: median of run medians / max | Page 1 s timer interval ms: median / max | rAF per s: median / min |
|---|---|---|---|---|---|---|
| Windows host | anim | 3 | 0 | 0.3 / 8.2 | 1000 / 1014 | 60 / 59 |
| Windows host | baseline | 5 | 0 | 0.2 / 9.1 | 1000 / 1015 | 60 / 59 |
| Windows host | busy10 | 5 | 0 | 0.3 / 9612.9 | 1000 / 10601 | 60 / 5 |
| Windows host | busy20 | 5 | 5 | 0.4 / 19993.3 | 1000 / 20978 | 60 / 9 |
| Windows host | busy3 | 5 | 0 | 0.2 / 3000.8 | 1000 / 3981 | 60 / 5 |
| Windows host | ctl-hidden | 3 | 0 | 0.3 / 9.5 | 999 / 1647 | 0 / 0 |
| Windows host | ctl-suspend | 3 | 3 | 4525.1 / 29061.5 | 1000 / 31939 | 60 / 0 |
| Windows host | healthy-kill | 5 | 0 | 0.2 / 8.9 | 1000 / 1011 | 60 / 59 |
| Windows host | healthy-reload | 5 | 0 | 0.2 / 8.9 | 1000 / 1010 | 60 / 59 |
| Windows host | mem-low | 3 | 0 | 0.2 / 9.1 | 1000 / 1015 | 60 / 59 |
| Windows host | noinject | 5 | 5 | no probe | - | - |
| Windows host | stress-healthy | 3 | 0 | 0.2 / 9.5 | 1000 / 1013 | 60 / 59 |
| Windows host | tasks100 | 3 | 0 | 46.2 / 100.0 | 1046 / 1053 | 22 / 14 |
| Windows host | tasks1000 | 3 | 0 | 164.8 / 1042.4 | 1005 / 2010 | 2 / 1 |
| Windows host | worker | 5 | 0 | 0.2 / 8.7 | 1000 / 1011 | 60 / 59 |
| Linux container | anim | 3 | 0 | 1.6 / 9.3 | 1004 / 1013 | 62 / 62 |
| Linux container | baseline | 5 | 0 | 0.8 / 14.4 | 1004 / 1005 | 62 / 62 |
| Linux container | busy10 | 5 | 0 | 1.4 / 9985.8 | 1004 / 10984 | 62 / 23 |
| Linux container | busy20 | 5 | 5 | 4.0 / 19906.0 | 1004 / 20834 | 62 / 7 |
| Linux container | busy3 | 5 | 0 | 0.8 / 2886.8 | 1004 / 3790 | 62 / 6 |
| Linux container | healthy-kill | 5 | 0 | 1.0 / 9.3 | 1000 / 1004 | 62 / 62 |
| Linux container | healthy-reload | 5 | 0 | 0.9 / 6.7 | 1000 / 1004 | 62 / 62 |
| Linux container | noinject | 5 | 5 | no probe | - | - |
| Linux container | st-cover | 3 | 0 | 1.2 / 14.2 | 1004 / 1004 | 62 / 62 |
| Linux container | st-hide | 3 | 0 | 1.1 / 26.9 | 2000 / 2000 | 0 / 0 |
| Linux container | st-minimize | 3 | 0 | 1.1 / 13.3 | 2000 / 2000 | 0 / 0 |
| Linux container | st-offscreen | 3 | 0 | 1.1 / 10.0 | 1004 / 1005 | 62 / 62 |
| Linux container | stress-healthy | 3 | 0 | 0.7 / 10.2 | 1004 / 1004 | 62 / 62 |
| Linux container | tasks100 | 3 | 0 | 45.5 / 99.8 | 1047 / 1056 | 12 / 5 |
| Linux container | tasks1000 | 3 | 0 | 145.2 / 263.6 | 1004 / 2001 | 1 / 0 |
| Linux container | wk-busy | 3 | 3 | 2097.1 / 19132.6 | 1004 / 20015 | 62 / 0 |
| Linux container | worker | 5 | 0 | 0.7 / 9.5 | 1004 / 1005 | 62 / 62 |
| Windows Sandbox | anim | 3 | 0 | 0.4 / 8.7 | 1000 / 1016 | 32 / 23 |
| Windows Sandbox | onscreen | 3 | 0 | 0.5 / 10.9 | 1000 / 1015 | 32 / 32 |
| Windows Sandbox | dialog-recover | 3 | 3 | 4536.8 / 24029.1 | 1000 / 25999 | 32 / 2 |
| Windows Sandbox | cover | 3 | 0 | 0.6 / 8.5 | 1000 / 1015 | 32 / 32 |
| Windows Sandbox | ctl-hidden | 3 | 0 | 0.5 / 5.2 | 999 / 2015 | 0 / 0 |
| Windows Sandbox | mem-low | 3 | 0 | 0.6 / 7.3 | 1000 / 1015 | 32 / 32 |
| Windows Sandbox | minimize | 3 | 0 | 0.5 / 7.3 | 1000 / 1806 | 0 / 0 |
| Windows Sandbox | offscreen | 3 | 0 | 0.5 / 8.7 | 1000 / 1015 | 32 / 31 |
| Windows Sandbox | suspend | 3 | 3 | 4563.5 / 29059.4 | 1000 / 31997 | 32 / 0 |

## Ungated pings per phase in the state cases (gate off): probe round trip, page timer, rAF, page visibility

| Host | Case | Phase | Probe rtt ms median / max | Probes answered / sent | Timer interval ms median / max | rAF/s median | visibilityState |
|---|---|---|---|---|---|---|---|
| Windows host | ctl-hidden | hide-controller | 0.3 / 9.5 | 180 / 180 | 999 / 1647 | 0 | hidden |
| Windows host | ctl-hidden | show-controller | 0.2 / 0.5 | 30 / 30 | 1000 / 1001 | 60 | visible |
| Windows host | ctl-suspend | hide-controller | 0.3 / 2.7 | 3 / 3 | 999 / 999 | 60 | hidden |
| Windows host | ctl-suspend | suspend | - | 0 / 90 | none | - | - |
| Windows host | ctl-suspend | resume | 14021.3 / 29061.5 | 93 / 3 | 31936 / 31939 | 0 | hidden |
| Windows host | ctl-suspend | show-controller | 0.2 / 0.3 | 39 / 39 | 1000 / 1004 | 60 | visible |
| Windows host | mem-low | mem:low | 0.2 / 9.0 | 90 / 90 | 1000 / 1011 | 60 | visible |
| Windows host | mem-low | mem:normal | 0.2 / 9.1 | 30 / 30 | 1000 / 1015 | 60 | visible |
| Linux container | gate-minimize-hang | minimize | 0.9 / 2.7 | 15 / 15 | 2000 / 2000 | 0 | hidden |
| Linux container | gate-minimize-hang | hang-idle | - | 0 / 105 | none | - | - |
| Linux container | gate-minimize-hang | restore | - | 0 / 75 | none | - | - |
| Linux container | st-cover | cover | 1.4 / 14.2 | 180 / 180 | 1004 / 1004 | 62 | visible |
| Linux container | st-cover | uncover | 0.9 / 10.1 | 30 / 30 | 1004 / 1004 | 62 | visible |
| Linux container | st-hide | hide | 1.1 / 26.9 | 180 / 180 | 2000 / 2000 | 0 | hidden |
| Linux container | st-hide | show | 1.1 / 8.5 | 30 / 30 | 1004 / 1004 | 62 | visible |
| Linux container | st-minimize | minimize | 1.0 / 10.4 | 180 / 180 | 2000 / 2000 | 0 | hidden |
| Linux container | st-minimize | restore | 1.4 / 13.3 | 30 / 30 | 1004 / 1004 | 62 | visible |
| Linux container | st-offscreen | sh:move-offscreen | 1.3 / 10.0 | 180 / 180 | 1004 / 1005 | 62 | visible |
| Linux container | st-offscreen | sh:move-back | 1.1 / 6.2 | 30 / 30 | 1004 / 1004 | 62 | visible |
| Windows Sandbox | gate-minimize-hang | minimize | 0.5 / 3.9 | 15 / 15 | 1000 / 1831 | 0 | hidden |
| Windows Sandbox | gate-minimize-hang | hang-idle | - | 0 / 105 | none | - | - |
| Windows Sandbox | gate-minimize-hang | restore | - | 0 / 75 | none | - | - |
| Windows Sandbox | cover | cover | 0.6 / 8.5 | 180 / 180 | 1000 / 1015 | 32 | visible |
| Windows Sandbox | cover | uncover | 0.6 / 6.7 | 30 / 30 | 1000 / 1015 | 32 | visible |
| Windows Sandbox | ctl-hidden | hide-controller | 0.5 / 5.2 | 180 / 180 | 999 / 2015 | 0 | hidden |
| Windows Sandbox | ctl-hidden | show-controller | 0.5 / 1.9 | 30 / 30 | 1000 / 1050 | 32 | visible |
| Windows Sandbox | mem-low | mem:low | 0.6 / 7.3 | 90 / 90 | 1000 / 1015 | 32 | visible |
| Windows Sandbox | mem-low | mem:normal | 0.6 / 5.6 | 30 / 30 | 1000 / 1012 | 32 | visible |
| Windows Sandbox | minimize | minimize | 0.5 / 7.3 | 180 / 180 | 999 / 1806 | 0 | hidden |
| Windows Sandbox | minimize | restore | 0.5 / 7.3 | 30 / 30 | 1000 / 1010 | 32 | visible |
| Windows Sandbox | offscreen | offscreen | 0.5 / 8.7 | 180 / 180 | 1000 / 1015 | 32 | visible |
| Windows Sandbox | offscreen | onscreen | 0.5 / 8.0 | 30 / 30 | 1000 / 1015 | 32 | visible |
| Windows Sandbox | suspend | hide-controller | 0.4 / 0.9 | 3 / 3 | 1000 / 1299 | 32 | hidden |
| Windows Sandbox | suspend | suspend | - | 0 / 90 | none | - | - |
| Windows Sandbox | suspend | resume | 14051.3 / 29059.4 | 93 / 3 | 31468 / 31929 | 0 | hidden |
| Windows Sandbox | suspend | show-controller | 0.6 / 8.2 | 39 / 39 | 999 / 31997 | 32 | visible |

## Engine signals: ProcessFailed (WebView2) and is-web-process-responsive / web-process-terminated (WebKitGTK)

Times are from the hang start, and from the last action before the event. WebKit flips after FINISH (teardown) are excluded.

| Host | Case | Runs | Event | Runs with it | From hang, s | From last action, s (action) |
|---|---|---|---|---|---|---|
| Windows host | healthy-kill | 5 | ProcessFailed render-exited/terminated | 5 | - | 0.0 (0.0-0.0) (kill) |
| Windows host | kill | 5 | ProcessFailed render-exited/terminated | 5 | 20.0 (20.0-20.0) | 0.0 (0.0-0.0) (kill) |
| Windows host | kill-load | 5 | ProcessFailed render-exited/terminated | 5 | 20.0 (20.0-20.0) | - () |
| Windows host | microtask | 5 | ProcessFailed render-exited/out-of-memory | 5 | 21.6 (20.5-25.7) | 21.6 (20.5-25.7) (hang-microtask) |
| Windows host | reload | 5 | ProcessFailed render-unresponsive/unresponsive | 5 | 50.0 (50.0-50.0) | 30.0 (30.0-30.0) (reload) |
| Windows host | renav | 5 | ProcessFailed render-unresponsive/unresponsive | 5 | 50.0 (50.0-50.0) | 30.0 (30.0-30.0) (nav:/index.html) |
| Windows host | stop-reload | 5 | ProcessFailed render-unresponsive/unresponsive | 5 | 50.1 (50.1-50.1) | 30.0 (30.0-30.0) (reload) |
| Linux container | blank | 5 | is-web-process-responsive=0 | 5 | 23.0 (23.0-23.0) | 1.0 (1.0-1.0) (nav:/index.html) |
| Linux container | healthy-kill | 5 | web-process-terminated terminated-by-api | 5 | - | 0.0 (0.0-0.0) (kill) |
| Linux container | kill | 5 | web-process-terminated terminated-by-api | 5 | 20.0 (20.0-20.0) | 0.0 (0.0-0.0) (kill) |
| Linux container | kill-load | 5 | web-process-terminated terminated-by-api | 5 | 19.9 (19.9-19.9) | - () |
| Linux container | kill-sigkill | 5 | web-process-terminated crashed | 5 | 20.0 (20.0-20.0) | 0.0 (0.0-0.0) (kill) |
| Linux container | reload | 5 | is-web-process-responsive=0 | 5 | 23.0 (23.0-23.0) | 3.0 (3.0-3.0) (reload) |
| Linux container | reload-load | 5 | is-web-process-responsive=0 | 5 | 22.9 (22.9-22.9) | - () |
| Linux container | renav | 5 | is-web-process-responsive=0 | 5 | 23.0 (23.0-23.0) | 3.0 (3.0-3.0) (nav:/index.html) |
| Windows Sandbox | dialog-reload | 3 | ProcessFailed render-exited/terminated | 3 | 19.0 (19.0-19.1) | 19.0 (19.0-19.1) (hang-idle) |
| Windows Sandbox | dialog-wait | 3 | ProcessFailed render-unresponsive/unresponsive | 3 | 32.0 (32.0-32.0) | 32.0 (32.0-32.0) (hang-idle) |
| Windows Sandbox | input-click | 3 | ProcessFailed render-unresponsive/unresponsive | 3 | 21.0 (21.0-21.1) | 16.0 (16.0-16.1) (click) |
| Windows Sandbox | input-click-quiet | 3 | ProcessFailed render-unresponsive/unresponsive | 3 | 21.0 (21.0-21.1) | 16.0 (16.0-16.1) (click) |
| Windows Sandbox | input-key | 3 | ProcessFailed render-unresponsive/unresponsive | 3 | 21.1 (21.0-21.1) | 15.0 (15.0-15.1) (key) |

## Recovery: from the recovery action to the new document (DOC), DOMContentLoaded (READY) and the first heartbeat answer

"never" counts runs where the step did not happen before FINISH. Backend counter: the value the new document read from the host (it was 2 before the hang in the hung cases, 1 in reload-load/kill-load which never load).

| Host | Case | Runs | Action | DOC ms | READY ms | First answer ms | Never ready | Backend counter seen |
|---|---|---|---|---|---|---|---|---|
| Windows host | blank | 5 | nav-about:blank | - | - | - | 5/5 | - |
| Windows host | blank | 5 | nav-index.html | - | - | - | 5/5 | - |
| Windows host | edge-kill | 2 | nav-edge://kill | - | - | - | 2/2 | - |
| Windows host | healthy-kill | 5 | kill + auto reload | 49 (46-55) | 51 (48-57) | 973 (952-983) | 0/5 | 3 |
| Windows host | healthy-reload | 5 | reload | 8 (8-9) | 9 (9-10) | 981 (968-989) | 0/5 | 3 |
| Windows host | kill | 5 | kill + auto reload | 48 (45-54) | 50 (47-57) | 967 (951-984) | 0/5 | 3 |
| Windows host | kill-load | 5 | kill-on-unresponsive + auto reload | 49 (48-56) | - | - | 5/5 | - |
| Windows host | microtask | 5 | auto-reload-after-exit | 55 (49-64) | 59 (52-67) | 852 (355-974) | 0/5 | 2 |
| Windows host | reload | 5 | reload | - | - | - | 5/5 | - |
| Windows host | reload-load | 5 | reload-on-unresponsive | - | - | - | 5/5 | - |
| Windows host | renav | 5 | nav-index.html | - | - | - | 5/5 | - |
| Windows host | stop-reload | 5 | reload | - | - | - | 5/5 | - |
| Windows host | xsite | 5 | nav-https://other.localhost/index.html | 39 (38-61) | 41 (40-64) | 990 (980-998) | 0/5 | 3 |
| Windows host | xsite | 5 | nav-index.html | 58 (56-60) | 60 (58-62) | 979 (968-993) | 0/5 | 4 |
| Linux container | blank | 5 | nav-about:blank | - | - | - | 5/5 | - |
| Linux container | blank | 5 | nav-index.html | - | - | - | 5/5 | - |
| Linux container | healthy-kill | 5 | kill + auto reload | 76 (72-85) | 83 (79-94) | 924 (885-930) | 0/5 | 3 |
| Linux container | healthy-reload | 5 | reload | 15 (7-48) | 33 (13-70) | 925 (877-934) | 0/5 | 3 |
| Linux container | kill | 5 | kill + auto reload | 90 (71-125) | 100 (78-159) | 919 (891-930) | 0/5 | 3 |
| Linux container | kill-load | 5 | kill-on-unresponsive + auto reload | 89 (81-116) | - | - | 5/5 | - |
| Linux container | kill-sigkill | 5 | kill + auto reload | 87 (71-94) | 94 (78-134) | 905 (882-926) | 0/5 | 3 |
| Linux container | reload | 5 | reload | - | - | - | 5/5 | - |
| Linux container | reload-load | 5 | reload-on-unresponsive | - | - | - | 5/5 | - |
| Linux container | renav | 5 | nav-index.html | - | - | - | 5/5 | - |
| Windows Sandbox | dialog-reload | 3 | dialog-kill + auto reload | 100 (86-103) | 104 (95-111) | 949 (895-971) | 0/3 | 3 |

## Posting 1,000 messages of 1 KiB per second for 30 s: private memory (kB) before, at the end and 8 s later

| Host | Case | Runs | Messages sent | Mean / max post µs | Host before -> later | Renderer before -> peak -> later | Browser before -> later |
|---|---|---|---|---|---|---|---|
| Windows host | stress-healthy | 3 | 19200 (19180-19250) | 11.4 / 230 | 4348 -> 5872 | 11684 -> 67144 -> 30156 | 30700 -> 30776 |
| Windows host | stress-hung | 3 | 19220 (19190-19250) | 11.9 / 312 | 4564 -> 5828 | 11684 -> 42168 -> 42168 | 30724 -> 30804 |
| Linux container | stress-healthy | 3 | 24960 (24940-24960) | 6.7 / 7121 | 44060 -> 62800 | 19952 -> 23912 -> 21532 | - |
| Linux container | stress-hung | 3 | 24990 (24950-25000) | 8.3 / 34378 | 44320 -> 70824 | 19964 -> 55432 -> 55432 | - |

## UI-thread pump latency (post to run, 1 ms cadence) by phase, all runs of a case: median of p99 / worst max, ms

| Host | Case | Phase | Samples | p99 (median over runs) | Max (worst run) |
|---|---|---|---|---|---|
| Windows host | baseline | start | 19290 | 0.21 | 1.41 |
| Windows host | handler | start | 2234 | 0.24 | 1.23 |
| Windows host | handler | hang-handler | 9590 | 0.26 | 8.85 |
| Windows host | idle | start | 2518 | 0.21 | 2.44 |
| Windows host | idle | hang-idle | 9610 | 0.22 | 0.46 |
| Windows host | pf-pings | start | 588 | 0.31 | 0.96 |
| Windows host | pf-pings | hang-idle | 13849 | 0.26 | 0.52 |
| Windows host | pf-quiet | start | 584 | 0.25 | 0.68 |
| Windows host | pf-quiet | hang-idle | 13838 | 0.27 | 0.57 |
| Windows host | reload | start | 971 | 0.24 | 0.79 |
| Windows host | reload | state | 322 | 0.24 | 0.34 |
| Windows host | reload | hang-idle | 6406 | 0.26 | 0.60 |
| Windows host | reload | reload | 14736 | 0.29 | 0.62 |
| Windows host | stress-healthy | start | 585 | 0.27 | 0.42 |
| Windows host | stress-healthy | memory-before | 577 | 0.26 | 0.35 |
| Windows host | stress-healthy | stress-on-1000/1024 | 5764 | 0.34 | 0.72 |
| Windows host | stress-healthy | stress-off | 191 | 0.24 | 0.33 |
| Windows host | stress-healthy | memory-after | 1537 | 0.24 | 9.54 |
| Windows host | stress-healthy | memory-later | 194 | 0.22 | 0.41 |
| Windows host | stress-hung | start | 586 | 0.28 | 0.38 |
| Windows host | stress-hung | memory-before | 193 | 0.25 | 0.42 |
| Windows host | stress-hung | hang-idle | 385 | 0.27 | 0.44 |
| Windows host | stress-hung | stress-on-1000/1024 | 5767 | 0.37 | 0.54 |
| Windows host | stress-hung | stress-off | 191 | 0.21 | 0.42 |
| Windows host | stress-hung | memory-after | 1541 | 0.28 | 0.38 |
| Windows host | stress-hung | memory-later | 193 | 0.22 | 0.32 |
| Linux container | baseline | start | 264040 | 0.09 | 20.51 |
| Linux container | handler | start | 35706 | 0.10 | 8.90 |
| Linux container | handler | hang-handler | 132348 | 0.09 | 5.39 |
| Linux container | idle | start | 33213 | 0.09 | 29.92 |
| Linux container | idle | hang-idle | 132223 | 0.09 | 11.27 |
| Linux container | reload | start | 13436 | 0.10 | 15.17 |
| Linux container | reload | state | 4392 | 0.09 | 4.30 |
| Linux container | reload | hang-idle | 88558 | 0.09 | 8.21 |
| Linux container | reload | reload | 202446 | 0.09 | 22.84 |
| Linux container | stress-healthy | start | 8060 | 0.12 | 50.60 |
| Linux container | stress-healthy | memory-before | 7909 | 0.11 | 4.50 |
| Linux container | stress-healthy | stress-on-1000/1024 | 79698 | 0.10 | 14.22 |
| Linux container | stress-healthy | stress-off | 2648 | 0.09 | 2.53 |
| Linux container | stress-healthy | memory-after | 21192 | 0.09 | 4.85 |
| Linux container | stress-healthy | memory-later | 2644 | 0.09 | 1.41 |
| Linux container | stress-hung | start | 8099 | 0.10 | 9.81 |
| Linux container | stress-hung | memory-before | 2638 | 0.08 | 4.02 |
| Linux container | stress-hung | hang-idle | 5284 | 0.08 | 3.45 |
| Linux container | stress-hung | stress-on-1000/1024 | 79987 | 0.09 | 10.24 |
| Linux container | stress-hung | stress-off | 2648 | 0.08 | 1.97 |
| Linux container | stress-hung | memory-after | 21215 | 0.08 | 8.04 |
| Linux container | stress-hung | memory-later | 2652 | 0.08 | 5.15 |
| Linux container | wk-pings | start | 8110 | 0.11 | 8.65 |
| Linux container | wk-pings | hang-idle | 191557 | 0.09 | 32.12 |
| Windows Sandbox | idle | start | 1550 | 0.25 | 2.96 |
| Windows Sandbox | idle | hang-idle | 5760 | 0.22 | 1.77 |

## Other intervals and miss counts, replayed on the 1 s probe pings (Windows host and Linux container)

Detection over idle, handler and microtask runs; false-alarm runs over runs without an endless hang (baseline, anim, tasks100, tasks1000, worker, busy3, busy10, stress-healthy, state cases). busy20 is listed separately: a 20 s task exceeds most thresholds.

| Interval x misses | Detection s, median (min-max) | False-alarm runs | busy20 runs alarmed |
|---|---|---|---|
| 1 s x 3 | 3.5 (3.2-3.9) | 14/82 | 10/10 |
| 2 s x 3 | 6.7 (6.2-7.8) | 10/82 | 10/10 |
| 2 s x 5 | 10.8 (10.2-11.9) | 0/82 | 10/10 |
| 3 s x 3 | 10.4 (9.2-11.9) | 5/82 | 10/10 |
| 5 s x 2 | 12.3 (10.2-14.9) | 0/82 | 10/10 |
| 5 s x 3 | 17.3 (15.2-19.9) | 0/82 | 10/10 |
| 5 s x 4 | 22.0 (20.2-26.4) | 0/82 | 0/10 |
| 10 s x 2 | 22.0 (20.2-26.4) | 0/82 | 0/10 |

## Completeness: runs per case, and runs without a FINISH line

| Host | Cases | Runs | Runs without FINISH |
|---|---|---|---|
| Windows host | 33 | 142 | none |
| Linux container | 35 | 147 | none |
| Windows Sandbox | 17 | 51 | none |
