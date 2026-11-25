# Fuzz run `selftest`

- seed 1; cases requested 60; cases run 60; batch 20; workers 2
- fuzzing time 61.0 s (budget 420 s); total with minimisation 61.2 s
- runtime: .NET 11.0.0 (JIT)

Comparisons: `A~B` original generator vs System.Text.Json (bench options); `F~B` fixed generator vs B; `A~S`/`F~S` against the strict reference S. Classes: EQ both accept, equal; EQ-src equal to each other but not to the source value; NE both accept, values differ; RJ both reject; XR-YA left rejects, right accepts; XA-YR left accepts, right rejects; X-nonjson left throws something other than JsonException.

## Case counts

| Comparison | Stream | EQ | RJ | XA-YR | PASS | FAIL | CRASH | HANG | total |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ALL | Mutant | 0 | 0 | 0 | 0 | 0 | 1 | 1 | 2 |
| A~B | Mutant | 4 | 28 | 2 | 0 | 0 | 0 | 0 | 34 |
| A~B | Valid | 12 | 0 | 0 | 0 | 0 | 0 | 0 | 12 |
| A~S | Mutant | 4 | 28 | 2 | 0 | 0 | 0 | 0 | 34 |
| A~S | Valid | 12 | 0 | 0 | 0 | 0 | 0 | 0 | 12 |
| F~B | Mutant | 4 | 28 | 2 | 0 | 0 | 0 | 0 | 34 |
| F~B | Valid | 12 | 0 | 0 | 0 | 0 | 0 | 0 | 12 |
| F~S | Mutant | 4 | 28 | 2 | 0 | 0 | 0 | 0 | 34 |
| F~S | Valid | 12 | 0 | 0 | 0 | 0 | 0 | 0 | 12 |
| JS | Value | 0 | 0 | 0 | 4 | 8 | 0 | 0 | 12 |
| W | Value | 0 | 0 | 0 | 12 | 0 | 0 | 0 | 12 |

## Incidents (crash or hang)

| Kind | Case | Stream | Target | Ops | Exit | First stderr line |
|---|---:|---|---|---|---|---|
| CRASH | 5 | Mutant | Wide | trailing-comma deep-chain:62 | exit 0x80131623 | Process terminated. |
| HANG | 27 | Mutant | Note | bad-name duplicate-property | killed after 60 s without progress |  |

## Representatives

Key: left outcome | right outcome (normalised message). The example is the shortest case in the group, minimised with ddmin while class and keys stay the same.

### A~B XA-YR: 2 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2 | string? | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` ""/**/` | comment |

### A~B RJ: 28 cases in 18 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 4 | string? | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x1A\x0D` | root-7 byte-insert |
| 4 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{ "text":",)2-" , "tags" :[ ], "parent":null ,"score" : 3.0075949968991284E-89 , Text: false, "version":-1}` | bad-name |
| 2 | Note | x=JsonException: The JSON array contains a trailing comma at the end which is not supported in this mode. Change the reader opt \| y=JsonException | `{"\u0074\u0061\u0067\u0073":["\u0079\u005f\u007d\u005E\u007E\u0020\u0055\u0021","\u0072\u003b\u0073\u0078\u0048\u0052\u0066\u004F\u0065\u0071\u005D\u005F\u0046"… (566 bytes)` | trailing-comma omit-null-property none |
| 2 | Note | x=JsonException: '?' is invalid after a property name. Expected a '?'. \| y=JsonException | `{"tags":["\xCE\xA2/\xC2\xA0\xD8\x9Ck\xEA\xB4\x81\xEC\xB5\xA6\x60\xE5\xB1\x9Dl\u001f1\xC2\xA0r\xD8\x9C","1"],"te""xt":null,"parent":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[… (341 bytes)` | array-extra-item deep-wrap:array:100 bad-name |

### F~B XA-YR: 2 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2 | string? | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` ""/**/` | comment |

### F~B RJ: 28 cases in 18 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 4 | string? | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x1A\x0D` | root-7 byte-insert |
| 4 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{ "text":",)2-" , "tags" :[ ], "parent":null ,"score" : 3.0075949968991284E-89 , Text: false, "version":-1}` | bad-name |
| 2 | Note | x=JsonException: The JSON array contains a trailing comma at the end which is not supported in this mode. Change the reader opt \| y=JsonException | `{"\u0074\u0061\u0067\u0073":["\u0079\u005f\u007d\u005E\u007E\u0020\u0055\u0021","\u0072\u003b\u0073\u0078\u0048\u0052\u0066\u004F\u0065\u0071\u005D\u005F\u0046"… (566 bytes)` | trailing-comma omit-null-property none |
| 2 | Note | x=JsonException: '?' is invalid after a property name. Expected a '?'. \| y=JsonException | `{"tags":["\xCE\xA2/\xC2\xA0\xD8\x9Ck\xEA\xB4\x81\xEC\xB5\xA6\x60\xE5\xB1\x9Dl\u001f1\xC2\xA0r\xD8\x9C","1"],"te""xt":null,"parent":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[… (341 bytes)` | array-extra-item deep-wrap:array:100 bad-name |

### A~S XA-YR: 2 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2 | string? | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` ""/**/` | comment |

### F~S XA-YR: 2 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2 | string? | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` ""/**/` | comment |

## Writer and round-trip checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| PASS | 12 | - | case 10 int value:  |

## JavaScript JSON.parse checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 8 | integer above 2^53 rounded by JSON.parse | case 0 Note value: A wrote {"text":"","tags":[],"parent":{"text":"FoAQQw)C","tags":["]\u0022\u23BF\u1B86","WvYYD7","\u03EAV\uC9BCV\uDA4C\uDD8F\u002… (269 bytes) ; B wrote {"text":"","tags":[],"parent":{"text":"FoAQQw)C","tags":["]\u0022\u23BF\u1B86","WvYYD7","\u03EAV\uC9BCV\uDA4C\uDD8F\u002… (269 bytes) |
| PASS | 4 | - | case 10 int value:  |
