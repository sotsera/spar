# Page-side codec cost (JavaScript, ns per message)

Median over runs of each run's median of 7 samples (every sample at least 25 ms of repeated calls, encodings interleaved). Decoding: A and C slice a header and `JSON.parse` the payload; B parses the whole envelope with one `JSON.parse`. `payload only` is `JSON.parse` of the payload alone, the floor every encoding pays. Encoding includes `JSON.stringify` of the argument.

## WebView2

3 runs. `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/157.0.0.0 Safari/537.36 Edg/157.0.0.0`

| Decode (host → page) | A | B | C | payload only | B / A | C / A |
|---|---:|---:|---:|---:|---:|---:|
| open | 18 | 100 | 106 | — | 5.48 | 5.80 |
| result-void | 126 | 103 | 157 | 55 | 0.81 | 1.24 |
| result-echo | 296 | 255 | 307 | 183 | 0.86 | 1.04 |
| result-note | 272 | 235 | 291 | 169 | 0.86 | 1.07 |
| result-failure | 218 | 187 | 245 | 126 | 0.86 | 1.13 |
| items-rows-100 | 18078 | 17959 | 18332 | 17853 | 0.99 | 1.01 |
| items-rows-1000 | 188608 | 187952 | 187162 | 182927 | 1.00 | 0.99 |
| items-event-1 | 275 | 265 | 291 | 157 | 0.96 | 1.06 |
| items-events-20 | 2245 | 2234 | 2273 | 2064 | 1.00 | 1.01 |
| items-events-300 | 33049 | 32541 | 32637 | 32528 | 0.98 | 0.99 |
| end | 63 | 97 | 105 | — | 1.54 | 1.67 |
| cancelled | 62 | 96 | 106 | — | 1.54 | 1.71 |
| not-executed | 145 | 160 | 178 | — | 1.10 | 1.23 |
| outcome-unknown | 156 | 168 | 184 | — | 1.07 | 1.18 |

| Encode (page → host) | A | B | C | B / A | C / A |
|---|---:|---:|---:|---:|---:|
| call (note, no trace) | 82 | 156 | 109 | 1.90 | 1.33 |
| call (note, traceparent + tracestate) | 168 | 180 | 193 | 1.08 | 1.15 |
| call (no argument) | 33 | 61 | 61 | 1.83 | 1.84 |
| stream call | 78 | 91 | 101 | 1.16 | 1.29 |
| credits | 25 | 47 | 47 | 1.85 | 1.86 |
| cancel | 14 | 42 | 43 | 3.07 | 3.11 |

## Chromium headless shell

3 runs. `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/156.0.8078.4 Safari/537.36`

| Decode (host → page) | A | B | C | payload only | B / A | C / A |
|---|---:|---:|---:|---:|---:|---:|
| open | 19 | 92 | 98 | — | 4.97 | 5.28 |
| result-void | 120 | 92 | 148 | 49 | 0.76 | 1.23 |
| result-echo | 296 | 250 | 313 | 180 | 0.84 | 1.06 |
| result-note | 262 | 216 | 272 | 157 | 0.82 | 1.04 |
| result-failure | 219 | 174 | 228 | 119 | 0.79 | 1.04 |
| items-rows-100 | 20251 | 20188 | 20154 | 20000 | 1.00 | 1.00 |
| items-rows-1000 | 201987 | 201307 | 199338 | 200654 | 1.00 | 0.99 |
| items-event-1 | 267 | 257 | 292 | 150 | 0.96 | 1.09 |
| items-events-20 | 2410 | 2374 | 2422 | 2265 | 0.99 | 1.00 |
| items-events-300 | 36127 | 35431 | 35548 | 35198 | 0.98 | 0.98 |
| end | 60 | 90 | 100 | — | 1.50 | 1.66 |
| cancelled | 60 | 89 | 102 | — | 1.48 | 1.70 |
| not-executed | 152 | 146 | 165 | — | 0.96 | 1.08 |
| outcome-unknown | 161 | 164 | 176 | — | 1.02 | 1.09 |

| Encode (page → host) | A | B | C | B / A | C / A |
|---|---:|---:|---:|---:|---:|
| call (note, no trace) | 87 | 155 | 116 | 1.77 | 1.33 |
| call (note, traceparent + tracestate) | 180 | 178 | 189 | 0.99 | 1.05 |
| call (no argument) | 32 | 66 | 65 | 2.04 | 2.02 |
| stream call | 83 | 103 | 109 | 1.25 | 1.31 |
| credits | 23 | 48 | 48 | 2.12 | 2.14 |
| cancel | 14 | 45 | 44 | 3.16 | 3.14 |

## WebKitGTK (WSL)

3 runs. `Mozilla/5.0 (X11; Ubuntu; Linux x86_64) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/60.5 Safari/605.1.15`

| Decode (host → page) | A | B | C | payload only | B / A | C / A |
|---|---:|---:|---:|---:|---:|---:|
| open | 23 | 117 | 128 | — | 5.06 | 5.52 |
| result-void | 120 | 103 | 155 | 31 | 0.86 | 1.29 |
| result-echo | 347 | 355 | 413 | 220 | 1.02 | 1.19 |
| result-note | 310 | 270 | 330 | 193 | 0.87 | 1.06 |
| result-failure | 250 | 203 | 260 | 124 | 0.81 | 1.04 |
| items-rows-100 | 35516 | 36000 | 36000 | 34406 | 1.01 | 1.01 |
| items-rows-1000 | 355263 | 355263 | 346667 | 350000 | 1.00 | 0.98 |
| items-event-1 | 333 | 320 | 370 | 186 | 0.96 | 1.11 |
| items-events-20 | 3200 | 3300 | 3200 | 3000 | 1.03 | 1.00 |
| items-events-300 | 45000 | 45942 | 46667 | 45000 | 1.02 | 1.04 |
| end | 84 | 99 | 103 | — | 1.18 | 1.22 |
| cancelled | 77 | 97 | 100 | — | 1.26 | 1.30 |
| not-executed | 198 | 180 | 200 | — | 0.91 | 1.01 |
| outcome-unknown | 215 | 190 | 207 | — | 0.88 | 0.96 |

| Encode (page → host) | A | B | C | B / A | C / A |
|---|---:|---:|---:|---:|---:|
| call (note, no trace) | 144 | 177 | 181 | 1.23 | 1.26 |
| call (note, traceparent + tracestate) | 248 | 290 | 217 | 1.17 | 0.88 |
| call (no argument) | 24 | 90 | 93 | 3.72 | 3.84 |
| stream call | 75 | 134 | 160 | 1.80 | 2.14 |
| credits | 16 | 65 | 64 | 4.08 | 4.00 |
| cancel | 14 | 57 | 58 | 4.05 | 4.14 |
