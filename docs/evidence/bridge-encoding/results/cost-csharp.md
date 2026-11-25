# C# encode and decode cost (NativeAOT, ns per message, median of 7 samples)

Written by `Bridge.Bench cost`. .NET 11.0.0, Microsoft Windows 10.0.26320, NativeAOT: True, 32 logical processors

- **encode**: the header or envelope written around an already buffered payload, into a reused `ArrayBufferWriter`.
- **encode + string**: plus the UTF-8 → UTF-16 conversion that `PostWebMessageAsString` needs.
- **decode**: header parsed and payload located (B skips over `arg`/`items` with `Utf8JsonReader.Skip`).
- **decode + validate**: plus one full `Utf8JsonReader` pass over the payload, the least a generated reader does.
- **from string**: the host receives a UTF-16 string from WebView2 and transcodes it to UTF-8 first.

## Host → page (encode)

| Message | Bytes A/B/C | encode A | encode B | encode C | + string A | + string B | + string C |
|---|---|---:|---:|---:|---:|---:|---:|
| open | 12/45/45 | 17 | 54 | 53 | 31 | 68 | 69 |
| result-void | 18/37/39 | 16 | 39 | 39 | 30 | 56 | 57 |
| result-echo | 104/123/125 | 16 | 38 | 40 | 43 | 73 | 73 |
| result-note | 136/155/157 | 22 | 49 | 51 | 63 | 84 | 84 |
| result-failure | 87/106/108 | 16 | 43 | 42 | 40 | 74 | 71 |
| items-rows-100 | 22683/22721/22713 | 143 | 206 | 186 | 2805 | 2974 | 2772 |
| items-rows-1000 | 227786/227824/227816 | 3232 | 3511 | 3247 | 57852 | 59036 | 59225 |
| items-event-1 | 78/116/108 | 26 | 80 | 64 | 45 | 91 | 78 |
| items-events-20 | 1406/1444/1436 | 28 | 67 | 57 | 179 | 226 | 221 |
| items-events-300 | 21151/21189/21181 | 144 | 188 | 173 | 2263 | 2309 | 2244 |
| end | 6/24/24 | 11 | 33 | 33 | 23 | 48 | 48 |
| cancelled | 6/30/30 | 12 | 33 | 33 | 23 | 49 | 48 |
| not-executed | 54/88/88 | 52 | 70 | 70 | 77 | 97 | 97 |
| outcome-unknown | 83/120/120 | 59 | 77 | 73 | 84 | 112 | 106 |

## Page → host (decode)

| Message | Bytes A/B/C | decode A | decode B | decode C | + validate A | + validate B | + validate C | from string + decode + validate A | B | C |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| hello | 12/46/46 | 27 | 140 | 145 | 27 | 141 | 145 | 36 | 152 | 157 |
| call-note | 114/178/172 | 66 | 324 | 220 | 167 | 426 | 318 | 181 | 447 | 343 |
| call-note-traced | 168/250/244 | 91 | 378 | 271 | 195 | 545 | 376 | 209 | 497 | 395 |
| call-note-traced-state | 214/305/299 | 162 | 431 | 329 | 275 | 533 | 440 | 294 | 597 | 458 |
| call-void | 37/95/95 | 64 | 218 | 217 | 64 | 215 | 215 | 77 | 226 | 229 |
| stream-rows | 60/144/138 | 80 | 340 | 290 | 120 | 378 | 334 | 140 | 394 | 351 |
| stream-rows-traced | 114/216/210 | 103 | 396 | 347 | 145 | 436 | 385 | 159 | 451 | 402 |
| credits | 8/40/40 | 17 | 117 | 121 | 17 | 117 | 121 | 25 | 129 | 133 |
| cancel | 6/27/27 | 12 | 86 | 92 | 12 | 86 | 90 | 19 | 98 | 101 |

## Page → host with a large argument (B's extra pass over `arg`)

| Argument | decode A | decode B | decode C | + validate A | + validate B | + validate C |
|---|---:|---:|---:|---:|---:|---:|
| 10 rows (2,174 bytes) | 63 | 2397 | 217 | 2639 | 5076 | 2616 |
| 1000 rows (223,571 bytes) | 65 | 230499 | 231 | 231043 | 532759 | 220100 |
