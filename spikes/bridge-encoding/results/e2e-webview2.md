# End to end: WebView2 (NativeAOT host, postMessage)

Median over runs, range in brackets. Unary: 2,000 sequential calls each; `meanUs` = loop time / calls (finer than `performance.now()`, which is coarsened to 0.1 ms). Streams: 100,000 rows of about 228 bytes, credits window 8, 3 streams per run. Push: 20,000 events/s for 3 s from a 1,000-item drop-oldest buffer. Cancel: 200 calls that run until cancelled; time from sending the cancel to the confirmation. `task ms` is CDP `TaskDuration` (page main thread busy) over the phase; `handler ms` is the time spent inside the page's message handler (decode, dispatch and the consumer's own loop).

Runs per encoding: A 3, B 3, C 3, Bj 3. Browser: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36 Edg/157.0.0.0`.

| Measure | A | B | C | Bj |
|---|---:|---:|---:|---:|
| unary echo, mean µs | 58.9 (56.1–93.7) | 78.5 (61.3–102.0) | 61.2 (59.8–64.4) | 63.6 (61.2–107.3) |
| unary echo, p50 ms | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) |
| unary echo, p99 ms | 0.20 (0.20–0.30) | 0.20 (0.20–0.30) | 0.20 (0.20–0.20) | 0.20 (0.20–0.30) |
| unary echo, task ms / 2,000 calls | 37.5 (34.3–59.6) | 48.6 (37.0–65.9) | 38.3 (37.9–40.5) | 37.1 (37.1–62.7) |
| unary save + traceparent + tracestate, mean µs | 58.8 (56.5–65.0) | 64.3 (62.7–78.8) | 60.7 (59.2–60.8) | 76.4 (73.6–82.5) |
| unary save traced, p99 ms | 0.20 (0.20–0.20) | 0.20 (0.20–0.20) | 0.20 (0.20–0.20) | 0.20 (0.20–0.20) |
| 100k rows batch 1000, total ms | 162.6 (156.5–169.2) | 164.2 (158.3–167.2) | 163.1 (158.3–166.6) | 327.8 (308.8–352.2) |
| 100k rows batch 1000, rows/s (k) | 615 (591–639) | 609 (598–632) | 613 (600–632) | 305 (284–324) |
| 100k rows batch 1000, task ms | 76.6 (71.4–88.5) | 76.7 (70.3–89.0) | 77.0 (73.9–86.0) | 58.8 (47.4–72.4) |
| 100k rows batch 1000, handler ms | 32.9 (27.3–36.7) | 32.0 (26.4–38.8) | 34.1 (26.7–38.4) | 1.0 (0.4–1.7) |
| 100k rows batch 100, total ms | 188.6 (184.4–191.8) | 188.9 (184.1–200.1) | 192.9 (185.1–218.1) | 327.2 (318.6–382.9) |
| 100k rows batch 100, task ms | 87.4 (84.9–92.4) | 88.7 (84.9–92.4) | 99.9 (86.5–117.3) | 71.2 (63.2–79.0) |
| push 20k/s (≤1000 per frame), delivered | 59949 (59903–59976) | 59857 (59759–59898) | 59938 (59927–59939) | 59888 (59698–59920) |
| push 20k/s, frames | 191 (191–191) | 191 (190–191) | 191 (191–192) | 190 (190–191) |
| push 20k/s, latency p50 ms | 0.56 (0.45–0.69) | 0.58 (0.55–0.60) | 0.68 (0.56–0.71) | 0.74 (0.66–0.80) |
| push 20k/s, latency p99 ms | 1.37 (1.13–1.97) | 1.24 (0.97–1.41) | 1.23 (1.11–1.25) | 1.33 (1.18–1.38) |
| push 20k/s, task ms / 3 s | 42.1 (39.8–44.3) | 43.2 (37.6–43.5) | 47.2 (42.5–52.3) | 36.8 (36.2–41.6) |
| push 20k/s (≤20 per frame), frames | 3082 (3066–3085) | 3084 (3078–3085) | 3077 (3067–3081) | 3077 (3074–3083) |
| push 20k/s (≤20 per frame), latency p99 ms | 0.94 (0.91–1.19) | 1.09 (1.03–1.10) | 1.14 (1.06–1.25) | 1.13 (1.06–1.47) |
| push 20k/s (≤20 per frame), task ms / 3 s | 73.9 (70.4–84.3) | 71.7 (71.0–76.9) | 81.3 (76.8–91.5) | 77.0 (72.4–81.0) |
| push 20k/s (≤20 per frame), handler ms | 32.8 (31.7–35.3) | 31.2 (28.3–31.7) | 35.0 (30.4–38.5) | 12.5 (12.2–13.3) |
| push, gaps / dropped (both phases) | 0 (0–0) | 0 (0–0) | 0 (0–0) | 0 (0–0) |
| cancel → confirmed, p50 ms | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) | 0.10 (0.10–0.10) |
| cancel → confirmed, p99 ms | 0.20 (0.20–0.30) | 0.20 (0.20–0.30) | 0.30 (0.20–0.60) | 0.30 (0.20–0.30) |
| host process CPU ms (whole run) | 2094 (1953–2469) | 1953 (1812–2438) | 1891 (1812–2172) | 2969 (2672–3703) |

Functional checks per run (2,000 ok results per unary phase, every row delivered, 200 confirmed cancels, `response-serialization` failure and `unknown-command` classified, no out-of-order event, no malformed frame on the host): A: all checks passed in 3 runs; B: all checks passed in 3 runs; C: all checks passed in 3 runs; Bj: all checks passed in 3 runs.
