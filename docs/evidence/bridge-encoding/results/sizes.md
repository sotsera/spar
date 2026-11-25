# Encoded sizes (UTF-8 bytes)

Written by `Bridge.Bench sizes`. Payload bytes are identical for every encoding; the difference is the header or envelope.

## Per message

| Message | Payload | A | B | C | B − A | C − A |
|---|---:|---:|---:|---:|---:|---:|
| hello (page→host) | 0 | 12 | 46 | 46 | +34 | +34 |
| call-note (page→host) | 77 | 114 | 178 | 172 | +64 | +58 |
| call-note-traced (page→host) | 77 | 168 | 250 | 244 | +82 | +76 |
| call-note-traced-state (page→host) | 77 | 214 | 305 | 299 | +91 | +85 |
| call-void (page→host) | 0 | 37 | 95 | 95 | +58 | +58 |
| stream-rows (page→host) | 16 | 60 | 144 | 138 | +84 | +78 |
| stream-rows-traced (page→host) | 16 | 114 | 216 | 210 | +102 | +96 |
| credits (page→host) | 0 | 8 | 40 | 40 | +32 | +32 |
| cancel (page→host) | 0 | 6 | 27 | 27 | +21 | +21 |
| open (host→page) | 0 | 12 | 45 | 45 | +33 | +33 |
| result-void (host→page) | 11 | 18 | 37 | 39 | +19 | +21 |
| result-echo (host→page) | 97 | 104 | 123 | 125 | +19 | +21 |
| result-note (host→page) | 129 | 136 | 155 | 157 | +19 | +21 |
| result-failure (host→page) | 80 | 87 | 106 | 108 | +19 | +21 |
| items-rows-100 (host→page) | 22674 | 22683 | 22721 | 22713 | +38 | +30 |
| items-rows-1000 (host→page) | 227777 | 227786 | 227824 | 227816 | +38 | +30 |
| items-event-1 (host→page) | 67 | 76 | 114 | 106 | +38 | +30 |
| items-events-20 (host→page) | 1397 | 1406 | 1444 | 1436 | +38 | +30 |
| items-events-300 (host→page) | 21141 | 21151 | 21189 | 21181 | +38 | +30 |
| end (host→page) | 0 | 6 | 24 | 24 | +18 | +18 |
| cancelled (host→page) | 0 | 6 | 30 | 30 | +24 | +24 |
| not-executed (host→page) | 0 | 54 | 88 | 88 | +34 | +34 |
| outcome-unknown (host→page) | 0 | 83 | 120 | 120 | +37 | +37 |

## Whole exchanges

Credits: window 8, refilled in halves (one `credits` message per 4 batches), as in `spikes/react`.

| Exchange | Messages | Payload bytes | A | B | C | B overhead vs A | C overhead vs A |
|---|---:|---:|---:|---:|---:|---:|---:|
| unary call + result (note) | 2 | 206 | 250 | 333 | 329 | +83 (+33.20%) | +79 (+31.60%) |
| unary call with traceparent + result | 2 | 206 | 304 | 405 | 401 | +101 (+33.22%) | +97 (+31.91%) |
| unary call with traceparent + tracestate + result | 2 | 206 | 350 | 460 | 456 | +110 (+31.43%) | +106 (+30.29%) |
| cancel + cancelled | 2 | 0 | 12 | 57 | 57 | +45 (+375.00%) | +45 (+375.00%) |
| 100k rows, batch 1000 | 127 | 22,842,090 | 22,843,240 | 22,847,942 | 22,847,136 | +4,702 (+0.02%) | +3,896 (+0.02%) |
| 100k rows, batch 100 | 1,252 | 22,842,990 | 22,854,039 | 22,900,141 | 22,892,135 | +46,102 (+0.20%) | +38,096 (+0.17%) |
| 100k rows, batch 1 | 125,002 | 22,941,990 | 24,042,037 | 28,642,139 | 27,842,133 | +4,600,102 (+19.13%) | +3,800,096 (+15.81%) |
| 60,000 push events (20,000/s × 3 s), 1 per frame | 75,002 | 4,463,219 | 5,123,270 | 7,883,372 | 7,403,366 | +2,760,102 (+53.87%) | +2,280,096 (+44.50%) |
| 60,000 push events (20,000/s × 3 s), 20 per frame | 3,752 | 4,407,300 | 4,440,351 | 4,578,453 | 4,554,447 | +138,102 (+3.11%) | +114,096 (+2.57%) |
| 60,000 push events (20,000/s × 3 s), 300 per frame | 252 | 4,405,740 | 4,407,991 | 4,417,293 | 4,415,687 | +9,302 (+0.21%) | +7,696 (+0.17%) |
