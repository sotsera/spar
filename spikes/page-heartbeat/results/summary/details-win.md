# Summary of results/win (142 runs)

## anim (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=6)
probe rtt ms [load:anim]: median 0.3 p99 8.1 max 8.2 (n=174)
timer tick ms [load:anim]: median 1000 p99 1012 max 1014 (n=174); rAF/s median 60 min 59; vis visible
timer tick ms [start]: median 1005 p99 1005 max 1005 (n=1); rAF/s median 61 min 61; vis visible
pump [start]: 132 p50=0.06ms p99=0.24ms max=0.25ms | 131 p50=0.05ms p99=0.26ms max=2.62ms | 131 p50=0.06ms p99=0.27ms max=0.34ms
pump [load-anim]: 3700 p50=0.05ms p99=0.25ms max=0.35ms | 3700 p50=0.05ms p99=0.25ms max=0.43ms | 3700 p50=0.05ms p99=0.25ms max=0.38ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  anim-1.log 302 NAV-COMPLETED id=2 success=True status=0
  anim-2.log 303 NAV-COMPLETED id=2 success=True status=0
  anim-3.log 428 NAV-COMPLETED id=2 success=True status=0

## baseline (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 9.1 (n=300)
timer tick ms [start]: median 1000 p99 1013 max 1015 (n=292); rAF/s median 60 min 59; vis visible
pump [start]: 3857 p50=0.05ms p99=0.21ms max=1.41ms | 3863 p50=0.06ms p99=0.21ms max=0.43ms | 3858 p50=0.06ms p99=0.21ms max=0.90ms | 3856 p50=0.06ms p99=0.21ms max=0.56ms | 3856 p50=0.06ms p99=0.21ms max=0.34ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  baseline-1.log 281 NAV-COMPLETED id=2 success=True status=0
  baseline-2.log 298 NAV-COMPLETED id=2 success=True status=0
  baseline-3.log 308 NAV-COMPLETED id=2 success=True status=0
  baseline-4.log 311 NAV-COMPLETED id=2 success=True status=0
  baseline-5.log 314 NAV-COMPLETED id=2 success=True status=0

## blank (5 runs)
detection ms (hang start -> unresponsive): 15981 (15948-15994, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION nav:about:blank to DOC: - never in 5
recovery from ACTION nav:about:blank to READY: - never in 5
recovery from ACTION nav:/index.html to DOC: - never in 5
recovery from ACTION nav:/index.html to READY: - never in 5
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [state]: median 0.2 p99 0.3 max 0.3 (n=5)
timer tick ms [start]: median 1000 p99 1002 max 1002 (n=8); rAF/s median 60 min 59; vis visible
timer tick ms [state]: median 1000 p99 1000 max 1000 (n=6); rAF/s median 60 min 60; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 60 min 60; vis visible
pump [start]: 195 p50=0.06ms p99=0.29ms max=2.55ms | 196 p50=0.07ms p99=0.22ms max=0.37ms | 198 p50=0.07ms p99=0.23ms max=0.35ms | 193 p50=0.06ms p99=0.25ms max=0.32ms | 195 p50=0.06ms p99=0.29ms max=0.49ms
pump [state]: 64 p50=0.05ms p99=0.19ms max=0.36ms | 64 p50=0.05ms p99=0.22ms max=0.23ms | 63 p50=0.05ms p99=0.17ms max=0.18ms | 65 p50=0.04ms p99=0.27ms max=0.28ms | 64 p50=0.05ms p99=0.17ms max=0.24ms
pump [hang-idle]: 1280 p50=0.05ms p99=0.30ms max=0.47ms | 1280 p50=0.06ms p99=0.26ms max=0.37ms | 1279 p50=0.05ms p99=0.27ms max=0.52ms | 1275 p50=0.05ms p99=0.25ms max=0.37ms | 1278 p50=0.05ms p99=0.29ms max=0.42ms
pump [nav-about:blank]: 128 p50=0.05ms p99=0.26ms max=0.28ms | 129 p50=0.05ms p99=0.38ms max=0.45ms | 129 p50=0.05ms p99=0.24ms max=0.41ms | 129 p50=0.05ms p99=0.29ms max=0.30ms | 127 p50=0.05ms p99=0.23ms max=0.23ms
pump [nav-/index.html]: 1215 p50=0.06ms p99=0.30ms max=0.51ms | 1218 p50=0.05ms p99=0.30ms max=8.71ms | 1214 p50=0.06ms p99=0.27ms max=0.61ms | 1213 p50=0.05ms p99=0.25ms max=0.31ms | 1215 p50=0.06ms p99=0.26ms max=0.32ms
simulated (interval s x misses, from 1 s probes): 1x3: 3979 (3943-3990, n=5); 1x5: 5971 (5942-5989, n=5); 2x3: 7976 (7950-7991, n=5); 2x5: 11977 (11949-11987, n=5); 3x3: 10980 (10936-10991, n=5); 5x2: 10980 (10936-10991, n=5); 5x3: 15981 (15948-15994, n=5); 5x4: 20981 (20950-20990, n=5); 10x2: 25978 (25945-25987, n=5); 10x3: 35978 (35948-35985, n=5)
events:
  blank-1.log 299 (hang+-3994) NAV-COMPLETED id=2 success=True status=0
  blank-2.log 291 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  blank-3.log 327 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  blank-4.log 294 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  blank-5.log 293 (hang+-4002) NAV-COMPLETED id=2 success=True status=0

## busy10 (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.6 max 0.6 (n=29)
probe rtt ms [busy:10000]: median 0.3 p99 9492.9 max 9612.9 (n=125)
timer tick ms [start]: median 1000 p99 1010 max 1010 (n=24); rAF/s median 60 min 59; vis visible
timer tick ms [busy:10000]: median 1000 p99 10486 max 10601 (n=80); rAF/s median 60 min 5; vis visible
pump [start]: 492 p50=0.06ms p99=0.27ms max=0.43ms | 333 p50=0.07ms p99=0.32ms max=0.69ms | 402 p50=0.06ms p99=0.28ms max=0.41ms | 420 p50=0.07ms p99=0.32ms max=1.76ms | 331 p50=0.07ms p99=0.29ms max=0.43ms
pump [busy-10000]: 1611 p50=0.07ms p99=0.32ms max=8.43ms | 1620 p50=0.07ms p99=0.34ms max=0.80ms | 1615 p50=0.07ms p99=0.34ms max=0.53ms | 1612 p50=0.07ms p99=0.32ms max=0.56ms | 1617 p50=0.06ms p99=0.32ms max=0.74ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect; 3x3: no detect alarms=2; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  busy10-1.log 303 (hang+-7596) NAV-COMPLETED id=2 success=True status=0
  busy10-2.log 327 (hang+-5110) NAV-COMPLETED id=2 success=True status=0
  busy10-3.log 316 (hang+-6171) NAV-COMPLETED id=2 success=True status=0
  busy10-4.log 329 (hang+-6479) NAV-COMPLETED id=2 success=True status=0
  busy10-5.log 508 (hang+-5067) NAV-COMPLETED id=2 success=True status=0

## busy20 (5 runs)
detection ms (hang start -> unresponsive): 17825 (16014-19181, n=5); misses at alarm 3
alarms not caused by an endless hang: busy20-1.log@hang+17825 (busy20), busy20-2.log@hang+17102 (busy20), busy20-3.log@hang+18615 (busy20), busy20-4.log@hang+16014 (busy20), busy20-5.log@hang+19181 (busy20)
responsive again after alarm (ms): 2176 (819-3987, n=5)
probe rtt ms [start]: median 0.2 p99 0.5 max 0.5 (n=33)
probe rtt ms [busy:20000]: median 2813.6 p99 19813.6 max 19993.3 (n=175)
timer tick ms [start]: median 1000 p99 1012 max 1012 (n=28); rAF/s median 60 min 59; vis visible
timer tick ms [busy:20000]: median 1000 p99 20884 max 20978 (n=79); rAF/s median 60 min 9; vis visible
pump [start]: 466 p50=0.06ms p99=0.29ms max=0.36ms | 514 p50=0.06ms p99=0.30ms max=0.35ms | 413 p50=0.07ms p99=0.34ms max=0.51ms | 578 p50=0.06ms p99=0.30ms max=0.86ms | 376 p50=0.07ms p99=0.23ms max=0.48ms
pump [busy-20000]: 2266 p50=0.07ms p99=0.32ms max=0.57ms | 2257 p50=0.07ms p99=0.30ms max=0.42ms | 2249 p50=0.08ms p99=0.32ms max=8.58ms | 2246 p50=0.07ms p99=0.29ms max=0.54ms | 2241 p50=0.06ms p99=0.25ms max=105.09ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect alarms=5; 3x3: no detect alarms=5; 5x2: no detect alarms=5; 5x3: no detect alarms=5; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  busy20-1.log 382 (hang+-7154) NAV-COMPLETED id=2 success=True status=0
  busy20-2.log 456 (hang+-7880) NAV-COMPLETED id=2 success=True status=0
  busy20-3.log 449 (hang+-6374) NAV-COMPLETED id=2 success=True status=0
  busy20-4.log 322 (hang+-8974) NAV-COMPLETED id=2 success=True status=0
  busy20-5.log 309 (hang+-5798) NAV-COMPLETED id=2 success=True status=0

## busy3 (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=39)
probe rtt ms [busy:3000]: median 0.2 p99 2953.6 max 3000.8 (n=75)
timer tick ms [start]: median 1000 p99 1009 max 1009 (n=34); rAF/s median 60 min 59; vis visible
timer tick ms [busy:3000]: median 1000 p99 3919 max 3981 (n=64); rAF/s median 60 min 5; vis visible
pump [start]: 572 p50=0.07ms p99=0.25ms max=0.27ms | 624 p50=0.06ms p99=0.33ms max=0.51ms | 640 p50=0.06ms p99=0.32ms max=0.94ms | 520 p50=0.07ms p99=0.32ms max=0.40ms | 372 p50=0.06ms p99=0.30ms max=0.40ms
pump [busy-3000]: 957 p50=0.05ms p99=0.26ms max=0.36ms | 956 p50=0.06ms p99=0.26ms max=0.35ms | 961 p50=0.07ms p99=0.28ms max=0.41ms | 963 p50=0.06ms p99=0.27ms max=0.43ms | 964 p50=0.06ms p99=0.26ms max=0.51ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=1; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  busy3-1.log 291 (hang+-8919) NAV-COMPLETED id=2 success=True status=0
  busy3-2.log 435 (hang+-9716) NAV-COMPLETED id=2 success=True status=0
  busy3-3.log 284 (hang+-9978) NAV-COMPLETED id=2 success=True status=0
  busy3-4.log 305 (hang+-8060) NAV-COMPLETED id=2 success=True status=0
  busy3-5.log 346 (hang+-5774) NAV-COMPLETED id=2 success=True status=0

## ctl-hidden (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [hide-controller]: median 0.3 p99 0.5 max 9.5 (n=180)
probe rtt ms [show-controller]: median 0.2 p99 0.5 max 0.5 (n=30)
timer tick ms [start]: median 1000 p99 1001 max 1001 (n=11); rAF/s median 60 min 59; vis visible
timer tick ms [hide-controller]: median 999 p99 1356 max 1647 (n=178); rAF/s median 0 min 0; vis hidden/visible
timer tick ms [show-controller]: median 1000 p99 1001 max 1001 (n=33); rAF/s median 60 min 0; vis visible
pump [start]: 324 p50=0.06ms p99=0.29ms max=0.43ms | 323 p50=0.06ms p99=0.24ms max=0.29ms | 323 p50=0.05ms p99=0.25ms max=1.94ms
pump [hide-controller]: 3847 p50=0.05ms p99=0.28ms max=0.51ms | 3847 p50=0.05ms p99=0.25ms max=0.42ms | 3846 p50=0.05ms p99=0.25ms max=1.03ms
pump [show-controller]: 642 p50=0.06ms p99=0.29ms max=0.37ms | 642 p50=0.05ms p99=0.23ms max=0.38ms | 641 p50=0.05ms p99=0.26ms max=0.34ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  ctl-hidden-1.log 281 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-1.log 5288 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-1.log 5290 VIS state=hidden pagePerf=5031.6
  ctl-hidden-1.log 65287 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-1.log 65288 VIS state=visible pagePerf=65029.8
  ctl-hidden-2.log 310 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-2.log 5312 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-2.log 5313 VIS state=hidden pagePerf=5025.3
  ctl-hidden-2.log 65307 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-2.log 65307 VIS state=visible pagePerf=65019.5
  ctl-hidden-3.log 322 NAV-COMPLETED id=2 success=True status=0
  ctl-hidden-3.log 5323 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-hidden-3.log 5325 VIS state=hidden pagePerf=5024.3
  ctl-hidden-3.log 65333 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-hidden-3.log 65334 VIS state=visible pagePerf=65033.0

## ctl-suspend (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: ctl-suspend-1.log@25305, ctl-suspend-2.log@25280, ctl-suspend-3.log@25283
responsive again after alarm (ms): 11020 (11017-11063, n=3)
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=15)
probe rtt ms [hide-controller]: median 0.3 p99 2.7 max 2.7 (n=3)
probe rtt ms [resume]: median 14021.3 p99 29021.6 max 29061.5 (n=93)
probe rtt ms [show-controller]: median 0.2 p99 0.3 max 0.3 (n=39)
timer tick ms [start]: median 1000 p99 1002 max 1002 (n=11); rAF/s median 60 min 59; vis visible
timer tick ms [hide-controller]: median 999 p99 999 max 999 (n=1); rAF/s median 60 min 60; vis visible
timer tick ms [resume]: median 31936 p99 31939 max 31939 (n=3); rAF/s median 0 min 0; vis hidden
timer tick ms [show-controller]: median 1000 p99 1004 max 1004 (n=40); rAF/s median 60 min 0; vis visible
pump [start]: 321 p50=0.05ms p99=0.26ms max=0.83ms | 325 p50=0.06ms p99=0.28ms max=0.34ms | 324 p50=0.07ms p99=0.26ms max=0.28ms
pump [hide-controller]: 66 p50=0.05ms p99=0.35ms max=0.87ms | 64 p50=0.06ms p99=0.32ms max=0.79ms | 64 p50=0.05ms p99=0.20ms max=0.34ms
pump [suspend]: 1922 p50=0.05ms p99=0.25ms max=0.47ms | 1914 p50=0.06ms p99=0.26ms max=0.45ms | 1919 p50=0.05ms p99=0.26ms max=0.37ms
pump [resume]: 63 p50=0.05ms p99=0.23ms max=0.34ms | 64 p50=0.06ms p99=0.27ms max=0.29ms | 64 p50=0.06ms p99=0.23ms max=0.24ms
pump [show-controller]: 832 p50=0.05ms p99=0.25ms max=0.38ms | 830 p50=0.05ms p99=0.25ms max=0.37ms | 832 p50=0.06ms p99=0.26ms max=0.32ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=3; 1x5: no detect alarms=3; 2x3: no detect alarms=3; 2x5: no detect alarms=3; 3x3: no detect alarms=3; 5x2: no detect alarms=3; 5x3: no detect alarms=3; 5x4: no detect alarms=3; 10x2: no detect alarms=3; 10x3: no detect
events:
  ctl-suspend-1.log 320 NAV-COMPLETED id=2 success=True status=0
  ctl-suspend-1.log 5315 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-suspend-1.log 5316 VIS state=hidden pagePerf=5015.9
  ctl-suspend-1.log 6321 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.3
  ctl-suspend-1.log 36319 RESUME hr=0x00000000
  ctl-suspend-1.log 37321 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-suspend-1.log 37322 VIS state=visible pagePerf=37021.3
  ctl-suspend-2.log 335 NAV-COMPLETED id=2 success=True status=0
  ctl-suspend-2.log 5344 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-suspend-2.log 5346 VIS state=hidden pagePerf=5068.2
  ctl-suspend-2.log 6331 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.3
  ctl-suspend-2.log 36334 RESUME hr=0x00000000
  ctl-suspend-2.log 37338 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-suspend-2.log 37339 VIS state=visible pagePerf=37062.2
  ctl-suspend-3.log 291 NAV-COMPLETED id=2 success=True status=0
  ctl-suspend-3.log 5294 CONTROLLER-VISIBLE value=0 hr=0x00000000
  ctl-suspend-3.log 5295 VIS state=hidden pagePerf=5025.3
  ctl-suspend-3.log 6293 SUSPEND hr=0x00000000 completedHr=0x00000000 result=1 isSuspended=1 ms=0.3
  ctl-suspend-3.log 36300 RESUME hr=0x00000000
  ctl-suspend-3.log 37302 CONTROLLER-VISIBLE value=1 hr=0x00000000
  ctl-suspend-3.log 37303 VIS state=visible pagePerf=37033.7

## edge-kill (2 runs)
detection ms (hang start -> unresponsive): 15976 (15973-15979, n=2); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION nav:edge://kill to DOC: - never in 2
recovery from ACTION nav:edge://kill to READY: - never in 2
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=6)
probe rtt ms [state]: median 0.2 p99 0.2 max 0.2 (n=2)
timer tick ms [start]: median 1002 p99 1004 max 1004 (n=2); rAF/s median 60 min 60; vis visible
timer tick ms [state]: median 1000 p99 1000 max 1000 (n=4); rAF/s median 60 min 60; vis visible
pump [start]: 195 p50=0.06ms p99=0.31ms max=0.51ms | 194 p50=0.06ms p99=0.30ms max=0.53ms
pump [state]: 65 p50=0.06ms p99=0.24ms max=0.26ms | 66 p50=0.05ms p99=0.21ms max=0.23ms
pump [hang-idle]: 1282 p50=0.05ms p99=0.26ms max=0.57ms | 1283 p50=0.07ms p99=0.31ms max=0.54ms
pump [nav-edge://kill]: 1026 p50=0.06ms p99=0.28ms max=0.39ms | 1026 p50=0.06ms p99=0.28ms max=0.39ms
simulated (interval s x misses, from 1 s probes): 1x3: 3980 (3980-3980, n=2); 1x5: 5976 (5971-5981, n=2); 2x3: 7980 (7979-7982, n=2); 2x5: 11977 (11971-11982, n=2); 3x3: 10975 (10975-10976, n=2); 5x2: 10975 (10975-10976, n=2); 5x3: 15976 (15973-15979, n=2); 5x4: 20979 (20975-20983, n=2); 10x2: 25977 (25973-25980, n=2); 10x3: 35981 (35978-35983, n=2)
events:
  edge-kill-1.log 289 (hang+-4006) NAV-COMPLETED id=2 success=True status=0
  edge-kill-2.log 304 (hang+-4009) NAV-COMPLETED id=2 success=True status=0

## handler (5 runs)
detection ms (hang start -> unresponsive): 18496 (15876-19588, n=5); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.4 max 0.4 (n=33)
timer tick ms [start]: median 1000 p99 1012 max 1012 (n=28); rAF/s median 60 min 59; vis visible
pump [start]: 358 p50=0.06ms p99=0.22ms max=0.32ms | 347 p50=0.07ms p99=0.29ms max=0.34ms | 586 p50=0.05ms p99=0.24ms max=0.43ms | 415 p50=0.06ms p99=0.24ms max=1.23ms | 528 p50=0.06ms p99=0.24ms max=0.31ms
pump [hang-handler]: 1922 p50=0.07ms p99=0.25ms max=8.85ms | 1917 p50=0.06ms p99=0.26ms max=0.44ms | 1914 p50=0.05ms p99=0.26ms max=0.48ms | 1921 p50=0.05ms p99=0.26ms max=0.40ms | 1916 p50=0.06ms p99=0.26ms max=0.46ms
simulated (interval s x misses, from 1 s probes): 1x3: 3585 (3434-3871, n=5); 1x5: 5585 (5446-5876, n=5); 2x3: 6871 (6443-7802, n=5); 2x5: 10876 (10439-11803, n=5); 3x3: 9807 (9438-11879, n=5); 5x2: 13502 (10876-14588, n=5); 5x3: 18496 (15876-19588, n=5); 5x4: 23497 (20869-24586, n=5); 10x2: 23497 (20869-24586, n=5); 10x3: no detect
events:
  handler-1.log 288 (hang+-5529) NAV-COMPLETED id=2 success=True status=0
  handler-2.log 284 (hang+-5381) NAV-COMPLETED id=2 success=True status=0
  handler-3.log 287 (hang+-9098) NAV-COMPLETED id=2 success=True status=0
  handler-4.log 286 (hang+-6470) NAV-COMPLETED id=2 success=True status=0
  handler-5.log 278 (hang+-8161) NAV-COMPLETED id=2 success=True status=0

## healthy-kill (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
recovery auto-reload-after-exit RECOVERY-DOC: 41 (40-48, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 43 (41-50, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 966 (946-975, n=5)
recovery from ACTION kill to DOC: 49 (46-55, n=5)
recovery from ACTION kill to READY: 51 (48-57, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [state]: median 0.2 p99 8.9 max 8.9 (n=15)
probe rtt ms [kill]: median 0.2 p99 0.4 max 0.4 (n=45)
timer tick ms [start]: median 999 p99 1001 max 1001 (n=7); rAF/s median 60 min 60; vis visible
timer tick ms [state]: median 1000 p99 1008 max 1008 (n=16); rAF/s median 60 min 60; vis visible
timer tick ms [kill]: median 1000 p99 1011 max 1011 (n=37); rAF/s median 60 min 59; vis visible
pump [start]: 194 p50=0.06ms p99=0.25ms max=0.32ms | 196 p50=0.07ms p99=0.31ms max=0.40ms | 195 p50=0.06ms p99=0.27ms max=0.41ms | 195 p50=0.06ms p99=0.22ms max=0.52ms | 196 p50=0.06ms p99=0.27ms max=0.36ms
pump [state]: 193 p50=0.05ms p99=0.29ms max=0.31ms | 193 p50=0.05ms p99=0.26ms max=0.29ms | 192 p50=0.04ms p99=0.27ms max=0.32ms | 193 p50=0.05ms p99=0.29ms max=0.32ms | 192 p50=0.05ms p99=0.28ms max=0.37ms
pump [kill]: 579 p50=0.05ms p99=0.28ms max=0.36ms | 578 p50=0.05ms p99=0.26ms max=0.30ms | 577 p50=0.05ms p99=0.27ms max=0.39ms | 576 p50=0.05ms p99=0.24ms max=0.45ms | 575 p50=0.05ms p99=0.26ms max=0.45ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  healthy-kill-1.log 285 NAV-COMPLETED id=2 success=True status=0
  healthy-kill-1.log 6288 KILL-RENDERER pid=45108 error=0 callUs=155
  healthy-kill-1.log 6295 PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[utility:19700 gpu:24140 browser:45072 renderer:45108 utility:72204]
  healthy-kill-1.log 6334 VIS state=visible pagePerf=35.7
  healthy-kill-1.log 6341 NAV-COMPLETED id=3 success=True status=0
  healthy-kill-2.log 293 NAV-COMPLETED id=2 success=True status=0
  healthy-kill-2.log 6289 KILL-RENDERER pid=46488 error=0 callUs=142
  healthy-kill-2.log 6297 PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[utility:8320 browser:10888 gpu:19828 utility:20404 renderer:46488]
  healthy-kill-2.log 6339 VIS state=visible pagePerf=38.9
  healthy-kill-2.log 6345 NAV-COMPLETED id=3 success=True status=0
  healthy-kill-3.log 308 NAV-COMPLETED id=2 success=True status=0
  healthy-kill-3.log 6307 KILL-RENDERER pid=31632 error=0 callUs=136
  healthy-kill-3.log 6315 PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[renderer:31632 utility:44408 gpu:45396 utility:57028 browser:74144]
  healthy-kill-3.log 6356 VIS state=visible pagePerf=37.4
  healthy-kill-3.log 6363 NAV-COMPLETED id=3 success=True status=0
  healthy-kill-4.log 291 NAV-COMPLETED id=2 success=True status=0
  healthy-kill-4.log 6300 KILL-RENDERER pid=62488 error=0 callUs=189
  healthy-kill-4.log 6306 PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:51848 utility:52988 utility:57100 renderer:62488 gpu:72220]
  healthy-kill-4.log 6346 VIS state=visible pagePerf=36.5
  healthy-kill-4.log 6352 NAV-COMPLETED id=3 success=True status=0
  healthy-kill-5.log 293 NAV-COMPLETED id=2 success=True status=0
  healthy-kill-5.log 6292 KILL-RENDERER pid=55504 error=0 callUs=146
  healthy-kill-5.log 6299 PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:14636 utility:36904 renderer:55504 gpu:58396 utility:63176]
  healthy-kill-5.log 6347 VIS state=visible pagePerf=43.9
  healthy-kill-5.log 6354 NAV-COMPLETED id=3 success=True status=0

## healthy-reload (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
recovery reload RECOVERY-DOC: 8 (8-8, n=5)
recovery reload RECOVERY-READY: 9 (9-9, n=5)
recovery reload RECOVERY-PONG: 981 (968-989, n=5)
recovery from ACTION reload to DOC: 8 (8-9, n=5)
recovery from ACTION reload to READY: 9 (9-10, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [state]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [reload]: median 0.2 p99 8.9 max 8.9 (n=45)
timer tick ms [start]: median 1000 p99 1006 max 1006 (n=8); rAF/s median 60 min 60; vis visible
timer tick ms [state]: median 1000 p99 1010 max 1010 (n=15); rAF/s median 60 min 59; vis visible
timer tick ms [reload]: median 1000 p99 1008 max 1008 (n=37); rAF/s median 60 min 59; vis visible
pump [start]: 195 p50=0.06ms p99=0.27ms max=0.35ms | 196 p50=0.06ms p99=0.26ms max=0.36ms | 196 p50=0.07ms p99=0.32ms max=0.36ms | 194 p50=0.06ms p99=0.23ms max=0.30ms | 196 p50=0.06ms p99=0.29ms max=2.45ms
pump [state]: 191 p50=0.05ms p99=0.26ms max=0.39ms | 192 p50=0.05ms p99=0.29ms max=0.48ms | 192 p50=0.05ms p99=0.25ms max=0.27ms | 193 p50=0.05ms p99=0.25ms max=0.30ms | 192 p50=0.05ms p99=0.23ms max=0.29ms
pump [reload]: 575 p50=0.05ms p99=0.25ms max=0.32ms | 574 p50=0.05ms p99=0.28ms max=0.40ms | 575 p50=0.06ms p99=0.28ms max=0.49ms | 577 p50=0.05ms p99=0.24ms max=0.72ms | 577 p50=0.05ms p99=0.27ms max=8.65ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  healthy-reload-1.log 292 NAV-COMPLETED id=2 success=True status=0
  healthy-reload-1.log 6293 VIS state=hidden pagePerf=6025.5
  healthy-reload-1.log 6298 NAV-COMPLETED id=3 success=True status=0
  healthy-reload-2.log 299 NAV-COMPLETED id=2 success=True status=0
  healthy-reload-2.log 6312 VIS state=hidden pagePerf=6035.3
  healthy-reload-2.log 6317 NAV-COMPLETED id=3 success=True status=0
  healthy-reload-3.log 310 NAV-COMPLETED id=2 success=True status=0
  healthy-reload-3.log 6323 VIS state=hidden pagePerf=6041.7
  healthy-reload-3.log 6328 NAV-COMPLETED id=3 success=True status=0
  healthy-reload-4.log 289 NAV-COMPLETED id=2 success=True status=0
  healthy-reload-4.log 6300 VIS state=hidden pagePerf=6034.1
  healthy-reload-4.log 6305 NAV-COMPLETED id=3 success=True status=0
  healthy-reload-5.log 304 NAV-COMPLETED id=2 success=True status=0
  healthy-reload-5.log 6309 VIS state=hidden pagePerf=6023.4
  healthy-reload-5.log 6313 NAV-COMPLETED id=3 success=True status=0

## idle (5 runs)
detection ms (hang start -> unresponsive): 15794 (15337-19894, n=5); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=37)
timer tick ms [start]: median 999 p99 1012 max 1012 (n=32); rAF/s median 60 min 59; vis visible
pump [start]: 365 p50=0.07ms p99=0.21ms max=0.28ms | 592 p50=0.05ms p99=0.21ms max=0.27ms | 610 p50=0.05ms p99=0.20ms max=0.28ms | 623 p50=0.06ms p99=0.23ms max=0.29ms | 328 p50=0.06ms p99=0.22ms max=2.44ms
pump [hang-idle]: 1918 p50=0.05ms p99=0.20ms max=0.35ms | 1925 p50=0.06ms p99=0.23ms max=0.46ms | 1921 p50=0.05ms p99=0.22ms max=0.37ms | 1920 p50=0.06ms p99=0.21ms max=0.37ms | 1926 p50=0.06ms p99=0.23ms max=0.45ms
simulated (interval s x misses, from 1 s probes): 1x3: 3544 (3339-3906, n=5); 1x5: 5553 (5335-5900, n=5); 2x3: 6552 (6338-6896, n=5); 2x5: 10550 (10330-10899, n=5); 3x3: 11344 (9342-11799, n=5); 5x2: 10800 (10330-14909, n=5); 5x3: 15794 (15337-19894, n=5); 5x4: 20800 (20335-24908, n=5); 10x2: 20800 (20335-24908, n=5); 10x3: no detect
events:
  idle-1.log 290 (hang+-5622) NAV-COMPLETED id=2 success=True status=0
  idle-2.log 294 (hang+-9175) NAV-COMPLETED id=2 success=True status=0
  idle-3.log 277 (hang+-9439) NAV-COMPLETED id=2 success=True status=0
  idle-4.log 283 (hang+-9635) NAV-COMPLETED id=2 success=True status=0
  idle-5.log 272 (hang+-5076) NAV-COMPLETED id=2 success=True status=0

## kill (5 runs)
detection ms (hang start -> unresponsive): 15971 (15955-15976, n=5); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 5007 (4997-5013, n=5)
recovery auto-reload-after-exit RECOVERY-DOC: 41 (38-44, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 43 (40-48, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 960 (945-977, n=5)
recovery from ACTION kill to DOC: 48 (45-54, n=5)
recovery from ACTION kill to READY: 50 (47-57, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.2 p99 0.4 max 0.4 (n=15)
probe rtt ms [state]: median 0.2 p99 0.3 max 0.3 (n=5)
probe rtt ms [kill]: median 0.2 p99 0.3 max 8.9 (n=105)
timer tick ms [start]: median 1000 p99 1006 max 1006 (n=7); rAF/s median 60 min 59; vis visible
timer tick ms [state]: median 999 p99 1001 max 1001 (n=5); rAF/s median 60 min 59; vis visible
timer tick ms [kill]: median 1000 p99 1007 max 1011 (n=95); rAF/s median 60 min 59; vis visible
timer tick ms [hang-idle]: median 1002 p99 1006 max 1006 (n=3); rAF/s median 60 min 60; vis visible
pump [start]: 196 p50=0.06ms p99=0.29ms max=0.37ms | 195 p50=0.06ms p99=0.23ms max=0.34ms | 195 p50=0.07ms p99=0.27ms max=0.61ms | 196 p50=0.07ms p99=0.33ms max=0.45ms | 194 p50=0.06ms p99=0.24ms max=0.30ms
pump [state]: 63 p50=0.05ms p99=0.19ms max=0.19ms | 65 p50=0.06ms p99=0.20ms max=0.24ms | 64 p50=0.05ms p99=0.28ms max=0.32ms | 65 p50=0.05ms p99=0.22ms max=0.31ms | 64 p50=0.04ms p99=0.24ms max=0.33ms
pump [hang-idle]: 1281 p50=0.07ms p99=0.27ms max=0.37ms | 1274 p50=0.06ms p99=0.26ms max=0.43ms | 1281 p50=0.05ms p99=0.26ms max=0.35ms | 1281 p50=0.05ms p99=0.28ms max=0.60ms | 1282 p50=0.05ms p99=0.26ms max=0.33ms
pump [kill]: 1342 p50=0.05ms p99=0.27ms max=0.46ms | 1342 p50=0.05ms p99=0.27ms max=0.35ms | 1345 p50=0.05ms p99=0.27ms max=0.54ms | 1345 p50=0.05ms p99=0.26ms max=0.36ms | 1341 p50=0.05ms p99=0.25ms max=0.37ms
simulated (interval s x misses, from 1 s probes): 1x3: 3968 (3956-3981, n=5); 1x5: 5971 (5966-5983, n=5); 2x3: 7970 (7963-7983, n=5); 2x5: 11968 (11963-11973, n=5); 3x3: 10968 (10964-10976, n=5); 5x2: 10968 (10964-10976, n=5); 5x3: 15971 (15955-15976, n=5); 5x4: 20972 (20959-20983, n=5); 10x2: no detect; 10x3: no detect
events:
  kill-1.log 317 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  kill-1.log 24325 (hang+20001) KILL-RENDERER pid=13768 error=0 callUs=213
  kill-1.log 24334 (hang+20010) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[renderer:13768 browser:14772 utility:22324 utility:38924 gpu:46052]
  kill-1.log 24379 (hang+20054) VIS state=visible pagePerf=39.9
  kill-1.log 24387 (hang+20063) NAV-COMPLETED id=3 success=True status=0
  kill-2.log 304 (hang+-4001) NAV-COMPLETED id=2 success=True status=0
  kill-2.log 24310 (hang+20005) KILL-RENDERER pid=59256 error=0 callUs=148
  kill-2.log 24317 (hang+20012) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:25104 gpu:28840 utility:38216 utility:40484 renderer:59256]
  kill-2.log 24356 (hang+20051) VIS state=visible pagePerf=35.9
  kill-2.log 24363 (hang+20058) NAV-COMPLETED id=3 success=True status=0
  kill-3.log 291 (hang+-3998) NAV-COMPLETED id=2 success=True status=0
  kill-3.log 24296 (hang+20008) KILL-RENDERER pid=66804 error=0 callUs=163
  kill-3.log 24302 (hang+20014) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[gpu:25836 browser:30260 utility:43004 utility:63132 renderer:66804]
  kill-3.log 24341 (hang+20052) VIS state=visible pagePerf=34.6
  kill-3.log 24347 (hang+20059) NAV-COMPLETED id=3 success=True status=0
  kill-4.log 299 (hang+-4005) NAV-COMPLETED id=2 success=True status=0
  kill-4.log 24306 (hang+20002) KILL-RENDERER pid=63764 error=0 callUs=139
  kill-4.log 24315 (hang+20011) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:18272 utility:56248 renderer:63764 gpu:73816 utility:74872]
  kill-4.log 24356 (hang+20052) VIS state=visible pagePerf=38.5
  kill-4.log 24368 (hang+20064) NAV-COMPLETED id=3 success=True status=0
  kill-5.log 280 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  kill-5.log 24282 (hang+19999) KILL-RENDERER pid=62364 error=0 callUs=140
  kill-5.log 24288 (hang+20005) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[gpu:23792 browser:60880 renderer:62364 utility:63080 utility:63292]
  kill-5.log 24330 (hang+20047) VIS state=visible pagePerf=37.8
  kill-5.log 24336 (hang+20053) NAV-COMPLETED id=3 success=True status=0

## kill-load (5 runs)
detection ms (hang start -> unresponsive): 19981 (19971-19991, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery auto-reload-after-exit RECOVERY-DOC: 41 (40-48, n=5)
pump [start]: 2569 p50=0.06ms p99=0.28ms max=0.36ms | 2559 p50=0.06ms p99=0.29ms max=0.56ms | 2565 p50=0.06ms p99=0.29ms max=0.64ms | 2562 p50=0.06ms p99=0.27ms max=0.54ms | 2564 p50=0.06ms p99=0.27ms max=0.40ms
simulated (interval s x misses, from 1 s probes): 1x3: 3986 (3973-3999, n=5); 1x5: 5980 (5967-5999, n=5); 2x3: 7981 (7966-7992, n=5); 2x5: 11984 (11973-12000, n=5); 3x3: 11984 (11973-12000, n=5); 5x2: 14982 (14975-14994, n=5); 5x3: 19981 (19971-19991, n=5); 5x4: 24980 (24966-24996, n=5); 10x2: 29986 (29973-29995, n=5); 10x3: 39973 (39972-39975, n=2)
events:
  kill-load-1.log 20267 (hang+19980) KILL-RENDERER pid=49784 error=0 callUs=134
  kill-load-1.log 20273 (hang+19986) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[utility:2948 gpu:25976 browser:36308 renderer:49784 utility:72384]
  kill-load-1.log 20315 (hang+20028) VIS state=visible pagePerf=37.8
  kill-load-2.log 20298 (hang+19991) KILL-RENDERER pid=11412 error=0 callUs=129
  kill-load-2.log 20305 (hang+19999) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[renderer:11412 utility:34544 browser:60572 utility:72044 gpu:75656]
  kill-load-2.log 20347 (hang+20040) VIS state=visible pagePerf=37.6
  kill-load-3.log 20262 (hang+19989) KILL-RENDERER pid=39004 error=0 callUs=152
  kill-load-3.log 20269 (hang+19996) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:23524 utility:36392 renderer:39004 utility:50080 gpu:51508]
  kill-load-3.log 20311 (hang+20037) VIS state=visible pagePerf=37.6
  kill-load-4.log 20276 (hang+19981) KILL-RENDERER pid=15312 error=0 callUs=134
  kill-load-4.log 20284 (hang+19989) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[utility:13008 renderer:15312 browser:39796 utility:41300 gpu:64248]
  kill-load-4.log 20324 (hang+20030) VIS state=visible pagePerf=36.9
  kill-load-5.log 20279 (hang+19972) KILL-RENDERER pid=44240 error=0 callUs=142
  kill-load-5.log 20287 (hang+19980) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[browser:29984 utility:30116 renderer:44240 utility:47552 gpu:56936]
  kill-load-5.log 20335 (hang+20028) VIS state=visible pagePerf=43.9

## load-contentloading (5 runs)
detection ms (hang start -> unresponsive): 19983 (19975-19988, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 1916 p50=0.06ms p99=0.25ms max=0.35ms | 1924 p50=0.05ms p99=0.26ms max=0.49ms | 1923 p50=0.05ms p99=0.26ms max=0.45ms | 1920 p50=0.06ms p99=0.24ms max=0.55ms | 1919 p50=0.06ms p99=0.27ms max=0.63ms
simulated (interval s x misses, from 1 s probes): 1x3: 3985 (3977-3990, n=5); 1x5: 5985 (5978-5991, n=5); 2x3: 7989 (7977-7992, n=5); 2x5: 11979 (11975-11993, n=5); 3x3: 11979 (11975-11993, n=5); 5x2: 14985 (14973-14993, n=5); 5x3: 19983 (19975-19987, n=5); 5x4: 24982 (24975-24996, n=5); 10x2: 29986 (29979-29993, n=4); 10x3: no detect

## load-doc (5 runs)
detection ms (hang start -> unresponsive): 19979 (19967-19986, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 1922 p50=0.06ms p99=0.27ms max=0.40ms | 1921 p50=0.05ms p99=0.27ms max=8.57ms | 1919 p50=0.06ms p99=0.27ms max=0.35ms | 1919 p50=0.06ms p99=0.27ms max=0.40ms | 1915 p50=0.06ms p99=0.27ms max=0.40ms
simulated (interval s x misses, from 1 s probes): 1x3: 3975 (3964-3986, n=5); 1x5: 5976 (5966-5989, n=5); 2x3: 7972 (7962-7992, n=5); 2x5: 11971 (11958-11985, n=5); 3x3: 11971 (11958-11985, n=5); 5x2: 14977 (14960-14984, n=5); 5x3: 19979 (19967-19986, n=5); 5x4: 24971 (24970-24990, n=5); 10x2: 29970 (29963-29977, n=2); 10x3: no detect

## load-navcompleted (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
pump [start]: 1922 p50=0.07ms p99=0.27ms max=0.37ms | 1923 p50=0.06ms p99=0.27ms max=1.99ms | 1929 p50=0.06ms p99=0.29ms max=2.48ms | 1926 p50=0.06ms p99=0.28ms max=0.44ms | 1924 p50=0.06ms p99=0.26ms max=0.40ms
simulated (interval s x misses, from 1 s probes): 1x3: 3984 (3970-3998, n=5); 1x5: 5984 (5972-5998, n=5); 2x3: 7990 (7974-8001, n=5); 2x5: 11992 (11977-11999, n=5); 3x3: 11992 (11977-11999, n=5); 5x2: 14986 (14977-14994, n=5); 5x3: 19985 (19967-19997, n=5); 5x4: 24985 (24969-25001, n=5); 10x2: 29987 (29976-29993, n=4); 10x3: no detect

## mem-low (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 8.4 max 8.4 (n=15)
probe rtt ms [mem:low]: median 0.2 p99 8.8 max 9.0 (n=90)
probe rtt ms [mem:normal]: median 0.2 p99 9.1 max 9.1 (n=30)
timer tick ms [start]: median 1000 p99 1007 max 1007 (n=11); rAF/s median 60 min 59; vis visible
timer tick ms [mem:low]: median 1000 p99 1009 max 1011 (n=89); rAF/s median 60 min 59; vis visible
timer tick ms [mem:normal]: median 1000 p99 1015 max 1015 (n=31); rAF/s median 60 min 59; vis visible
pump [start]: 322 p50=0.06ms p99=0.26ms max=0.36ms | 323 p50=0.06ms p99=0.29ms max=1.17ms | 324 p50=0.06ms p99=0.26ms max=0.33ms
pump [mem-low]: 1920 p50=0.05ms p99=0.27ms max=0.52ms | 1926 p50=0.05ms p99=0.27ms max=8.82ms | 1918 p50=0.06ms p99=0.26ms max=0.44ms
pump [mem-normal]: 641 p50=0.06ms p99=0.25ms max=9.15ms | 642 p50=0.05ms p99=0.26ms max=0.37ms | 641 p50=0.05ms p99=0.24ms max=0.37ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  mem-low-1.log 292 NAV-COMPLETED id=2 success=True status=0
  mem-low-1.log 5298 MEMORY-TARGET set=1 read=1
  mem-low-1.log 35300 MEMORY-TARGET set=0 read=0
  mem-low-2.log 281 NAV-COMPLETED id=2 success=True status=0
  mem-low-2.log 5280 MEMORY-TARGET set=1 read=1
  mem-low-2.log 35286 MEMORY-TARGET set=0 read=0
  mem-low-3.log 281 NAV-COMPLETED id=2 success=True status=0
  mem-low-3.log 5282 MEMORY-TARGET set=1 read=1
  mem-low-3.log 35277 MEMORY-TARGET set=0 read=0

## microtask (5 runs)
detection ms (hang start -> unresponsive): 17421 (15926-19474, n=5); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 5000 (4996-10006, n=5)
recovery auto-reload-after-exit RECOVERY-DOC: 55 (49-64, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 59 (52-67, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 852 (355-974, n=5)
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=36)
probe rtt ms [hang-microtask]: median 0.2 p99 0.4 max 0.4 (n=38)
timer tick ms [start]: median 1000 p99 1009 max 1009 (n=31); rAF/s median 60 min 59; vis visible
timer tick ms [hang-microtask]: median 1000 p99 1006 max 1006 (n=31); rAF/s median 60 min 59; vis visible
pump [start]: 481 p50=0.06ms p99=0.27ms max=0.39ms | 567 p50=0.06ms p99=0.27ms max=0.46ms | 485 p50=0.06ms p99=0.31ms max=0.35ms | 579 p50=0.06ms p99=0.28ms max=0.97ms | 354 p50=0.06ms p99=0.30ms max=2.29ms
pump [hang-microtask]: 1914 p50=0.07ms p99=0.28ms max=0.53ms | 1915 p50=0.07ms p99=0.29ms max=0.48ms | 1914 p50=0.06ms p99=0.29ms max=0.42ms | 1915 p50=0.07ms p99=0.31ms max=0.40ms | 1917 p50=0.06ms p99=0.30ms max=4.30ms
simulated (interval s x misses, from 1 s probes): 1x3: 3463 (3169-3926, n=5); 1x5: 5466 (5166-5931, n=5); 2x3: 6473 (6425-7168, n=5); 2x5: 10484 (10429-11164, n=5); 3x3: 10429 (9172-11933, n=5); 5x2: 12435 (10930-14471, n=5); 5x3: 17421 (15926-19474, n=5); 5x4: 21167 (20922-22420, n=3); 10x2: 21167 (20922-22420, n=3); 10x3: no detect
events:
  microtask-1.log 275 (hang+-7506) NAV-COMPLETED id=2 success=True status=0
  microtask-1.log 28272 (hang+20491) PROCESS-FAILED kind=render-exited reason=out-of-memory exitCode=-536870904 description= processes=[browser:17976 gpu:35304 utility:47748 utility:50488 renderer:67160]
  microtask-1.log 28321 (hang+20540) VIS state=visible pagePerf=48.6
  microtask-1.log 28329 (hang+20548) NAV-COMPLETED id=3 success=True status=0
  microtask-2.log 274 (hang+-8817) NAV-COMPLETED id=2 success=True status=0
  microtask-2.log 34819 (hang+25728) PROCESS-FAILED kind=render-exited reason=out-of-memory exitCode=-536870904 description= processes=[utility:29516 renderer:46212 browser:58176 utility:66196 gpu:73552]
  microtask-2.log 34880 (hang+25790) VIS state=visible pagePerf=60.9
  microtask-2.log 34887 (hang+25796) NAV-COMPLETED id=3 success=True status=0
  microtask-3.log 305 (hang+-7532) NAV-COMPLETED id=2 success=True status=0
  microtask-3.log 29405 (hang+21568) PROCESS-FAILED kind=render-exited reason=out-of-memory exitCode=-536870904 description= processes=[utility:37372 browser:51572 gpu:53788 utility:60340 renderer:63132]
  microtask-3.log 29460 (hang+21623) VIS state=visible pagePerf=54.7
  microtask-3.log 29469 (hang+21632) NAV-COMPLETED id=3 success=True status=0
  microtask-4.log 293 (hang+-9039) NAV-COMPLETED id=2 success=True status=0
  microtask-4.log 31336 (hang+22003) PROCESS-FAILED kind=render-exited reason=out-of-memory exitCode=-536870904 description= processes=[gpu:16944 utility:34524 renderer:51644 utility:62744 browser:72724]
  microtask-4.log 31400 (hang+22068) VIS state=visible pagePerf=63.7
  microtask-4.log 31408 (hang+22076) NAV-COMPLETED id=3 success=True status=0
  microtask-5.log 292 (hang+-5500) NAV-COMPLETED id=2 success=True status=0
  microtask-5.log 26920 (hang+21127) PROCESS-FAILED kind=render-exited reason=out-of-memory exitCode=-536870904 description= processes=[utility:9500 gpu:37756 renderer:44920 utility:65960 browser:67660]
  microtask-5.log 26969 (hang+21177) VIS state=visible pagePerf=49.4
  microtask-5.log 26981 (hang+21189) NAV-COMPLETED id=3 success=True status=0

## noinject (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: noinject-1.log@20207, noinject-2.log@20201, noinject-3.log@20203, noinject-4.log@20220, noinject-5.log@20260
pump [start]: 1925 p50=0.05ms p99=0.25ms max=0.52ms | 1917 p50=0.05ms p99=0.25ms max=0.42ms | 1922 p50=0.05ms p99=0.25ms max=0.34ms | 1922 p50=0.05ms p99=0.27ms max=1.00ms | 1923 p50=0.05ms p99=0.27ms max=0.35ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect alarms=5; 3x3: no detect alarms=5; 5x2: no detect alarms=5; 5x3: no detect alarms=5; 5x4: no detect alarms=5; 10x2: no detect alarms=3; 10x3: no detect
events:
  noinject-1.log 278 NAV-COMPLETED id=2 success=True status=0
  noinject-2.log 273 NAV-COMPLETED id=2 success=True status=0
  noinject-3.log 269 NAV-COMPLETED id=2 success=True status=0
  noinject-4.log 288 NAV-COMPLETED id=2 success=True status=0
  noinject-5.log 325 NAV-COMPLETED id=2 success=True status=0

## pf-pings (3 runs)
detection ms (hang start -> unresponsive): 16973 (16973-16976, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.4 max 0.4 (n=9)
timer tick ms [start]: median 1002 p99 1008 max 1008 (n=4); rAF/s median 61 min 59; vis visible
timer tick ms [hang-idle]: median 996 p99 998 max 998 (n=2); rAF/s median 60 min 59; vis visible
pump [start]: 196 p50=0.06ms p99=0.37ms max=0.96ms | 196 p50=0.06ms p99=0.31ms max=0.31ms | 196 p50=0.06ms p99=0.22ms max=0.40ms
pump [hang-idle]: 4617 p50=0.06ms p99=0.26ms max=0.52ms | 4617 p50=0.05ms p99=0.26ms max=0.52ms | 4615 p50=0.05ms p99=0.26ms max=0.45ms
simulated (interval s x misses, from 1 s probes): 1x3: 3966 (3966-3971, n=3); 1x5: 5974 (5965-5975, n=3); 2x3: 6971 (6969-6979, n=3); 2x5: 10973 (10965-10975, n=3); 3x3: 11974 (11965-11975, n=3); 5x2: 11974 (11965-11975, n=3); 5x3: 16973 (16973-16976, n=3); 5x4: 21971 (21962-21974, n=3); 10x2: 26969 (26966-26977, n=3); 10x3: 36972 (36963-36978, n=3)
events:
  pf-pings-1.log 317 (hang+-2995) NAV-COMPLETED id=2 success=True status=0
  pf-pings-2.log 278 (hang+-3003) NAV-COMPLETED id=2 success=True status=0
  pf-pings-3.log 299 (hang+-2997) NAV-COMPLETED id=2 success=True status=0

## pf-quiet (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
timer tick ms [start]: median 999 p99 1003 max 1003 (n=5); rAF/s median 60 min 59; vis visible
pump [start]: 195 p50=0.06ms p99=0.25ms max=0.47ms | 194 p50=0.06ms p99=0.29ms max=0.68ms | 195 p50=0.06ms p99=0.25ms max=0.44ms
pump [hang-idle]: 4618 p50=0.06ms p99=0.29ms max=0.57ms | 4612 p50=0.05ms p99=0.26ms max=0.52ms | 4608 p50=0.05ms p99=0.27ms max=0.45ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  pf-quiet-1.log 277 (hang+-2994) NAV-COMPLETED id=2 success=True status=0
  pf-quiet-2.log 278 (hang+-2998) NAV-COMPLETED id=2 success=True status=0
  pf-quiet-3.log 278 (hang+-3003) NAV-COMPLETED id=2 success=True status=0

## reload (5 runs)
detection ms (hang start -> unresponsive): 15977 (15967-15995, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION reload to DOC: - never in 5
recovery from ACTION reload to READY: - never in 5
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=15)
probe rtt ms [state]: median 0.2 p99 0.2 max 0.2 (n=5)
timer tick ms [start]: median 1000 p99 1002 max 1002 (n=7); rAF/s median 60 min 60; vis visible
timer tick ms [state]: median 1000 p99 1006 max 1006 (n=7); rAF/s median 60 min 59; vis visible
timer tick ms [hang-idle]: median 997 p99 997 max 997 (n=1); rAF/s median 60 min 60; vis visible
pump [start]: 194 p50=0.06ms p99=0.28ms max=0.37ms | 193 p50=0.05ms p99=0.24ms max=0.34ms | 195 p50=0.05ms p99=0.26ms max=0.37ms | 194 p50=0.06ms p99=0.22ms max=0.29ms | 195 p50=0.06ms p99=0.24ms max=0.79ms
pump [state]: 64 p50=0.05ms p99=0.24ms max=0.25ms | 64 p50=0.06ms p99=0.30ms max=0.34ms | 65 p50=0.05ms p99=0.26ms max=0.32ms | 65 p50=0.05ms p99=0.23ms max=0.24ms | 64 p50=0.06ms p99=0.22ms max=0.25ms
pump [hang-idle]: 1281 p50=0.05ms p99=0.27ms max=0.41ms | 1282 p50=0.05ms p99=0.26ms max=0.37ms | 1279 p50=0.05ms p99=0.25ms max=0.44ms | 1282 p50=0.06ms p99=0.31ms max=0.60ms | 1282 p50=0.06ms p99=0.26ms max=0.34ms
pump [reload]: 2947 p50=0.06ms p99=0.28ms max=0.41ms | 2944 p50=0.05ms p99=0.29ms max=0.41ms | 2949 p50=0.06ms p99=0.29ms max=0.62ms | 2947 p50=0.06ms p99=0.28ms max=0.35ms | 2949 p50=0.07ms p99=0.30ms max=0.39ms
simulated (interval s x misses, from 1 s probes): 1x3: 3978 (3969-3985, n=5); 1x5: 5982 (5968-5992, n=5); 2x3: 7980 (7973-7991, n=5); 2x5: 11980 (11966-11994, n=5); 3x3: 10971 (10970-10994, n=5); 5x2: 10971 (10970-10994, n=5); 5x3: 15977 (15967-15995, n=5); 5x4: 20970 (20966-20993, n=5); 10x2: 25985 (25971-25993, n=5); 10x3: 35979 (35974-35988, n=5)
events:
  reload-1.log 281 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  reload-1.log 54292 (hang+50008) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[gpu:40312 browser:51304 renderer:66188 utility:66492 utility:75864]
  reload-2.log 277 (hang+-3997) NAV-COMPLETED id=2 success=True status=0
  reload-2.log 54300 (hang+50026) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:8232 gpu:10016 renderer:42624 utility:48172 browser:73916]
  reload-3.log 274 (hang+-4009) NAV-COMPLETED id=2 success=True status=0
  reload-3.log 54284 (hang+50001) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:20748 utility:43756 browser:52760 renderer:64368 gpu:76704]
  reload-4.log 290 (hang+-3995) NAV-COMPLETED id=2 success=True status=0
  reload-4.log 54312 (hang+50026) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:3456 utility:4600 gpu:40536 renderer:50848 browser:63132]
  reload-5.log 287 (hang+-4011) NAV-COMPLETED id=2 success=True status=0
  reload-5.log 54312 (hang+50014) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:26292 renderer:39460 utility:58800 gpu:68284 utility:76492]

## reload-load (5 runs)
detection ms (hang start -> unresponsive): 19986 (19950-19991, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 2560 p50=0.05ms p99=0.25ms max=1.86ms | 2560 p50=0.06ms p99=0.29ms max=0.60ms | 2560 p50=0.05ms p99=0.28ms max=0.48ms | 2565 p50=0.06ms p99=0.28ms max=0.60ms | 2562 p50=0.06ms p99=0.30ms max=0.55ms
simulated (interval s x misses, from 1 s probes): 1x3: 3985 (3954-3994, n=5); 1x5: 5983 (5952-5995, n=5); 2x3: 7981 (7950-7992, n=5); 2x5: 11988 (11945-11996, n=5); 3x3: 11988 (11945-11996, n=5); 5x2: 14988 (14955-14995, n=5); 5x3: 19986 (19950-19991, n=5); 5x4: 24985 (24956-24992, n=5); 10x2: 29991 (29946-29993, n=5); 10x3: 39982 (39978-39987, n=2)

## renav (5 runs)
detection ms (hang start -> unresponsive): 15976 (15958-15991, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION nav:/index.html to DOC: - never in 5
recovery from ACTION nav:/index.html to READY: - never in 5
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [state]: median 0.2 p99 0.2 max 0.2 (n=5)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=8); rAF/s median 60 min 59; vis visible
timer tick ms [state]: median 1001 p99 1005 max 1005 (n=5); rAF/s median 60 min 60; vis visible
timer tick ms [hang-idle]: median 1000 p99 1003 max 1003 (n=2); rAF/s median 60 min 60; vis visible
pump [start]: 194 p50=0.06ms p99=0.28ms max=0.50ms | 197 p50=0.07ms p99=0.25ms max=0.38ms | 194 p50=0.05ms p99=0.23ms max=0.34ms | 195 p50=0.05ms p99=0.23ms max=0.32ms | 194 p50=0.06ms p99=0.24ms max=0.32ms
pump [state]: 62 p50=0.05ms p99=0.26ms max=0.28ms | 63 p50=0.08ms p99=0.24ms max=0.25ms | 64 p50=0.05ms p99=0.17ms max=0.20ms | 64 p50=0.04ms p99=0.22ms max=0.23ms | 64 p50=0.04ms p99=0.27ms max=0.27ms
pump [hang-idle]: 1275 p50=0.05ms p99=0.24ms max=0.40ms | 1286 p50=0.06ms p99=0.26ms max=0.40ms | 1281 p50=0.05ms p99=0.24ms max=0.56ms | 1285 p50=0.06ms p99=0.25ms max=0.44ms | 1284 p50=0.06ms p99=0.29ms max=0.64ms
pump [nav-/index.html]: 2953 p50=0.06ms p99=0.28ms max=0.43ms | 2951 p50=0.06ms p99=0.27ms max=0.42ms | 2944 p50=0.05ms p99=0.25ms max=0.76ms | 2943 p50=0.05ms p99=0.27ms max=0.48ms | 2952 p50=0.06ms p99=0.27ms max=0.44ms
simulated (interval s x misses, from 1 s probes): 1x3: 3977 (3948-3985, n=5); 1x5: 5979 (5961-5984, n=5); 2x3: 7982 (7955-7987, n=5); 2x5: 11982 (11961-11992, n=5); 3x3: 10977 (10949-10992, n=5); 5x2: 10977 (10949-10992, n=5); 5x3: 15976 (15958-15991, n=5); 5x4: 20980 (20951-20988, n=5); 10x2: 25974 (25951-25995, n=5); 10x3: 35977 (35953-35988, n=5)
events:
  renav-1.log 289 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  renav-1.log 54303 (hang+50012) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:28560 utility:32540 gpu:35080 browser:47240 renderer:59532]
  renav-2.log 303 (hang+-3999) NAV-COMPLETED id=2 success=True status=0
  renav-2.log 54314 (hang+50012) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:25944 renderer:45464 gpu:48740 browser:61180 utility:68220]
  renav-3.log 288 (hang+-4009) NAV-COMPLETED id=2 success=True status=0
  renav-3.log 54295 (hang+49998) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:29804 utility:42416 gpu:42684 utility:42808 renderer:50352]
  renav-4.log 303 (hang+-4008) NAV-COMPLETED id=2 success=True status=0
  renav-4.log 54311 (hang+50000) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[renderer:22152 utility:30128 browser:37580 utility:41156 gpu:65760]
  renav-5.log 306 (hang+-3998) NAV-COMPLETED id=2 success=True status=0
  renav-5.log 54321 (hang+50016) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:15332 browser:41276 renderer:46212 utility:64332 gpu:69648]

## stop-reload (5 runs)
detection ms (hang start -> unresponsive): 15978 (15955-15986, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION reload to DOC: - never in 5
recovery from ACTION reload to READY: - never in 5
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=15)
probe rtt ms [state]: median 0.1 p99 0.2 max 0.2 (n=5)
timer tick ms [start]: median 1003 p99 1007 max 1007 (n=6); rAF/s median 60 min 59; vis visible
timer tick ms [state]: median 1000 p99 1008 max 1008 (n=5); rAF/s median 60 min 60; vis visible
timer tick ms [hang-idle]: median 1006 p99 1011 max 1011 (n=4); rAF/s median 60 min 60; vis visible
pump [start]: 195 p50=0.07ms p99=0.23ms max=0.32ms | 195 p50=0.06ms p99=0.28ms max=0.33ms | 195 p50=0.05ms p99=0.27ms max=0.38ms | 197 p50=0.06ms p99=0.27ms max=0.38ms | 194 p50=0.06ms p99=0.21ms max=0.25ms
pump [state]: 64 p50=0.08ms p99=0.23ms max=0.25ms | 64 p50=0.05ms p99=0.21ms max=0.24ms | 64 p50=0.05ms p99=0.24ms max=0.24ms | 65 p50=0.05ms p99=0.20ms max=0.23ms | 66 p50=0.06ms p99=0.18ms max=0.19ms
pump [hang-idle]: 1280 p50=0.05ms p99=0.26ms max=3.01ms | 1281 p50=0.05ms p99=0.25ms max=0.34ms | 1280 p50=0.05ms p99=0.25ms max=0.54ms | 1275 p50=0.05ms p99=0.24ms max=0.55ms | 1281 p50=0.05ms p99=0.25ms max=0.47ms
pump [stop]: 7 p50=0.04ms p99=0.13ms max=0.13ms | 7 p50=0.04ms p99=0.23ms max=0.23ms | 7 p50=0.04ms p99=0.20ms max=0.20ms | 8 p50=0.07ms p99=0.17ms max=0.17ms | 6 p50=0.05ms p99=0.16ms max=0.16ms
pump [reload]: 2944 p50=0.06ms p99=0.28ms max=0.53ms | 2939 p50=0.06ms p99=0.27ms max=0.47ms | 2937 p50=0.05ms p99=0.27ms max=0.45ms | 2938 p50=0.06ms p99=0.27ms max=0.39ms | 2939 p50=0.06ms p99=0.26ms max=0.40ms
simulated (interval s x misses, from 1 s probes): 1x3: 3976 (3950-3990, n=5); 1x5: 5976 (5963-5991, n=5); 2x3: 7972 (7964-7985, n=5); 2x5: 11971 (11953-11983, n=5); 3x3: 10972 (10954-10981, n=5); 5x2: 10972 (10954-10981, n=5); 5x3: 15978 (15955-15986, n=5); 5x4: 20976 (20952-20984, n=5); 10x2: 25976 (25955-25984, n=5); 10x3: 35974 (35953-35987, n=5)
events:
  stop-reload-1.log 323 (hang+-3999) NAV-COMPLETED id=2 success=True status=0
  stop-reload-1.log 54442 (hang+50120) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[renderer:23792 gpu:46104 browser:51412 utility:60364 utility:73876]
  stop-reload-2.log 306 (hang+-4001) NAV-COMPLETED id=2 success=True status=0
  stop-reload-2.log 54418 (hang+50111) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:15144 renderer:33212 utility:35412 utility:47456 gpu:71036]
  stop-reload-3.log 314 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  stop-reload-3.log 54429 (hang+50113) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[gpu:24000 utility:26016 renderer:59200 utility:63188 browser:75224]
  stop-reload-4.log 287 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  stop-reload-4.log 54406 (hang+50111) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[renderer:44128 browser:49140 utility:52836 utility:64780 gpu:65248]
  stop-reload-5.log 284 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  stop-reload-5.log 54395 (hang+50104) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:19532 utility:33088 renderer:52720 utility:64700 gpu:73032]

## stress-healthy (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.5 max 0.5 (n=9)
probe rtt ms [memory:before]: median 0.2 p99 0.3 max 0.3 (n=9)
probe rtt ms [stress-on:1000/1024]: median 0.2 p99 0.4 max 0.5 (n=90)
probe rtt ms [stress-off]: median 0.2 p99 0.2 max 0.2 (n=3)
probe rtt ms [memory:after]: median 0.2 p99 9.5 max 9.5 (n=24)
probe rtt ms [memory:later]: median 0.2 p99 0.2 max 0.2 (n=3)
timer tick ms [start]: median 1000 p99 1008 max 1008 (n=5); rAF/s median 60 min 60; vis visible
timer tick ms [memory:before]: median 1000 p99 1010 max 1010 (n=8); rAF/s median 60 min 60; vis visible
timer tick ms [stress-on:1000/1024]: median 1000 p99 1013 max 1013 (n=91); rAF/s median 60 min 59; vis visible
timer tick ms [stress-off]: median 1000 p99 1001 max 1001 (n=3); rAF/s median 60 min 59; vis visible
timer tick ms [memory:after]: median 1000 p99 1005 max 1005 (n=24); rAF/s median 60 min 59; vis visible
timer tick ms [memory:later]: median 1000 p99 1008 max 1008 (n=4); rAF/s median 60 min 60; vis visible
pump [start]: 194 p50=0.06ms p99=0.31ms max=0.36ms | 196 p50=0.07ms p99=0.27ms max=0.31ms | 195 p50=0.06ms p99=0.25ms max=0.42ms
pump [memory-before]: 193 p50=0.06ms p99=0.32ms max=0.35ms | 193 p50=0.05ms p99=0.25ms max=0.31ms | 191 p50=0.06ms p99=0.26ms max=0.30ms
pump [stress-on-1000/1024]: 1919 p50=0.08ms p99=0.34ms max=0.49ms | 1925 p50=0.08ms p99=0.33ms max=0.52ms | 1920 p50=0.08ms p99=0.34ms max=0.72ms
pump [stress-off]: 64 p50=0.05ms p99=0.26ms max=0.33ms | 64 p50=0.06ms p99=0.24ms max=0.25ms | 63 p50=0.05ms p99=0.23ms max=0.26ms
pump [memory-after]: 513 p50=0.05ms p99=0.25ms max=9.54ms | 511 p50=0.06ms p99=0.24ms max=0.34ms | 513 p50=0.05ms p99=0.24ms max=0.31ms
pump [memory-later]: 64 p50=0.05ms p99=0.20ms max=0.27ms | 65 p50=0.06ms p99=0.28ms max=0.29ms | 65 p50=0.06ms p99=0.22ms max=0.41ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  stress-healthy-1.log 287 NAV-COMPLETED id=2 success=True status=0
  stress-healthy-1.log 3288 MEMORY label=before hostKb=4300 gpu:20028 utility:8508 utility:4264 browser:31188 renderer:14456
  stress-healthy-1.log 7835 MEMORY label=stress hostKb=4480 gpu:20060 utility:8508 utility:4264 browser:31244 renderer:19148
  stress-healthy-1.log 9397 MEMORY label=stress hostKb=4536 gpu:20060 utility:8520 utility:4264 browser:31244 renderer:22240
  stress-healthy-1.log 10960 MEMORY label=stress hostKb=4656 gpu:20060 utility:8524 utility:4264 browser:31340 renderer:26968
  stress-healthy-1.log 12530 MEMORY label=stress hostKb=4704 gpu:20060 utility:8520 utility:4264 browser:31308 renderer:28860
  stress-healthy-1.log 14092 MEMORY label=stress hostKb=4760 gpu:20060 utility:8520 utility:4264 browser:31308 renderer:31916
  stress-healthy-1.log 15650 MEMORY label=stress hostKb=4840 gpu:20060 utility:8520 utility:4264 browser:31312 renderer:39164
  stress-healthy-1.log 17220 MEMORY label=stress hostKb=4896 gpu:20060 utility:8520 utility:4264 browser:31312 renderer:41096
  stress-healthy-1.log 18789 MEMORY label=stress hostKb=4948 gpu:20060 utility:8520 utility:4264 browser:31312 renderer:42968
  stress-healthy-1.log 20356 MEMORY label=stress hostKb=5020 gpu:20060 utility:8520 utility:4264 browser:31344 renderer:45768
  stress-healthy-1.log 21919 MEMORY label=stress hostKb=5080 gpu:20064 utility:8520 utility:4264 browser:31344 renderer:48800
  stress-healthy-1.log 23477 MEMORY label=stress hostKb=5160 gpu:20164 utility:8644 utility:4456 browser:32452 renderer:52164
  stress-healthy-1.log 25040 MEMORY label=stress hostKb=5196 gpu:20168 utility:8644 utility:4456 browser:32452 renderer:64712
  stress-healthy-1.log 26604 MEMORY label=stress hostKb=5264 gpu:20168 utility:8644 utility:4456 browser:32452 renderer:66388
  stress-healthy-1.log 28164 MEMORY label=stress hostKb=5316 gpu:20168 utility:8644 utility:4456 browser:32452 renderer:68324
  stress-healthy-1.log 29732 MEMORY label=stress hostKb=5380 gpu:20168 utility:8644 utility:4456 browser:32452 renderer:70196
  stress-healthy-1.log 31295 MEMORY label=stress hostKb=5448 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:21220
  stress-healthy-1.log 32858 MEMORY label=stress hostKb=5500 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:25220
  stress-healthy-1.log 34423 MEMORY label=stress hostKb=5556 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:30276
  stress-healthy-1.log 35972 MEMORY label=stress hostKb=5612 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:31948
  stress-healthy-1.log 36299 STRESS-END sent=19200 meanPostUs=11.4 maxPostUs=177
  stress-healthy-1.log 37286 MEMORY label=after hostKb=5660 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:32340
  stress-healthy-1.log 45289 MEMORY label=later hostKb=5804 gpu:20176 utility:8560 utility:4456 browser:32356 renderer:32464
  stress-healthy-2.log 311 NAV-COMPLETED id=2 success=True status=0
  stress-healthy-2.log 3316 MEMORY label=before hostKb=4680 renderer:11684 gpu:20004 utility:8716 browser:30688 utility:4264
  stress-healthy-2.log 7864 MEMORY label=stress hostKb=4876 renderer:16404 gpu:20044 utility:8716 browser:30748 utility:4264
  stress-healthy-2.log 9423 MEMORY label=stress hostKb=4928 renderer:19504 gpu:20044 utility:8800 browser:30748 utility:4264
  stress-healthy-2.log 10985 MEMORY label=stress hostKb=5072 renderer:24116 gpu:20044 utility:8808 browser:30800 utility:4264
  stress-healthy-2.log 12536 MEMORY label=stress hostKb=5128 renderer:26016 gpu:20044 utility:8804 browser:30800 utility:4264
  stress-healthy-2.log 14092 MEMORY label=stress hostKb=5184 renderer:29072 gpu:20044 utility:8804 browser:30800 utility:4264
  stress-healthy-2.log 15635 MEMORY label=stress hostKb=5264 renderer:36420 gpu:20044 utility:8804 browser:30804 utility:4264
  stress-healthy-2.log 17188 MEMORY label=stress hostKb=5316 renderer:38352 gpu:20044 utility:8804 browser:30804 utility:4264
  stress-healthy-2.log 18747 MEMORY label=stress hostKb=5372 renderer:40224 gpu:20044 utility:8804 browser:30804 utility:4264
  stress-healthy-2.log 20307 MEMORY label=stress hostKb=5436 renderer:43084 gpu:20044 utility:8804 browser:30808 utility:4264
  stress-healthy-2.log 21870 MEMORY label=stress hostKb=5492 renderer:46120 gpu:20048 utility:8804 browser:30804 utility:4264
  stress-healthy-2.log 23422 MEMORY label=stress hostKb=5568 renderer:52676 gpu:20048 utility:8804 browser:30872 utility:4264
  stress-healthy-2.log 24981 MEMORY label=stress hostKb=5620 renderer:61652 gpu:20048 utility:8804 browser:30824 utility:4264
  stress-healthy-2.log 26553 MEMORY label=stress hostKb=5688 renderer:63392 gpu:20048 utility:8804 browser:30824 utility:4264
  stress-healthy-2.log 28110 MEMORY label=stress hostKb=5740 renderer:65272 gpu:20048 utility:8804 browser:30824 utility:4264
  stress-healthy-2.log 29673 MEMORY label=stress hostKb=5800 renderer:67144 gpu:20048 utility:8804 browser:30824 utility:4264
  stress-healthy-2.log 31234 MEMORY label=stress hostKb=5860 renderer:20800 gpu:20040 utility:8676 browser:30752 utility:4264
  stress-healthy-2.log 32792 MEMORY label=stress hostKb=5920 renderer:25060 gpu:20040 utility:8676 browser:30752 utility:4264
  stress-healthy-2.log 34351 MEMORY label=stress hostKb=5968 renderer:27532 gpu:20040 utility:8676 browser:30752 utility:4264
  stress-healthy-2.log 35912 MEMORY label=stress hostKb=6032 renderer:29308 gpu:20040 utility:8676 browser:30752 utility:4264
  stress-healthy-2.log 36320 STRESS-END sent=19250 meanPostUs=11.3 maxPostUs=230
  stress-healthy-2.log 37318 MEMORY label=after hostKb=6068 renderer:29976 gpu:20040 utility:8676 browser:30752 utility:4264
  stress-healthy-2.log 45306 MEMORY label=later hostKb=6228 renderer:30156 gpu:20040 utility:8676 browser:30756 utility:4264
  stress-healthy-3.log 295 NAV-COMPLETED id=2 success=True status=0
  stress-healthy-3.log 3302 MEMORY label=before hostKb=4348 utility:4264 utility:8712 gpu:19988 browser:30700 renderer:11684
  stress-healthy-3.log 7849 MEMORY label=stress hostKb=4496 utility:4264 utility:8712 gpu:20060 browser:30752 renderer:16376
  stress-healthy-3.log 9405 MEMORY label=stress hostKb=4556 utility:4264 utility:8712 gpu:20060 browser:30752 renderer:19468
  stress-healthy-3.log 10967 MEMORY label=stress hostKb=4656 utility:4264 utility:8724 gpu:20060 browser:30808 renderer:24160
  stress-healthy-3.log 12535 MEMORY label=stress hostKb=4716 utility:4264 utility:8716 gpu:20060 browser:30800 renderer:26044
  stress-healthy-3.log 14110 MEMORY label=stress hostKb=4772 utility:4264 utility:8716 gpu:20060 browser:30800 renderer:29116
  stress-healthy-3.log 15671 MEMORY label=stress hostKb=4888 utility:4264 utility:8716 gpu:20064 browser:30804 renderer:36416
  stress-healthy-3.log 17229 MEMORY label=stress hostKb=4940 utility:4264 utility:8716 gpu:20064 browser:30804 renderer:38352
  stress-healthy-3.log 18794 MEMORY label=stress hostKb=5000 utility:4264 utility:8716 gpu:20064 browser:30804 renderer:40224
  stress-healthy-3.log 20363 MEMORY label=stress hostKb=5064 utility:4264 utility:8716 gpu:20064 browser:30828 renderer:43076
  stress-healthy-3.log 21921 MEMORY label=stress hostKb=5116 utility:4264 utility:8716 gpu:20064 browser:30820 renderer:46128
  ... 12 more

## stress-hung (3 runs)
detection ms (hang start -> unresponsive): 15986 (15962-15989, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=9)
probe rtt ms [memory:before]: median 0.2 p99 0.2 max 0.2 (n=3)
timer tick ms [start]: median 998 p99 1000 max 1000 (n=4); rAF/s median 60 min 60; vis visible
timer tick ms [memory:before]: median 1003 p99 1006 max 1006 (n=3); rAF/s median 60 min 60; vis visible
timer tick ms [hang-idle]: median 1002 p99 1002 max 1002 (n=2); rAF/s median 60 min 60; vis visible
pump [start]: 196 p50=0.06ms p99=0.24ms max=0.33ms | 195 p50=0.05ms p99=0.28ms max=0.38ms | 195 p50=0.06ms p99=0.33ms max=0.36ms
pump [memory-before]: 64 p50=0.05ms p99=0.19ms max=0.22ms | 64 p50=0.05ms p99=0.25ms max=0.34ms | 65 p50=0.07ms p99=0.35ms max=0.42ms
pump [hang-idle]: 129 p50=0.05ms p99=0.24ms max=0.40ms | 128 p50=0.06ms p99=0.27ms max=0.44ms | 128 p50=0.05ms p99=0.35ms max=0.37ms
pump [stress-on-1000/1024]: 1919 p50=0.10ms p99=0.37ms max=0.54ms | 1927 p50=0.10ms p99=0.36ms max=0.53ms | 1921 p50=0.10ms p99=0.38ms max=0.51ms
pump [stress-off]: 63 p50=0.07ms p99=0.21ms max=0.23ms | 64 p50=0.06ms p99=0.26ms max=0.42ms | 64 p50=0.06ms p99=0.19ms max=0.20ms
pump [memory-after]: 512 p50=0.05ms p99=0.23ms max=0.30ms | 514 p50=0.06ms p99=0.28ms max=0.38ms | 515 p50=0.05ms p99=0.28ms max=0.36ms
pump [memory-later]: 65 p50=0.06ms p99=0.19ms max=0.23ms | 64 p50=0.06ms p99=0.26ms max=0.32ms | 64 p50=0.06ms p99=0.22ms max=0.28ms
simulated (interval s x misses, from 1 s probes): 1x3: 3979 (3965-3980, n=3); 1x5: 5979 (5960-5980, n=3); 2x3: 7987 (7960-7989, n=3); 2x5: 11979 (11957-11986, n=3); 3x3: 10977 (10954-10979, n=3); 5x2: 10977 (10954-10979, n=3); 5x3: 15986 (15962-15990, n=3); 5x4: 20977 (20954-20988, n=3); 10x2: 25987 (25961-25990, n=3); 10x3: 35983 (35959-35984, n=3)
events:
  stress-hung-1.log 292 (hang+-4007) NAV-COMPLETED id=2 success=True status=0
  stress-hung-1.log 3294 (hang+-1006) MEMORY label=before hostKb=4564 utility:4268 gpu:19960 browser:30632 utility:8752 renderer:11680
  stress-hung-1.log 7844 (hang+3544) MEMORY label=stress hostKb=4752 utility:4268 gpu:20016 browser:30688 utility:8752 renderer:13736
  stress-hung-1.log 9403 (hang+5104) MEMORY label=stress hostKb=4808 utility:4268 gpu:20016 browser:30688 utility:8780 renderer:15384
  stress-hung-1.log 10961 (hang+6661) MEMORY label=stress hostKb=4908 utility:4268 gpu:20016 browser:30760 utility:8784 renderer:16992
  stress-hung-1.log 12524 (hang+8224) MEMORY label=stress hostKb=4956 utility:4268 gpu:20016 browser:30752 utility:8780 renderer:18408
  stress-hung-1.log 14086 (hang+9786) MEMORY label=stress hostKb=5004 utility:4268 gpu:20016 browser:30752 utility:8780 renderer:20056
  stress-hung-1.log 15643 (hang+11344) MEMORY label=stress hostKb=5068 utility:4268 gpu:20016 browser:30752 utility:8780 renderer:21540
  stress-hung-1.log 17208 (hang+12908) MEMORY label=stress hostKb=5104 utility:4268 gpu:20016 browser:30752 utility:8780 renderer:23016
  stress-hung-1.log 18777 (hang+14477) MEMORY label=stress hostKb=5156 utility:4268 gpu:20016 browser:30752 utility:8780 renderer:24496
  stress-hung-1.log 20339 (hang+16039) MEMORY label=stress hostKb=5208 utility:4268 gpu:20020 browser:30760 utility:8780 renderer:26332
  stress-hung-1.log 21897 (hang+17598) MEMORY label=stress hostKb=5256 utility:4268 gpu:20016 browser:30756 utility:8780 renderer:27820
  stress-hung-1.log 23458 (hang+19158) MEMORY label=stress hostKb=5308 utility:4268 gpu:20016 browser:30780 utility:8780 renderer:29292
  stress-hung-1.log 25027 (hang+20728) MEMORY label=stress hostKb=5364 utility:4268 gpu:20016 browser:30780 utility:8780 renderer:30772
  stress-hung-1.log 26611 (hang+22311) MEMORY label=stress hostKb=5420 utility:4268 gpu:20016 browser:30780 utility:8780 renderer:32184
  stress-hung-1.log 28170 (hang+23871) MEMORY label=stress hostKb=5464 utility:4268 gpu:20016 browser:30780 utility:8780 renderer:33668
  stress-hung-1.log 29729 (hang+25429) MEMORY label=stress hostKb=5528 utility:4268 gpu:20016 browser:30780 utility:8780 renderer:35136
  stress-hung-1.log 31292 (hang+26993) MEMORY label=stress hostKb=5568 utility:4268 gpu:20032 browser:30732 utility:8688 renderer:36552
  stress-hung-1.log 32859 (hang+28559) MEMORY label=stress hostKb=5616 utility:4268 gpu:20032 browser:30732 utility:8688 renderer:38836
  stress-hung-1.log 34426 (hang+30127) MEMORY label=stress hostKb=5656 utility:4268 gpu:20032 browser:30732 utility:8688 renderer:40336
  stress-hung-1.log 35994 (hang+31695) MEMORY label=stress hostKb=5708 utility:4268 gpu:20032 browser:30732 utility:8688 renderer:41804
  stress-hung-1.log 36308 (hang+32008) STRESS-END sent=19190 meanPostUs=11.1 maxPostUs=145
  stress-hung-1.log 37293 (hang+32994) MEMORY label=after hostKb=5736 utility:4268 gpu:20036 browser:30732 utility:8688 renderer:42080
  stress-hung-1.log 45293 (hang+40994) MEMORY label=later hostKb=5828 utility:4268 gpu:20036 browser:30732 utility:8688 renderer:42084
  stress-hung-2.log 277 (hang+-3998) NAV-COMPLETED id=2 success=True status=0
  stress-hung-2.log 3278 (hang+-997) MEMORY label=before hostKb=4180 gpu:20080 utility:4264 renderer:11684 utility:8672 browser:30736
  stress-hung-2.log 7817 (hang+3542) MEMORY label=stress hostKb=4348 gpu:20128 utility:4264 renderer:13740 utility:8672 browser:30796
  stress-hung-2.log 9382 (hang+5107) MEMORY label=stress hostKb=4396 gpu:20128 utility:4264 renderer:15388 utility:8676 browser:30796
  stress-hung-2.log 10940 (hang+6665) MEMORY label=stress hostKb=4512 gpu:20132 utility:4264 renderer:17000 utility:8676 browser:30856
  stress-hung-2.log 12499 (hang+8224) MEMORY label=stress hostKb=4560 gpu:20132 utility:4264 renderer:18408 utility:8676 browser:30852
  stress-hung-2.log 14064 (hang+9789) MEMORY label=stress hostKb=4596 gpu:20132 utility:4264 renderer:20060 utility:8676 browser:30852
  stress-hung-2.log 15626 (hang+11351) MEMORY label=stress hostKb=4672 gpu:20132 utility:4264 renderer:21544 utility:8676 browser:30856
  stress-hung-2.log 17182 (hang+12907) MEMORY label=stress hostKb=4712 gpu:20132 utility:4264 renderer:23020 utility:8676 browser:30856
  stress-hung-2.log 18745 (hang+14470) MEMORY label=stress hostKb=4764 gpu:20132 utility:4264 renderer:24500 utility:8676 browser:30856
  stress-hung-2.log 20296 (hang+16021) MEMORY label=stress hostKb=4816 gpu:20132 utility:4264 renderer:26340 utility:8676 browser:30860
  stress-hung-2.log 21848 (hang+17573) MEMORY label=stress hostKb=4860 gpu:20132 utility:4264 renderer:27824 utility:8676 browser:30860
  stress-hung-2.log 23412 (hang+19138) MEMORY label=stress hostKb=4924 gpu:20132 utility:4264 renderer:29296 utility:8676 browser:30880
  stress-hung-2.log 24969 (hang+20695) MEMORY label=stress hostKb=4980 gpu:20132 utility:4264 renderer:30776 utility:8676 browser:30880
  stress-hung-2.log 26528 (hang+22253) MEMORY label=stress hostKb=5028 gpu:20132 utility:4264 renderer:32188 utility:8676 browser:30880
  stress-hung-2.log 28082 (hang+23807) MEMORY label=stress hostKb=5072 gpu:20132 utility:4264 renderer:33672 utility:8676 browser:30880
  stress-hung-2.log 29639 (hang+25364) MEMORY label=stress hostKb=5124 gpu:20132 utility:4264 renderer:35140 utility:8676 browser:30880
  stress-hung-2.log 31200 (hang+26925) MEMORY label=stress hostKb=5168 gpu:20136 utility:4264 renderer:36552 utility:8676 browser:30800
  stress-hung-2.log 32754 (hang+28479) MEMORY label=stress hostKb=5224 gpu:20136 utility:4264 renderer:38840 utility:8588 browser:30800
  stress-hung-2.log 34311 (hang+30037) MEMORY label=stress hostKb=5264 gpu:20140 utility:4264 renderer:40340 utility:8588 browser:30800
  stress-hung-2.log 35870 (hang+31596) MEMORY label=stress hostKb=5308 gpu:20140 utility:4264 renderer:41808 utility:8588 browser:30800
  stress-hung-2.log 36278 (hang+32003) STRESS-END sent=19250 meanPostUs=12.4 maxPostUs=182
  stress-hung-2.log 37274 (hang+32999) MEMORY label=after hostKb=5344 gpu:20140 utility:4264 renderer:42168 utility:8588 browser:30800
  stress-hung-2.log 45284 (hang+41009) MEMORY label=later hostKb=5436 gpu:20140 utility:4264 renderer:42168 utility:8588 browser:30804
  stress-hung-3.log 300 (hang+-3997) NAV-COMPLETED id=2 success=True status=0
  stress-hung-3.log 3296 (hang+-1001) MEMORY label=before hostKb=4568 browser:30724 renderer:14432 gpu:20224 utility:4276 utility:8476
  stress-hung-3.log 7839 (hang+3542) MEMORY label=stress hostKb=4756 browser:30780 renderer:16492 gpu:20260 utility:4276 utility:8476
  stress-hung-3.log 9404 (hang+5106) MEMORY label=stress hostKb=4812 browser:30780 renderer:18140 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 10965 (hang+6667) MEMORY label=stress hostKb=4908 browser:30836 renderer:19748 gpu:20260 utility:4276 utility:8512
  stress-hung-3.log 12532 (hang+8235) MEMORY label=stress hostKb=4948 browser:30832 renderer:21156 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 14103 (hang+9806) MEMORY label=stress hostKb=4984 browser:30832 renderer:22812 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 15663 (hang+11366) MEMORY label=stress hostKb=5060 browser:30836 renderer:24296 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 17230 (hang+12933) MEMORY label=stress hostKb=5100 browser:30836 renderer:25772 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 18788 (hang+14490) MEMORY label=stress hostKb=5152 browser:30836 renderer:27252 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 20346 (hang+16049) MEMORY label=stress hostKb=5204 browser:30856 renderer:29088 gpu:20260 utility:4276 utility:8504
  stress-hung-3.log 21903 (hang+17605) MEMORY label=stress hostKb=5248 browser:30852 renderer:30572 gpu:20268 utility:4276 utility:8504
  ... 12 more

## tasks100 (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.3 max 0.3 (n=6)
probe rtt ms [load:tasks100]: median 50.0 p99 99.1 max 100.0 (n=172)
timer tick ms [start]: median 987 p99 988 max 988 (n=2); rAF/s median 60 min 60; vis visible
timer tick ms [load:tasks100]: median 1046 p99 1051 max 1053 (n=172); rAF/s median 22 min 14; vis visible
pump [start]: 131 p50=0.07ms p99=0.25ms max=0.28ms | 133 p50=0.07ms p99=0.33ms max=0.34ms | 130 p50=0.07ms p99=0.36ms max=0.75ms
pump [load-tasks100]: 3702 p50=0.05ms p99=0.26ms max=0.44ms | 3696 p50=0.05ms p99=0.26ms max=0.42ms | 3688 p50=0.06ms p99=0.27ms max=0.41ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  tasks100-1.log 299 NAV-COMPLETED id=2 success=True status=0
  tasks100-2.log 376 NAV-COMPLETED id=2 success=True status=0
  tasks100-3.log 279 NAV-COMPLETED id=2 success=True status=0

## tasks1000 (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=6)
probe rtt ms [load:tasks1000]: median 167.4 p99 1037.4 max 1042.4 (n=171)
timer tick ms [start]: median 1002 p99 1006 max 1006 (n=3); rAF/s median 61 min 59; vis visible
timer tick ms [load:tasks1000]: median 1005 p99 2007 max 2010 (n=161); rAF/s median 2 min 1; vis visible
pump [start]: 130 p50=0.05ms p99=0.21ms max=0.23ms | 132 p50=0.07ms p99=0.19ms max=0.21ms | 133 p50=0.06ms p99=0.25ms max=1.28ms
pump [load-tasks1000]: 3691 p50=0.06ms p99=0.27ms max=0.42ms | 3716 p50=0.06ms p99=0.27ms max=0.57ms | 3717 p50=0.06ms p99=0.27ms max=8.55ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=3; 1x5: no detect alarms=3; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  tasks1000-1.log 302 NAV-COMPLETED id=2 success=True status=0
  tasks1000-2.log 286 NAV-COMPLETED id=2 success=True status=0
  tasks1000-3.log 285 NAV-COMPLETED id=2 success=True status=0

## worker (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.2 p99 8.7 max 8.7 (n=30)
probe rtt ms [hang-worker]: median 0.2 p99 0.3 max 0.4 (n=150)
timer tick ms [start]: median 1000 p99 1011 max 1011 (n=25); rAF/s median 60 min 59; vis visible
timer tick ms [hang-worker]: median 1000 p99 1009 max 1010 (n=150); rAF/s median 60 min 59; vis visible
pump [start]: 350 p50=0.06ms p99=0.24ms max=0.33ms | 480 p50=0.05ms p99=0.28ms max=0.80ms | 361 p50=0.06ms p99=0.24ms max=0.34ms | 392 p50=0.06ms p99=0.24ms max=0.40ms | 506 p50=0.05ms p99=0.27ms max=0.45ms
pump [hang-worker]: 1925 p50=0.06ms p99=0.27ms max=0.60ms | 1923 p50=0.06ms p99=0.25ms max=0.37ms | 1920 p50=0.05ms p99=0.27ms max=0.37ms | 1917 p50=0.05ms p99=0.25ms max=0.40ms | 1923 p50=0.05ms p99=0.26ms max=0.45ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  worker-1.log 290 (hang+-5414) NAV-COMPLETED id=2 success=True status=0
  worker-2.log 284 (hang+-7424) NAV-COMPLETED id=2 success=True status=0
  worker-3.log 277 (hang+-5568) NAV-COMPLETED id=2 success=True status=0
  worker-4.log 305 (hang+-6058) NAV-COMPLETED id=2 success=True status=0
  worker-5.log 278 (hang+-7834) NAV-COMPLETED id=2 success=True status=0

## xsite (5 runs)
detection ms (hang start -> unresponsive): 15974 (15971-15980, n=5); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 5008 (5003-5015, n=5)
recovery nav-https://other.localhost/index.html RECOVERY-DOC: 39 (38-61, n=5)
recovery nav-https://other.localhost/index.html RECOVERY-READY: 41 (40-64, n=5)
recovery nav-https://other.localhost/index.html RECOVERY-PONG: 990 (980-998, n=5)
recovery nav-index.html RECOVERY-DOC: 57 (56-60, n=5)
recovery nav-index.html RECOVERY-READY: 60 (58-62, n=5)
recovery nav-index.html RECOVERY-PONG: 979 (968-993, n=5)
recovery from ACTION nav:https://other.localhost/index.html to DOC: 39 (38-61, n=5)
recovery from ACTION nav:https://other.localhost/index.html to READY: 41 (40-64, n=5)
recovery backend counter seen after recovery: 4 (3-4, n=10)
recovery from ACTION nav:/index.html to DOC: 58 (56-60, n=5)
recovery from ACTION nav:/index.html to READY: 60 (58-62, n=5)
probe rtt ms [start]: median 0.2 p99 0.2 max 0.2 (n=14)
probe rtt ms [state]: median 0.2 p99 0.2 max 0.2 (n=6)
probe rtt ms [nav:https://other.localhost/index.html]: median 0.2 p99 9.2 max 9.2 (n=50)
probe rtt ms [nav:/index.html]: median 0.2 p99 8.8 max 8.8 (n=80)
timer tick ms [start]: median 1000 p99 1008 max 1008 (n=9); rAF/s median 60 min 60; vis visible
timer tick ms [state]: median 1000 p99 1001 max 1001 (n=6); rAF/s median 60 min 59; vis visible
timer tick ms [nav:https://other.localhost/index.html]: median 1000 p99 1014 max 1014 (n=40); rAF/s median 60 min 59; vis visible
timer tick ms [nav:/index.html]: median 1000 p99 1009 max 1012 (n=74); rAF/s median 60 min 59; vis visible
pump [start]: 196 p50=0.06ms p99=0.30ms max=0.31ms | 195 p50=0.05ms p99=0.22ms max=0.29ms | 195 p50=0.06ms p99=0.23ms max=0.33ms | 196 p50=0.06ms p99=0.28ms max=0.57ms | 196 p50=0.07ms p99=0.30ms max=0.49ms
pump [state]: 64 p50=0.06ms p99=0.22ms max=0.25ms | 63 p50=0.06ms p99=0.18ms max=0.22ms | 65 p50=0.04ms p99=0.17ms max=0.18ms | 63 p50=0.06ms p99=0.28ms max=0.28ms | 64 p50=0.07ms p99=0.21ms max=0.23ms
pump [hang-idle]: 1281 p50=0.06ms p99=0.25ms max=0.32ms | 1283 p50=0.05ms p99=0.26ms max=0.53ms | 1282 p50=0.05ms p99=0.26ms max=0.46ms | 1280 p50=0.05ms p99=0.24ms max=0.46ms | 1285 p50=0.06ms p99=0.26ms max=0.40ms
pump [nav-https://other.localhost/index.html]: 642 p50=0.06ms p99=0.27ms max=0.37ms | 642 p50=0.05ms p99=0.29ms max=0.37ms | 642 p50=0.05ms p99=0.25ms max=9.18ms | 641 p50=0.05ms p99=0.24ms max=0.58ms | 641 p50=0.06ms p99=0.25ms max=0.29ms
pump [nav-/index.html]: 1027 p50=0.05ms p99=0.25ms max=0.32ms | 1030 p50=0.05ms p99=0.24ms max=0.45ms | 1025 p50=0.05ms p99=0.24ms max=0.32ms | 1030 p50=0.06ms p99=0.30ms max=0.84ms | 1028 p50=0.05ms p99=0.25ms max=0.54ms
simulated (interval s x misses, from 1 s probes): 1x3: 3981 (3969-3992, n=5); 1x5: 5977 (5971-5984, n=5); 2x3: 7980 (7973-7985, n=5); 2x5: 11976 (11967-11983, n=5); 3x3: 10976 (10968-10982, n=5); 5x2: 10976 (10968-10982, n=5); 5x3: 15974 (15971-15980, n=5); 5x4: 20981 (20978-20994, n=5); 10x2: no detect; 10x3: no detect
events:
  xsite-1.log 277 (hang+-4004) NAV-COMPLETED id=2 success=True status=0
  xsite-1.log 24328 (hang+20046) NAV-COMPLETED id=3 success=True status=0
  xsite-1.log 34337 (hang+30055) VIS state=hidden pagePerf=10060.0
  xsite-1.log 34349 (hang+30068) NAV-COMPLETED id=4 success=True status=0
  xsite-2.log 300 (hang+-3999) NAV-COMPLETED id=2 success=True status=0
  xsite-2.log 24342 (hang+20043) NAV-COMPLETED id=3 success=True status=0
  xsite-2.log 34352 (hang+30053) VIS state=hidden pagePerf=10051.3
  xsite-2.log 34364 (hang+30065) NAV-COMPLETED id=4 success=True status=0
  xsite-3.log 300 (hang+-4015) NAV-COMPLETED id=2 success=True status=0
  xsite-3.log 24344 (hang+20028) NAV-COMPLETED id=3 success=True status=0
  xsite-3.log 34352 (hang+30036) VIS state=hidden pagePerf=10050.8
  xsite-3.log 34365 (hang+30049) NAV-COMPLETED id=4 success=True status=0
  xsite-4.log 290 (hang+-4008) NAV-COMPLETED id=2 success=True status=0
  xsite-4.log 24339 (hang+20040) NAV-COMPLETED id=3 success=True status=0
  xsite-4.log 34350 (hang+30052) VIS state=hidden pagePerf=10053.1
  xsite-4.log 34363 (hang+30065) NAV-COMPLETED id=4 success=True status=0
  xsite-5.log 332 (hang+-3996) NAV-COMPLETED id=2 success=True status=0
  xsite-5.log 24404 (hang+20077) NAV-COMPLETED id=3 success=True status=0
  xsite-5.log 34377 (hang+30050) VIS state=hidden pagePerf=10041.0
  xsite-5.log 34391 (hang+30064) NAV-COMPLETED id=4 success=True status=0
