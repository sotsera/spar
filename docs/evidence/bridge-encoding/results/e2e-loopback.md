# End to end: Chromium over the loopback channel (NativeAOT Kestrel host)

Median over runs, range in brackets. Unary: 2,000 sequential calls each; `meanUs` = loop time / calls (finer than `performance.now()`, which is coarsened to 0.1 ms). Streams: 100,000 rows of about 228 bytes, credits window 8, 3 streams per run. Push: 20,000 events/s for 3 s from a 1,000-item drop-oldest buffer. Cancel: 200 calls that run until cancelled; time from sending the cancel to the confirmation. `task ms` is CDP `TaskDuration` (page main thread busy) over the phase; `handler ms` is the time spent inside the page's message handler (decode, dispatch and the consumer's own loop).

Runs per encoding: A 3, B 3, C 3. Browser: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/156.0.8078.4 Safari/537.36`.

| Measure | A | B | C |
|---|---:|---:|---:|
| unary echo, mean µs | 438.1 (410.9–544.1) | 398.8 (369.5–613.9) | 404.6 (385.8–455.8) |
| unary echo, p50 ms | 0.40 (0.40–0.50) | 0.40 (0.40–0.60) | 0.40 (0.40–0.40) |
| unary echo, p99 ms | 0.80 (0.80–1.10) | 0.70 (0.60–1.00) | 0.80 (0.80–0.90) |
| unary echo, task ms / 2,000 calls | 443.8 (420.5–541.1) | 409.6 (373.5–623.4) | 403.1 (393.3–461.7) |
| unary save + traceparent + tracestate, mean µs | 382.6 (381.0–451.2) | 404.6 (378.8–483.2) | 469.0 (441.8–471.5) |
| unary save traced, p99 ms | 0.70 (0.70–0.90) | 0.80 (0.60–0.80) | 0.90 (0.80–0.90) |
| 100k rows batch 1000, total ms | 65.8 (49.2–107.1) | 69.3 (54.3–97.4) | 59.1 (47.5–97.8) |
| 100k rows batch 1000, rows/s (k) | 1520 (934–2033) | 1443 (1027–1842) | 1692 (1022–2105) |
| 100k rows batch 1000, task ms | 66.2 (50.8–91.6) | 67.0 (51.1–93.2) | 58.6 (46.0–94.7) |
| 100k rows batch 1000, handler ms | 36.3 (26.7–49.6) | 37.3 (29.9–43.3) | 31.0 (25.9–46.2) |
| 100k rows batch 100, total ms | 108.3 (96.7–172.1) | 116.0 (101.6–141.3) | 116.9 (93.9–223.1) |
| 100k rows batch 100, task ms | 97.2 (87.5–147.6) | 98.2 (87.4–116.9) | 105.7 (85.7–192.9) |
| push 20k/s (≤1000 per frame), delivered | 59721 (59719–59963) | 59771 (59717–59980) | 59875 (59867–59891) |
| push 20k/s, frames | 190 (190–191) | 191 (191–191) | 192 (191–192) |
| push 20k/s, latency p50 ms | 0.52 (0.43–0.53) | 0.56 (0.49–0.62) | 0.60 (0.53–0.71) |
| push 20k/s, latency p99 ms | 1.37 (1.03–2.35) | 1.08 (0.85–1.11) | 1.05 (0.97–1.73) |
| push 20k/s, task ms / 3 s | 62.4 (60.6–78.8) | 74.2 (72.1–83.3) | 70.2 (60.1–93.0) |
| push 20k/s (≤20 per frame), frames | 3081 (3079–3083) | 3085 (3081–3088) | 3075 (3072–3081) |
| push 20k/s (≤20 per frame), latency p99 ms | 2.00 (1.93–2.46) | 1.90 (1.82–1.91) | 2.34 (1.90–2.39) |
| push 20k/s (≤20 per frame), task ms / 3 s | 162.6 (146.6–188.7) | 154.4 (152.5–157.2) | 186.8 (158.0–188.6) |
| push 20k/s (≤20 per frame), handler ms | 22.3 (21.5–28.6) | 24.9 (22.0–25.9) | 28.0 (23.8–33.5) |
| push, gaps / dropped (both phases) | 0 (0–0) | 0 (0–0) | 0 (0–0) |
| cancel → confirmed, p50 ms | 0.50 (0.40–0.50) | 0.60 (0.40–0.60) | 0.50 (0.40–0.90) |
| cancel → confirmed, p99 ms | 0.80 (0.60–1.10) | 1.30 (0.70–2.70) | 0.90 (0.80–1.30) |
| host process CPU ms (whole run) | 1328 (1234–1328) | 1250 (1234–1578) | 1250 (1250–1500) |

Functional checks per run (2,000 ok results per unary phase, every row delivered, 200 confirmed cancels, `response-serialization` failure and `unknown-command` classified, no out-of-order event, no malformed frame on the host): A: all checks passed in 3 runs; B: all checks passed in 3 runs; C: all checks passed in 3 runs.
