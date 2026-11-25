# Summary of results/sandbox/base (9 runs)

## anim (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.3 p99 0.5 max 0.5 (n=6)
probe rtt ms [load:anim]: median 0.4 p99 8.6 max 8.7 (n=174)
timer tick ms [start]: median 999 p99 999 max 999 (n=1); rAF/s median 32 min 32; vis visible
timer tick ms [load:anim]: median 1000 p99 1016 max 1016 (n=174); rAF/s median 32 min 23; vis visible
pump [start]: 134 p50=0.08ms p99=0.28ms max=0.28ms | 133 p50=0.08ms p99=0.55ms max=0.65ms | 135 p50=0.08ms p99=0.56ms max=3.46ms
pump [load-anim]: 3772 p50=0.07ms p99=0.21ms max=0.83ms | 4564 p50=0.07ms p99=0.19ms max=5.79ms | 4301 p50=0.07ms p99=0.21ms max=0.54ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  anim-1.log 2706 NAV-COMPLETED id=2 success=True status=0
  anim-2.log 3001 NAV-COMPLETED id=2 success=True status=0
  anim-3.log 2911 NAV-COMPLETED id=2 success=True status=0

## idle (3 runs)
detection ms (hang start -> unresponsive): 16918 (15204-19002, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.4 p99 2.7 max 2.7 (n=22)
timer tick ms [start]: median 1000 p99 1014 max 1014 (n=19); rAF/s median 32 min 32; vis visible
pump [start]: 524 p50=0.08ms p99=0.21ms max=0.49ms | 389 p50=0.08ms p99=0.26ms max=2.96ms | 637 p50=0.08ms p99=0.25ms max=0.65ms
pump [hang-idle]: 1920 p50=0.08ms p99=0.22ms max=0.52ms | 1920 p50=0.10ms p99=0.24ms max=0.69ms | 1920 p50=0.08ms p99=0.22ms max=1.77ms
simulated (interval s x misses, from 1 s probes): 1x3: 3204 (3002-3918, n=3); 1x5: 5204 (5002-5917, n=3); 2x3: 6204 (6002-7918, n=3); 2x5: 10205 (10002-11918, n=3); 3x3: 9918 (9002-11204, n=3); 5x2: 11918 (10205-14003, n=3); 5x3: 16917 (15205-19003, n=3); 5x4: 21918 (20204-24002, n=3); 10x2: 21918 (20204-24002, n=3); 10x3: no detect
events:
  idle-1.log 2709 (hang+-8054) NAV-COMPLETED id=2 success=True status=0
  idle-2.log 2973 (hang+-5965) NAV-COMPLETED id=2 success=True status=0
  idle-3.log 3228 (hang+-9759) NAV-COMPLETED id=2 success=True status=0

## onscreen (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 6.2 max 10.9 (n=180)
timer tick ms [start]: median 1000 p99 1014 max 1015 (n=176); rAF/s median 32 min 32; vis visible
pump [start]: 3859 p50=0.08ms p99=0.18ms max=2.25ms | 3859 p50=0.07ms p99=0.16ms max=0.62ms | 3860 p50=0.08ms p99=0.18ms max=4.05ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  onscreen-1.log 3035 NAV-COMPLETED id=2 success=True status=0
  onscreen-2.log 2719 NAV-COMPLETED id=2 success=True status=0
  onscreen-3.log 2682 NAV-COMPLETED id=2 success=True status=0
