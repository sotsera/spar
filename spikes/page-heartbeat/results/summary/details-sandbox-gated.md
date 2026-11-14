# Summary of results/sandbox/gated (6 runs)

## cover-hang (3 runs)
detection ms (hang start -> unresponsive): 18531 (16363-19129, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.4 p99 2.5 max 2.5 (n=15)
probe rtt ms [cover]: median 0.5 p99 4.8 max 4.8 (n=19)
timer tick ms [start]: median 1000 p99 1013 max 1013 (n=10); rAF/s median 32 min 31; vis visible
timer tick ms [cover]: median 1000 p99 1014 max 1014 (n=21); rAF/s median 32 min 31; vis visible
pump [start]: 325 p50=0.07ms p99=0.32ms max=0.56ms | 325 p50=0.07ms p99=0.23ms max=0.48ms | 327 p50=0.07ms p99=0.29ms max=2.71ms
pump [cover]: 413 p50=0.06ms p99=0.15ms max=0.46ms | 552 p50=0.07ms p99=0.16ms max=0.38ms | 376 p50=0.07ms p99=0.17ms max=0.22ms
pump [hang-idle]: 2239 p50=0.07ms p99=0.20ms max=10.28ms | 2240 p50=0.08ms p99=0.18ms max=0.33ms | 2240 p50=0.07ms p99=0.21ms max=1.63ms
simulated (interval s x misses, from 1 s probes): 1x3: 3362 (3129-3533, n=3); 1x5: 5362 (5128-5527, n=3); 2x3: 6527 (6362-7129, n=3); 2x5: 10526 (10362-11129, n=3); 3x3: 10128 (9526-10362, n=3); 5x2: 13530 (11368-14129, n=3); 5x3: 18527 (16364-19129, n=3); 5x4: 23527 (21363-24129, n=3); 10x2: 28526 (26364-29129, n=3); 10x3: no detect
events:
  cover-hang-1.log 2929 (hang+-11441) NAV-COMPLETED id=2 success=True status=0
  cover-hang-2.log 2950 (hang+-13610) NAV-COMPLETED id=2 success=True status=0
  cover-hang-3.log 4472 (hang+-10849) NAV-COMPLETED id=2 success=True status=0

## gate-minimize-hang (3 runs)
detection ms (hang start -> unresponsive): 54971 (54962-54978, n=3); misses at alarm 3
alarms not caused by an endless hang: none
probe rtt ms [start]: median 0.5 p99 1.2 max 1.2 (n=15)
probe rtt ms [minimize]: median 0.5 p99 3.9 max 3.9 (n=15)
timer tick ms [start]: median 1000 p99 1008 max 1008 (n=12); rAF/s median 32 min 32; vis visible
timer tick ms [minimize]: median 1000 p99 1831 max 1831 (n=12); rAF/s median 0 min 0; vis hidden
pump [start]: 325 p50=0.07ms p99=0.25ms max=0.32ms | 326 p50=0.07ms p99=0.19ms max=0.34ms | 328 p50=0.07ms p99=0.22ms max=0.29ms
pump [minimize]: 316 p50=0.08ms p99=0.17ms max=0.52ms | 316 p50=0.08ms p99=0.18ms max=0.63ms | 316 p50=0.08ms p99=0.23ms max=1.07ms
pump [hang-idle]: 2209 p50=0.08ms p99=0.21ms max=0.77ms | 2209 p50=0.07ms p99=0.19ms max=3.58ms | 2210 p50=0.07ms p99=0.18ms max=0.64ms
pump [restore]: 1581 p50=0.07ms p99=0.17ms max=1.05ms | 1581 p50=0.07ms p99=0.19ms max=9.51ms | 1578 p50=0.07ms p99=0.18ms max=2.14ms
simulated (interval s x misses, from 1 s probes): 1x3: 3976 (3962-3978, n=3); 1x5: 5975 (5974-5976, n=3); 2x3: 7970 (7969-7973, n=3); 2x5: 11966 (11963-11978, n=3); 3x3: 10967 (10964-10980, n=3); 5x2: 14968 (14962-14975, n=3); 5x3: 19968 (19968-19972, n=3); 5x4: 24975 (24974-24977, n=3); 10x2: 29966 (29962-29968, n=3); 10x3: 39968 (39968-39971, n=3)
events:
  gate-minimize-hang-1.log 4761 (hang+-10006) NAV-COMPLETED id=2 success=True status=0
  gate-minimize-hang-1.log 9759 (hang+-5008) VIS state=hidden pagePerf=5028.4
  gate-minimize-hang-1.log 9912 (hang+-4856) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-1.log 49988 (hang+35221) HB-RESUME hb=ping reason=minimised
  gate-minimize-hang-2.log 2855 (hang+-10006) NAV-COMPLETED id=2 success=True status=0
  gate-minimize-hang-2.log 7854 (hang+-5006) VIS state=hidden pagePerf=5031.0
  gate-minimize-hang-2.log 8023 (hang+-4837) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-2.log 48084 (hang+35224) HB-RESUME hb=ping reason=minimised
  gate-minimize-hang-3.log 4872 (hang+-9996) NAV-COMPLETED id=2 success=True status=0
  gate-minimize-hang-3.log 9881 (hang+-4988) VIS state=hidden pagePerf=5045.6
  gate-minimize-hang-3.log 10016 (hang+-4853) HB-PAUSE hb=ping reason=minimised
  gate-minimize-hang-3.log 50077 (hang+35208) HB-RESUME hb=ping reason=minimised
