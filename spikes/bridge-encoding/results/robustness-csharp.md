# C# decoder robustness (page → host frames)

Written by `Bridge.Bench robust`. "decode" is the header decoder; "payload" is a full `Utf8JsonReader` pass over the located payload, and "strings" also calls `GetString()` on every string token, as a generated reader does. "id" is the call id the decoder could still attribute the frame to (−1: none).

## A

| Case | Bytes | decode | id | kind | payload | strings |
|---|---:|---|---:|---|---|---|
| valid call | 214 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (4.0 µs) | 1234 | Call | valid | ok |
| truncated in the payload | 204 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (0.6 µs) | 1234 | Call | **invalid** | **JsonReaderException** |
| truncated in the header | 9 | **rejected**: missing header field (0.2 µs) | 1234 | Call | — | — |
| two frames concatenated | 428 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (0.4 µs) | 1234 | Call | **invalid** | **JsonReaderException** |
| leading BOM | 217 | **rejected**: the kind must be one letter followed by a space (0.0 µs) | -1 | Invalid | — | — |
| empty | 0 | **rejected**: empty frame (0.1 µs) | -1 | Invalid | — | — |
| id with 10,000 digits | 10,067 | **rejected**: bad id (0.8 µs) | -1 | Call | — | — |
| id 2^53 + 1 | 83 | **rejected**: bad id (0.1 µs) | -1 | Call | — | — |
| id as a decimal fraction | 70 | **rejected**: bad id (0.0 µs) | -1 | Call | — | — |
| lone surrogate escape in the argument | 75 | ok (0.1 µs) | 7 | Call | valid | **InvalidOperationException** |
| invalid UTF-8 in the argument | 69 | ok (0.1 µs) | 7 | Call | valid | **InvalidOperationException** |
| raw newline as JSON whitespace in the argument | 69 | ok (0.1 µs) | 7 | Call | valid | ok |
| 10 MB argument | 10,000,067 | ok (0.7 µs) | 7 | Call | valid | ok |
| contract with a space | 69 | ok (1.5 µs) | 7 | Call | **invalid** | **JsonReaderException** |
| tracestate with a percent-encoded surrogate (%ED%A0%80) | 134 | ok (tracestate `k=%ED%A0%80`) (1.2 µs) | 7 | Call | valid | ok |
| tracestate with a raw space | 128 | ok (tracestate `k=a`) (0.2 µs) | 7 | Call | **invalid** | **JsonReaderException** |
| unknown kind from a newer page | 20 | ok (0.1 µs) | 7 | unrecognized `p` | — | — |
| newer page adds a header field to q | 72 | ok (0.1 µs) | 7 | Call | **invalid** | **JsonReaderException** |
| cancel with trailing data | 7 | **rejected**: unexpected trailing data (0.1 µs) | 7 | Cancel | — | — |

## B

| Case | Bytes | decode | id | kind | payload | strings |
|---|---:|---|---:|---|---|---|
| valid call | 305 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (4.3 µs) | 1234 | Call | valid | ok |
| truncated in the payload | 295 | **rejected**: JsonException: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 \| BytePositionInLine: 295. (11.2 µs) | 1234 | Call | — | — |
| truncated in the header | 20 | **rejected**: JsonException: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 \| BytePositionInLine: 20. (1.0 µs) | -1 | Call | — | — |
| two frames concatenated | 610 | **rejected**: JsonException: '{' is invalid after a single JSON value. Expected end of data. LineNumber: 0 \| BytePositionInLine: 305. (3.8 µs) | 1234 | Call | — | — |
| leading BOM | 308 | **rejected**: JsonException: '0xEF' is an invalid start of a value. LineNumber: 0 \| BytePositionInLine: 0. (1.4 µs) | -1 | Invalid | — | — |
| empty | 0 | **rejected**: JsonException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. LineNumber: 0 \| BytePositionInLine: 0. (11.8 µs) | -1 | Invalid | — | — |
| id with 10,000 digits | 10,131 | **rejected**: bad id (5.5 µs) | -1 | Call | — | — |
| id 2^53 + 1 | 147 | **rejected**: bad id (0.2 µs) | -1 | Call | — | — |
| id as a decimal fraction | 134 | **rejected**: bad id (0.2 µs) | -1 | Call | — | — |
| lone surrogate escape in the argument | 139 | ok (0.8 µs) | 7 | Call | valid | **InvalidOperationException** |
| invalid UTF-8 in the argument | 133 | ok (0.5 µs) | 7 | Call | valid | **InvalidOperationException** |
| raw newline as JSON whitespace in the argument | 133 | ok (0.8 µs) | 7 | Call | valid | ok |
| 10 MB argument | 10,000,131 | ok (876.2 µs) | 7 | Call | valid | ok |
| lone surrogate escape in tracestate | 228 | **rejected**: InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate. (17.6 µs) | 7 | Call | — | — |
| unknown kind from a newer page | 41 | ok (1.0 µs) | 7 | unrecognized `progress` | — | — |
| newer page adds a header field to call | 149 | ok (0.6 µs) | 7 | Call | valid | ok |
| duplicate id key in cancel | 31 | ok (0.3 µs) | 8 | Cancel | — | — |
| arg before type | 132 | ok (0.6 µs) | 7 | Call | valid | ok |
| id as a string | 26 | **rejected**: bad id (0.1 µs) | -1 | Cancel | — | — |

## C

| Case | Bytes | decode | id | kind | payload | strings |
|---|---:|---|---:|---|---|---|
| valid call | 299 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (6.5 µs) | 1234 | Call | valid | ok |
| truncated in the payload | 289 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (0.7 µs) | 1234 | Call | **invalid** | **JsonReaderException** |
| truncated in the header | 20 | **rejected**: JsonException: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 \| BytePositionInLine: 20. (2.6 µs) | -1 | Call | — | — |
| two frames concatenated | 598 | ok (tracestate `congo=t61rcWkgMzE,rojo=00f067aa0ba902b7`) (0.5 µs) | 1234 | Call | **invalid** | **JsonReaderException** |
| leading BOM | 302 | **rejected**: JsonException: '0xEF' is an invalid start of a value. LineNumber: 0 \| BytePositionInLine: 0. (1.6 µs) | -1 | Invalid | — | — |
| empty | 0 | **rejected**: JsonException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. LineNumber: 0 \| BytePositionInLine: 0. (1.1 µs) | -1 | Invalid | — | — |
| id with 10,000 digits | 10,125 | **rejected**: bad id (4.8 µs) | -1 | Call | — | — |
| id 2^53 + 1 | 141 | **rejected**: bad id (0.1 µs) | -1 | Call | — | — |
| id as a decimal fraction | 128 | **rejected**: bad id (0.1 µs) | -1 | Call | — | — |
| lone surrogate escape in the argument | 133 | ok (0.4 µs) | 7 | Call | valid | **InvalidOperationException** |
| invalid UTF-8 in the argument | 127 | ok (0.3 µs) | 7 | Call | valid | **InvalidOperationException** |
| raw newline as JSON whitespace in the argument | 127 | ok (0.4 µs) | 7 | Call | valid | ok |
| 10 MB argument | 10,000,125 | ok (0.4 µs) | 7 | Call | valid | ok |
| lone surrogate escape in tracestate | 222 | **rejected**: InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate. (9.0 µs) | 7 | Call | — | — |
| unknown kind from a newer page | 43 | ok (0.6 µs) | 7 | unrecognized `progress` | valid | ok |
| newer page adds a header field to call | 143 | ok (0.7 µs) | 7 | Call | valid | ok |
| duplicate id key in cancel | 31 | ok (0.3 µs) | 8 | Cancel | — | — |
| raw TAB inside a header string | 34 | **rejected**: JsonException: Expected end of string, but instead reached end of data. LineNumber: 0 \| BytePositionInLine: 30. (2.5 µs) | 7 | Cancel | — | — |
| id as a string | 26 | **rejected**: bad id (0.1 µs) | -1 | Cancel | — | — |

## Writer output for awkward strings (shared by all encodings)

| Input | Default encoder | `UnsafeRelaxedJsonEscaping` |
|---|---|---|
| newline, tab | `"a\nb\tc"` | `"a\nb\tc"` |
| U+2028, U+2029 | `"a\u2028b\u2029c"` | `"a\u2028b\u2029c"` |
| lone surrogate U+D800 | `"a\uFFFDb"` | `"a\uFFFDb"` |
| é, 日本 | `"\u00E9\u65E5\u672C"` | `"é日本"` |
| <script>&' | `"\u003Cscript\u003E\u0026\u0027"` | `"<script>&'"` |
