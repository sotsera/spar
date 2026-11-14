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
| open | 12/45/45 | 17 | 52 | 51 | 29 | 67 | 67 |
| result-void | 18/37/39 | 15 | 38 | 39 | 30 | 57 | 57 |
| result-echo | 104/123/125 | 16 | 38 | 40 | 42 | 71 | 71 |
| result-note | 136/155/157 | 16 | 39 | 40 | 41 | 71 | 72 |
| result-failure | 87/106/108 | 16 | 39 | 40 | 39 | 68 | 68 |
| items-rows-100 | 22683/22721/22713 | 174 | 228 | 210 | 2610 | 2679 | 2634 |
| items-rows-1000 | 227786/227824/227816 | 3175 | 3202 | 3178 | 57880 | 57017 | 58597 |
| items-event-1 | 78/116/108 | 19 | 56 | 47 | 43 | 88 | 75 |
| items-events-20 | 1406/1444/1436 | 27 | 71 | 56 | 169 | 219 | 212 |
| items-events-300 | 21151/21189/21181 | 130 | 185 | 168 | 2196 | 2315 | 2237 |
| end | 6/24/24 | 12 | 33 | 33 | 23 | 49 | 49 |
| cancelled | 6/30/30 | 11 | 33 | 33 | 24 | 49 | 49 |
| not-executed | 54/88/88 | 53 | 73 | 83 | 74 | 96 | 97 |
| outcome-unknown | 83/120/120 | 56 | 72 | 76 | 83 | 107 | 107 |

## Page → host (decode)

| Message | Bytes A/B/C | decode A | decode B | decode C | + validate A | + validate B | + validate C | from string + decode + validate A | B | C |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| hello | 12/46/46 | 27 | 139 | 143 | 27 | 140 | 143 | 39 | 155 | 160 |
| call-note | 114/178/172 | 65 | 329 | 219 | 169 | 426 | 319 | 181 | 448 | 343 |
| call-note-traced | 168/250/244 | 90 | 376 | 268 | 194 | 485 | 372 | 214 | 507 | 403 |
| call-note-traced-state | 214/305/299 | 161 | 437 | 324 | 272 | 534 | 431 | 290 | 558 | 455 |
| call-void | 37/95/95 | 67 | 245 | 217 | 64 | 212 | 216 | 77 | 227 | 230 |
| stream-rows | 60/144/138 | 80 | 337 | 293 | 121 | 382 | 334 | 195 | 623 | 524 |
| stream-rows-traced | 114/216/210 | 105 | 389 | 347 | 146 | 429 | 383 | 164 | 456 | 412 |
| credits | 8/40/40 | 17 | 118 | 121 | 17 | 118 | 128 | 25 | 129 | 132 |
| cancel | 6/27/27 | 12 | 86 | 90 | 12 | 86 | 90 | 20 | 97 | 102 |

## Page → host with a large argument (B's extra pass over `arg`)

| Argument | decode A | decode B | decode C | + validate A | + validate B | + validate C |
|---|---:|---:|---:|---:|---:|---:|
| 10 rows (2,174 bytes) | 67 | 2734 | 219 | 2141 | 4475 | 2316 |
| 1000 rows (223,571 bytes) | 64 | 208649 | 219 | 200677 | 408655 | 202216 |

