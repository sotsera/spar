# Summary of results/linux (147 runs)

## anim (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.8 p99 9.3 max 9.3 (n=6)
probe rtt ms [load:anim]: median 2.6 p99 8.2 max 9.0 (n=174)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 62 min 62; vis visible
timer tick ms [load:anim]: median 1004 p99 1012 max 1013 (n=173); rAF/s median 62 min 62; vis visible
pump [start]: 1803 p50=0.03ms p99=0.11ms max=10.16ms | 1811 p50=0.03ms p99=0.12ms max=9.86ms | 1803 p50=0.03ms p99=0.13ms max=32.46ms
pump [load-anim]: 49220 p50=0.03ms p99=1.11ms max=5.69ms | 49356 p50=0.03ms p99=1.10ms max=8.63ms | 49301 p50=0.03ms p99=1.12ms max=5.13ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## baseline (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.8 p99 9.7 max 14.4 (n=300)
timer tick ms [start]: median 1004 p99 1005 max 1005 (n=290); rAF/s median 62 min 62; vis visible
pump [start]: 52861 p50=0.03ms p99=0.09ms max=20.51ms | 52783 p50=0.03ms p99=0.09ms max=14.26ms | 52691 p50=0.03ms p99=0.09ms max=14.80ms | 52778 p50=0.03ms p99=0.09ms max=13.57ms | 52927 p50=0.03ms p99=0.09ms max=10.40ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## blank (5 runs)
detection ms (hang start -> unresponsive): 15922 (15858-15932, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION nav:about:blank to DOC: - never in 5
recovery from ACTION nav:about:blank to READY: - never in 5
recovery from ACTION nav:/index.html to DOC: - never in 5
recovery from ACTION nav:/index.html to READY: - never in 5
probe rtt ms [start]: median 1.0 p99 11.0 max 11.0 (n=15)
probe rtt ms [state]: median 1.0 p99 2.8 max 2.8 (n=5)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=9); rAF/s median 62 min 62; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 63 min 63; vis visible
timer tick ms [state]: median 1000 p99 1000 max 1000 (n=5); rAF/s median 63 min 62; vis visible
pump [start]: 2688 p50=0.03ms p99=0.10ms max=10.02ms | 2686 p50=0.03ms p99=0.12ms max=37.11ms | 2665 p50=0.03ms p99=0.10ms max=10.48ms | 2675 p50=0.03ms p99=0.10ms max=8.45ms | 2657 p50=0.03ms p99=0.11ms max=18.30ms
pump [state]: 876 p50=0.03ms p99=0.09ms max=2.71ms | 878 p50=0.03ms p99=0.11ms max=1.12ms | 870 p50=0.03ms p99=0.09ms max=6.25ms | 879 p50=0.04ms p99=0.09ms max=0.97ms | 872 p50=0.03ms p99=0.09ms max=0.71ms
pump [hang-idle]: 17707 p50=0.02ms p99=0.08ms max=18.33ms | 17683 p50=0.02ms p99=0.08ms max=19.12ms | 17665 p50=0.03ms p99=0.08ms max=13.12ms | 17746 p50=0.03ms p99=0.09ms max=6.66ms | 17671 p50=0.03ms p99=0.09ms max=2.90ms
pump [nav-about:blank]: 1748 p50=0.03ms p99=0.09ms max=10.95ms | 1740 p50=0.03ms p99=0.08ms max=13.25ms | 1752 p50=0.03ms p99=0.09ms max=3.01ms | 1729 p50=0.03ms p99=0.09ms max=5.41ms | 1735 p50=0.03ms p99=0.10ms max=4.16ms
pump [nav-/index.html]: 16672 p50=0.03ms p99=0.08ms max=14.81ms | 16599 p50=0.03ms p99=0.09ms max=11.36ms | 16500 p50=0.03ms p99=0.09ms max=15.68ms | 16567 p50=0.03ms p99=0.09ms max=15.17ms | 16511 p50=0.03ms p99=0.09ms max=7.96ms
simulated (interval s x misses, from 1 s probes): 1x3: 3923 (3853-3927, n=5); 1x5: 5921 (5852-5925, n=5); 2x3: 7920 (7851-7923, n=5); 2x5: 11921 (11852-11924, n=5); 3x3: 10922 (10852-10924, n=5); 5x2: 10922 (10852-10924, n=5); 5x3: 15924 (15852-15928, n=5); 5x4: 20921 (20853-20926, n=5); 10x2: 25921 (25852-25926, n=5); 10x3: 35921 (35852-35925, n=5)
events:
  blank-1.log 27157 (hang+23000) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=4420
  blank-2.log 27273 (hang+23004) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=4615
  blank-3.log 27164 (hang+22997) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=4810
  blank-4.log 27162 (hang+23006) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=5006
  blank-5.log 27235 (hang+23001) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=5207

## busy10 (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.1 p99 10.1 max 10.1 (n=40)
probe rtt ms [busy:10000]: median 2.3 p99 9697.0 max 9985.8 (n=125)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=34); rAF/s median 62 min 62; vis visible
timer tick ms [busy:10000]: median 1004 p99 10865 max 10984 (n=75); rAF/s median 62 min 23; vis visible
pump [start]: 8358 p50=0.03ms p99=0.10ms max=8.92ms | 7582 p50=0.03ms p99=0.10ms max=8.52ms | 8843 p50=0.03ms p99=0.09ms max=15.55ms | 7817 p50=0.03ms p99=0.10ms max=21.07ms | 4794 p50=0.03ms p99=0.09ms max=10.58ms
pump [busy-10000]: 21957 p50=0.03ms p99=0.10ms max=20.08ms | 21841 p50=0.03ms p99=0.10ms max=8.49ms | 22035 p50=0.03ms p99=0.09ms max=15.97ms | 21865 p50=0.03ms p99=0.10ms max=32.86ms | 21945 p50=0.03ms p99=0.09ms max=8.15ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect; 3x3: no detect alarms=3; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## busy20 (5 runs)
detection ms (hang start -> unresponsive): 17788 (15505-19100, n=5); misses at alarm 3
alarms not caused by an endless hang: busy20-1.log@hang+17372 (busy20), busy20-2.log@hang+19100 (busy20), busy20-3.log@hang+19093 (busy20), busy20-4.log@hang+15505 (busy20), busy20-5.log@hang+17788 (busy20)
responsive again after alarm (ms): 2226 (907-4497, n=5)
probe rtt ms [start]: median 0.8 p99 10.7 max 10.7 (n=33)
probe rtt ms [busy:20000]: median 2654.8 p99 19631.8 max 19906.0 (n=175)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=28); rAF/s median 62 min 62; vis visible
timer tick ms [busy:20000]: median 1004 p99 20806 max 20834 (n=75); rAF/s median 62 min 7; vis visible
pump [start]: 6617 p50=0.03ms p99=0.10ms max=22.71ms | 5156 p50=0.03ms p99=0.09ms max=18.44ms | 5249 p50=0.02ms p99=0.09ms max=7.56ms | 8317 p50=0.03ms p99=0.10ms max=12.27ms | 6305 p50=0.03ms p99=0.10ms max=12.28ms
pump [busy-20000]: 30632 p50=0.03ms p99=0.09ms max=23.89ms | 30659 p50=0.03ms p99=0.09ms max=12.00ms | 30799 p50=0.03ms p99=0.09ms max=14.05ms | 30712 p50=0.03ms p99=0.09ms max=18.60ms | 30665 p50=0.03ms p99=0.09ms max=99.50ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect alarms=5; 3x3: no detect alarms=5; 5x2: no detect alarms=5; 5x3: no detect alarms=5; 5x4: no detect; 10x2: no detect; 10x3: no detect

## busy3 (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 10.2 max 10.2 (n=35)
probe rtt ms [busy:3000]: median 0.8 p99 2312.9 max 2886.8 (n=75)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=30); rAF/s median 62 min 62; vis visible
timer tick ms [busy:3000]: median 1004 p99 3231 max 3790 (n=60); rAF/s median 62 min 6; vis visible
pump [start]: 6458 p50=0.03ms p99=0.09ms max=10.31ms | 7392 p50=0.03ms p99=0.09ms max=7.97ms | 6434 p50=0.03ms p99=0.09ms max=8.07ms | 5417 p50=0.03ms p99=0.10ms max=9.66ms | 6946 p50=0.03ms p99=0.09ms max=10.07ms
pump [busy-3000]: 13284 p50=0.03ms p99=0.09ms max=83.29ms | 13390 p50=0.03ms p99=0.09ms max=1.48ms | 13165 p50=0.03ms p99=0.10ms max=1.42ms | 13271 p50=0.03ms p99=0.09ms max=2.95ms | 13157 p50=0.03ms p99=0.09ms max=6.13ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## gate-minimize-hang (3 runs)
detection ms (hang start -> unresponsive): 54914 (54853-54915, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.0 p99 6.9 max 6.9 (n=15)
probe rtt ms [minimize]: median 0.9 p99 2.7 max 2.7 (n=15)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=12); rAF/s median 62 min 62; vis visible
timer tick ms [minimize]: median 2000 p99 2000 max 2000 (n=7); rAF/s median 0 min 0; vis hidden
pump [start]: 4486 p50=0.02ms p99=0.09ms max=7.46ms | 4415 p50=0.03ms p99=0.10ms max=45.82ms | 4432 p50=0.03ms p99=0.11ms max=8.70ms
pump [minimize]: 4411 p50=0.03ms p99=0.09ms max=2.07ms | 4414 p50=0.03ms p99=0.08ms max=5.60ms | 4401 p50=0.03ms p99=0.10ms max=2.05ms
pump [hang-idle]: 30606 p50=0.03ms p99=0.09ms max=16.87ms | 30888 p50=0.03ms p99=0.09ms max=6.86ms | 30877 p50=0.03ms p99=0.09ms max=13.59ms
pump [restore]: 21853 p50=0.03ms p99=0.08ms max=18.57ms | 21893 p50=0.03ms p99=0.10ms max=4.38ms | 21951 p50=0.03ms p99=0.09ms max=14.55ms
simulated (interval s x misses, from 1 s probes): 1x3: 3912 (3851-3912, n=3); 1x5: 5912 (5851-5915, n=3); 2x3: 7912 (7852-7913, n=3); 2x5: 11913 (11852-11914, n=3); 3x3: 10912 (10852-10913, n=3); 5x2: 14912 (14852-14915, n=3); 5x3: 19911 (19853-19914, n=3); 5x4: 24912 (24852-24913, n=3); 10x2: 29913 (29851-29914, n=3); 10x3: 39915 (39852-39915, n=3)
events:
  gate-minimize-hang-1.log 5170 (hang+-5007) VIS state=hidden pagePerf=5019.0
  gate-minimize-hang-1.log 5380 (hang+-4797) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-1.log 45196 (hang+35019) HB-RESUME hb=ping reason=minimised
  gate-minimize-hang-2.log 5245 (hang+-4999) VIS state=hidden pagePerf=5089.0
  gate-minimize-hang-2.log 5388 (hang+-4857) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-2.log 45454 (hang+35210) HB-RESUME hb=ping reason=minimised
  gate-minimize-hang-3.log 5165 (hang+-4997) VIS state=hidden pagePerf=5030.0
  gate-minimize-hang-3.log 5365 (hang+-4797) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-3.log 45181 (hang+35019) HB-RESUME hb=ping reason=minimised

## handler (5 runs)
detection ms (hang start -> unresponsive): 16368 (15372-19781, n=5); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 2.9 max 2.9 (n=39)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=33); rAF/s median 62 min 62; vis visible
pump [start]: 7950 p50=0.03ms p99=0.10ms max=8.58ms | 7562 p50=0.03ms p99=0.09ms max=8.10ms | 4566 p50=0.03ms p99=0.09ms max=7.87ms | 7178 p50=0.03ms p99=0.10ms max=7.02ms | 8450 p50=0.03ms p99=0.10ms max=8.90ms
pump [hang-handler]: 26700 p50=0.02ms p99=0.08ms max=5.39ms | 26451 p50=0.03ms p99=0.09ms max=4.40ms | 26462 p50=0.03ms p99=0.09ms max=4.51ms | 26343 p50=0.03ms p99=0.10ms max=4.35ms | 26392 p50=0.03ms p99=0.10ms max=3.77ms
simulated (interval s x misses, from 1 s probes): 1x3: 3762 (3369-3929, n=5); 1x5: 5763 (5367-5928, n=5); 2x3: 6928 (6370-7764, n=5); 2x5: 10927 (10370-11763, n=5); 3x3: 9780 (9368-11928, n=5); 5x2: 11367 (10370-14780, n=5); 5x3: 16367 (15370-19780, n=5); 5x4: 21368 (20369-24779, n=5); 10x2: 21368 (20369-24779, n=5); 10x3: no detect

## healthy-kill (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
recovery auto-reload-after-exit RECOVERY-DOC: 70 (69-77, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 77 (76-84, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 915 (877-928, n=5)
recovery from ACTION kill to DOC: 77 (73-89, n=5)
recovery from ACTION kill to READY: 83 (80-96, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.9 p99 5.0 max 5.0 (n=15)
probe rtt ms [state]: median 1.0 p99 4.9 max 4.9 (n=15)
probe rtt ms [kill]: median 1.0 p99 9.3 max 9.3 (n=45)
timer tick ms [start]: median 1000 p99 1001 max 1001 (n=8); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1001 max 1001 (n=14); rAF/s median 62 min 62; vis visible
timer tick ms [kill]: median 1000 p99 1004 max 1004 (n=35); rAF/s median 62 min 62; vis visible
pump [start]: 2659 p50=0.03ms p99=0.15ms max=7.83ms | 2666 p50=0.03ms p99=0.13ms max=7.90ms | 2674 p50=0.03ms p99=0.12ms max=15.87ms | 2677 p50=0.03ms p99=0.10ms max=9.88ms | 2684 p50=0.03ms p99=0.11ms max=8.21ms
pump [state]: 2597 p50=0.03ms p99=0.11ms max=4.16ms | 2621 p50=0.04ms p99=0.10ms max=3.91ms | 2598 p50=0.03ms p99=0.10ms max=4.97ms | 2619 p50=0.03ms p99=0.10ms max=2.59ms | 2631 p50=0.03ms p99=0.10ms max=6.60ms
pump [kill]: 7828 p50=0.03ms p99=0.10ms max=15.23ms | 7848 p50=0.03ms p99=0.10ms max=19.81ms | 7791 p50=0.03ms p99=0.10ms max=28.30ms | 7865 p50=0.03ms p99=0.10ms max=19.84ms | 7893 p50=0.03ms p99=0.10ms max=14.41ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  healthy-kill-1.log 6165 WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  healthy-kill-1.log 6176 KILL-RENDERER how=api pid=6406 callUs=12252
  healthy-kill-2.log 6158 WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  healthy-kill-2.log 6173 KILL-RENDERER how=api pid=6636 callUs=15596
  healthy-kill-3.log 6206 WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  healthy-kill-3.log 6226 KILL-RENDERER how=api pid=6867 callUs=20000
  healthy-kill-4.log 6230 WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  healthy-kill-4.log 6242 KILL-RENDERER how=api pid=7095 callUs=12439
  healthy-kill-5.log 6163 WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  healthy-kill-5.log 6174 KILL-RENDERER how=api pid=7325 callUs=11398

## healthy-reload (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
recovery reload RECOVERY-DOC: 15 (7-48, n=5)
recovery reload RECOVERY-READY: 33 (13-70, n=5)
recovery reload RECOVERY-PONG: 925 (876-934, n=5)
recovery from ACTION reload to DOC: 15 (7-48, n=5)
recovery from ACTION reload to READY: 33 (14-71, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.9 p99 2.0 max 2.0 (n=15)
probe rtt ms [state]: median 1.1 p99 2.5 max 2.5 (n=15)
probe rtt ms [reload]: median 1.0 p99 6.7 max 6.7 (n=45)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=8); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1000 max 1000 (n=14); rAF/s median 62 min 62; vis visible
timer tick ms [reload]: median 1000 p99 1004 max 1004 (n=38); rAF/s median 62 min 62; vis visible
pump [start]: 2647 p50=0.03ms p99=0.10ms max=7.43ms | 2674 p50=0.03ms p99=0.11ms max=33.62ms | 2677 p50=0.03ms p99=0.12ms max=8.74ms | 2661 p50=0.04ms p99=0.12ms max=9.40ms | 2671 p50=0.03ms p99=0.13ms max=8.64ms
pump [state]: 2602 p50=0.03ms p99=0.09ms max=1.82ms | 2606 p50=0.03ms p99=0.11ms max=2.95ms | 2622 p50=0.03ms p99=0.10ms max=2.01ms | 2607 p50=0.03ms p99=0.11ms max=3.27ms | 2619 p50=0.03ms p99=0.10ms max=1.37ms
pump [reload]: 7965 p50=0.02ms p99=0.08ms max=7.02ms | 7774 p50=0.04ms p99=0.10ms max=22.14ms | 7840 p50=0.04ms p99=0.10ms max=15.46ms | 7766 p50=0.03ms p99=0.10ms max=29.43ms | 7821 p50=0.04ms p99=0.11ms max=4.40ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  healthy-reload-1.log 6155 VIS state=hidden pagePerf=6019.0
  healthy-reload-2.log 6233 VIS state=hidden pagePerf=6087.0
  healthy-reload-3.log 6160 VIS state=hidden pagePerf=6025.0
  healthy-reload-4.log 6205 VIS state=hidden pagePerf=6058.0
  healthy-reload-5.log 6181 VIS state=hidden pagePerf=6021.0

## idle (5 runs)
detection ms (hang start -> unresponsive): 17155 (15600-19262, n=5); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.0 p99 14.7 max 14.7 (n=35)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=30); rAF/s median 62 min 62; vis visible
pump [start]: 7706 p50=0.03ms p99=0.10ms max=16.17ms | 4948 p50=0.03ms p99=0.10ms max=29.92ms | 5426 p50=0.03ms p99=0.09ms max=9.10ms | 6877 p50=0.03ms p99=0.09ms max=6.71ms | 8256 p50=0.03ms p99=0.09ms max=9.38ms
pump [hang-idle]: 26218 p50=0.03ms p99=0.09ms max=7.81ms | 26447 p50=0.03ms p99=0.10ms max=11.27ms | 26598 p50=0.03ms p99=0.09ms max=9.36ms | 26415 p50=0.03ms p99=0.10ms max=5.09ms | 26545 p50=0.02ms p99=0.09ms max=4.05ms
simulated (interval s x misses, from 1 s probes): 1x3: 3266 (3153-3799, n=5); 1x5: 5268 (5152-5799, n=5); 2x3: 6601 (6151-7800, n=5); 2x5: 10602 (10151-11798, n=5); 3x3: 10151 (9174-11798, n=5); 5x2: 12152 (10602-14267, n=5); 5x3: 17152 (15603-19266, n=5); 5x4: 22151 (20601-24267, n=5); 10x2: 22151 (20601-24267, n=5); 10x3: no detect

## kill (5 runs)
detection ms (hang start -> unresponsive): 15915 (15891-15922, n=5); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 5004 (5000-5008, n=5)
recovery auto-reload-after-exit RECOVERY-DOC: 87 (68-123, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 98 (75-157, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 917 (885-927, n=5)
recovery from ACTION kill to DOC: 90 (71-124, n=5)
recovery from ACTION kill to READY: 101 (78-157, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.9 p99 4.6 max 4.6 (n=15)
probe rtt ms [state]: median 1.3 p99 2.5 max 2.5 (n=5)
probe rtt ms [kill]: median 0.9 p99 9.4 max 9.8 (n=105)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=8); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1001 max 1001 (n=6); rAF/s median 63 min 62; vis visible
timer tick ms [kill]: median 1004 p99 1004 max 1005 (n=95); rAF/s median 62 min 62; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 64 min 64; vis visible
pump [start]: 2659 p50=0.03ms p99=0.10ms max=9.73ms | 2693 p50=0.03ms p99=0.14ms max=11.07ms | 2690 p50=0.03ms p99=0.10ms max=19.40ms | 2676 p50=0.03ms p99=0.12ms max=10.93ms | 2684 p50=0.03ms p99=0.10ms max=18.55ms
pump [state]: 867 p50=0.03ms p99=0.10ms max=0.56ms | 877 p50=0.03ms p99=0.11ms max=3.19ms | 880 p50=0.03ms p99=0.10ms max=3.42ms | 876 p50=0.03ms p99=0.11ms max=1.73ms | 885 p50=0.03ms p99=0.08ms max=0.85ms
pump [hang-idle]: 17712 p50=0.02ms p99=0.08ms max=5.99ms | 17564 p50=0.03ms p99=0.09ms max=10.33ms | 17575 p50=0.03ms p99=0.09ms max=20.64ms | 17478 p50=0.03ms p99=0.09ms max=14.78ms | 17523 p50=0.03ms p99=0.08ms max=14.03ms
pump [kill]: 18410 p50=0.03ms p99=0.09ms max=18.50ms | 18406 p50=0.03ms p99=0.09ms max=38.78ms | 18451 p50=0.03ms p99=0.09ms max=21.98ms | 18445 p50=0.03ms p99=0.09ms max=13.79ms | 18462 p50=0.03ms p99=0.09ms max=27.27ms
simulated (interval s x misses, from 1 s probes): 1x3: 3911 (3884-3924, n=5); 1x5: 5911 (5882-5923, n=5); 2x3: 7909 (7882-7923, n=5); 2x5: 11909 (11882-11931, n=5); 3x3: 10911 (10887-10922, n=5); 5x2: 10911 (10887-10922, n=5); 5x3: 15912 (15883-15923, n=5); 5x4: 20912 (20881-20923, n=5); 10x2: no detect; 10x3: no detect
events:
  kill-1.log 24155 (hang+19999) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-1.log 24165 (hang+20009) KILL-RENDERER how=api pid=1174 callUs=10440
  kill-2.log 24182 (hang+19998) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-2.log 24192 (hang+20008) KILL-RENDERER how=api pid=1402 callUs=10100
  kill-3.log 24219 (hang+20000) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-3.log 24238 (hang+20019) KILL-RENDERER how=api pid=1629 callUs=19006
  kill-4.log 24183 (hang+20000) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-4.log 24194 (hang+20011) KILL-RENDERER how=api pid=1856 callUs=11580
  kill-5.log 24212 (hang+20002) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-5.log 24229 (hang+20018) KILL-RENDERER how=api pid=2087 callUs=16922

## kill-load (5 runs)
detection ms (hang start -> unresponsive): 19932 (19881-19939, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery auto-reload-after-exit RECOVERY-DOC: 83 (79-113, n=5)
pump [start]: 35155 p50=0.03ms p99=0.09ms max=17.68ms | 35185 p50=0.03ms p99=0.08ms max=30.68ms | 35254 p50=0.02ms p99=0.09ms max=27.10ms | 35181 p50=0.03ms p99=0.08ms max=28.64ms | 35233 p50=0.02ms p99=0.08ms max=29.41ms
simulated (interval s x misses, from 1 s probes): 1x3: 3929 (3885-3940, n=5); 1x5: 5928 (5884-5939, n=5); 2x3: 7927 (7885-7940, n=5); 2x5: 11927 (11884-11939, n=5); 3x3: 11927 (11884-11939, n=5); 5x2: 14927 (14884-14938, n=5); 5x3: 19929 (19911-19952, n=5); 5x4: 24926 (24884-24938, n=5); 10x2: 29931 (29888-29949, n=5); 10x3: 39925 (39885-39927, n=3)
events:
  kill-load-1.log 20079 (hang+19932) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-load-1.log 20087 (hang+19940) KILL-RENDERER how=api pid=8529 callUs=8322
  kill-load-2.log 20078 (hang+19937) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-load-2.log 20089 (hang+19949) KILL-RENDERER how=api pid=8746 callUs=12234
  kill-load-3.log 20087 (hang+19936) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-load-3.log 20103 (hang+19952) KILL-RENDERER how=api pid=8963 callUs=16719
  kill-load-4.log 20118 (hang+19886) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-load-4.log 20139 (hang+19907) KILL-RENDERER how=api pid=9186 callUs=21735
  kill-load-5.log 20108 (hang+19943) WEB-PROCESS-TERMINATED reason=terminated-by-api responsive=1
  kill-load-5.log 20120 (hang+19955) KILL-RENDERER how=api pid=9397 callUs=12137

## kill-sigkill (5 runs)
detection ms (hang start -> unresponsive): 15905 (15884-15924, n=5); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 5004 (5000-5024, n=5)
recovery auto-reload-after-exit RECOVERY-DOC: 77 (66-88, n=5)
recovery auto-reload-after-exit RECOVERY-READY: 83 (73-129, n=5)
recovery auto-reload-after-exit RECOVERY-PONG: 900 (872-921, n=5)
recovery from ACTION kill to DOC: 91 (72-94, n=5)
recovery from ACTION kill to READY: 98 (78-134, n=5)
recovery backend counter seen after recovery: 3 (3-3, n=5)
probe rtt ms [start]: median 0.8 p99 4.6 max 4.6 (n=15)
probe rtt ms [state]: median 0.7 p99 5.6 max 5.6 (n=5)
probe rtt ms [kill]: median 0.9 p99 6.6 max 7.6 (n=105)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=8); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1001 max 1001 (n=7); rAF/s median 63 min 62; vis visible
timer tick ms [kill]: median 1004 p99 1005 max 1005 (n=95); rAF/s median 62 min 62; vis visible
pump [start]: 2696 p50=0.03ms p99=0.11ms max=13.37ms | 2722 p50=0.03ms p99=0.10ms max=7.80ms | 2702 p50=0.03ms p99=0.12ms max=23.57ms | 2700 p50=0.02ms p99=0.12ms max=23.36ms | 2700 p50=0.03ms p99=0.10ms max=13.07ms
pump [state]: 887 p50=0.03ms p99=0.09ms max=0.71ms | 889 p50=0.03ms p99=0.09ms max=1.42ms | 879 p50=0.03ms p99=0.11ms max=5.87ms | 877 p50=0.02ms p99=0.10ms max=7.74ms | 878 p50=0.03ms p99=0.10ms max=5.28ms
pump [hang-idle]: 17638 p50=0.02ms p99=0.09ms max=13.56ms | 17519 p50=0.03ms p99=0.09ms max=7.93ms | 17580 p50=0.03ms p99=0.09ms max=6.71ms | 17471 p50=0.03ms p99=0.09ms max=12.71ms | 17376 p50=0.04ms p99=0.11ms max=3.58ms
pump [kill]: 18547 p50=0.03ms p99=0.09ms max=38.53ms | 18508 p50=0.03ms p99=0.09ms max=7.85ms | 18629 p50=0.03ms p99=0.09ms max=14.20ms | 18418 p50=0.03ms p99=0.09ms max=25.65ms | 18476 p50=0.03ms p99=0.10ms max=10.29ms
simulated (interval s x misses, from 1 s probes): 1x3: 3905 (3889-3924, n=5); 1x5: 5904 (5888-5925, n=5); 2x3: 7903 (7887-7924, n=5); 2x5: 11905 (11887-11927, n=5); 3x3: 10905 (10887-10927, n=5); 5x2: 10905 (10887-10927, n=5); 5x3: 15905 (15888-15925, n=5); 5x4: 20904 (20888-20924, n=5); 10x2: no detect; 10x3: no detect
events:
  kill-sigkill-1.log 24203 (hang+19999) KILL-RENDERER how=sigkill pid=2309 result=0
  kill-sigkill-1.log 24206 (hang+20003) WEB-PROCESS-TERMINATED reason=crashed responsive=1
  kill-sigkill-2.log 24171 (hang+20001) KILL-RENDERER how=sigkill pid=2535 result=0
  kill-sigkill-2.log 24174 (hang+20004) WEB-PROCESS-TERMINATED reason=crashed responsive=1
  kill-sigkill-3.log 24249 (hang+20004) KILL-RENDERER how=sigkill pid=2766 result=0
  kill-sigkill-3.log 24252 (hang+20006) WEB-PROCESS-TERMINATED reason=crashed responsive=1
  kill-sigkill-4.log 24239 (hang+20000) KILL-RENDERER how=sigkill pid=2989 result=0
  kill-sigkill-4.log 24242 (hang+20003) WEB-PROCESS-TERMINATED reason=crashed responsive=1
  kill-sigkill-5.log 24188 (hang+20002) KILL-RENDERER how=sigkill pid=3216 result=0
  kill-sigkill-5.log 24191 (hang+20005) WEB-PROCESS-TERMINATED reason=crashed responsive=1

## load-contentloading (5 runs)
detection ms (hang start -> unresponsive): 19920 (19888-19934, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 26520 p50=0.02ms p99=0.09ms max=15.51ms | 26502 p50=0.03ms p99=0.09ms max=29.28ms | 26451 p50=0.03ms p99=0.08ms max=12.04ms | 26517 p50=0.03ms p99=0.08ms max=10.03ms | 26313 p50=0.03ms p99=0.09ms max=36.72ms
simulated (interval s x misses, from 1 s probes): 1x3: 3915 (3881-3929, n=5); 1x5: 5915 (5886-5929, n=5); 2x3: 7915 (7879-7930, n=5); 2x5: 11913 (11880-11929, n=5); 3x3: 11913 (11880-11929, n=5); 5x2: 14915 (14880-14930, n=5); 5x3: 19919 (19880-19931, n=5); 5x4: 24915 (24880-24932, n=5); 10x2: 29902 (29879-29925, n=2); 10x3: no detect

## load-doc (5 runs)
detection ms (hang start -> unresponsive): 19924 (19883-19929, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 26454 p50=0.03ms p99=0.09ms max=26.64ms | 26396 p50=0.03ms p99=0.09ms max=8.33ms | 26549 p50=0.03ms p99=0.09ms max=9.45ms | 26576 p50=0.03ms p99=0.09ms max=11.75ms | 26530 p50=0.03ms p99=0.09ms max=8.11ms
simulated (interval s x misses, from 1 s probes): 1x3: 3926 (3881-3928, n=5); 1x5: 5924 (5881-5929, n=5); 2x3: 7925 (7883-7929, n=5); 2x5: 11926 (11882-11929, n=5); 3x3: 11926 (11882-11929, n=5); 5x2: 14925 (14882-14929, n=5); 5x3: 19928 (19882-19931, n=5); 5x4: 24925 (24882-24931, n=5); 10x2: 29928 (29926-29929, n=2); 10x3: no detect

## load-navcompleted (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
pump [start]: 26484 p50=0.03ms p99=0.08ms max=20.44ms | 26399 p50=0.03ms p99=0.08ms max=24.58ms | 26556 p50=0.02ms p99=0.08ms max=12.71ms | 26550 p50=0.03ms p99=0.08ms max=11.36ms | 26433 p50=0.02ms p99=0.08ms max=12.31ms
simulated (interval s x misses, from 1 s probes): 1x3: 3925 (3895-3930, n=5); 1x5: 5924 (5894-5935, n=5); 2x3: 7925 (7895-7932, n=5); 2x5: 11924 (11896-11931, n=5); 3x3: 11924 (11896-11931, n=5); 5x2: 14925 (14895-14932, n=5); 5x3: 19925 (19896-19931, n=5); 5x4: 24926 (24896-24932, n=5); 10x2: 29913 (29896-29930, n=2); 10x3: no detect

## microtask (5 runs)
detection ms (hang start -> unresponsive): 17958 (15152-19720, n=5); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.6 p99 61.3 max 61.3 (n=34)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=28); rAF/s median 62 min 60; vis visible
pump [start]: 8609 p50=0.03ms p99=0.10ms max=8.72ms | 6619 p50=0.03ms p99=0.11ms max=7.54ms | 6261 p50=0.03ms p99=0.10ms max=8.62ms | 4733 p50=0.03ms p99=0.08ms max=11.33ms | 5812 p50=0.03ms p99=0.10ms max=8.96ms
pump [hang-microtask]: 26336 p50=0.03ms p99=0.10ms max=5.90ms | 26678 p50=0.03ms p99=0.08ms max=2.11ms | 16218 p50=0.03ms p99=0.11ms max=1743.75ms | 26877 p50=0.03ms p99=0.08ms max=2.27ms | 14993 p50=0.03ms p99=0.11ms max=12447.32ms
simulated (interval s x misses, from 1 s probes): 1x3: 3410 (3152-3922, n=5); 1x5: 5410 (5151-5921, n=5); 2x3: 6720 (6154-7246, n=5); 2x5: 10721 (10151-11886, n=5); 3x3: 11151 (9719-11886, n=5); 5x2: 13246 (10151-14719, n=5); 5x3: 18927 (15151-19719, n=5); 5x4: 22922 (20151-26375, n=5); 10x2: 22922 (20151-26375, n=5); 10x3: no detect

## noinject (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: noinject-1.log@20091, noinject-2.log@20094, noinject-3.log@20077, noinject-4.log@20093, noinject-5.log@20079
pump [start]: 26462 p50=0.03ms p99=0.08ms max=8.97ms | 26303 p50=0.03ms p99=0.09ms max=22.99ms | 26412 p50=0.03ms p99=0.09ms max=23.03ms | 26253 p50=0.03ms p99=0.09ms max=28.84ms | 26359 p50=0.03ms p99=0.10ms max=8.47ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect alarms=5; 1x5: no detect alarms=5; 2x3: no detect alarms=5; 2x5: no detect alarms=5; 3x3: no detect alarms=5; 5x2: no detect alarms=5; 5x3: no detect alarms=5; 5x4: no detect alarms=5; 10x2: no detect alarms=4; 10x3: no detect

## reload (5 runs)
detection ms (hang start -> unresponsive): 15916 (15900-15924, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION reload to DOC: - never in 5
recovery from ACTION reload to READY: - never in 5
probe rtt ms [start]: median 1.3 p99 5.2 max 5.2 (n=15)
probe rtt ms [state]: median 0.7 p99 2.0 max 2.0 (n=5)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=9); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1000 max 1000 (n=5); rAF/s median 63 min 62; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 63 min 63; vis visible
pump [start]: 2688 p50=0.03ms p99=0.17ms max=8.73ms | 2696 p50=0.03ms p99=0.10ms max=9.14ms | 2684 p50=0.03ms p99=0.09ms max=15.17ms | 2690 p50=0.03ms p99=0.11ms max=8.52ms | 2678 p50=0.03ms p99=0.09ms max=12.37ms
pump [state]: 876 p50=0.03ms p99=0.11ms max=4.30ms | 885 p50=0.03ms p99=0.10ms max=0.99ms | 874 p50=0.03ms p99=0.09ms max=3.04ms | 881 p50=0.03ms p99=0.09ms max=1.03ms | 876 p50=0.03ms p99=0.09ms max=4.10ms
pump [hang-idle]: 17558 p50=0.03ms p99=0.09ms max=8.21ms | 17727 p50=0.02ms p99=0.09ms max=7.27ms | 17798 p50=0.02ms p99=0.08ms max=3.50ms | 17776 p50=0.03ms p99=0.09ms max=4.82ms | 17699 p50=0.03ms p99=0.10ms max=4.30ms
pump [reload]: 40543 p50=0.03ms p99=0.09ms max=10.84ms | 40437 p50=0.03ms p99=0.09ms max=22.15ms | 40586 p50=0.03ms p99=0.09ms max=10.72ms | 40521 p50=0.03ms p99=0.09ms max=22.84ms | 40359 p50=0.03ms p99=0.09ms max=12.11ms
simulated (interval s x misses, from 1 s probes): 1x3: 3916 (3903-3924, n=5); 1x5: 5916 (5904-5924, n=5); 2x3: 7916 (7903-7924, n=5); 2x5: 11914 (11902-11923, n=5); 3x3: 10915 (10903-10925, n=5); 5x2: 10915 (10903-10925, n=5); 5x3: 15915 (15902-15927, n=5); 5x4: 20920 (20903-20924, n=5); 10x2: 25918 (25903-25924, n=5); 10x3: 35915 (35903-35925, n=5)
events:
  reload-1.log 27220 (hang+22996) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=203
  reload-2.log 27149 (hang+23000) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=392
  reload-3.log 27196 (hang+23000) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=589
  reload-4.log 27163 (hang+23001) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=782
  reload-5.log 27250 (hang+23001) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=977

## reload-load (5 runs)
detection ms (hang start -> unresponsive): 19930 (19927-19934, n=5); misses at alarm 3
alarms not caused by an endless hang: none
pump [start]: 35361 p50=0.02ms p99=0.09ms max=14.89ms | 35224 p50=0.03ms p99=0.09ms max=8.13ms | 35219 p50=0.03ms p99=0.09ms max=9.39ms | 35372 p50=0.03ms p99=0.09ms max=13.90ms | 35070 p50=0.03ms p99=0.09ms max=21.40ms
simulated (interval s x misses, from 1 s probes): 1x3: 3933 (3930-3934, n=5); 1x5: 5931 (5929-5934, n=5); 2x3: 7934 (7931-7936, n=5); 2x5: 11933 (11928-11934, n=5); 3x3: 11933 (11928-11934, n=5); 5x2: 14932 (14930-14933, n=5); 5x3: 19934 (19929-19935, n=5); 5x4: 24933 (24930-24952, n=5); 10x2: 29934 (29929-29936, n=5); 10x3: 39929 (39929-39929, n=1)
events:
  reload-load-1.log 23089 (hang+22932) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=7556
  reload-load-2.log 23080 (hang+22936) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=7750
  reload-load-3.log 23083 (hang+22932) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=7944
  reload-load-4.log 23080 (hang+22933) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=8139
  reload-load-5.log 23079 (hang+22934) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=8334

## renav (5 runs)
detection ms (hang start -> unresponsive): 15927 (15915-15931, n=5); misses at alarm 3
alarms not caused by an endless hang: none
recovery from ACTION nav:/index.html to DOC: - never in 5
recovery from ACTION nav:/index.html to READY: - never in 5
probe rtt ms [start]: median 0.9 p99 5.3 max 5.3 (n=15)
probe rtt ms [state]: median 0.8 p99 3.8 max 3.8 (n=5)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=8); rAF/s median 62 min 62; vis visible
timer tick ms [state]: median 1000 p99 1001 max 1001 (n=5); rAF/s median 63 min 62; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=2); rAF/s median 63 min 63; vis visible
pump [start]: 2707 p50=0.03ms p99=0.12ms max=10.57ms | 2656 p50=0.03ms p99=0.11ms max=11.25ms | 2671 p50=0.03ms p99=0.11ms max=8.24ms | 2673 p50=0.03ms p99=0.12ms max=8.48ms | 2670 p50=0.03ms p99=0.10ms max=7.71ms
pump [state]: 897 p50=0.02ms p99=0.09ms max=1.86ms | 862 p50=0.03ms p99=0.10ms max=3.62ms | 873 p50=0.03ms p99=0.09ms max=1.09ms | 873 p50=0.03ms p99=0.08ms max=6.00ms | 866 p50=0.04ms p99=0.11ms max=7.92ms
pump [hang-idle]: 17504 p50=0.03ms p99=0.10ms max=13.31ms | 17649 p50=0.03ms p99=0.09ms max=3.93ms | 17755 p50=0.03ms p99=0.09ms max=3.93ms | 17757 p50=0.03ms p99=0.08ms max=4.46ms | 17732 p50=0.03ms p99=0.09ms max=9.58ms
pump [nav-/index.html]: 40379 p50=0.03ms p99=0.10ms max=15.87ms | 40137 p50=0.03ms p99=0.09ms max=15.11ms | 40082 p50=0.03ms p99=0.09ms max=14.56ms | 40390 p50=0.03ms p99=0.09ms max=18.81ms | 40401 p50=0.03ms p99=0.09ms max=17.91ms
simulated (interval s x misses, from 1 s probes): 1x3: 3928 (3921-3930, n=5); 1x5: 5928 (5916-5928, n=5); 2x3: 7926 (7916-7928, n=5); 2x5: 11925 (11913-11929, n=5); 3x3: 10925 (10914-10928, n=5); 5x2: 10925 (10914-10928, n=5); 5x3: 15926 (15915-15929, n=5); 5x4: 20927 (20916-20928, n=5); 10x2: 25926 (25913-25929, n=5); 10x3: 35927 (35915-35929, n=5)
events:
  renav-1.log 27174 (hang+22998) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=3444
  renav-2.log 27156 (hang+23005) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=3639
  renav-3.log 27158 (hang+23002) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=3834
  renav-4.log 27154 (hang+23000) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=4029
  renav-5.log 27149 (hang+22995) WEBKIT-RESPONSIVE value=0 flips=1 webProcess=4224

## st-cover (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.4 p99 11.5 max 11.5 (n=15)
probe rtt ms [cover]: median 1.4 p99 8.3 max 14.2 (n=180)
probe rtt ms [uncover]: median 0.9 p99 10.1 max 10.1 (n=30)
timer tick ms [start]: median 1000 p99 1001 max 1001 (n=11); rAF/s median 62 min 62; vis visible
timer tick ms [cover]: median 1004 p99 1004 max 1004 (n=178); rAF/s median 62 min 62; vis visible
timer tick ms [uncover]: median 1004 p99 1004 max 1004 (n=30); rAF/s median 62 min 62; vis visible
pump [start]: 4509 p50=0.02ms p99=0.09ms max=12.39ms | 4476 p50=0.02ms p99=0.09ms max=9.65ms | 4468 p50=0.03ms p99=0.09ms max=41.63ms
pump [cover]: 53265 p50=0.02ms p99=0.09ms max=13.36ms | 53009 p50=0.03ms p99=0.10ms max=12.28ms | 53213 p50=0.02ms p99=0.09ms max=18.63ms
pump [uncover]: 8896 p50=0.02ms p99=0.08ms max=9.79ms | 8848 p50=0.03ms p99=0.09ms max=14.19ms | 8907 p50=0.02ms p99=0.09ms max=7.95ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## st-hide (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.1 p99 3.7 max 3.7 (n=15)
probe rtt ms [hide]: median 1.1 p99 15.9 max 26.9 (n=180)
probe rtt ms [show]: median 1.1 p99 8.5 max 8.5 (n=30)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=11); rAF/s median 62 min 62; vis visible
timer tick ms [hide]: median 2000 p99 2000 max 2000 (n=91); rAF/s median 0 min 0; vis hidden/visible
timer tick ms [show]: median 1004 p99 1004 max 1004 (n=30); rAF/s median 62 min 11; vis visible
pump [start]: 4478 p50=0.02ms p99=0.09ms max=9.03ms | 4468 p50=0.03ms p99=0.09ms max=32.79ms | 4470 p50=0.03ms p99=0.09ms max=35.30ms
pump [hide]: 53405 p50=0.02ms p99=0.09ms max=26.22ms | 53005 p50=0.03ms p99=0.09ms max=8.32ms | 53182 p50=0.02ms p99=0.09ms max=15.38ms
pump [show]: 8861 p50=0.03ms p99=0.10ms max=9.13ms | 8847 p50=0.03ms p99=0.09ms max=4.98ms | 8900 p50=0.02ms p99=0.08ms max=5.80ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  st-hide-1.log 5167 VIS state=hidden pagePerf=5025.0
  st-hide-1.log 65175 VIS state=visible pagePerf=65030.0
  st-hide-2.log 5221 VIS state=hidden pagePerf=5071.0
  st-hide-2.log 65228 VIS state=visible pagePerf=65077.0
  st-hide-3.log 5229 VIS state=hidden pagePerf=5056.0
  st-hide-3.log 65235 VIS state=visible pagePerf=65060.0

## st-minimize (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.9 p99 12.1 max 12.1 (n=15)
probe rtt ms [minimize]: median 1.0 p99 9.8 max 10.4 (n=180)
probe rtt ms [restore]: median 1.4 p99 13.3 max 13.3 (n=30)
timer tick ms [start]: median 1000 p99 1001 max 1001 (n=11); rAF/s median 62 min 62; vis visible
timer tick ms [minimize]: median 2000 p99 2000 max 2000 (n=91); rAF/s median 0 min 0; vis visible/hidden
timer tick ms [restore]: median 1004 p99 1004 max 1004 (n=29); rAF/s median 62 min 38; vis visible
pump [start]: 4483 p50=0.02ms p99=0.10ms max=48.37ms | 4478 p50=0.02ms p99=0.09ms max=11.81ms | 4453 p50=0.02ms p99=0.10ms max=26.17ms
pump [minimize]: 53274 p50=0.02ms p99=0.09ms max=9.76ms | 53013 p50=0.03ms p99=0.09ms max=13.61ms | 53243 p50=0.02ms p99=0.09ms max=10.18ms
pump [restore]: 8871 p50=0.02ms p99=0.09ms max=9.16ms | 8860 p50=0.03ms p99=0.09ms max=5.16ms | 8890 p50=0.02ms p99=0.09ms max=19.01ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  st-minimize-1.log 5321 VIS state=hidden pagePerf=5091.0
  st-minimize-1.log 65324 VIS state=visible pagePerf=65095.0
  st-minimize-2.log 5166 VIS state=hidden pagePerf=5032.0
  st-minimize-2.log 65151 VIS state=visible pagePerf=65017.0
  st-minimize-3.log 5296 VIS state=hidden pagePerf=5069.0
  st-minimize-3.log 65296 VIS state=visible pagePerf=65069.0

## st-offscreen (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 1.2 p99 4.0 max 4.0 (n=15)
probe rtt ms [sh:xdotool]: median 1.2 p99 8.9 max 10.0 (n=210)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=12); rAF/s median 62 min 62; vis visible
timer tick ms [sh:xdotool]: median 1004 p99 1004 max 1005 (n=207); rAF/s median 62 min 62; vis visible
pump [start]: 4514 p50=0.02ms p99=0.08ms max=16.30ms | 4443 p50=0.03ms p99=0.10ms max=41.60ms | 4448 p50=0.03ms p99=0.11ms max=48.38ms
pump [sh-xdotool]: 53114 p50=0.03ms p99=0.10ms max=14.81ms | 8841 p50=0.03ms p99=0.09ms max=13.87ms | 53404 p50=0.02ms p99=0.09ms max=16.31ms | 8850 p50=0.03ms p99=0.09ms max=10.14ms | 53054 p50=0.03ms p99=0.09ms max=13.75ms | 8856 p50=0.02ms p99=0.09ms max=6.24ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## stress-healthy (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 1.8 max 1.8 (n=9)
probe rtt ms [memory:before]: median 0.7 p99 2.1 max 2.1 (n=9)
probe rtt ms [stress-on:1000/1024]: median 0.7 p99 3.5 max 10.2 (n=90)
probe rtt ms [stress-off]: median 0.7 p99 1.9 max 1.9 (n=3)
probe rtt ms [memory:after]: median 0.7 p99 2.6 max 2.6 (n=24)
probe rtt ms [memory:later]: median 0.7 p99 1.2 max 1.2 (n=3)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=5); rAF/s median 62 min 62; vis visible
timer tick ms [memory:before]: median 1000 p99 1000 max 1000 (n=7); rAF/s median 62 min 62; vis visible
timer tick ms [stress-on:1000/1024]: median 1004 p99 1004 max 1004 (n=90); rAF/s median 62 min 62; vis visible
timer tick ms [stress-off]: median 1004 p99 1004 max 1004 (n=3); rAF/s median 63 min 62; vis visible
timer tick ms [memory:after]: median 1004 p99 1004 max 1004 (n=24); rAF/s median 62 min 62; vis visible
timer tick ms [memory:later]: median 1004 p99 1004 max 1004 (n=3); rAF/s median 62 min 62; vis visible
pump [start]: 2701 p50=0.03ms p99=0.09ms max=50.60ms | 2697 p50=0.03ms p99=0.12ms max=10.38ms | 2662 p50=0.03ms p99=0.13ms max=10.57ms
pump [memory-before]: 2638 p50=0.03ms p99=0.08ms max=4.50ms | 2668 p50=0.03ms p99=0.11ms max=0.96ms | 2603 p50=0.03ms p99=0.12ms max=0.98ms
pump [stress-on-1000/1024]: 26683 p50=0.02ms p99=0.09ms max=10.08ms | 26628 p50=0.03ms p99=0.10ms max=10.53ms | 26387 p50=0.03ms p99=0.11ms max=14.22ms
pump [stress-off]: 884 p50=0.03ms p99=0.08ms max=2.42ms | 881 p50=0.03ms p99=0.09ms max=2.53ms | 883 p50=0.03ms p99=0.09ms max=0.70ms
pump [memory-after]: 7061 p50=0.03ms p99=0.09ms max=3.08ms | 7093 p50=0.03ms p99=0.09ms max=1.71ms | 7038 p50=0.03ms p99=0.09ms max=4.85ms
pump [memory-later]: 882 p50=0.03ms p99=0.09ms max=1.10ms | 882 p50=0.03ms p99=0.12ms max=1.41ms | 880 p50=0.03ms p99=0.09ms max=0.72ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
events:
  stress-healthy-1.log 3263 MEMORY label=before hostKb=43980 web:19956
  stress-healthy-1.log 7449 MEMORY label=stress hostKb=48236 web:21436
  stress-healthy-1.log 8649 MEMORY label=stress hostKb=51828 web:21732
  stress-healthy-1.log 9852 MEMORY label=stress hostKb=55384 web:21784
  stress-healthy-1.log 11052 MEMORY label=stress hostKb=59104 web:21996
  stress-healthy-1.log 12256 MEMORY label=stress hostKb=62160 web:22140
  stress-healthy-1.log 13457 MEMORY label=stress hostKb=62160 web:22284
  stress-healthy-1.log 14659 MEMORY label=stress hostKb=62164 web:22348
  stress-healthy-1.log 15859 MEMORY label=stress hostKb=62232 web:23524
  stress-healthy-1.log 17061 MEMORY label=stress hostKb=62272 web:23452
  stress-healthy-1.log 18261 MEMORY label=stress hostKb=62580 web:23616
  stress-healthy-1.log 19461 MEMORY label=stress hostKb=62588 web:23752
  stress-healthy-1.log 20661 MEMORY label=stress hostKb=62596 web:23864
  stress-healthy-1.log 21862 MEMORY label=stress hostKb=62604 web:23908
  stress-healthy-1.log 23072 MEMORY label=stress hostKb=62612 web:23920
  stress-healthy-1.log 24277 MEMORY label=stress hostKb=62620 web:23220
  stress-healthy-1.log 25483 MEMORY label=stress hostKb=62756 web:23212
  stress-healthy-1.log 26684 MEMORY label=stress hostKb=62748 web:23232
  stress-healthy-1.log 27897 MEMORY label=stress hostKb=62772 web:23240
  stress-healthy-1.log 29095 MEMORY label=stress hostKb=62776 web:23220
  stress-healthy-1.log 30296 MEMORY label=stress hostKb=62804 web:23260
  stress-healthy-1.log 31495 MEMORY label=stress hostKb=62812 web:23220
  stress-healthy-1.log 32697 MEMORY label=stress hostKb=62820 web:23212
  stress-healthy-1.log 33899 MEMORY label=stress hostKb=62832 web:23236
  stress-healthy-1.log 35100 MEMORY label=stress hostKb=62840 web:23256
  stress-healthy-1.log 36262 STRESS-END sent=24960 meanPostUs=6.5 maxPostUs=3101
  stress-healthy-1.log 37259 MEMORY label=after hostKb=62844 web:21616
  stress-healthy-1.log 45261 MEMORY label=later hostKb=62708 web:21504
  stress-healthy-2.log 3162 MEMORY label=before hostKb=44088 web:19952
  stress-healthy-2.log 7358 MEMORY label=stress hostKb=48296 web:21632
  stress-healthy-2.log 8558 MEMORY label=stress hostKb=51852 web:21828
  stress-healthy-2.log 9758 MEMORY label=stress hostKb=55372 web:22008
  stress-healthy-2.log 10963 MEMORY label=stress hostKb=59072 web:22124
  stress-healthy-2.log 12175 MEMORY label=stress hostKb=62260 web:22392
  stress-healthy-2.log 13374 MEMORY label=stress hostKb=62276 web:22588
  stress-healthy-2.log 14582 MEMORY label=stress hostKb=62276 web:22748
  stress-healthy-2.log 15782 MEMORY label=stress hostKb=62344 web:23812
  stress-healthy-2.log 16982 MEMORY label=stress hostKb=62384 web:23692
  stress-healthy-2.log 18186 MEMORY label=stress hostKb=62392 web:23816
  stress-healthy-2.log 19387 MEMORY label=stress hostKb=62696 web:23780
  stress-healthy-2.log 20588 MEMORY label=stress hostKb=62724 web:23792
  stress-healthy-2.log 21792 MEMORY label=stress hostKb=62732 web:23760
  stress-healthy-2.log 22996 MEMORY label=stress hostKb=62740 web:23804
  stress-healthy-2.log 24197 MEMORY label=stress hostKb=62748 web:23828
  stress-healthy-2.log 25408 MEMORY label=stress hostKb=62884 web:23840
  stress-healthy-2.log 26609 MEMORY label=stress hostKb=62892 web:23808
  stress-healthy-2.log 27811 MEMORY label=stress hostKb=62900 web:23788
  stress-healthy-2.log 29013 MEMORY label=stress hostKb=62908 web:23804
  stress-healthy-2.log 30218 MEMORY label=stress hostKb=62916 web:23848
  stress-healthy-2.log 31423 MEMORY label=stress hostKb=62924 web:23156
  stress-healthy-2.log 32627 MEMORY label=stress hostKb=62932 web:23136
  stress-healthy-2.log 33829 MEMORY label=stress hostKb=62940 web:23156
  stress-healthy-2.log 35029 MEMORY label=stress hostKb=62952 web:23164
  stress-healthy-2.log 36170 STRESS-END sent=24940 meanPostUs=6.7 maxPostUs=6343
  stress-healthy-2.log 37166 MEMORY label=after hostKb=62956 web:21668
  stress-healthy-2.log 45166 MEMORY label=later hostKb=62800 web:21532
  stress-healthy-3.log 3165 MEMORY label=before hostKb=44060 web:19952
  stress-healthy-3.log 7351 MEMORY label=stress hostKb=48268 web:21516
  stress-healthy-3.log 8559 MEMORY label=stress hostKb=51824 web:21712
  stress-healthy-3.log 9761 MEMORY label=stress hostKb=55352 web:21864
  ... 24 more

## stress-hung (3 runs)
detection ms (hang start -> unresponsive): 15924 (15900-15927, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.8 p99 3.4 max 3.4 (n=9)
probe rtt ms [memory:before]: median 2.8 p99 3.0 max 3.0 (n=3)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=6); rAF/s median 62 min 61; vis visible
timer tick ms [memory:before]: median 1000 p99 1001 max 1001 (n=3); rAF/s median 62 min 62; vis visible
pump [start]: 2669 p50=0.03ms p99=0.11ms max=8.27ms | 2688 p50=0.02ms p99=0.10ms max=7.64ms | 2742 p50=0.02ms p99=0.09ms max=9.81ms
pump [memory-before]: 873 p50=0.03ms p99=0.10ms max=0.59ms | 876 p50=0.02ms p99=0.08ms max=3.19ms | 889 p50=0.02ms p99=0.08ms max=4.02ms
pump [hang-idle]: 1746 p50=0.03ms p99=0.09ms max=1.76ms | 1763 p50=0.02ms p99=0.08ms max=1.48ms | 1775 p50=0.02ms p99=0.08ms max=3.45ms
pump [stress-on-1000/1024]: 26476 p50=0.03ms p99=0.09ms max=6.97ms | 26649 p50=0.02ms p99=0.10ms max=10.24ms | 26862 p50=0.02ms p99=0.09ms max=5.42ms
pump [stress-off]: 880 p50=0.03ms p99=0.08ms max=0.95ms | 893 p50=0.03ms p99=0.08ms max=1.97ms | 875 p50=0.03ms p99=0.10ms max=0.88ms
pump [memory-after]: 7071 p50=0.02ms p99=0.08ms max=4.62ms | 7116 p50=0.02ms p99=0.08ms max=8.04ms | 7028 p50=0.03ms p99=0.09ms max=7.31ms
pump [memory-later]: 887 p50=0.02ms p99=0.08ms max=0.85ms | 882 p50=0.02ms p99=0.08ms max=5.15ms | 883 p50=0.03ms p99=0.08ms max=1.11ms
simulated (interval s x misses, from 1 s probes): 1x3: 3925 (3903-3927, n=3); 1x5: 5924 (5902-5925, n=3); 2x3: 7924 (7902-7925, n=3); 2x5: 11923 (11904-11925, n=3); 3x3: 10922 (10904-10925, n=3); 5x2: 10922 (10904-10925, n=3); 5x3: 15923 (15904-15934, n=3); 5x4: 20923 (20904-20930, n=3); 10x2: 25923 (25904-25925, n=3); 10x3: 35922 (35906-35925, n=3)
events:
  stress-hung-1.log 3168 (hang+-1000) MEMORY label=before hostKb=46508 web:19964
  stress-hung-1.log 7355 (hang+3188) MEMORY label=stress hostKb=51092 web:22076
  stress-hung-1.log 8558 (hang+4391) MEMORY label=stress hostKb=54900 web:23468
  stress-hung-1.log 9762 (hang+5595) MEMORY label=stress hostKb=58800 web:24876
  stress-hung-1.log 10962 (hang+6794) MEMORY label=stress hostKb=62788 web:26276
  stress-hung-1.log 12195 (hang+8027) MEMORY label=stress hostKb=66528 web:27684
  stress-hung-1.log 13398 (hang+9231) MEMORY label=stress hostKb=66728 web:29100
  stress-hung-1.log 14603 (hang+10435) MEMORY label=stress hostKb=66456 web:30048
  stress-hung-1.log 15803 (hang+11635) MEMORY label=stress hostKb=66764 web:31444
  stress-hung-1.log 17003 (hang+12836) MEMORY label=stress hostKb=67484 web:32864
  stress-hung-1.log 18203 (hang+14035) MEMORY label=stress hostKb=67744 web:34264
  stress-hung-1.log 19409 (hang+15241) MEMORY label=stress hostKb=68328 web:35652
  stress-hung-1.log 20614 (hang+16446) MEMORY label=stress hostKb=68636 web:37060
  stress-hung-1.log 21815 (hang+17647) MEMORY label=stress hostKb=68952 web:38464
  stress-hung-1.log 23019 (hang+18851) MEMORY label=stress hostKb=69284 web:39912
  stress-hung-1.log 24219 (hang+20051) MEMORY label=stress hostKb=69588 web:41308
  stress-hung-1.log 25418 (hang+21250) MEMORY label=stress hostKb=70044 web:42708
  stress-hung-1.log 26623 (hang+22455) MEMORY label=stress hostKb=71120 web:44104
  stress-hung-1.log 27823 (hang+23655) MEMORY label=stress hostKb=71424 web:45516
  stress-hung-1.log 29027 (hang+24860) MEMORY label=stress hostKb=71592 web:46912
  stress-hung-1.log 30231 (hang+26063) MEMORY label=stress hostKb=71760 web:48324
  stress-hung-1.log 31431 (hang+27263) MEMORY label=stress hostKb=72052 web:49732
  stress-hung-1.log 32631 (hang+28464) MEMORY label=stress hostKb=72364 web:51172
  stress-hung-1.log 33830 (hang+29662) MEMORY label=stress hostKb=72668 web:52572
  stress-hung-1.log 35030 (hang+30862) MEMORY label=stress hostKb=72960 web:53972
  stress-hung-1.log 36182 (hang+32014) STRESS-END sent=24950 meanPostUs=8.5 maxPostUs=34378
  stress-hung-1.log 37170 (hang+33002) MEMORY label=after hostKb=73220 web:55304
  stress-hung-1.log 45170 (hang+41002) MEMORY label=later hostKb=73144 web:55396
  stress-hung-2.log 3157 (hang+-1000) MEMORY label=before hostKb=44104 web:19972
  stress-hung-2.log 7346 (hang+3190) MEMORY label=stress hostKb=48772 web:22068
  stress-hung-2.log 8545 (hang+4388) MEMORY label=stress hostKb=52684 web:23460
  stress-hung-2.log 9746 (hang+5589) MEMORY label=stress hostKb=56552 web:24868
  stress-hung-2.log 10946 (hang+6789) MEMORY label=stress hostKb=60492 web:26268
  stress-hung-2.log 12145 (hang+7989) MEMORY label=stress hostKb=63952 web:27676
  stress-hung-2.log 13344 (hang+9188) MEMORY label=stress hostKb=64120 web:29092
  stress-hung-2.log 14545 (hang+10389) MEMORY label=stress hostKb=63892 web:30016
  stress-hung-2.log 15745 (hang+11589) MEMORY label=stress hostKb=64196 web:31412
  stress-hung-2.log 16945 (hang+12788) MEMORY label=stress hostKb=64932 web:32836
  stress-hung-2.log 18145 (hang+13989) MEMORY label=stress hostKb=65524 web:34232
  stress-hung-2.log 19344 (hang+15188) MEMORY label=stress hostKb=65768 web:35624
  stress-hung-2.log 20550 (hang+16393) MEMORY label=stress hostKb=66060 web:37032
  stress-hung-2.log 21748 (hang+17592) MEMORY label=stress hostKb=66272 web:38436
  stress-hung-2.log 22949 (hang+18793) MEMORY label=stress hostKb=66580 web:39884
  stress-hung-2.log 24151 (hang+19994) MEMORY label=stress hostKb=66900 web:41276
  stress-hung-2.log 25353 (hang+21197) MEMORY label=stress hostKb=67340 web:42672
  stress-hung-2.log 26553 (hang+22396) MEMORY label=stress hostKb=68420 web:44076
  stress-hung-2.log 27752 (hang+23596) MEMORY label=stress hostKb=68724 web:45484
  stress-hung-2.log 28952 (hang+24796) MEMORY label=stress hostKb=69032 web:46884
  stress-hung-2.log 30154 (hang+25997) MEMORY label=stress hostKb=69332 web:48292
  stress-hung-2.log 31353 (hang+27197) MEMORY label=stress hostKb=69652 web:49700
  stress-hung-2.log 32554 (hang+28397) MEMORY label=stress hostKb=69960 web:51144
  stress-hung-2.log 33754 (hang+29597) MEMORY label=stress hostKb=70280 web:52544
  stress-hung-2.log 34953 (hang+30797) MEMORY label=stress hostKb=70596 web:53936
  stress-hung-2.log 36152 (hang+31996) MEMORY label=stress hostKb=70900 web:55336
  stress-hung-2.log 36165 (hang+32009) STRESS-END sent=25000 meanPostUs=8.3 maxPostUs=3326
  stress-hung-2.log 37158 (hang+33002) MEMORY label=after hostKb=70816 web:55340
  stress-hung-2.log 45160 (hang+41003) MEMORY label=later hostKb=70824 web:55432
  stress-hung-3.log 3190 (hang+-997) MEMORY label=before hostKb=44320 web:19948
  stress-hung-3.log 7375 (hang+3187) MEMORY label=stress hostKb=48960 web:22048
  stress-hung-3.log 8576 (hang+4388) MEMORY label=stress hostKb=52856 web:23440
  ... 25 more

## tasks100 (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 2.0 max 2.0 (n=6)
probe rtt ms [load:tasks100]: median 48.8 p99 98.3 max 99.8 (n=174)
timer tick ms [load:tasks100]: median 1047 p99 1052 max 1056 (n=167); rAF/s median 12 min 5; vis visible
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 62 min 62; vis visible
pump [start]: 1795 p50=0.03ms p99=0.13ms max=9.05ms | 1804 p50=0.03ms p99=0.13ms max=9.48ms | 1819 p50=0.03ms p99=0.12ms max=10.21ms
pump [load-tasks100]: 51117 p50=0.03ms p99=0.09ms max=8.21ms | 51232 p50=0.03ms p99=0.10ms max=9.29ms | 51244 p50=0.03ms p99=0.09ms max=6.59ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## tasks1000 (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 3.3 max 3.3 (n=6)
probe rtt ms [load:tasks1000]: median 148.1 p99 258.9 max 263.6 (n=171)
timer tick ms [load:tasks1000]: median 1004 p99 2000 max 2001 (n=157); rAF/s median 1 min 0; vis visible
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=1); rAF/s median 63 min 63; vis visible
pump [start]: 1807 p50=0.03ms p99=0.11ms max=9.08ms | 1811 p50=0.03ms p99=0.12ms max=7.59ms | 1812 p50=0.03ms p99=0.11ms max=8.61ms
pump [load-tasks1000]: 51293 p50=0.03ms p99=0.09ms max=6.96ms | 51334 p50=0.03ms p99=0.10ms max=7.40ms | 51417 p50=0.02ms p99=0.09ms max=5.47ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## wk-busy (3 runs)
detection ms (hang start -> unresponsive): 16913 (16870-16914, n=3); misses at alarm 3
alarms not caused by an endless hang: none
responsive again after alarm (ms): 3089 (3088-3134, n=3)
probe rtt ms [start]: median 0.7 p99 1.1 max 1.1 (n=9)
probe rtt ms [sh:xdotool]: median 3617.1 p99 19089.4 max 19132.6 (n=96)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=6); rAF/s median 62 min 62; vis visible
timer tick ms [sh:xdotool]: median 1004 p99 20015 max 20015 (n=36); rAF/s median 62 min 0; vis visible
pump [start]: 2732 p50=0.02ms p99=0.09ms max=8.44ms | 2698 p50=0.03ms p99=0.11ms max=11.17ms | 2714 p50=0.03ms p99=0.11ms max=22.51ms
pump [busy-20000]: 1785 p50=0.02ms p99=0.08ms max=2.60ms | 1772 p50=0.03ms p99=0.10ms max=0.91ms | 1771 p50=0.02ms p99=0.09ms max=1.85ms
pump [sh-xdotool]: 26776 p50=0.02ms p99=0.08ms max=7.92ms | 26361 p50=0.03ms p99=0.09ms max=8.90ms | 26431 p50=0.03ms p99=0.09ms max=9.31ms
simulated (interval s x misses, from 1 s probes): 1x3: 3912 (3871-3914, n=3); 1x5: 5912 (5871-5913, n=3); 2x3: 6912 (6871-6914, n=3); 2x5: 10913 (10870-10914, n=3); 3x3: 11913 (11871-11916, n=3); 5x2: 11913 (11871-11916, n=3); 5x3: 16913 (16870-16915, n=3); 5x4: no detect; 10x2: no detect; 10x3: no detect

## wk-click (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=6); rAF/s median 62 min 62; vis visible
pump [start]: 2711 p50=0.02ms p99=0.09ms max=8.11ms | 2718 p50=0.03ms p99=0.10ms max=11.74ms | 2712 p50=0.03ms p99=0.09ms max=9.36ms
pump [hang-idle]: 4426 p50=0.02ms p99=0.09ms max=0.28ms | 4438 p50=0.03ms p99=0.08ms max=1.28ms | 4445 p50=0.03ms p99=0.08ms max=5.59ms
pump [sh-xdotool]: 32706 p50=0.03ms p99=0.09ms max=15.16ms | 33013 p50=0.02ms p99=0.08ms max=11.18ms | 33161 p50=0.02ms p99=0.08ms max=1.44ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## wk-pings (3 runs)
detection ms (hang start -> unresponsive): 16930 (16920-16930, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.7 p99 1.3 max 1.3 (n=9)
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=4); rAF/s median 62 min 62; vis visible
pump [start]: 2689 p50=0.03ms p99=0.11ms max=8.65ms | 2701 p50=0.03ms p99=0.12ms max=7.54ms | 2720 p50=0.02ms p99=0.10ms max=8.10ms
pump [hang-idle]: 63777 p50=0.03ms p99=0.09ms max=32.12ms | 63743 p50=0.03ms p99=0.09ms max=10.57ms | 64037 p50=0.03ms p99=0.09ms max=9.09ms
simulated (interval s x misses, from 1 s probes): 1x3: 3927 (3919-3929, n=3); 1x5: 5928 (5921-5930, n=3); 2x3: 6929 (6920-6929, n=3); 2x5: 10929 (10919-10929, n=3); 3x3: 11928 (11920-11929, n=3); 5x2: 11928 (11920-11929, n=3); 5x3: 16929 (16920-16929, n=3); 5x4: 21928 (21920-21930, n=3); 10x2: 26928 (26920-26929, n=3); 10x3: 36927 (36919-36928, n=3)

## wk-quiet (3 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
timer tick ms [start]: median 1000 p99 1000 max 1000 (n=3); rAF/s median 62 min 62; vis visible
timer tick ms [hang-idle]: median 1000 p99 1000 max 1000 (n=2); rAF/s median 62 min 62; vis visible
pump [start]: 2700 p50=0.03ms p99=0.11ms max=12.10ms | 2731 p50=0.02ms p99=0.09ms max=8.27ms | 2723 p50=0.02ms p99=0.11ms max=8.58ms
pump [hang-idle]: 63711 p50=0.03ms p99=0.09ms max=0.42ms | 64049 p50=0.02ms p99=0.08ms max=0.43ms | 64122 p50=0.02ms p99=0.08ms max=0.63ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect

## worker (5 runs)
detection: no alarm after a hang in any run
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.9 p99 9.5 max 9.5 (n=30)
probe rtt ms [hang-worker]: median 0.7 p99 4.6 max 8.4 (n=150)
timer tick ms [start]: median 1000 p99 1004 max 1004 (n=24); rAF/s median 62 min 62; vis visible
timer tick ms [hang-worker]: median 1004 p99 1005 max 1005 (n=148); rAF/s median 62 min 62; vis visible
pump [start]: 6150 p50=0.03ms p99=0.10ms max=8.80ms | 4677 p50=0.03ms p99=0.10ms max=8.84ms | 7894 p50=0.03ms p99=0.09ms max=8.57ms | 4526 p50=0.03ms p99=0.10ms max=11.42ms | 4547 p50=0.03ms p99=0.10ms max=9.32ms
pump [hang-worker]: 26234 p50=0.03ms p99=0.09ms max=28.83ms | 26424 p50=0.03ms p99=0.09ms max=29.57ms | 26051 p50=0.03ms p99=0.10ms max=8.03ms | 26365 p50=0.03ms p99=0.09ms max=33.09ms | 26309 p50=0.03ms p99=0.10ms max=4.73ms
simulated (interval s x misses, from 1 s probes): 1x3: no detect; 1x5: no detect; 2x3: no detect; 2x5: no detect; 3x3: no detect; 5x2: no detect; 5x3: no detect; 5x4: no detect; 10x2: no detect; 10x3: no detect
