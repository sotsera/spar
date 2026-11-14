# Summary of results/sandbox/states (18 runs)

## cover (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 4.2 max 4.2 (n=15)
probe rtt ms [cover]: median 0.6 p99 5.4 max 8.5 (n=180)
probe rtt ms [uncover]: median 0.6 p99 6.7 max 6.7 (n=30)
timer tick ms [start]: median 1000 p99 1009 max 1009 (n=10); rAF/s median 32 min 32; vis visible
timer tick ms [cover]: median 1000 p99 1012 max 1015 (n=182); rAF/s median 32 min 32; vis visible
timer tick ms [uncover]: median 1000 p99 1015 max 1015 (n=29); rAF/s median 32 min 32; vis visible
pump [start]: 329 p50=0.07ms p99=0.27ms max=1.05ms | 324 p50=0.07ms p99=0.31ms max=0.49ms | 325 p50=0.07ms p99=0.25ms max=1.84ms
pump [cover]: 3850 p50=0.07ms p99=0.15ms max=5.39ms | 3850 p50=0.07ms p99=0.16ms max=1.27ms | 3851 p50=0.07ms p99=0.18ms max=7.20ms
pump [uncover]: 644 p50=0.07ms p99=0.16ms max=1.34ms | 642 p50=0.07ms p99=0.16ms max=0.57ms | 642 p50=0.07ms p99=0.17ms max=3.09ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  cover-1.log 4107 NAV-COMPLETED id=2 success=True status=0
  cover-2.log 4311 NAV-COMPLETED id=2 success=True status=0
  cover-3.log 3197 NAV-COMPLETED id=2 success=True status=0

## ctl-hidden (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.6 p99 3.8 max 3.8 (n=15)
probe rtt ms [hide-controller]: median 0.5 p99 3.9 max 5.2 (n=180)
probe rtt ms [show-controller]: median 0.5 p99 1.9 max 1.9 (n=30)
timer tick ms [start]: median 1000 p99 1010 max 1010 (n=10); rAF/s median 32 min 32; vis visible
timer tick ms [hide-controller]: median 999 p99 2014 max 2015 (n=150); rAF/s median 0 min 0; vis hidden/visible
timer tick ms [show-controller]: median 1000 p99 1050 max 1050 (n=31); rAF/s median 32 min 0; vis visible
pump [start]: 326 p50=0.07ms p99=0.23ms max=0.55ms | 326 p50=0.07ms p99=0.22ms max=0.43ms | 328 p50=0.08ms p99=0.16ms max=0.40ms
pump [hide-controller]: 3785 p50=0.08ms p99=0.20ms max=3.87ms | 3784 p50=0.08ms p99=0.20ms max=2.25ms | 3783 p50=0.08ms p99=0.21ms max=3.17ms
pump [show-controller]: 645 p50=0.07ms p99=0.17ms max=0.64ms | 644 p50=0.07ms p99=0.17ms max=0.38ms | 645 p50=0.07ms p99=0.35ms max=1.02ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  ctl-hidden-1.log 2640 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-1.log 7633 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-1.log 7634 VIS state=hidden pagePerf=5033.0
  ctl-hidden-1.log 67634 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-1.log 67635 VIS state=visible pagePerf=65033.6
  ctl-hidden-2.log 5015 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-2.log 10019 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-2.log 10020 VIS state=hidden pagePerf=5035.0
  ctl-hidden-2.log 70016 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-2.log 70017 VIS state=visible pagePerf=65031.3
  ctl-hidden-3.log 2699 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-3.log 7700 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-3.log 7701 VIS state=hidden pagePerf=5039.1
  ctl-hidden-3.log 67695 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-3.log 67696 VIS state=visible pagePerf=65034.1

## mem-low (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 2.2 max 2.2 (n=15)
probe rtt ms [mem:low]: median 0.6 p99 6.2 max 7.3 (n=90)
probe rtt ms [mem:normal]: median 0.6 p99 5.6 max 5.6 (n=30)
timer tick ms [start]: median 1000 p99 1015 max 1015 (n=10); rAF/s median 32 min 32; vis visible
timer tick ms [mem:low]: median 1000 p99 1015 max 1015 (n=90); rAF/s median 32 min 32; vis visible
timer tick ms [mem:normal]: median 1000 p99 1012 max 1012 (n=29); rAF/s median 32 min 32; vis visible
pump [start]: 325 p50=0.07ms p99=0.17ms max=3.03ms | 325 p50=0.07ms p99=0.17ms max=0.33ms | 326 p50=0.07ms p99=0.25ms max=0.56ms
pump [mem-low]: 1923 p50=0.06ms p99=0.14ms max=0.36ms | 1926 p50=0.07ms p99=0.16ms max=4.58ms | 1926 p50=0.07ms p99=0.17ms max=4.05ms
pump [mem-normal]: 642 p50=0.07ms p99=0.15ms max=0.22ms | 642 p50=0.07ms p99=0.18ms max=2.06ms | 643 p50=0.07ms p99=0.17ms max=0.51ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  mem-low-1.log 3545 NAV-COMPLETED id=2 success=True status=0
  mem-low-1.log 8543 MEMORY-TARGET set=1 read=1
  mem-low-1.log 38537 MEMORY-TARGET set=0 read=0
  mem-low-2.log 5836 NAV-COMPLETED id=2 success=True status=0
  mem-low-2.log 10828 MEMORY-TARGET set=1 read=1
  mem-low-2.log 40831 MEMORY-TARGET set=0 read=0
  mem-low-3.log 5727 NAV-COMPLETED id=2 success=True status=0
  mem-low-3.log 10719 MEMORY-TARGET set=1 read=1
  mem-low-3.log 40728 MEMORY-TARGET set=0 read=0

## minimize (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 6.5 max 6.5 (n=15)
probe rtt ms [minimize]: median 0.5 p99 6.1 max 7.3 (n=180)
probe rtt ms [restore]: median 0.5 p99 7.3 max 7.3 (n=30)
timer tick ms [start]: median 1001 p99 1010 max 1010 (n=9); rAF/s median 32 min 32; vis visible
timer tick ms [minimize]: median 999 p99 1018 max 1806 (n=180); rAF/s median 0 min 0; vis visible/hidden
timer tick ms [restore]: median 1000 p99 1010 max 1010 (n=31); rAF/s median 32 min 0; vis visible
pump [start]: 327 p50=0.08ms p99=0.31ms max=2.90ms | 327 p50=0.07ms p99=0.17ms max=0.30ms | 325 p50=0.07ms p99=0.27ms max=1.84ms
pump [minimize]: 3775 p50=0.08ms p99=0.25ms max=115.80ms | 3782 p50=0.08ms p99=0.24ms max=6.14ms | 3787 p50=0.08ms p99=0.23ms max=7.00ms
pump [restore]: 645 p50=0.07ms p99=0.25ms max=5.37ms | 648 p50=0.08ms p99=0.18ms max=4.82ms | 641 p50=0.07ms p99=0.17ms max=1.24ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  minimize-1.log 2775 NAV-COMPLETED id=2 success=True status=0
  minimize-1.log 7773 VIS state=hidden pagePerf=5027.1
  minimize-1.log 67777 VIS state=visible pagePerf=65030.8
  minimize-2.log 3729 NAV-COMPLETED id=2 success=True status=0
  minimize-2.log 8736 VIS state=hidden pagePerf=5038.6
  minimize-2.log 68728 VIS state=visible pagePerf=65030.1
  minimize-3.log 3191 NAV-COMPLETED id=2 success=True status=0
  minimize-3.log 8200 VIS state=hidden pagePerf=5037.6
  minimize-3.log 68198 VIS state=visible pagePerf=65036.3

## offscreen (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 6.0 max 6.0 (n=15)
probe rtt ms [offscreen]: median 0.5 p99 8.0 max 8.7 (n=180)
probe rtt ms [onscreen]: median 0.5 p99 8.0 max 8.0 (n=30)
timer tick ms [start]: median 999 p99 1010 max 1010 (n=10); rAF/s median 32 min 32; vis visible
timer tick ms [offscreen]: median 1000 p99 1015 max 1015 (n=179); rAF/s median 32 min 31; vis visible
timer tick ms [onscreen]: median 1000 p99 1015 max 1015 (n=31); rAF/s median 32 min 32; vis visible
pump [start]: 326 p50=0.07ms p99=0.28ms max=5.61ms | 327 p50=0.07ms p99=0.28ms max=0.55ms | 326 p50=0.07ms p99=0.24ms max=3.57ms
pump [offscreen]: 3851 p50=0.07ms p99=0.16ms max=1.06ms | 3849 p50=0.07ms p99=0.16ms max=6.55ms | 3856 p50=0.07ms p99=0.19ms max=6.86ms
pump [onscreen]: 641 p50=0.07ms p99=0.17ms max=0.48ms | 643 p50=0.08ms p99=0.18ms max=0.37ms | 641 p50=0.06ms p99=0.18ms max=1.11ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  offscreen-1.log 4979 NAV-COMPLETED id=2 success=True status=0
  offscreen-2.log 4584 NAV-COMPLETED id=2 success=True status=0
  offscreen-3.log 2934 NAV-COMPLETED id=2 success=True status=0

## suspend (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: suspend-1.log@30727, suspend-2.log@29417, suspend-3.log@28744
responsive again after alarm (ms): 11031 (11025-11051, n=3)
probe rtt ms [start]: median 0.4 p99 2.1 max 2.1 (n=15)
probe rtt ms [hide-controller]: median 0.4 p99 0.9 max 0.9 (n=3)
probe rtt ms [resume]: median 14051.3 p99 29034.7 max 29059.4 (n=93)
probe rtt ms [show-controller]: median 0.6 p99 8.2 max 8.2 (n=39)
timer tick ms [start]: median 1000 p99 1013 max 1013 (n=9); rAF/s median 32 min 32; vis visible
timer tick ms [hide-controller]: median 1000 p99 1299 max 1299 (n=3); rAF/s median 32 min 32; vis hidden/visible
timer tick ms [resume]: median 31468 p99 31929 max 31929 (n=2); rAF/s median 0 min 0; vis hidden
timer tick ms [show-controller]: median 999 p99 31997 max 31997 (n=39); rAF/s median 32 min 0; vis visible/hidden
pump [start]: 326 p50=0.07ms p99=0.20ms max=0.28ms | 325 p50=0.07ms p99=0.17ms max=2.82ms | 327 p50=0.07ms p99=0.19ms max=0.36ms
pump [hide-controller]: 64 p50=0.07ms p99=0.16ms max=0.19ms | 64 p50=0.07ms p99=0.13ms max=0.50ms | 63 p50=0.07ms p99=0.13ms max=0.45ms
pump [suspend]: 1892 p50=0.08ms p99=0.21ms max=4.11ms | 1893 p50=0.08ms p99=0.18ms max=3.38ms | 1893 p50=0.07ms p99=0.19ms max=6.75ms
pump [resume]: 62 p50=0.08ms p99=0.18ms max=1.92ms | 61 p50=0.07ms p99=0.13ms max=33.31ms | 64 p50=0.08ms p99=0.27ms max=0.67ms
pump [show-controller]: 837 p50=0.07ms p99=0.16ms max=8.76ms | 838 p50=0.07ms p99=0.16ms max=0.27ms | 835 p50=0.06ms p99=0.18ms max=0.51ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=3; 1x5: no detect alarms=3; 2x3: no detect alarms=3; 2x5: no detect alarms=3; 3x3: no detect alarms=3; 5x2: no detect alarms=3; 5x3: no detect alarms=3; 5x4: no detect alarms=3; 10x2: no detect alarms=3; 10x3: no detect
events:
  suspend-1.log 5766 NAV-COMPLETED id=2 success=True status=0
  suspend-1.log 10750 CONTROLLER-VISIBLE value=0 hr=0x00000000
  suspend-1.log 10752 VIS state=hidden pagePerf=5034.9
  suspend-1.log 11765 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.5
  suspend-1.log 41772 RESUME hr=0x00000000
  suspend-1.log 42778 CONTROLLER-VISIBLE value=1 hr=0x00000000
  suspend-1.log 42781 VIS state=visible pagePerf=37063.6
  suspend-2.log 4434 NAV-COMPLETED id=2 success=True status=0
  suspend-2.log 9428 CONTROLLER-VISIBLE value=0 hr=0x00000000
  suspend-2.log 9430 VIS state=hidden pagePerf=5027.4
  suspend-2.log 10427 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.5
  suspend-2.log 40438 RESUME hr=0x00000000
  suspend-2.log 41437 CONTROLLER-VISIBLE value=1 hr=0x00000000
  suspend-2.log 41440 VIS state=visible pagePerf=37037.5
  suspend-3.log 3767 NAV-COMPLETED id=2 success=True status=0
  suspend-3.log 8773 CONTROLLER-VISIBLE value=0 hr=0x00000000
  suspend-3.log 8775 VIS state=hidden pagePerf=5039.4
  suspend-3.log 9773 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.5
  suspend-3.log 39771 RESUME hr=0x00000000
  suspend-3.log 40771 CONTROLLER-VISIBLE value=1 hr=0x00000000
  suspend-3.log 40772 VIS state=visible pagePerf=37036.7
