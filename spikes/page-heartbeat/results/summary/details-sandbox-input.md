# Summary of results/sandbox/input (9 runs)

## input-click (3 runs)
detection ms (hang start -> unresponsive): 16979 (16966-16981, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 2.4 max 2.4 (n=9)
timer tick ms [start]: median 1000 p99 1008 max 1008 (n=4); rAF/s median 32 min 32; vis visible
timer tick ms [hang-idle]: median 993 p99 993 max 993 (n=1); rAF/s median 32 min 32; vis visible
pump [start]: 196 p50=0.06ms p99=0.23ms max=0.28ms | 196 p50=0.07ms p99=0.20ms max=0.46ms | 196 p50=0.07ms p99=0.18ms max=0.30ms
pump [hang-idle]: 320 p50=0.07ms p99=0.17ms max=0.25ms | 319 p50=0.07ms p99=0.20ms max=0.32ms | 319 p50=0.07ms p99=0.18ms max=1.67ms
pump [click]: 4286 p50=0.08ms p99=0.20ms max=13.85ms | 4287 p50=0.08ms p99=0.21ms max=17.15ms | 4287 p50=0.07ms p99=0.21ms max=6.03ms
simulated (interval s x misses, from 1 s probes): 1x3: 3981 (3965-3986, n=3); 1x5: 5980 (5974-5987, n=3); 2x3: 6980 (6965-6994, n=3); 2x5: 10981 (10967-10982, n=3); 3x3: 11981 (11966-11982, n=3); 5x2: 11981 (11966-11982, n=3); 5x3: 16979 (16966-16981, n=3); 5x4: 21980 (21967-21982, n=3); 10x2: 26981 (26976-26982, n=3); 10x3: 36981 (36967-36982, n=3)
events:
  input-click-1.log 4196 (hang+-2981) NAV-COMPLETED id=2 success=True status=0
  input-click-1.log 12207 (hang+5030) INPUT kind=click sent=3 at=420,290
  input-click-1.log 28222 (hang+21045) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[renderer:1584 utility:3500 utility:3804 gpu:5024 browser:5820]
  input-click-2.log 2411 (hang+-3003) NAV-COMPLETED id=2 success=True status=0
  input-click-2.log 10444 (hang+5029) INPUT kind=click sent=3 at=420,290
  input-click-2.log 26474 (hang+21059) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[gpu:1952 utility:2080 browser:4772 renderer:6996 utility:7264]
  input-click-3.log 3078 (hang+-3001) NAV-COMPLETED id=2 success=True status=0
  input-click-3.log 11090 (hang+5012) INPUT kind=click sent=3 at=420,290
  input-click-3.log 27112 (hang+21033) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:2396 renderer:4216 utility:4768 gpu:7544 utility:8016]

## input-click-quiet (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
timer tick ms [start]: median 1008 p99 1012 max 1012 (n=4); rAF/s median 32 min 32; vis visible
timer tick ms [hang-idle]: median 1003 p99 1007 max 1007 (n=2); rAF/s median 32 min 32; vis visible
pump [start]: 198 p50=0.07ms p99=0.28ms max=3.17ms | 198 p50=0.07ms p99=0.24ms max=0.32ms | 198 p50=0.07ms p99=0.26ms max=0.34ms
pump [hang-idle]: 319 p50=0.07ms p99=0.16ms max=0.84ms | 319 p50=0.07ms p99=0.16ms max=0.21ms | 319 p50=0.08ms p99=0.20ms max=0.22ms
pump [click]: 4287 p50=0.08ms p99=0.20ms max=18.08ms | 4288 p50=0.08ms p99=0.20ms max=0.70ms | 4287 p50=0.08ms p99=0.19ms max=1.92ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  input-click-quiet-1.log 3441 (hang+-3006) NAV-COMPLETED id=2 success=True status=0
  input-click-quiet-1.log 11474 (hang+5028) INPUT kind=click sent=3 at=420,290
  input-click-quiet-1.log 27505 (hang+21059) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:1964 gpu:3424 browser:3512 utility:4168 renderer:6980]
  input-click-quiet-2.log 2589 (hang+-3005) NAV-COMPLETED id=2 success=True status=0
  input-click-quiet-2.log 10608 (hang+5014) INPUT kind=click sent=3 at=420,290
  input-click-quiet-2.log 26624 (hang+21030) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:1888 gpu:5532 utility:6908 utility:7228 renderer:7500]
  input-click-quiet-3.log 4433 (hang+-3005) NAV-COMPLETED id=2 success=True status=0
  input-click-quiet-3.log 12444 (hang+5006) INPUT kind=click sent=3 at=420,290
  input-click-quiet-3.log 28474 (hang+21036) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[gpu:2148 utility:2656 renderer:5732 utility:7340 browser:7616]

## input-key (3 runs)
detection ms (hang start -> unresponsive): 16986 (16983-16997, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 2.1 max 2.1 (n=8)
timer tick ms [start]: median 1000 p99 1001 max 1001 (n=4); rAF/s median 32 min 32; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 32 min 32; vis visible
pump [start]: 196 p50=0.06ms p99=0.28ms max=0.56ms | 195 p50=0.06ms p99=0.19ms max=0.35ms | 196 p50=0.06ms p99=0.17ms max=0.36ms
pump [hang-idle]: 320 p50=0.07ms p99=0.17ms max=0.24ms | 320 p50=0.07ms p99=0.18ms max=0.81ms | 320 p50=0.07ms p99=0.16ms max=0.25ms
pump [click]: 63 p50=0.06ms p99=0.15ms max=17.04ms | 65 p50=0.06ms p99=0.23ms max=1.21ms | 65 p50=0.06ms p99=0.15ms max=1.99ms
pump [key]: 4222 p50=0.07ms p99=0.19ms max=4.04ms | 4222 p50=0.07ms p99=0.18ms max=4.88ms | 4223 p50=0.08ms p99=0.21ms max=9.43ms
simulated (interval s x misses, from 1 s probes): 1x3: 3983 (2997-3985, n=3); 1x5: 5983 (4998-5987, n=3); 2x3: 6979 (6974-6989, n=3); 2x5: 10992 (10985-10996, n=3); 3x3: 11984 (8998-11985, n=3); 5x2: 11985 (11984-11997, n=3); 5x3: 16987 (16983-16997, n=3); 5x4: 21986 (21982-21997, n=3); 10x2: 26992 (26988-26997, n=3); 10x3: 36984 (36983-36997, n=3)
events:
  input-key-1.log 4195 (hang+-2993) NAV-COMPLETED id=2 success=True status=0
  input-key-1.log 12233 (hang+5045) INPUT kind=click sent=3 at=420,290
  input-key-1.log 13192 (hang+6004) INPUT kind=key sent=2
  input-key-1.log 28264 (hang+21076) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[renderer:2228 utility:2360 utility:2988 browser:3476 gpu:3676]
  input-key-2.log 6972 (hang+-2998) NAV-COMPLETED id=2 success=True status=0
  input-key-2.log 14985 (hang+5015) INPUT kind=click sent=3 at=420,290
  input-key-2.log 15968 (hang+5998) INPUT kind=key sent=2
  input-key-2.log 31014 (hang+21044) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:4132 renderer:4604 gpu:5756 browser:6276 utility:6528]
  input-key-3.log 6981 (hang+-2984) NAV-COMPLETED id=2 success=True status=0
  input-key-3.log 14993 (hang+5028) INPUT kind=click sent=3 at=420,290
  input-key-3.log 15978 (hang+6012) INPUT kind=key sent=2
  input-key-3.log 31022 (hang+21057) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[browser:2400 utility:3720 gpu:6456 renderer:7340 utility:7640]
