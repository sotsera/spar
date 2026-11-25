# Summary of results/sandbox/dialog (9 runs)

## dialog-recover (3 runs)
detection ms (hang start -> unresponsive): 15982 (15969-15987, n=3); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 9022 (9015-9033, n=3)
probe rtt ms [start]: median 0.5 p99 6.0 max 6.0 (n=12)
probe rtt ms [busy:25000]: median 6536.7 p99 24028.4 max 24029.1 (n=108)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=7); rAF/s median 32 min 32; vis visible
timer tick ms [busy:25000]: median 1000 p99 25999 max 25999 (n=36); rAF/s median 32 min 2; vis visible
pump [start]: 262 p50=0.08ms p99=0.17ms max=1.14ms | 261 p50=0.06ms p99=0.22ms max=0.33ms | 262 p50=0.06ms p99=0.17ms max=0.35ms
pump [busy-25000]: 1022 p50=0.07ms p99=0.19ms max=4.50ms | 1022 p50=0.07ms p99=0.18ms max=3.08ms | 1019 p50=0.07ms p99=0.16ms max=0.70ms
pump [dialog]: 1281 p50=0.07ms p99=0.20ms max=33.84ms | 1270 p50=0.07ms p99=0.21ms max=198.35ms | 1284 p50=0.08ms p99=0.23ms max=23.73ms
simulated (interval s x misses, from 1 s probes): 1x3: 3982 (3967-3985, n=3); 1x5: 5983 (5967-5984, n=3); 2x3: 7982 (7966-7982, n=3); 2x5: 11982 (11965-11982, n=3); 3x3: 10982 (10977-10982, n=3); 5x2: 10982 (10977-10982, n=3); 5x3: 15982 (15967-15982, n=3); 5x4: 20970 (20968-20983, n=3); 10x2: no detect; 10x3: no detect
events:
  dialog-recover-1.log 2627 (hang+-3992) NAV-COMPLETED id=2 success=True status=0
  dialog-recover-1.log 22609 (hang+15991) DIALOG-SHOW 
  dialog-recover-1.log 31620 (hang+25001) DIALOG-AUTO-CLOSE why=responsive-again
  dialog-recover-1.log 31623 (hang+25005) DIALOG-CLOSED hr=0x00000000 button=wait openMs=9014
  dialog-recover-2.log 5788 (hang+-3990) NAV-COMPLETED id=2 success=True status=0
  dialog-recover-2.log 25763 (hang+15985) DIALOG-SHOW 
  dialog-recover-2.log 34782 (hang+25004) DIALOG-AUTO-CLOSE why=responsive-again
  dialog-recover-2.log 34796 (hang+25018) DIALOG-CLOSED hr=0x00000000 button=wait openMs=9033
  dialog-recover-3.log 5244 (hang+-4005) NAV-COMPLETED id=2 success=True status=0
  dialog-recover-3.log 25221 (hang+15971) DIALOG-SHOW 
  dialog-recover-3.log 34251 (hang+25002) DIALOG-AUTO-CLOSE why=responsive-again
  dialog-recover-3.log 34254 (hang+25005) DIALOG-CLOSED hr=0x00000000 button=wait openMs=9033

## dialog-reload (3 runs)
detection ms (hang start -> unresponsive): 15977 (15976-15979, n=3); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 4995 (4992-4998, n=3)
recovery auto-reload-after-exit RECOVERY-DOC: 91 (79-93, n=3)
recovery auto-reload-after-exit RECOVERY-READY: 95 (88-102, n=3)
recovery auto-reload-after-exit RECOVERY-PONG: 940 (885-963, n=3)
probe rtt ms [start]: median 0.4 p99 0.5 max 0.5 (n=9)
probe rtt ms [state]: median 0.6 p99 0.9 max 0.9 (n=3)
probe rtt ms [hang-idle]: median 0.5 p99 1.6 max 1.6 (n=51)
timer tick ms [start]: median 1007 p99 1008 max 1008 (n=3); rAF/s median 32 min 32; vis visible
timer tick ms [state]: median 996 p99 1010 max 1010 (n=4); rAF/s median 32 min 32; vis visible
timer tick ms [hang-idle]: median 1000 p99 1014 max 1014 (n=47); rAF/s median 32 min 31; vis visible
pump [start]: 198 p50=0.07ms p99=0.22ms max=0.30ms | 198 p50=0.07ms p99=0.39ms max=2.49ms | 198 p50=0.08ms p99=0.24ms max=0.35ms
pump [state]: 64 p50=0.06ms p99=0.14ms max=0.30ms | 65 p50=0.08ms p99=0.13ms max=0.19ms | 65 p50=0.08ms p99=0.12ms max=0.12ms
pump [hang-idle]: 1022 p50=0.07ms p99=0.21ms max=2.20ms | 1022 p50=0.07ms p99=0.20ms max=0.59ms | 1022 p50=0.08ms p99=0.21ms max=0.71ms
pump [dialog]: 1282 p50=0.07ms p99=0.19ms max=71.41ms | 1283 p50=0.06ms p99=0.20ms max=12.63ms | 1286 p50=0.08ms p99=0.20ms max=13.48ms
simulated (interval s x misses, from 1 s probes): 1x3: 3979 (3978-3979, n=3); 1x5: 5978 (5977-5986, n=3); 2x3: 7978 (7978-7978, n=3); 2x5: 11978 (11977-11979, n=3); 3x3: 10978 (10977-10978, n=3); 5x2: 10978 (10977-10978, n=3); 5x3: 15978 (15977-15979, n=3); 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  dialog-reload-1.log 2783 (hang+-3999) NAV-COMPLETED id=2 success=True status=0
  dialog-reload-1.log 22759 (hang+15977) DIALOG-SHOW 
  dialog-reload-1.log 25858 (hang+19076) DIALOG-CLOSED hr=0x00000000 button=reload openMs=3098
  dialog-reload-1.log 25861 (hang+19078) KILL-RENDERER pid=6304 error=0 callUs=210
  dialog-reload-1.log 25867 (hang+19084) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[gpu:2824 utility:3476 utility:4248 browser:4928 renderer:6304]
  dialog-reload-1.log 25961 (hang+19179) VIS state=visible pagePerf=70.5
  dialog-reload-1.log 25970 (hang+19188) NAV-COMPLETED id=3 success=True status=0
  dialog-reload-2.log 2670 (hang+-3994) NAV-COMPLETED id=2 success=True status=0
  dialog-reload-2.log 22644 (hang+15980) DIALOG-SHOW 
  dialog-reload-2.log 25675 (hang+19010) DIALOG-CLOSED hr=0x00000000 button=reload openMs=3031
  dialog-reload-2.log 25676 (hang+19011) KILL-RENDERER pid=5056 error=0 callUs=227
  dialog-reload-2.log 25682 (hang+19017) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[utility:3712 utility:3732 renderer:5056 gpu:5872 browser:7144]
  dialog-reload-2.log 25762 (hang+19097) VIS state=visible pagePerf=66.8
  dialog-reload-2.log 25771 (hang+19106) NAV-COMPLETED id=3 success=True status=0
  dialog-reload-3.log 2675 (hang+-4003) NAV-COMPLETED id=2 success=True status=0
  dialog-reload-3.log 22657 (hang+15979) DIALOG-SHOW 
  dialog-reload-3.log 25697 (hang+19019) DIALOG-CLOSED hr=0x00000000 button=reload openMs=3041
  dialog-reload-3.log 25698 (hang+19020) KILL-RENDERER pid=4664 error=0 callUs=271
  dialog-reload-3.log 25706 (hang+19028) PROCESS-FAILED kind=render-exited reason=terminated exitCode=1 description= processes=[gpu:1756 utility:2780 renderer:4664 utility:6452 browser:6680]
  dialog-reload-3.log 25798 (hang+19120) VIS state=visible pagePerf=77.7
  dialog-reload-3.log 25807 (hang+19129) NAV-COMPLETED id=3 success=True status=0

## dialog-wait (3 runs)
detection ms (hang start -> unresponsive): 15980 (15968-15989, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.4 p99 1.3 max 1.3 (n=12)
timer tick ms [start]: median 1000 p99 1007 max 1007 (n=7); rAF/s median 32 min 32; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=2); rAF/s median 32 min 32; vis visible
pump [start]: 262 p50=0.06ms p99=0.29ms max=0.50ms | 261 p50=0.06ms p99=0.19ms max=0.40ms | 260 p50=0.06ms p99=0.18ms max=0.44ms
pump [hang-idle]: 1024 p50=0.07ms p99=0.20ms max=2.11ms | 1022 p50=0.07ms p99=0.22ms max=2.03ms | 1022 p50=0.07ms p99=0.19ms max=0.98ms
pump [dialog]: 1280 p50=0.08ms p99=0.20ms max=16.20ms | 1280 p50=0.08ms p99=0.20ms max=20.19ms | 1281 p50=0.07ms p99=0.21ms max=10.28ms
simulated (interval s x misses, from 1 s probes): 1x3: 3982 (3969-3985, n=3); 1x5: 5980 (5968-5984, n=3); 2x3: 7980 (7969-7985, n=3); 2x5: 11980 (11968-11985, n=3); 3x3: 10980 (10968-10985, n=3); 5x2: 10980 (10968-10985, n=3); 5x3: 15980 (15968-15988, n=3); 5x4: 20979 (20968-20985, n=3); 10x2: 25980 (25967-25988, n=3); 10x3: 35980 (35968-35986, n=3)
events:
  dialog-wait-1.log 6039 (hang+-3993) NAV-COMPLETED id=2 success=True status=0
  dialog-wait-1.log 26021 (hang+15989) DIALOG-SHOW 
  dialog-wait-1.log 29057 (hang+19025) DIALOG-CLOSED hr=0x00000000 button=wait openMs=3036
  dialog-wait-1.log 42079 (hang+32047) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:3136 gpu:3724 browser:4864 renderer:6396 utility:6948]
  dialog-wait-2.log 4592 (hang+-3993) NAV-COMPLETED id=2 success=True status=0
  dialog-wait-2.log 24565 (hang+15981) DIALOG-SHOW 
  dialog-wait-2.log 27611 (hang+19027) DIALOG-CLOSED hr=0x00000000 button=wait openMs=3046
  dialog-wait-2.log 40611 (hang+32026) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:1908 renderer:2888 gpu:3176 browser:5688 utility:7532]
  dialog-wait-3.log 5085 (hang+-3998) NAV-COMPLETED id=2 success=True status=0
  dialog-wait-3.log 25053 (hang+15969) DIALOG-SHOW 
  dialog-wait-3.log 28088 (hang+19004) DIALOG-CLOSED hr=0x00000000 button=wait openMs=3035
  dialog-wait-3.log 41083 (hang+32000) PROCESS-FAILED kind=render-unresponsive reason=unresponsive exitCode=259 description= processes=[utility:568 gpu:2628 browser:3716 utility:3732 renderer:4796]
