# Wire details: 64-bit integers, SQL types, lone surrogates, null, duplicates, writer failures

**Question.** The plan leaves five wire details open before M1's serializer work (`long` above 2^53 in JavaScript, lone
surrogates, top-level `null`, duplicate properties, mapping writer exceptions), and the SQL Server tool needs types the
DTO subset does not define yet. What do `Utf8JsonWriter`/`Utf8JsonReader` and JavaScript actually do in each case, and
what rule should Spar adopt?

**Answer.** Every behaviour below was observed by running it: a .NET console probe (`WireProbe`), Node scripts, a
read-only SqlClient probe against the local SQL Server, and a feature probe in Microsoft Edge 158 (the Chromium engine
behind WebView2). The [recommendation table](#recommendation) gives one rule per item. In short:

- **`long`/`ulong`:** a JSON string, `bigint` in TypeScript. As numbers, 99,397 of 100,000 random 64-bit ids came back
  wrong from `JSON.parse`. The string costs 2 bytes per value and about 5–6 ms per 100,000 values for `BigInt()`.
  A reviver costs 9–10 times more than plain `JSON.parse`.
- **SQL types:** `decimal` as a string (JavaScript changed 5 of 7 sample values, including `money`'s maximum).
  Date and time types as ISO strings, because `new Date()` shifts dates and times without an offset. `byte[]` as base64,
  `Guid` as lower-case text, enums as names, and finite doubles only.
- **Lone surrogates:** every .NET writer already replaces them with U+FFFD. The readers throw on `"\ud800"`, which is
  exactly what `JSON.stringify` sends. **SqlClient itself replaces lone surrogates in `nvarchar` in both directions**, so a
  lossless wire would not preserve SQL data anyway. Proposed rule: replace with U+FFFD on both sides, never an error.
- **Top-level `null`:** the generated client knows the return type, so `{"ok":true}` (void) and
  `{"ok":true,"value":null}` (`T?`) never need to be told apart at run time. Both are still distinct in JSON and in
  JavaScript.
- **Duplicates:** reject a repeated declared property with its byte offset. The bit-mask check cost nothing measurable
  (141–147.6 ms against 142–145.7 ms for 1,000,000 parses).
- **Writer failures:** unary responses are always buffered, so a failure leaves nothing sent and the page gets
  `failure.kind = "response-serialization"`, `execution = "completed"`. A stream ends with the same failure after the
  items already sent. An unbuffered writer had already flushed 278,560 bytes of invalid JSON when it failed.

## Setup

Windows 11 Pro 10.0.26320, .NET SDK 11.0.100 (Release, JIT, `InvariantGlobalization=true` for `WireProbe`),
Node v24.21.0, Microsoft Edge 158.0.4370.16 (the installed WebView2 runtime is 157.0.4341.46), SQL Server 17.0.1155.3
(local default instance), Microsoft.Data.SqlClient 7.1.2. Local time zone W. Europe Standard Time (+01:00). Other agents
were running spikes, so timings are indicative; repeated runs are given as ranges.

| Path | Contents |
|---|---|
| `WireProbe/` | Console probe, one class per item: `LongProbe`, `TypesProbe`, `SurrogateProbe` (with the `LosslessString` prototype), `NullProbe`, `DuplicateProbe` (with a generated-style reader), `WriterErrorProbe` (with buffered dispatch and stream prototypes). Repository settings: warnings as errors, `IsAotCompatible`, STJ only through source-generated contexts. |
| `SqlProbe/` | Read-only `SELECT`s over shared memory with integrated security (`Pooling=false`, 5 s timeout): what SqlClient returns for `nvarchar` with lone surrogates, `decimal(38)`, `money`, dates, `uniqueidentifier`, `@@DBTS`, `float`, and the `bigint` values a monitor reads. Nothing is created or changed on the server. |
| `js/long.mjs`, `js/types.mjs`, `js/surrogates.mjs`, `js/null-dup-errors.mjs` | Node side of each item; they read the probe's `results/*.json`. |
| `js/engine-probe.html`, `js/engine-probe.mjs` | Feature probe run in Edge through the Playwright driver already in the NuGet cache (`microsoft.playwright/1.64.1`); no download. |
| `ts/ergonomics.ts` | TypeScript usage of each `long` option, checked with `tsc` 7.0.2 (from `../npm-ignore-scripts/react/frontend/node_modules`). |
| `results/` | `csharp.md` (all probe output), `js-*.md`, `sql-probe.md`, `engine-edge.json`, `ts-ergonomics.txt`, and the JSON exchanged between the two sides. |

Reproduce, from `spikes/wire-details`:

```powershell
dotnet build WireProbe -c Release; .\WireProbe\bin\Release\net11.0\WireProbe.exe > results\csharp.md
node js/surrogates.mjs; .\WireProbe\bin\Release\net11.0\WireProbe.exe surrogates-back
node js/long.mjs; node js/types.mjs; node js/null-dup-errors.mjs; node js/engine-probe.mjs
dotnet build SqlProbe -c Release; .\SqlProbe\bin\Release\net11.0\win-x64\SqlProbe.exe     # optional data source argument
node ..\npm-ignore-scripts\react\frontend\node_modules\typescript\bin\tsc -p ts
```

## 1. `long` and `ulong` beyond ±(2^53 − 1)

### Observed

**C#.** `WriteNumberValue(long)` writes every value exactly. `TryGetInt64` accepts `-0` (as 0) and rejects `1e3`, `1.0`,
`1E+21` and `9007199254740992.0`; `9223372036854775808` fails `TryGetInt64` and passes `TryGetUInt64`. For strings,
`Utf8Parser.TryParse` and `long.TryParse` are too lenient for a canonical form: they accept `+1`, `007` and `-0`, and
`Utf8Parser` also stops early on `1 ` and `1e3` (consumed 1 of 2 or 3 bytes). A 15-line canonical check
(`-?(0|[1-9][0-9]*)`, at most 19 digits, then `Utf8Parser` with full consumption) rejects all of them.

**JavaScript (`JSON.parse`).**

| Written by C# | `JSON.parse` gives | `JSON.stringify` sends back |
|---|---|---|
| `9007199254740991` (2^53 − 1) | exact | same |
| `9007199254740992` | exact (a power of two), but not a safe integer | same |
| `9007199254740993` | **9007199254740992** | `9007199254740992` |
| `-9007199254740993` | **-9007199254740992** | `-9007199254740992` |
| `9223372036854775807` (`long.MaxValue`) | **9223372036854775808** | `9223372036854776000` |
| `-9223372036854775808` (`long.MinValue`) | exact (a power of two) | `-9223372036854776000` |
| `18446744073709551615` (`ulong.MaxValue`) | **18446744073709551616** | `18446744073709552000` |

Sending: `JSON.stringify({ id: 9007199254740993n })` throws `TypeError: Do not know how to serialize a BigInt`. A replacer
can write it as a string, or as a number through `JSON.rawJSON`. A page that types `2 ** 53 + 1` sends
`9007199254740992`, `1e21` goes as `1e+21`, which `TryGetInt64` rejects, and `1.5` goes as is.

**Engines.** `JSON.parse` source-text access (`context.source`) and `JSON.rawJSON` exist in Node 24 and Edge 158.
Not checked in WebKitGTK.

### The options, measured

100,000 rows `{"id":…,"qty":…,"name":"row …"}`, Node 24, minimum of 9 runs, two runs reported. The "safe" ids are
10^9 + 7i; the "full-range" ids are random 64-bit values, 99,897 of which are outside the safe range.

| Option | Payload | Parse | Exact ids | TypeScript type |
|---|---|---:|---:|---|
| A: number | safe ids, 4.58 MB | 14.0–16.4 ms | 100,000 | `number` |
| A: number | full-range ids, 5.52 MB | 16.3–16.7 ms | **603** | `number` |
| B: always a string | full-range, 5.72 MB | 13.8–14.9 ms | 100,000 | `string` |
| B → `bigint`: string + generated post-pass `BigInt()` | full-range | 19.5–20.2 ms | 100,000 | `bigint` |
| B → `bigint`, safe ids | safe, 4.78 MB | 17.6–19.1 ms | 100,000 | `bigint` |
| C: a string only when out of range | full-range | 13.3–15.9 ms | 100,000 | `number \| string` |
| D: number + reviver with `context.source` → `BigInt` | full-range | 137.7–151.9 ms | 100,000 | `bigint` |
| (an identity reviver alone) | safe | 131.4–132.3 ms | — | — |
| D′: regex quotes `"id":` numbers, then B → `bigint` | full-range | 26.8–29.5 ms | 100,000 | `bigint` |

Sending 100,000 rows: numbers 8.0 ms (8.5–8.6 ms with a `Number.isSafeInteger` check per id); strings 8.6–8.7 ms;
`bigint` through a generated pre-pass to strings 12.4–13.1 ms; through a replacer 26.8–26.9 ms; as numbers through
`JSON.rawJSON` 40.2–42.5 ms.

C#, 1,000,000 random 64-bit values (minimum of 5 runs, three runs): writing numbers takes 12.0–12.2 ms; writing strings
takes 17.4–18.5 ms (41.4 ms in one run under load) and 2 more bytes per value (22.38 MB against 20.38 MB). Reading
numbers with `TryGetInt64` takes 31.1–35.8 ms; reading strings with the canonical check, 35.6–36.2 ms.

TypeScript 7.0.2 (`results/ts-ergonomics.txt`):

- `string`: `id + 1` compiles and concatenates (`"41" + 1 = "411"`); arithmetic is TS2362/TS2363.
- `number | string`: every use needs narrowing (TS2365, TS2345).
- `bigint`:
  - `id + 1` is TS2365, while `id + 1n` and `Number(id)` work;
  - `Math.max(id, 1n)` is TS2345, and a `(a, b) => a.id - b.id` comparator is TS2345 (it returns `bigint`);
  - `===`, template literals and `Map` keys work by value;
  - `@types/react` 19 includes `bigint` in `ReactNode`, so it renders;
  - `JSON.stringify` of a `bigint` type-checks and then throws at run time, so only the generated encoder may serialize
    it.

### What the SQL tool needs

- **`bigint IDENTITY(1,1)`** never gets near 2^53 in practice: that takes 9 × 10^15 inserts, 285 years at a million per
  second. But an identity seeded at `-9223372036854775808` (a common way to double the range) is out of the safe range
  from its first row.
- **Monitor values cross it.** `sys.dm_os_sys_info.cpu_ticks` was 546,893,217,367,201 after 182,432 s of uptime
  (3.0 × 10^9 ticks per second, 6.07% of 2^53). It passes 2^53 after about 35 days of server uptime.
  `CAST(query_hash AS bigint)` from `sys.dm_exec_query_stats` was −4,944,027,996,587,653,929.
- **A data-copy and compare tool** handles arbitrary user `bigint` columns, so it cannot tolerate rounding.

### Proposal

`long` and `ulong` travel as JSON strings in canonical decimal form, and the generated TypeScript decoder converts them
to `bigint`. The encoder converts `bigint` to a string. The C# reader accepts only the canonical string: no JSON number,
no escapes, no `+`, no leading zeros, no `-0`. `int`, `uint`, `short`, `ushort`, `byte` and `sbyte` stay JSON numbers,
because they are always safe.

Rejected:

- A, a number with a range check, fails at run time on legitimate data, as the monitor values above show.
- C, a string only when out of range, gives an unusable union type.
- D, a reviver, is 9–10 times slower and depends on source-text access in every engine.

An author who wants a plain `number` for a counter can declare `double`.

## 2. Types the SQL tool needs

### Observed (C# writer, `results/csharp.md`; JavaScript, `results/js-types.md`; SqlClient, `results/sql-probe.md`)

**`decimal`.**

- STJ writes a JSON number with the scale kept (`1.50`, `922337203685477.5807`, `79228162514264337593543950335`) and reads
  each back exactly.
- `JSON.parse` changed 5 of 7 values:

  | C# `decimal` | JavaScript number |
  |---|---|
  | `1.50` | `1.5` |
  | `922337203685477.5807` (`money` max) | `922337203685477.6` |
  | `12345678901234567890.123456789` | `12345678901234567000` |
  | `decimal.MaxValue` | `7.922816251426434e+28` |
  | `0.0000000000000000000000000001` | `1e-28` |

- `GetDecimal` rounds a 30-digit input to 28 places and rejects `decimal.MaxValue + 1`.
- **SQL `decimal(38,s)` does not fit `System.Decimal`.** For `CAST(99999999999999999999999999999999999999 AS decimal(38,0))`,
  SqlClient's `GetDecimal` and `GetValue` throw `OverflowException`, and `SqlDecimal.Value` throws too. Only
  `GetSqlDecimal().ToString()` gives the value.
- SqlClient returned `decimal(10,4)` 1.5 as `1.5000`, `money` max as `922337203685477.5807` and `smallmoney` min as
  `-214748.3648`.
- In Edge 158, `Intl.NumberFormat` formats a decimal string exactly (`"12345678901234567890.123456789"` →
  `12,345,678,901,234,567,890.123456789`), so a decimal string can be displayed without precision loss.

**`DateTime`.**

- STJ writes the suffix by `Kind`: `Z` for `Utc`, `+01:00` for `Local`, nothing for `Unspecified`. Up to 7 fraction
  digits, trimmed (`…T10:20:30Z` for whole seconds).
- On this machine, all three kinds round-trip with ticks and `Kind` equal.
- Reading `…+01:00` gives `Kind=Local`, converted to the machine's zone. `Z` gives `Utc`. A date-only string is accepted
  as `Unspecified`. Eight fraction digits are truncated. A space instead of `T` is rejected.
- SqlClient returns `datetime2` **and `SYSUTCDATETIME()`** with `Kind=Unspecified`, so SQL values carry no suffix.
- In JavaScript, `new Date()` of the no-suffix form uses the *page's* time zone:

  | Page time zone | `new Date("2026-11-14T10:20:30.1234567")` |
  |---|---|
  | UTC | 10:20Z |
  | Los Angeles | 18:20Z |
  | Tokyo | 01:20Z |

  Sub-millisecond digits are dropped in every case (`.123`).

**`DateTimeOffset`.** Written as `…T10:20:30.1234567+01:00`, with `+00:00` rather than `Z`, and read back with the
offset and ticks equal. Reading a string without an offset silently assumes the machine's offset (+01:00). JavaScript
`Date` keeps the instant at millisecond precision and drops the offset. SqlClient gives `datetimeoffset(7)` with
100 ns.

**`DateOnly`, `TimeOnly`, `TimeSpan`.**

- `DateOnly` is `"2026-11-14"`, and STJ rejects a value with a time part. **`new Date("2026-11-14")` is UTC midnight,
  which is 13 November in Los Angeles.**
- `TimeOnly` is `"10:20:30.1234567"` or `"10:20:00"`. `new Date()` gives `Invalid Date`.
- `TimeSpan` uses the constant format: `"1.02:03:04.5000000"`, `"-00:00:01"`, `"10675199.02:48:05.4775807"`.
  STJ rejects ISO 8601 `"PT1H"`.
- `Temporal` is missing in Node 24 and present in Edge 158. There, `Temporal.PlainDateTime.from` keeps all 7 digits, and
  `Temporal.Duration` prints `P1DT2H3M4.5S`.
- SqlClient returns `date` as `DateTime` from `GetValue` and as `DateOnly` from `GetFieldValue<DateOnly>`, and `time(7)`
  as `TimeSpan` or `TimeOnly`, all to 100 ns.

**`Guid`.** Written lower-case `D` (`"3f2504e0-4f89-11d3-9a0c-0305e82c3301"`). `TryGetGuid` accepts upper case and
rejects braces, `N` and a leading space. SQL's `CAST(… AS varchar)` gives upper case, and `crypto.randomUUID()` gives
lower case.

**`byte[]`.**

- `WriteBase64StringValue` writes standard padded base64: rowversion `0x00000000000007D1` is `"AAAAAAAAB9E="`, and
  `@@DBTS` came back as `byte[]`.
- `TryGetBytesFromBase64` rejects missing padding and URL-safe characters. It **accepts embedded spaces and escaped
  newlines** (`"AQ ID"`).
- In JavaScript, `Uint8Array.fromBase64`/`toBase64` exist in Edge 158 and not in Node 24. The `atob` fallback decodes
  1 MiB in 1.6 ms; `btoa` encodes it in 25.8 ms.
- Base64 is 1.33 times the bytes; a JSON array of numbers would be 3.57 times.

**`char`.** Written as a one-character string. `'\uD800'` is written as `"\uFFFD"`, the same as strings.
`"ab"` is rejected.

**`double`/`float`.**

- `WriteNumberValue(NaN or ±Infinity)` throws `ArgumentException` on any writer. STJ's named-literal option writes
  `"NaN"`, a string.
- `-0` is written as `-0`; `JSON.parse` keeps it, and `JSON.stringify` sends `0`.
- **In JavaScript, `JSON.stringify({ x: NaN })` is `{"x":null}`**, the same for ±Infinity. A NaN in a `double?` field
  would arrive in C# as a valid `null`. `JSON.parse("1e999")` is `Infinity`.
- `float` is written shortest (`0.1`, which `JSON.parse` reads as 0.1).
- **`TryGetSingle("3.5e38")` returns `Infinity`**, so the R4 overflow rule must cover `float` too.
- SQL `float` cannot hold non-finite values: a NaN parameter is rejected with a TDS error, and overflow is an arithmetic
  error.

**Enums.**

- STJ writes numbers by default. With the string converter it writes the declared name (`"Warning"`); the camelCase
  property policy does not apply to values.
- An undefined value is written as a number (`42`). A `[Flags]` combination is written as `"Read, Write"`, and an
  undefined bit as `8`.
- Reading is case-insensitive (`"warning"` → `Warning`) and accepts the number `42` even with the string converter.
  `"Bogus"` is rejected.

### Proposal

See the table. In short:

- `decimal` is a canonical decimal string (`-?\d+(\.\d+)?`, scale kept, no exponent) typed `string`. For SQL values
  beyond 28 digits the app uses `string` from `SqlDecimal.ToString()`.
- Date and time types are strings in the STJ forms, typed `string`. The SDK documents that `new Date()` must not be used
  on `DateTime`, `DateOnly` or `TimeOnly` text, and offers `Temporal` where the engine has it.
- `Guid` is lower-case `D`, read case-insensitively.
- `byte[]` is strict padded base64 without whitespace, typed `Uint8Array`.
- `char` is a one-code-unit string.
- `double`/`float` must be finite in both directions, and the TypeScript encoder checks `Number.isFinite` before
  sending.
- An enum is its member name, matched exactly, typed as a union of string literals. An undefined value is a writer
  failure (item 6).

## 3. Lone surrogates

### Observed

| What | Result |
|---|---|
| `Utf8JsonWriter.WriteStringValue("a\uD800b")` with the default, `JavaScriptEncoder.Default`, `UnsafeRelaxedJsonEscaping` and `Create(UnicodeRanges.All)` encoders | `"a\uFFFDb"` for all four; the same for a lone low, reversed, and trailing high surrogate; the same for `ReadOnlySpan<char>`, a property name, `SkipValidation = true` and `JsonSerializer`. Never throws. |
| `JsonEncodedText.Encode("a\uD800b")` | throws `ArgumentException` (cannot transcode invalid UTF-16) |
| `WriteStringValue` of UTF-8 bytes `ED A0 80` (CESU) or `FF` | `"a\uFFFD\uFFFD\uFFFDb"`, `"a\uFFFD"`: replaced, no error |
| `WriteRawValue("\"\ud800\"")` with validation | accepted |
| `Utf8JsonReader` on `"\ud800"` or `"a\udc00b"` | `Read()` succeeds (`ValueIsEscaped`); `GetString()`, `CopyString()` and `JsonDocument.GetString()` throw `InvalidOperationException`; `JsonSerializer` throws `JsonException` |
| `Utf8JsonReader` on raw bytes `ED A0 80` | `Read()` succeeds; `GetString()` throws `InvalidOperationException` (invalid UTF-8) |
| `JSON.stringify("a\ud800b")` | `"a\ud800b"` (escaped; well-formed `JSON.stringify`), so **the page's natural output is rejected by every .NET reader** |
| `JSON.parse('"\ud800"')` | a one-unit string `d800` |
| `TextEncoder`, a `fetch`/`Response` body of the raw string | `61 EF BF BD 62`: UTF-8 encoding replaces it |
| `encodeURIComponent("a\ud800b")` | `URIError` |
| `isWellFormed()` / `toWellFormed()` | in Node 24 and Edge 158 |
| **SqlClient**, `SELECT N'a' + NCHAR(0xD800) + N'b'` | the server stores `61 00 00 D8 62 00`, but `GetString` returns `0061 FFFD 0062` |
| **SqlClient**, parameter `@p = "a\uDC00b"` (`nvarchar`) | the server receives `61 00 FD FF 62 00`: replaced before sending |

**Prototypes** (`LosslessString` in `SurrogateProbe.cs`):

- **Lossless.** The writer escapes each lone surrogate as `\udXXX` and takes the normal path for well-formed strings,
  with byte-identical output. The reader calls `GetString()` and, only when that throws on a surrogate escape, unescapes
  itself. Every case round-tripped C# → JSON → `JSON.parse` → `JSON.stringify` → C# with identical code units.
- **Replacing.** The same reader fallback, but it writes U+FFFD.
- Raw invalid UTF-8 stays a `JsonException` in both.

Cost, 1,000,000 well-formed strings (a quarter non-ASCII, minimum of 5 runs):

| Path | Time |
|---|---:|
| `WriteStringValue` | 22.8–33.3 ms |
| lossless writer (`IndexOfAnyInRange` scan first) | 29.3–29.8 ms |
| `GetString` | 55.8–58 ms |
| reader with fallback (lossless) | 59.8 ms |
| reader with fallback (replacing) | 60.8 ms |

Each string that actually takes the fallback costs about 2.5 µs (an exception and an unescape).

### Proposal

**Spar strings are sequences of Unicode scalar values. A lone surrogate becomes U+FFFD in either direction, and it is
never an error.**

- The C# writer already does this, at no cost.
- The C# reader needs the fallback above, in replacing mode, so that `JSON.stringify` output with a lone surrogate is
  accepted. Today it fails the whole request.
- The TypeScript side needs nothing: `JSON.stringify` escapes, and C# replaces. It never receives a lone surrogate.
- This matches UTF-8 everywhere else (`TextEncoder`, `fetch` bodies, `Encoding.UTF8`, files and logs) and, decisively
  for the SQL tool, **SqlClient's own behaviour in both directions**.

Rejected:

- **Reject** would fail a whole response because one value was cut in the middle of an emoji (`Substring`, `slice`).
- **Lossless** works (prototype above), but it costs custom writer and reader code and buys nothing for SQL text. It
  would matter only if a value arrived as `varbinary`, which the app can send as `byte[]` instead.

## 4. Top-level `null`, `void` and `Result<T?, E>`

### Observed

- `Utf8JsonReader` reads `null` as a `Null` token. An empty or whitespace payload throws `JsonReaderException` ("does
  not contain any JSON tokens"). STJ's `Deserialize<string>("null")` returns null.
- In JavaScript:
  - `JSON.stringify(undefined)` returns `undefined` (not a string);
  - `JSON.stringify({ a: undefined, b: null })` is `{"b":null}`, and `[undefined, null]` becomes `[null,null]`;
  - `JSON.parse('{"ok":true}').value` is `undefined`, and `'value' in` distinguishes `{"ok":true}` (false) from
    `{"ok":true,"value":null}` (true);
  - `JSON.parse('')` throws.
- A decoder prototype that takes the return kind from the contract decoded all five envelopes. It reported a protocol
  error for `null` where the result is non-nullable and for a value where the command is void.

### Proposal

| C# return | Envelope | TypeScript |
|---|---|---|
| `Task` / `ValueTask` | `{"id":…,"ok":true}`, no `value` key | `Call<void>`, resolves `undefined` |
| `Task<T>` | `{"ok":true,"value":V}`, V never `null` | `T` |
| `Task<T?>` | `{"ok":true,"value":null}` or a value; the key is always present | `T \| null` |
| `Task<Result<T?, E>>` | success as `Task<T?>`; error `{"ok":false,"error":{"code":…}}` | `T \| null` in `success` |
| a stream of `T?` | `{"type":"item","value":null}` | `T \| null` per item |
| nullable DTO property, response | always written, as `null` when null | `p: T \| null` |
| nullable DTO property, request | missing or `null` both read as null | `p?: T \| null` (`JSON.stringify` drops `undefined`) |
| request argument | `"arg"` key present only when the command takes one; `null` only if declared nullable | parameter, or none |

`undefined` appears in TypeScript only as `void` and as an optional request property, never as a value. A `null`
returned at run time for a non-nullable `T` (C# annotations can lie) is a writer failure (item 6), not a `null` on the
wire. The rule that top-level `null` and `null` list items are rejected unless declared nullable stays.

## 5. Duplicate properties

### Observed

The probe read `{…,"qty":1,…,"qty":2}`, the same with the second name escaped as `"q\u0074y"`, a case variant `"Qty"`,
and a repeated unknown property `"extra"`:

| Reader | `qty` twice | `q\u0074y` | `Qty` | `extra` twice |
|---|---|---|---|---|
| `Utf8JsonReader` | tokenizes both | both, unescaped | both | both |
| `JsonDocument`, default | last wins (2) | last wins | `Qty` is another property | accepted |
| `JsonDocument`, `AllowDuplicateProperties = false` | `JsonException: Duplicate property 'qty'` (no position) | rejected | accepted | **rejected** |
| `JsonNode.Parse`, default | `ArgumentException` on access ("same key has already been added") | same | accepted | same |
| `JsonSerializer`, default | last wins | last wins | ignored | accepted |
| `JsonSerializer`, `AllowDuplicateProperties = false` | `JsonException` with type name | rejected | ignored | **accepted** (unknown properties are not tracked) |
| generated-style reader, bit per declared property | `JsonException: Duplicate property at byte 68` | rejected (`ValueTextEquals` unescapes) | ignored | accepted |
| `JSON.parse` | last wins (2) | last wins | separate key | last wins |

`JSON.parse('{"__proto__":{"x":1}}')` creates an own `__proto__` key without changing the prototype, but
`Object.assign({}, that)` then sets the copy's prototype (`x` inherited = 1). The generated client should build its
objects field by field, not by copying parsed objects.

Cost: 1,000,000 parses of a 76-byte, 6-property object took 142–145.7 ms with last-wins and 141–147.6 ms with the bit
mask, no measurable difference. STJ source-generated took 275.9–325.2 ms, 300.2–308.1 ms with
`AllowDuplicateProperties = false`.

### Proposal

The C# reader rejects a second occurrence of a declared property, compared after unescaping, with a `JsonException`
that gives the declared name and the byte offset of the duplicate (`TokenStartIndex`). Unknown properties are skipped
without tracking, as STJ's serializer does. JavaScript needs no check: it only parses what the generated C# writer
produced, which never repeats a name.

## 6. Exceptions while writing the response

### Observed

| Failure | Exception (pooled writer: `SkipValidation = true`, `MaxDepth = 64`) |
|---|---|
| NaN or ±Infinity | `ArgumentException` |
| depth 65 | `InvalidOperationException` ("CurrentDepth (64) is equal to or larger than the maximum allowed depth of 64"); `SkipValidation` does not disable it |
| a property getter throws (user code) | whatever it throws (`InvalidOperationException` here) |
| lone surrogate, invalid UTF-8 bytes | no exception: replaced with U+FFFD |

- **Reuse after a failure.** After each exception, `BytesCommitted` was 0 and the `ArrayBufferWriter` held 0 bytes:
  the writer had flushed nothing. `BytesPending` was 9–72 and `CurrentDepth` 1–64. `writer.Reset(buffer)` plus
  `buffer.ResetWrittenCount()` then produced a clean `{"ok":true}`, so a pooled writer can be reused after a failure.
- **Buffered unary prototype.** On any exception the dispatch discards the buffer and writes
  `{"id":7,"ok":false,"failure":{"kind":"response-serialization","execution":"completed","detail":"ArgumentException"}}`.
  Node's client prototype classified NaN, depth 65 and the throwing getter as `ResponseSerializationError`.
- **Stream prototype.** Item 3 of 5 was NaN. Items 1 and 2 were sent. Then came
  `{"id":9,"type":"end","ok":false,"failure":{"kind":"response-serialization","execution":"stopped-after-items","itemsSent":2,…}}`.
  The enumerator's `finally` ran when it was disposed.
- **Unbuffered unary** (a writer on the transport stream, flushing every 16 KB, failing at element 50,000 of 100,000):
  278,560 bytes ending in `,48272,48273` had already left. `JSON.parse` of them throws `SyntaxError` at position
  278,560.

### Proposal

- **Unary responses are always fully buffered** in the pooled writer before the transport sees a byte. The M1
  requirement for a reused, pooled writer already implies it. Any exception while writing the result is caught by dispatch, which:
  - resets the writer and buffer;
  - logs the exception (type, message, contract, command, call id);
  - sends `{"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}`, adding the exception type
    as `detail` in non-Production variants only.

  The same path covers an undefined enum value, a `null` in a non-nullable result, and any exception from user code that
  runs during serialization (getters, custom collections).
- **TypeScript:** `ResponseSerializationError`, an infrastructure failure with `execution: "completed"`. It goes to the
  `failure` handler or the app-wide interceptor and is never retried, because the command's effects happened.
- **"How much was written" can then never be unclear for a unary call.** If a transport breaks mid-message anyway
  (connection loss), the page already gets `OutcomeUnknownError` (`lost`). Large results should be streams. A
  configurable cap on a unary response size would make a too-large result a writer failure too; that cap is not measured
  here.
- **Streams:** each item is serialized into its own buffer before it is sent, so a failure never splits an item. The
  stream ends with `{"type":"end","ok":false,"failure":{"kind":"response-serialization","execution":"stopped","itemsSent":N}}`,
  and dispatch disposes the enumerator. Disposal does not prove the producer's side effects stopped, so nothing is
  replayed. TypeScript: the async iterator yields the N items, then throws `ResponseSerializationError` with
  `itemsReceived`.
- **Loopback HTTP:** the same envelope with status 500. The client trusts `execution` only from a well-formed envelope
  for its call id; anything else is `OutcomeUnknownError`.

## Recommendation

| Item | Rule | C# behaviour | JSON | TypeScript type | Cost (measured) |
|---|---|---|---|---|---|
| `long`, `ulong` | String, canonical decimal | Write formatted digits; read only `-?(0\|[1-9]\d*)` within range; reject numbers, escapes, `+`, leading zeros, `-0` | `"9007199254740993"` | `bigint` (generated decode and encode) | +2 B per value. C#: write +5–6 ms per 1M, read ≈ same. JS: +5–6 ms per 100k to decode, +4 ms to encode |
| `int`, `uint`, `short`, `ushort`, `byte`, `sbyte` | Number | as now | `42` | `number` | none |
| `decimal` | String, canonical, scale kept, no exponent; SQL beyond 28 digits goes through `string` in the app | Format and parse invariant; overflow → `JsonException` | `"922337203685477.5807"` | `string` | not measured (same shape as `long`) |
| `DateTime` | ISO 8601 as STJ writes it (`Z` for Utc, offset for Local, none for Unspecified; ≤ 7 fraction digits, trimmed) | STJ `GetDateTime` forms | `"2026-11-14T10:20:30.1234567"` | `string` | none |
| `DateTimeOffset` | ISO 8601 with offset `±hh:mm` | require an offset on read (STJ assumes local when missing) | `"2026-11-14T10:20:30.1234567+01:00"` | `string` | none |
| `DateOnly` | `yyyy-MM-dd` | exact form only | `"2026-11-14"` | `string` | none |
| `TimeOnly` | `HH:mm:ss[.fffffff]` | exact form only | `"10:20:30.1234567"` | `string` | none |
| `TimeSpan` | Constant format `[-][d.]hh:mm:ss[.fffffff]` | exact form only | `"1.02:03:04.5000000"` | `string` | none |
| `Guid` | Lower-case `D` | read `D`, any case | `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"` | `string` | none |
| `byte[]` | Standard padded base64, no whitespace | reject whitespace (STJ accepts it), missing padding and URL-safe characters | `"AAAAAAAAB9E="` | `Uint8Array` (`fromBase64`, or the `atob` fallback) | 1.33× size; JS decode 1.6 ms/MiB on the fallback |
| `char` | One UTF-16 code unit as a string; lone surrogate → U+FFFD | read exactly one unit | `"a"` | `string` | none |
| `double`, `float` | Finite only | writer failure on NaN/±Infinity; reader rejects overflow for `double` **and `float`** (R4) | `0.1`, `-0` | `number`; encoder throws `RangeError` unless `Number.isFinite` | one check per value |
| enum | Member name, exact match | undefined value → writer failure; unknown name → `JsonException` | `"warning"` or `"Warning"` (decision) | string-literal union | none |
| `[Flags]` enum | Not yet supported (decision) | diagnostic | — | — | — |
| `string` | Lone surrogate → U+FFFD both ways, never an error | writer: as now; reader: replacing fallback when `GetString` throws; raw invalid UTF-8 → `JsonException` | `"a\uFFFDb"` | `string` (nothing to do) | write: none; read: 59.8–60.8 ms against 55.8–58 ms per 1M valid strings (+3–9%, one run, not separated from noise), 2.5 µs per string that needs the fallback |
| top-level `null` / void | `value` absent for void, present (possibly `null`) for `T?`; nullable DTO properties written explicitly, read missing-or-null | null for a non-nullable result → writer failure | `{"ok":true}` / `{"ok":true,"value":null}` | `void` → `undefined`; `T?` → `T \| null`; request props `?: T \| null` | none |
| Duplicate properties | Reject repeated declared properties (after unescaping); skip unknown ones | `JsonException` with name and byte offset | — | none needed | none measurable (bit mask) |
| Writer exceptions | Buffer every unary response; per-item buffers for streams | catch, reset the pooled writer, log, send a failure | `{"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}`; streams: `"execution":"stopped","itemsSent":N` | `ResponseSerializationError` (`execution`, `itemsReceived`) | none on success (buffering is already the plan) |

## Decisions for the user

1. **`long` in TypeScript:** `bigint` (recommended) or `string`. The wire is a string either way.
2. **Lone surrogates:** replace (recommended), lossless, or reject.
3. **Enum names:** camelCase like contract, command and error-code names, or the declared name. And `[Flags]`: an array of
   names, a number, or unsupported for now.
4. **`byte[]` in TypeScript:** `Uint8Array` (recommended) or the base64 `string`, which suits rowversion tokens that the
   page only echoes back.
5. **`TimeSpan`:** STJ's constant format (recommended for parity with .NET), or ISO 8601 durations, which
   `Temporal.Duration` parses and STJ does not.
6. **`DateTime` strictness:** accept STJ's three suffix forms (recommended), or allow only `Unspecified` on the wire.
7. **SQL `decimal(38)`:** apps use `string`, or Spar adds a wire type for `SqlDecimal`.
8. **Stream failure wording:** `execution: "stopped"` with `itemsSent` (proposed), and whether loopback uses status 500.

## Proposed pitfalls (not yet added to `docs/pitfalls.md`)

- SqlClient replaces lone surrogates in `nvarchar` with U+FFFD in both directions; the server stores them intact.
- `new Date()` of `DateTime` without an offset, or of `DateOnly`, uses the page's time zone (13 November in Los Angeles
  for `"2026-11-14"`).
- `JSON.stringify` turns NaN and ±Infinity into `null`; in a nullable field that is a silent data change.
- `TryGetSingle` returns `Infinity` for `3.5e38`; `TryGetBytesFromBase64` accepts embedded whitespace;
  STJ enum reading is case-insensitive and accepts numbers.
- `--ignore-scripts` in `.npmrc` also disables `pre`/`post` hooks of `npm run` (see `spikes/npm-ignore-scripts`).

## Observed, skipped, not tested

**Observed (real execution):**

- every `WireProbe` section, run two or three times; the Node scripts, at least twice;
- the C# → Node → C# surrogate round trip;
- the TypeScript 7.0.2 check;
- the Edge 158 feature probe;
- the SqlClient probe against the local SQL Server (read-only).

**Skipped:**

- NativeAOT runs of the probe (not required; `WireProbe` builds with `IsAotCompatible` and no warnings, but it was not
  published);
- BenchmarkDotNet (timings are Stopwatch minimums under background load);
- decimal and date formatting costs.

**Not tested:**

- WebKitGTK: `JSON.parse` source access, `Temporal`, `Uint8Array.fromBase64` and `isWellFormed` are unverified on Linux;
- the WebView2 runtime itself: the probe used Edge 158, and the runtime is 157;
- `chrome.webview.postMessage` with lone surrogates;
- `SqlBulkCopy` with lone surrogates;
- the real M1 generator, dispatch and TypeScript client (all prototypes here are spike code);
- `bigint` rendering in React (taken from the `@types/react` declaration only);
- time zones other than the machine's on the C# side (the JavaScript date checks ran under UTC, Los Angeles and Tokyo).
