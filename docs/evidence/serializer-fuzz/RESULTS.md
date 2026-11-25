# Serializer fuzz: generated `Utf8Json*` code (A) vs System.Text.Json (B), differential

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/serializer-fuzz/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/serializer-fuzz) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** Before M1, where does the generated serializer chosen in `docs/decisions.md` (2026-11-13, "Wire serialization: generated code, chosen by speed then size") diverge from System.Text.Json source generation, and where does it crash?

**Answer.** No crash and no hang in 1,320,000 distinct generated and mutated cases: seeds 1 and 2 at 600,000 each, and seed 3 at 120,000. That is 2,040,000 case executions across the baseline, final, JIT and NativeAOT runs. On valid input A and B agree everywhere: 240,000 valid documents in varied formatting gave equal values, and A's writer output was byte-identical to B's for every value both could write. The divergences are:

1. **Two known bugs, now measured:** A accepts trailing content (13,103 of 360,000 mutants on seed 1), and 31,418 mutants (8.7%) make A throw `InvalidOperationException` or `FormatException` instead of `JsonException`.
2. **Three new findings:**
   - A writes values nested 65–999 deep that its own reader rejects (751 of 120,000 values).
   - Every reader, B included, accepts overflowing number literals such as `1e999` as ±Infinity, which then cannot be written back (427 mutants).
   - The tuned emitter (A2) emits code that does not compile when an array element type is nullable.
3. **Two contract gaps the fixes do not close:**
   - Every writer, B included, silently replaces lone surrogates with U+FFFD (2,762 values do not round-trip).
   - `JSON.parse` rounds every `long` above 2^53 (62,722 of 111,983 documents).

Four fixes in a copy of the generator (F) remove every non-contract divergence. F against a strict System.Text.Json reference has **0** "accepts what the reference rejects", **0** non-`JsonException` failures and **0** unwritable values. Its only remaining differences are rejections the contract intends. See [Fixes](#fixes-as-generator-requirements).

## Setup

Everything is under `spikes/serializer-fuzz/`. Nothing outside this folder changed. `spikes/serializer-bench` and other spikes were read but not built or modified.

| Path | Contents |
|---|---|
| `Fuzz.Generator.Original/` | Copy of `serializer-bench/Bench.Generator`. One change, in `CSharpEmitter.TunedItems`: list elements are first assigned to a local (finding R0 below). Without it the copy does not compile against the fuzz DTOs. |
| `Fuzz.Generator.Fixed/` | The same copy retargeted to `Spar.Generated.Fixed` / `Bench.SparJsonFixed`, with the fixes (`// FIX` comments). |
| `Fuzz.Runtime/` | `SparJson.cs` and `Attributes.cs` copied unchanged. `SparJsonFixed.cs` is the fixed runtime. |
| `Fuzz/` | Harness (console app): DTOs and contract, STJ contexts, value generator, renderer, mutator, worker, coordinator, minimiser, `js-check.mjs`. |
| `results/` | Reports of the runs below (`*.md`) and every outcome group (`*.groups.tsv`). |

### What is compared

Five targets, each with the generated `Parse…`/`Serialize…`/`WriteTuned…` codecs from both generators:

- the bench DTO `Note`, unchanged;
- `Note[]`;
- `Wide`, with every supported wire kind, nullable and not: `string`, `int`, `long`, `double`, `bool`, `T[]`, `IReadOnlyList<T>`, `long?[]`, `double[][]`, `IReadOnlyList<string?>?`, nested and recursive records;
- top-level `int`;
- top-level `string?`.

| Reader | What |
|---|---|
| **A** | Original generator as copied (the D1/bench code). |
| **F** | Fixed generator. |
| **B** | System.Text.Json context with the bench (D1) options: Web defaults, `RespectNullableAnnotations`, `RespectRequiredConstructorParameters`. |
| **S** | Strict reference: General defaults with camelCase names (case-sensitive, no numbers in strings), the same two Respect switches, and a resolver modifier that makes nullable constructor parameters optional. S encodes A's intended contract, so **a divergence from B that disappears against S is a contract choice, not a bug.** |

Writers: A (`SparJson.Write`, as shipped), A2 (`WriteTuned…` on a writer with `SkipValidation = true`, the bench recommendation), F and F2 (fixed equivalents), and B (`SerializeToUtf8Bytes`).

### Case generation (deterministic)

Case *i* is a pure function of (seed, *i*): `new Random(splitmix(seed, i))`. Any case can be regenerated for minimisation or inspection. The case streams by `i mod 10`:

- **Value (20%).** A random DTO value. Writer and round-trip properties are checked on it.
- **Valid (20%).** The value rendered as a valid document in varied formatting. Expected result: A = B = S = the source value.
- **Mutant (60%).** 0–3 tree mutations, then (always when there were no tree mutations, otherwise half the time) one byte mutation.

Values:

- **Strings:** empty, ASCII, all C0 controls and DEL, `" \ / < > & ' + \``, 2- and 3-byte BMP, specials (U+00A0, U+0085, U+00AD, U+061C, U+200B, U+200D, U+2028, U+2029, U+E000, U+F8FF, U+FEFF, U+FFFD, U+FFFE, U+FFFF), and supplementary-plane characters from planes 1, 2, 3, 14, 15 and 16, including U+10FFFF.
- **Integers:** int and long limits, ±2^53 and ±(2^53 ± 1), and random 64-bit values.
- **Doubles:** ±0, ±`Epsilon`, min normal, max subnormal, ±`MaxValue`, 0.1, 1/3, 1e21, 1e-7, 2^53 + 1, random finite bit patterns, and rounded decimals.
- **Shapes:** null optionals, empty arrays, nesting up to 6 parents deep.
- **Large values (1 in 40):** strings of 1,000–300,000 characters and arrays of 50–2,000 items. An item budget of 5,000 caps the total.
- **Values JSON cannot represent (1 value case in 8):** NaN, ±Infinity, lone or reversed surrogates, and parent chains 63, 64, 65, 100, 999, 1000, 1001 and 5000 deep.

Valid-document formatting:

- whitespace: none, single spaces, or random space, tab, CR and LF;
- string escaping: like the default encoder, raw UTF-8, `\u` for every character, or mixed per character, with random hex case, short or `\u00XX` control escapes, and `/` as `/`, `\/` or `/`;
- property names escaped the same ways;
- numbers: shortest, `G17`, `E16`–`E39`, 40–800-digit `E` mantissas, `F0`–`F3` for integral values, an `e0`/`E+00` suffix, and `-0` for integer zero;
- shuffled property order;
- unknown properties holding nested values.

Tree mutations:

- drop, duplicate (same or different value), reorder or rename a property (PascalCase, upper case, trailing space, Cyrillic homoglyph, prefix, NUL suffix, empty name);
- a type swap, including numeric strings such as `"2.5"`; `null`-out;
- a malformed number: leading zeros, `+1`, `.5`, `1.`, `1e`, NaN, Infinity, hex, digit separators, `1e999`, 400-digit numbers, `2147483648`, `9223372036854775808`, `1e2` and `1.5` for integers;
- a malformed string: raw control characters, bad escapes, lone or reversed surrogate escapes, single quotes, unterminated;
- a malformed literal: `True`, `nul`, `undefined`;
- trailing commas in objects and arrays (`[,]`, `{,}`); doubled or missing separators;
- deep wrapping in arrays or objects, 1–100,000 levels, closed or not;
- `Note` parent chains as text, 1–10,000 deep;
- omitting a null nullable property;
- replacing the root with `null`, `[]`, `{}`, a string, a number, `[root]` or `{"value":root}`;
- an extra array item of the wrong type; a malformed property-name token.

Byte mutations:

- truncation at a random offset;
- trailing content: ` 42`, `{}`, `x`, NUL, `,`, `]`, `}`, whitespace, comments, a BOM, `null`, `"s"`, `[]`;
- a leading BOM;
- comments (`/*c*/`, `//c`, an unterminated `/*`, `#c`) at a token boundary;
- invalid UTF-8 inside a string or a property name: `FF`, `FE`, overlong `C0 80` and `E0 80 80`, lone continuation `80`, truncated `C3` and `E2 82`, CESU surrogates `ED A0 80` and `ED BF BF`, above U+10FFFF `F4 90 80 80`, 5-byte `F8…`;
- whitespace that is valid (tab, CR, LF, CRLF) or invalid (VT, FF, NBSP, U+3000, U+2028, NUL, U+FEFF) at a token boundary;
- a bit flip, byte insert or byte delete.

### Classification

For each text case, every reader runs, and the comparisons A~B, F~B, A~S and F~S each place the case in one class. Values are compared through a canonical form: STJ serialization with `MaxDepth` 1100, so `string[]` and `List<string>` compare equal and double bit patterns are preserved.

| Class | Meaning (left~right) |
|---|---|
| EQ | both accept, values equal |
| EQ-src | both accept and agree, but differ from the source value (valid stream only) |
| NE | both accept, values differ |
| RJ | both reject (left with `JsonException`) |
| XR-YA | left rejects, right accepts |
| XA-YR | left accepts, right rejects |
| X-nonjson | left throws something other than `JsonException` |
| X-unwritable | left accepts a value that cannot be written back as JSON (an infinity) |
| CRASH / HANG | the worker process died or was silent for 60 s |

Value cases run all five writers. A writer FAILs the case when:

- its output bytes differ from B's;
- it throws when B does not, or the other way round, or throws a different exception type;
- or the matching reader, and B, cannot read its output back to exactly the source value (ordinal strings, double bit patterns, element by element).

Writer outcomes are counted as `W` and node results as `JS`.

**JavaScript check.** For each batch, the outputs A wrote go through one `node` process (v24.21.0). It runs `TextDecoder` + `JSON.parse` and writes every number and string back, tagged, including `-0`. Each string must equal .NET's `GetString`. Each number must equal .NET's parse bit for bit, and each integer literal must be exactly representable.

**Isolation.** The coordinator runs 4,000-case batches in child processes (6 at a time) that print progress after every case. A child that exits early is recorded as a CRASH of the next case, and one silent for 60 s is killed and recorded as a HANG. The rest of the batch reruns in new children. **Self-test:** injected `Environment.FailFast` at case 5 and an infinite sleep at case 27. Both were reported, with the right case, exit code `0x80131623` and the 60 s kill, and the other 58 cases completed (`results/selftest.md`).

**Minimisation.** For the largest groups of each class (up to 14 per class for A~B and F~B, 8 for the S comparisons), the shortest case is shrunk by byte-level ddmin. The class and both normalised outcome keys must stay the same; the limit is 5,000 evaluations or 15 s per case. Messages are normalised by stripping paths and positions, digits and quoted input characters, so causes group together.

## Runs

| Run | Seed | Cases | Fuzzing time | Build |
|---|---:|---:|---:|---|
| `baseline` | 1 | 600,000 | 181 s | F identical to A (namespace only). Canonical round-trip check, which hides lossy writes. |
| `final-seed1` | 1 | 600,000 | 188 s | All four fixes in F; exact round-trip check; X-unwritable class |
| `final-seed2` | 2 | 600,000 | 187 s | same as final-seed1 |
| `aot-seed3` / `jit-seed3` | 3 | 120,000 each | 23 s / 37 s | NativeAOT publish of the harness vs the same build on CoreCLR |

Environment: Windows 11 Pro 10.0.26320, .NET SDK 11.0.100, runtime 11.0.0. Other agents were building in the repository at the same time, so times are indicative only. Every run finished well inside the 330 s fuzzing budget. The longest, minimisation included, took 193 s.

`baseline` and `final-seed1` share their A~B and A~S rows: same seed, same A, case for case. The only exception is the new X-unwritable class, which moves 427 cases out of EQ and XA-YR. The baseline's F rows equalled its A rows in every cell, which confirms the retargeted copy behaved identically before the fixes.

## Results

### Case counts, seed 1 (`results/final-seed1.md`)

Text cases, 360,000 mutants per row. No CRASH or HANG occurred in any run.

| Comparison | EQ | NE | RJ | XR-YA | XA-YR | X-nonjson | X-unwritable |
|---|---:|---:|---:|---:|---:|---:|---:|
| A~B | 58,674 | 210 | 239,160 | 6,286 | 23,825 | **31,418** | **427** |
| F~B | 58,674 | 210 | 283,314 | 7,051 | 10,751 | **0** | **0** |
| A~S | 69,635 | 0 | 239,865 | 5,581 | **13,074** | **31,418** | **427** |
| F~S | 69,635 | 0 | 284,386 | 5,979 | **0** | **0** | **0** |

The 120,000 valid documents were **EQ in all four comparisons**, with EQ-src 0: every reader returned exactly the source value.

| Value stream (120,000) | PASS | PASS-both-throw | FAIL |
|---|---:|---:|---:|
| W (writers and round trips) | 108,470 | 8,017 | 3,513 |

| JS (111,983 docs A could write) | PASS | FAIL |
|---|---:|---:|
| JS | 49,261 | 62,722 |

### Case counts, seed 2 (`results/final-seed2.md`)

| Comparison | EQ | NE | RJ | XR-YA | XA-YR | X-nonjson | X-unwritable |
|---|---:|---:|---:|---:|---:|---:|---:|
| A~B | 58,819 | 221 | 239,247 | 6,260 | 23,985 | **31,049** | **419** |
| F~B | 58,818 | 221 | 283,290 | 7,027 | 10,644 | **0** | **0** |
| A~S | 69,684 | 0 | 239,819 | 5,688 | **13,341** | **31,049** | **419** |
| F~S | 69,683 | 0 | 284,226 | 6,091 | **0** | **0** | **0** |

Valid: 120,000 EQ in all four. W: 108,507 PASS, 8,033 PASS-both-throw, 3,460 FAIL (2,680 lone surrogates, 780 depth asymmetry). JS: 49,625 PASS, 62,342 FAIL (all "integer above 2^53"). No incidents.

The one case where F~S is not EQ but A~S is (`scan 2`, case 242254) is a `Note` with a duplicate `"score":-1e999` that a later `"score"` overrides. A and S keep the last value; F rejects the overflowing literal as soon as it reads it. That is R4 applied eagerly, and it is consistent with rejecting malformed values anywhere in the payload.

### Divergences before the fixes, with minimised examples

A~B / A~S, seed 1. "Example" is the ddmin-minimised input (bytes shown as `\xHH`, JSON escapes as written).

**A accepts trailing content (bug).** A~S XA-YR 13,074, one group. Every example is accepted by A and rejected by B and S:

- `0]` (int target)
- `{…}2` (`Note`)
- `{…} 42`
- `{…}{}`
- `{…},`

Whitespace-only trailing content is accepted by all four.

**A throws a non-`JsonException` (bug).** 31,418 cases (8.7% of mutants) in 36 groups vs S. B and S throw `JsonException` for all of them.

| Count | Exception from A | Minimised example (target) |
|---:|---|---|
| 15,363 | `InvalidOperationException`: Cannot get the value of a token type 'Null' / 'String' / 'StartArray' / 'StartObject' / 'True' / 'False' as a number | `null` (int), `{"score":null`, `{"version":""`, `{"":"","":[],"score":[` |
| 4,809 | `InvalidOperationException`: … as a boolean | `{"pinned":null` |
| 3,790 | `InvalidOperationException`: Cannot transcode invalid UTF-8 JSON text to UTF-16 string | `"\xC3"` (string?) |
| 3,758 | `FormatException`: not in a supported format, or out of bounds for the Int32/Int64 type | `1.5` (int), `{"":"","":false,"version":1e1}`, `2147483648` |
| 2,143 | `InvalidOperationException`: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value | `"\udc00"` |
| 1,555 | `InvalidOperationException`: … missing low surrogate | `"\ud800"` |

**All readers accept overflowing number literals (bug in A; B and S share it).** 427 cases. Minimised examples:

- `{"text":"","tags":[],"parent":null,"score":1e999,"pinned":false,"version":1}`
- `…,"score":9e308,…`

`probe Note` shows A, B and S all returning `score = +Infinity`, and so does `-1e400` → -Infinity. Writing that value back throws `ArgumentException` in A and B. An echo command would accept a request and then fail to respond.

Underflow is different: `1e-999` and `2.4e-324` become 0 in every reader. A 1,000-digit `1.999…9` rounds to 2. Both are correct IEEE rounding.

**Contract differences (the same in A and F, and gone or explained against S):**

| Count (seed 1) | Comparison | What | Minimised example |
|---:|---|---|---|
| 10,751 | XA-YR vs B only | B requires nullable properties to be present (`RespectRequiredConstructorParameters`); A treats them as optional | `{"text":"","tags":[],"score":8,"pinned":true,"version":1}` (no `parent`) |
| 210 | NE vs B only | B matches names case-insensitively, so `"versIon":8` sets `version` in B and is ignored by A | `{"text":"","tags":[],"parent":null,"score":8,"pinned":true,"versIon":8}` |
| ~700 | XR-YA vs B only | B accepts PascalCase/upper-case names for required properties, numbers in strings (`"score":"1"`) | `{"SCORE":2,"pinned":false,"tags":[],"text":"","parent":null,"version":null}` |
| 4,104 | XR-YA vs B and S | top-level `null` for `Note`, `Note[]`, `Wide`: STJ returns null despite the non-nullable declaration; A rejects | `null` |
| 1,471 | XR-YA vs B and S | `null` inside `IReadOnlyList<string>`: STJ does not enforce element nullability; A rejects | `{"text":"","tags":[null],"score":3,"pinned":false,"":null}` |

Both sides agree on these: duplicate properties (last wins in all four), a leading BOM (rejected by all), `-0` for `int` (accepted as 0 by all), and unknown properties whose skipped values contain invalid UTF-8 or lone surrogate escapes (accepted by all four; the reader does not decode skipped strings).

### Writer and JavaScript properties

- **Byte equality.** A, A2 and F output was byte-identical to B for every value B could write: 108,470 PASS plus 2,762 values (seed 1) whose only failure was a lossy round trip. Under the default encoder this covers every escape class above, supplementary planes, 300,000-character strings, 2,000-item arrays, ±0, subnormals and `MaxValue`.
- **Both refuse (PASS-both-throw, 8,017).**
  - NaN or ±Infinity: `ArgumentException` from every writer (7,455).
  - Depth 1000 or more: `InvalidOperationException` from every writer (562).
- **Depth asymmetry (bug, 751 FAIL).** For values 65–999 deep, A and A2 write. A's writer `MaxDepth` defaults to 1000, while B's serializer limit is 64 and throws `InvalidOperationException`. Neither A nor B can read A's output back: "The maximum configured depth of 64 has been exceeded". The managed test client, which reads responses with the generated readers, would fail on a response the server produced. F and F2 throw at the same depth as B, and the A~F round trip holds for 63- and 64-deep values.
- **Lone surrogates (contract gap, 2,762 FAIL, identical in A, A2, F, F2 and B).**
  - A .NET string containing an unpaired surrogate is written as `"�"` (`describe 1 11731`: A and B both wrote `"�"`) without any error. read(write(x)) ≠ x.
  - In the other direction, a JavaScript string with a lone surrogate is serialized by `JSON.stringify` as `"\ud800"`. Every .NET reader rejects that.
- **JavaScript.** Of the 111,983 documents A wrote, `JSON.parse` returned exactly the .NET value for every string, every double (including `-0`, subnormals, 1e21, `MaxValue`) and every integer up to 2^53. **62,722 failed, all with the one cause "integer above 2^53 rounded by JSON.parse"**. Example: `"version":9007199254740993` reads as 9007199254740992. The generator's `long` is therefore not safe through the TypeScript client.

### Under NativeAOT

The harness was published with NativeAOT: `-p:FuzzAot=true`, with the repository's settings and warnings as errors. **No warnings.** It ran seed 3 for 120,000 cases, and the same build ran the same cases on CoreCLR (`results/aot-seed3.md`, `results/jit-seed3.md`).

- **The case-count tables are identical in every cell.** This covers A, F, B and S, the writers and the JS check, with no incidents.
- 20 of about 1,800 RJ groups per comparison have a different key, because the NativeAOT runtime words a few reader messages slightly differently. No other class is affected.
- Speed: 23.3 s on NativeAOT, 37.2 s on CoreCLR. Child processes start faster under NativeAOT.

## Fixes, as generator requirements

The fixes are in `Fuzz.Generator.Fixed/CSharpEmitter.cs` and `Fuzz.Runtime/SparJsonFixed.cs`. A fixed `Note` reader looks like this:

```csharp
var reader = SparJsonFixed.Begin(payload);            // MaxDepth = 64
global::Bench.Note result = SparSerializers.ReadNote(ref reader);
SparJsonFixed.End(ref reader);                         // trailing content -> JsonException
return result;
// properties: SparJsonFixed.ReadDouble(ref reader) instead of reader.GetDouble(), and so on
```

| # | Requirement for the real generator | Effect (seed 1, before → after) |
|---|---|---|
| R0 | **Emitted code must compile for every supported shape under nullable warnings as errors.** The tuned (A2) emitter writes `list[i] is null ? … : list[i].Value`. The compiler does not narrow an indexer, so `long?[]` gives CS8629 and `IReadOnlyList<Wide?>` gives CS8604. Assign each element to a local first. Generator tests must compile a DTO with every nullable element kind. | build error → builds. Applied to both copies, since the original could not otherwise compile against `Wide`. |
| R1 | **Reject trailing content.** After the top-level value, call `reader.Read()` once and require `false`. With default reader options the reader itself throws `JsonException` for non-whitespace after a complete value. | A~S XA-YR 13,074 → **0** |
| R2 | **Every malformed-input failure is a `JsonException`.** Emit typed read helpers instead of `Get*()`: check the token type, use `TryGetInt32`/`TryGetInt64`/`TryGetDouble`, read `True`/`False` explicitly, and wrap `GetString()`, which throws `InvalidOperationException` for invalid UTF-8 and unpaired `\u` surrogates. Include the byte offset (`TokenStartIndex`) in the message. B gives a JSON path; tracking a path is not implemented here. | X-nonjson 31,418 → **0** |
| R3 | **One depth limit for reading and writing (64).** Pass `MaxDepth = 64` to the response writer as well as the reader. The bench's reused `SkipValidation` writer still enforces `MaxDepth`. A value too deep for the reader then fails when it is written, as B does, instead of producing a payload Spar's own reader rejects. Decide how dispatch reports that `InvalidOperationException` (internal error). | W depth FAIL 751 → **0** for F/F2 |
| R4 | **Reject number literals that overflow to ±Infinity.** `TryGetDouble` returns `true` with an infinity for `1e999`. Require `double.IsFinite`. This is stricter than STJ, which accepts these. | X-unwritable 427 → **0**; F~S XR-YA +398 (intended) |

**Total effect against the strict reference (F~S, seed 1).** XA-YR 0, X-nonjson 0, X-unwritable 0, NE 0. Valid documents are 100% EQ. XR-YA is 5,979, made of:

- top-level `null`: 4,104;
- `null` in a string list: 1,471;
- overflowing doubles: 404.

All three are deliberate strictness. F accepted nothing that A accepted and S rejected. On seed 1, F's EQ count equals A's, so the fixes rejected no input that A and S both accept. On seed 2, exactly one such case occurred: a duplicate `"score":-1e999` that a later valid `score` overrides (see seed 2).

**Decisions the fuzzer surfaced but did not settle** (contract choices; record in `docs/decisions.md` before M1):

1. **`long` above 2^53 through `JSON.parse`.** Options:
   - restrict the range and report a diagnostic;
   - encode `long` as a JSON string;
   - have the TypeScript client read with a reviver that uses the source text (Chromium's JSON.parse source-text access), mapping to `bigint`.
2. **Lone surrogates.** Writers replace them with U+FFFD silently and readers reject them. Either document "strings are Unicode scalar sequences" and keep the replacement, or throw at write. The TypeScript side may need to normalise or reject such strings before sending.
3. **Top-level `null` and `null` list elements.** Keep A's stricter rule, which matches the annotations, and document it, since STJ differs.
4. **Non-finite doubles in responses.** Every writer throws `ArgumentException`. Map it, with R3's depth error, to one defined serialization error at dispatch.
5. **Duplicate properties.** Last one wins, the same as `JSON.parse`. Rejecting them would be stricter; it is a choice, not a bug.

## Observed, skipped, not tested

**Observed (real execution):**

- the two seed-1 campaigns (`baseline`, `final-seed1`), `final-seed2`, and the 120,000-case JIT/AOT pair on seed 3, all on this machine. Intermediate runs before R4 and the exact round-trip check (fixes 1–3 only, seeds 1 and 2, plus an AOT/JIT pair on seed 1 with identical counts) gave the same F results apart from X-unwritable. Their reports were replaced by the final runs;
- the injected crash and hang self-test;
- the NativeAOT publish of the harness (`dotnet publish Fuzz/Fuzz.csproj -c Release -p:FuzzAot=true`), with no warnings; warnings are errors, and there are no suppressions;
- one `node` process per batch for the JavaScript check;
- `scan 2` over all 600,000 seed-2 cases, looking for inputs F newly rejects;
- `probe` runs for `1e999`, `-1e400`, `1e-999`, `2.4e-324`, `1.7976931348623158e308` / `…159e308`, a 1,000-digit mantissa, trailing ` 42`, a lone surrogate and invalid UTF-8 in skipped values, a duplicate `text`, a BOM, and `-0`.

**Skipped:**

- Minimisation of value-stream representatives: those are described with `describe` instead.
- Minimisation of EQ-src groups: there were none.
- Under NativeAOT, minimisation and the full 600,000-case campaigns: AOT ran 120,000 cases without minimisation.
- A full listing of every RJ group: there are about 1,700 groups, and the `.groups.tsv` files have them all.

**Not tested:**

- Code from the real M1 generator: this is the D1/bench copy.
- The dispatch layer, the bridge transport and the TypeScript client itself. Only `JSON.parse` in node was exercised, not WebView2's engine or `fetch().json()`.
- Concurrency and pooled writers.
- Streaming or partial (`isFinalBlock: false`) payloads.
- Enums, dictionaries, typed error hierarchies, `int`/`long` keys, `float`/`decimal` and other wire types the plan may add.
- Property names longer than about 10 characters and non-ASCII declared names.
- Payloads above about 2 MB.
- Inputs that are not UTF-8 (UTF-16 bodies).
- Writer `Encoder` choices other than the default.
- Performance of the fixes. The helpers add one token-type branch per value, and a `try` only around `GetString`; this was not benchmarked.
- Coverage-guided fuzzing. The generator is grammar- and mutation-based, with no external engine.

Two harness caveats:

- NativeAOT runtime messages differ slightly in quoting from CoreCLR's. A few rare RJ groups normalise to different keys; the class counts are unaffected.
- The ddmin minimiser works on bytes, so some examples that need many required properties stay a few hundred bytes long.

## Reproduce

```powershell
cd spikes\serializer-fuzz
dotnet build Fuzz\Fuzz.csproj -c Release
.\Fuzz\bin\Release\net11.0\Fuzz.exe --seed 1 --cases 600000 --workers 6 --budget 330 --label final-seed1   # writes results\final-seed1.md and .groups.tsv
.\Fuzz\bin\Release\net11.0\Fuzz.exe probe Note '{"text":"t","tags":[],"parent":null,"score":1e999,"pinned":false,"version":null}'
.\Fuzz\bin\Release\net11.0\Fuzz.exe minimise 1 A~B <caseId>      # or: describe 1 <caseId>
.\Fuzz\bin\Release\net11.0\Fuzz.exe --cases 60 --batch 20 --workers 2 --label selftest --minimise false --inject-crash 5 --inject-hang 27
dotnet publish Fuzz\Fuzz.csproj -c Release -p:FuzzAot=true      # NativeAOT harness in Fuzz\bin\Release\net11.0\win-x64\publish
```

`node` must be on `PATH` for the JavaScript check; without it the JS rows are recorded as SKIPPED.
