# SPAR050: catching default `Result` values at compile time

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/spar050/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/spar050) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** `Result<T, TError>` and `Result<TError>` are `readonly struct` unions (decision "2026-11-14, The C# `Result` container is a union struct"). A default one is neither a value nor an error, and every member throws on it. The SPAR050 prototype in `spikes/result-api` caught 4 of the 6 probe cases. How many ways are there to create or observe a default `Result`, how many can an analyzer catch soundly, at what cost and with how many false positives, and which rules should Spar ship?

## Summary and recommendation

- **Coverage.** The corpus has 199 cases (`Corpus/Cases/`, list at the end). 131 of them create or observe a default `Result`.
  - **The prototype plus the compiler catch 33 of the 131 (25 %).** That is 28 by SPAR050 and 5 by compiler or .NET diagnostics. It catches 4 of the 6 original cases.
  - **The new rules plus the compiler catch 117 of the 131 (89 %).** That is 112 by SPAR050–SPAR054 and 5 by CS0649 or CA2019. All 6 original cases are caught: the array is SPAR052 and the field is SPAR053.
  - **14 remain for the runtime guard.** 11 are not visible to a per-compilation analyzer. 3 are visible but deliberately not caught (F41, P05, G05).
  - Another 7 constructs do not compile at all (for example `stackalloc`, `r == default`, `Volatile.Read`). 4 more are flagged by design even though the code may be correct (A07, A22, F26, F27).
  - Every case's build output matches its markers exactly: each diagnostic ID and line, with nothing extra (`results/cases.md`).
- **False positives.**
  - **0 on the 57 clean cases.** The prototype had 3 there (the Try-pattern `default` in L02, and `Equals(default(Result))` twice in L26). It also put 2 diagnostics at the wrong place in detect cases.
  - **0 on the copy of `spikes/app-model-check`.** Its `Spar.Stubs/Result.cs` is identical to the chosen form, so the `Result` source itself is checked too (`results/appmodel-check.txt`).
  - **0 on the 200-file timing sample.**
  - The 4 by-design cases are the price of the rules: a `Parallel.For` fill, a slot cleared with `default`, a field assigned by a helper method, and a base field assigned by a derived constructor. The last two are exactly what CS8618 also reports.
- **Cost.** The sample has 200 files and 17,200 lines, and was recompiled 9 times with `-p:ReportAnalyzer=true` (`results/timing.txt`).
  - **The new analyzer takes 31–63 ms per build (median 36 ms).** The prototype takes 1 ms, and all analyzers together take about 0.7 s. That is mostly the IDE code-style analyzers the repository enables, with 0.33 s for `Microsoft.CodeAnalysis.CSharp.CodeStyle`.
  - The Csc task time is 520–565 ms with no Spar analyzer, the prototype, or the new rules. The analyzer's cost does not show above that noise.
  - The analyzer is about 2,100 lines in 12 files, against 40 for the prototype. That is the real cost to weigh.
- **Code fixes** (`results/codefixes.txt`, 10 of 10 pass, each with no new compiler error):
  - SPAR050 → `Result.Success`, or an error case with a parameterless constructor. `Result<T, TError>` gets no fix when every case needs arguments.
  - SPAR051 → `First()`/`Last()`/`Single()`/`ElementAt()`, or `.Value`.
  - SPAR053 → `Result<…>?`, or `required`.
- **Recommendation:** ship SPAR050–SPAR054 as below, in category `Spar.Results` (they are not contract checks), next to the generator in `Spar.Sdk`. Keep the runtime guard as it is: it still covers the 14 cases above, and every rule except SPAR050 is a warning.

### Recommended rule set

| ID | Severity | Message (format) | Catches | Exempt | Fix |
|---|---|---|---|---|---|
| SPAR050 | Error | `'{0}' creates {1}, which is neither a value nor an error. Use a value, an error case, or Result.Success.` | `default`, `default(R)`, `new()`/`new R()`. Also a struct holding a Result (tuple, record struct, `KeyValuePair`, `ValueTask<R>`, `[InlineArray]`, nested structs, generic wrappers) when it has no parameterless constructor or its initializer skips the Result. Also optional parameters `= default`/`= new()`, and calls that omit a metadata optional Result argument; `this = default`; `Activator.CreateInstance<R>()` and `(typeof(R))`; `RuntimeHelpers.GetUninitializedObject`; `Unsafe.SkipInit`; `new Lazy<R>()` without a factory; a Result bound to a `new()`-constrained type parameter. | An `Equals` argument. A value assigned to a `[MaybeNull]`/`[MaybeNullWhen]` out parameter, or returned from `[return: MaybeNull]`, also through `?:`, `??` and switch arms. | `Result.Success`; an error case without arguments |
| SPAR051 | Warning | `'{0}' may give a default {1} ({2}), which is neither a value nor an error` | Calls whose signature says "maybe default" for a Result: a `T?` return on an unconstrained T, or `[return: MaybeNull]`. That covers `FirstOrDefault`, `LastOrDefault`, `SingleOrDefault`, `ElementAtOrDefault`, `GetValueOrDefault(key)`, `List.Find`, `Array.Find`, `ImmutableArray` extensions, and source or library methods. Also `DefaultIfEmpty()`, `Result?.GetValueOrDefault()`, `new ThreadLocal<R>()` without a factory, `new AsyncLocal<R>()`, `new StrongBox<R>()`, and `Unsafe.As`/`BitCast`/`Read`/`ReadUnaligned` to R. `[MaybeNullWhen]` out arguments (`TryGetValue`, `TryDequeue`, `TryRead`, `TryRemove`) are reported when the bool is ignored, when the value is read in the failing branch, or when it is read after an `if` whose failing branch neither exits nor assigns. | `Min`, `Max`, `MinBy`, `MaxBy` (annotated `T?`, but they throw for value types); a `while (TryX(out r))` loop; `&&` conditions | `First()` and the like; `.Value` |
| SPAR052 | Warning | `'{0}' leaves {1} elements default until each is assigned. Build the collection from values, or assign every element before reading it.` | `new R[n]` and `new R[a, b]` without an initializer; `Array.Resize`, `Array.Clear`, `Span<R>.Clear`; `GC.AllocateArray` and `AllocateUninitializedArray`; `Array.CreateInstance` and `CreateInstanceFromArrayType`; `CollectionsMarshal.SetCount` and `GetValueRefOrAddDefault`; `ArrayPool<R>.Rent`, `MemoryPool<R>.Rent`; `ImmutableArray<R>.Builder.Count = n` | Length 0. The fill loop that directly follows the declaration and writes `a[i]` for every `i` from 0 to `a.Length` or `n`, with no `continue`/`break`/`return` before the write. | none |
| SPAR053 | Warning | `'{0}' {1}, so it can be read as a default {2}. Assign it in every constructor, give it an initializer, make it required, or declare it as '{2}?'.` | A field or auto-property that holds a Result (also `field`-keyword properties and struct-typed members): not assigned on every path of every constructor. The paths followed are if/else, switch with default, try/catch, `: this(…)` chains, deconstruction and out arguments. Also a required member that a `[SetsRequiredMembers]` constructor skips, a `[ThreadStatic]` field set by the static constructor, and a static initializer that reads a static field initialized later. | Initializers, positional record parameters, `required`, a struct without constructors (its creations are SPAR050). Never-written private or internal fields are CS0649's report; `[ThreadStatic]` initializers are CA2019's. | `Result<…>?`; `required` |
| SPAR054 | Warning (compilation end) | `'{0}' creates default({1}) in its body, and {1} is '{2}' here` | A generic method in source whose body creates `default(T)` or calls `Activator.CreateInstance<T>()`, directly, in a lambda, or by passing T to another such method. Reported where it is called with T bound to a Result; type-level T is included. | Calls already covered by SPAR051 (`T?`) or SPAR050 (`new()` constraint); `EqualityComparer<T>.Default.Equals(x, default)`; Try patterns | none |

SPAR053 is a warning for the same reason CS8618 is one: assignments in helpers and in derived constructors are not followed. With `TreatWarningsAsErrors`, as in this repository and every spike, all five fail the build.

### What stays with the runtime guard

Every member of a default `Result` throws `InvalidOperationException("default(Result) is neither a value nor an error. …")`, and so does a union `switch` over it. `results/runtime-probes.txt` confirms each construct below at run time.

| Case | Construct | Why not static |
|---|---|---|
| X01, X06, X10 | Library methods that return `default`, `default!` for T, or `new R[n]`, built without the analyzer | Only the library's IL knows. Annotated library methods (`T?`, `[MaybeNull]`, `[MaybeNullWhen]`, `new()`) are caught (X02, X03, X05, X07, X09). |
| X04 | A library class with an unassigned public Result field | The consumer only reads a field. |
| X11 | A third-party generator's `default` and its unassigned field | Generated code is not analysed, because the user cannot fix it. |
| F30 | A base constructor calls a virtual method that reads a derived field before the derived constructor assigns it | CA2214 reports the virtual call (off by default); following it across types is beyond a per-type check. |
| F31 | A constructor publishes `this` before assigning the Result | Escape analysis. |
| P22 | `List<R>.Enumerator.Current` before `MoveNext` | Nothing in the signature says so. |
| U04, U07, P11 | `MemoryMarshal.Cast<byte, R>`, `Unsafe.NullRef<R>()`, `Interlocked.Exchange(ref r, …)` | .NET throws first: `ArgumentException` ("contains references"), `NullReferenceException`, and `NotSupportedException` ("must be a reference type, a primitive type, or an enum type"). No default Result appears. |
| F41 (missed) | An internal field written only by another type | SPAR053 leaves never-written fields to CS0649, which is silent here. A compilation-wide pass would catch it. |
| P05 (missed) | `var found = d.TryGetValue(k, out var r);` then `r` | Following the bool through a local needs real flow analysis. |
| G05 (missed) | `Box<T>.Item = default!` in a generic type's property initializer | The creation `new Box<R>()` is not followed into initializers. |

### Compiler and .NET diagnostics Spar relies on

- **CS0649** reports a private or internal field, instance or static, that nothing assigns (F01, F04, F19, F32). It does not cover public fields or auto-properties; SPAR053 covers those.
- **CA2019** reports a `[ThreadStatic]` field with an initializer (F23). It is on by default in SDK 11.
- **CS0165** and **CS0177** report unassigned locals, `ref` arguments, pattern variables and out parameters. **CS0208** rejects `stackalloc` of a managed type. **CS0452** rejects `Volatile.Read` on a struct. **CS0019** rejects `r == default`, because Result declares no `==`.
- Seen incidentally: CA1825 (`new R[0]`), CA1855 (`Fill(default)` → `Clear()`, which SPAR052 then reports) and CA2263.

**Proposals for the user.** Nothing in this list has been applied, and `.editorconfig` is unchanged.

1. CS0649 and CA2019 are warnings, so a consumer app without `TreatWarningsAsErrors` only sees a warning for F01, F04, F19, F23 and F32. If that matters, `Spar.Sdk` could raise them to error for app projects. That is an analyzer severity override: diagnostic IDs CS0649 and CA2019, severity error, scope Spar app projects, reason "a default Result throws on every read". It needs the user's agreement first.
2. Spar's own `Result` tests deliberately create `default(Result)` to test the guard (`DefaultThrowsOnEveryRead`). Under SPAR050 they would need a local `#pragma warning disable SPAR050`. That is a suppression and needs the user's agreement first; the alternative is creating the value in a helper the analyzer does not see.

### Shipping in Spar.Sdk

- **Two assemblies in `analyzers/dotnet/cs/`, both `netstandard2.0` against the pinned Roslyn 5.9:**
  - `Spar.Analyzers.dll` holds the rules. It needs no Workspaces reference, so it could also merge into the generator assembly that holds the contract `DiagnosticAnalyzer`. Keeping it separate lets the code fix reference it without the generator.
  - `Spar.Analyzers.CodeFixes.dll` holds the code fixes. It must be separate: an analyzer assembly may not reference Workspaces (RS1038).
  - The harness ran the analyzer and the code fixes in-process with Roslyn 5.9.0 on 10 snippets with C# 15 unions, with 0 compiler errors. The corpus ran inside the SDK's compiler, Roslyn 5.12.
- **Release tracking (RS2008):** `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` are `AdditionalFiles` of the analyzer project (`Analyzers/`). They list all five IDs with category and severity. At each release, the Unshipped table moves to Shipped under `## Release x.y`. RS2008 is satisfied, not suppressed.
- **The category is `Spar.Results`, not `Spar.Contracts`.** The skill's rule "category `Spar.Contracts`, all errors" is about contract analysis. If these rules ship, that line should say that the SPAR05x rules are a separate category with warnings.
- **The analyzer registers nothing when the compilation cannot see `Spar.Result\`1` or `Spar.Result\`2`,** so non-Spar projects pay nothing.
- **Generated code is not analysed** (`GeneratedCodeAnalysisFlags.None`). Spar's own generator never emits a default Result, and its tests should run these rules over the generated sources to prove it.
- **SPAR054 is a compilation-end diagnostic.** `dotnet build` reports it, but IDEs show such diagnostics only with full-solution analysis. This was not checked in an IDE.

## Method

Everything is under `spikes/spar050/`. It was built with SDK 11.0.100 (C# 15, Roslyn 5.12 in csc) and the repository's `Directory.Build.props` (nullable, warnings as errors, code style). Nothing was suppressed and no severity was changed.

| Path | Contents |
|---|---|
| `Result/` | `Result.cs` copied unchanged from `spikes/result-api/Forms/UnionStruct/`. |
| `Baseline/` | The SPAR050 prototype copied unchanged (`Spar050.Baseline.csproj`). |
| `Analyzers/` | The new rules. `DefaultResultAnalyzer.cs` is the entry point. `KnownApis.cs` holds the BCL list, `MaybeDefaultCalls.cs` SPAR051, `FillLoop.cs` the array exemption, `MemberAssignment.cs` and `DefiniteAssignment.cs` SPAR053, `GenericDefaults.cs` SPAR054 and `ResultTypes.cs` the containment cache. Release files are next to them. |
| `CodeFixes/` | `DefaultResultCodeFix.cs`. |
| `Corpus/` | `Spar050.Corpus.csproj` builds every `Cases/**/*.cs` in one compilation and writes a SARIF log; `-p:Analyzer=None\|Baseline\|New` selects the analyzer. `Shared/` holds the `app-model` domain types, two aliases (`PlanResult`, `CancelResult`) and `Values`. |
| `Corpus/Cases/<Group>/` | One case per file. Its header gives `Case`, `Kind` and `Why`, and `//~ ID …` marks each line where a diagnostic is expected. Kinds: `detect` (a SPAR05x is expected), `by-design` (flagged although the code may be fine), `compiler` (CS/CA catches it), `impossible` (does not compile), `runtime` (not statically detectable), `missed` (detectable, not caught), `clean` (must not be flagged). |
| `CorpusLibrary/` | A referenced assembly built without the analyzer: unannotated, annotated and generic APIs, an unassigned field, and a struct with a private Result field. |
| `CorpusGenerator/` | A source generator whose output returns `default` and declares an unassigned Result field. |
| `Sample/` | `generate.ps1` writes 200 files of ordinary service code into `obj/sample/`: commands, conversions, switches, TryGetValue, fill loops, constructor-assigned fields, LINQ and generics. |
| `AppModelCheck/` | An unchanged copy of the C# part of `spikes/app-model-check`, plus a `Directory.Build.targets` that adds the analyzer (`diff -r` showed the copy identical). |
| `Harness/` | `codefix <folder>` applies each fix in an `AdhocWorkspace` (Roslyn 5.9) and recompiles. `runtime` executes the constructs against the real Result. |
| `results/` | `cases.md` (full scoring table), `case-list.md` (the list below), `corpus-{None,Baseline,New}.txt` (every diagnostic per case), `timing.txt`, `codefixes.txt`, `runtime-probes.txt`, `appmodel-check.txt`. |

Commands, from the repository root:

```
pwsh spikes/spar050/run-corpus.ps1      # three corpus builds, scoring, results/cases.md and case-list.md
pwsh spikes/spar050/run-checks.ps1      # app-model copy, code fixes, runtime probes
pwsh spikes/spar050/run-timing.ps1 -Runs 9
```

**How a case is scored:** the SARIF log of the corpus build gives every diagnostic as (file, line, ID). A case matches when that set equals its markers, so an extra diagnostic of any ID counts against it, CA and IDE included. The prototype "caught" a case when it reported SPAR050 on every line where a SPAR05x is expected.

## Results

### Coverage by group (new rules)

| Group | Cases | detect | by-design | compiler | impossible | runtime | missed | clean | prototype caught |
|---|---|---|---|---|---|---|---|---|---|
| Existing (the 6 probe cases) | 6 | 6 | | | | | | | 4 |
| Arrays and storage | 28 | 15 | 2 | | 1 | | | 10 | 2 |
| Fields and properties | 44 | 17 | 2 | 5 | | 2 | 1 | 17 | 0 |
| Structs holding a Result | 17 | 15 | | | | | | 2 | 1 |
| Unsafe and reflection-style | 7 | 4 | | | | 2 | | 1 | 0 |
| APIs that return default | 29 | 19 | | | 1 | 2 | 1 | 6 | 3 |
| Generics | 13 | 10 | | | | | 1 | 2 | 0 |
| Control flow and expressions | 30 | 20 | | | 5 | | | 5 | 18 |
| Referenced assemblies and generators | 11 | 6 | | | | 5 | | | 1 |
| Clean code | 14 | | | | | | | 14 | |
| **Total** | **199** | **112** | **4** | **5** | **7** | **11** | **3** | **57** | **29** |

Diagnostics per rule in the corpus: SPAR050 60 (53 cases), SPAR051 29 (22), SPAR052 18 (16), SPAR053 20 (20), SPAR054 6 (6). No location has two Spar diagnostics.

### Findings that were not on the list

- **Optional parameters:** `void M(PlanResult r = default)` gives every caller that omits the argument a default Result. It is reported at the parameter (L17), and at the call when the parameter comes from a referenced assembly (X09). The prototype reported both the parameter and every call.
- **`[ThreadStatic]`:** with an initializer, or set by the static constructor, the field is default on every other thread. The runtime probe shows "this thread: success; other thread: default". CA2019 reports only the initializer form.
- **Static initializer order:** `static readonly R s_first = s_fallback;` before `s_fallback`'s own initializer reads default (F24, confirmed at run time).
- **`[SetsRequiredMembers]`** switches the compiler's required check off for that constructor (F10).
- **`new ValueTask<R>()` and `default(ValueTask<R>)`** are completed tasks holding a default Result (S10, confirmed by `await`).
- **`CollectionsMarshal.GetValueRefOrAddDefault`** adds a default Result for a missing key (A26). **`ImmutableArray<R>.Builder.Count = n`** appends defaults (A18).
- **`List.Find`/`Array.Find`** return default on no match (P25). **`DefaultIfEmpty()`** returns IEnumerable<T?>, so the annotation check misses it and it is on the known list (P27).
- **`MaxBy`/`Max`** are annotated `T?` but throw on empty for value types. They are excluded: reporting them would be a false positive (P24).
- **`ArrayPool<R>.Rent` can return the previous renter's valid results,** not only defaults ("[0] = success (left by the previous renter)"). That is a data leak between callers, worth a pitfall.
- **`Interlocked.Exchange(ref resultField, …)` compiles** (the generic T is unconstrained since .NET 9) but throws `NotSupportedException` at run time (P11).
- **A referenced struct's private Result field is visible:** the corpus compiles against the library's reference assembly (`obj/ref/`), and `default(LibPair)` is still SPAR050 (X08). `KeyValuePair` and `ValueTask` are listed by name anyway, in case a metadata struct's fields are hidden.

## Problems met

1. **A constructor's operation blocks are its body and its `: this(…)` initializer, separately.** The `IConstructorBodyOperation` is their parent. The first version looked for it among the blocks and found nothing, so every constructor-assigned field was reported (12 false positives, fixed). *(New pitfall.)*
2. **CS0649 overlap.** Reporting SPAR053 on a private field that nothing writes gave two diagnostics at one place (CS0649 and SPAR053). SPAR053 now leaves never-written private and internal fields to the compiler, and `[ThreadStatic]` initializers to CA2019. The cost is F41.
3. **A `default` in `ok ? x : default` under `[return: MaybeNull]`** first counted as undeclared. Exemptions now follow the value through `?:`, `??` and switch arms (L28).
4. **Every invocation built a metadata-name string** for the known-API check: 42 ms median on the sample. A method-name filter first brought it to 32–36 ms.
5. **Long multi-file heredocs broke the Bash tool's quoting.** The cases were written as bundles in the scratchpad and split with `awk` (tooling only).

Proposed pitfalls for `docs/pitfalls.md`, not added there because this task writes only under the spike: problem 1; CS0649's scope (private and internal, including static, but not public fields or auto-properties); CA2019, CA1825, CA1855 and CA2263 being on by default in SDK 11; `Interlocked.Exchange<T>` on structs; `ArrayPool` returning stale values; and `MaxBy` annotated `T?` but throwing.

## Not tested

- **IDE behaviour:** squiggles, the code fix light bulb and its preview in Visual Studio and VS Code, and whether SPAR054 (compilation end) shows without full-solution analysis.
- **Linux,** and a real `Spar.Sdk` package layout (the analyzer was referenced as a project with `OutputItemType="Analyzer"`).
- **Partial types split across files** for the static-initializer-order check. Only the same-file order was checked; different files are reported because their order is undefined.
- **A large real app.** The timing sample is generated and uniform. The app-model copy is the only hand-written app code.
- **Run-to-run noise:** the Csc task varies by ±40 ms and background load was not sampled, so the analyzer's own time (ReportAnalyzer) is the number to use, not the Csc difference.
- **The fix-all provider (batch fixer),** which was not exercised. Nor were the code fixes on files that use the full type name instead of an alias.

## Effect on the design records

- **`docs/app-model.md` section 3.1** says that `default` throws and that SPAR050 reports it. If the rule set is adopted, it would name SPAR050–SPAR054 and the `Result?` opt-out, and its check would gain a SPAR050 positive control. Nothing was edited (outside this task's write scope).
- **The decision "2026-11-14, The C# `Result` container is a union struct"** weighed the class form's "5 of 6 via nullable analysis" against "4 of 6 with SPAR050". With these rules the struct form catches 6 of 6. The cost moves from "about 40 lines" to about 2,100 lines of analyzer plus a code fix assembly.

## Case list

Generated by `run-corpus.ps1` (`results/case-list.md`). "With SPAR05x" is the new analyzer's build. "Prototype" is the `result-api` SPAR050: `caught`, `missed`, `false positive`, or `-` when nothing was expected and nothing was reported.

| Case | Construct | With SPAR05x | Prototype | Why |
|---|---|---|---|---|
| E01 | => default (spikes/result-api DefaultValue.cs) | caught: SPAR050 | caught | the default literal converted to a Result is an IDefaultValueOperation of a Result type. |
| E02 | return default; in an async method | caught: SPAR050 | caught | same operation inside the async state machine body. |
| E03 | Result<CancelError> => default | caught: SPAR050 | caught | Result`1 is recognised like Result`2. |
| E04 | new() of a Result | caught: SPAR050 | caught | new() on a struct without an explicit parameterless constructor is default. |
| E05 | new Result<…>[4] | caught: SPAR052 | missed | an array creation without an initializer leaves every element default (SPAR052, a warning: the array may still be filled before it is read). |
| E06 | a field assigned by a method but not by the constructor | caught: SPAR053 | missed | between construction and Set, Last reads a default Result. CS0649 is silent because Set writes the field. |
| A01 | new PlanResult[count] returned | caught: SPAR052 | missed | every element is default until assigned. |
| A02 | new PlanResult[0] | clean (.NET: CA1825) | - | an empty array has no element to read. (.NET's CA1825 suggests Array.Empty.) |
| A03 | new PlanResult[] { … } and new PlanResult[2] { … } | clean | - | the compiler requires one initializer element per slot. |
| A04 | collection expressions and spreads | clean | - | every element comes from a value. |
| A05 | new PlanResult[n] filled by the for loop that follows it | clean | - | the recognised fill shape writes every element before the array can be read (FillLoop.cs). |
| A06 | a fill loop that can skip an element | caught: SPAR052 | missed | continue skips the assignment, so the skipped slot stays default. Not the recognised shape. |
| A07 | new PlanResult[n] filled with Parallel.For | flagged by design: SPAR052 | missed | correct code, but the analyzer cannot prove the delegate writes every slot. Rewrite with Select/ToArray or suppress at the site. |
| A08 | new PlanResult[2, 3] | caught: SPAR052 | missed | every element of a rectangular array starts default. |
| A09 | new PlanResult[3][] (jagged outer array) | clean | - | the elements are null arrays, not Results. |
| A10 | Array.Resize growing an array | caught: SPAR052 | missed | the new slots are default. |
| A11 | GC.AllocateUninitializedArray and GC.AllocateArray | caught: SPAR052 | missed | for a type holding references the runtime zeroes the array, so every element is default. |
| A12 | Array.Clear on a Result array | caught: SPAR052 | missed | clearing writes default into each slot. |
| A13 | Span<Result>.Clear() and Span<Result>.Fill(default) | caught: CA1855, SPAR050, SPAR052 | missed | Clear writes default (SPAR052); Fill(default) passes a default Result (SPAR050 on the argument). CA1855 (.NET) suggests Clear() for Fill(default), which SPAR052 then reports. |
| A14 | CollectionsMarshal.SetCount on a List<Result> | caught: SPAR052 | missed | growing the count exposes default slots. |
| A15 | CollectionsMarshal.AsSpan on a List<Result> | clean | - | the span covers only the Count elements, which were added as values. |
| A16 | ArrayPool<Result>.Shared.Rent, and Return with clearArray | caught: SPAR052 | missed | a rented array is new (default elements) or was returned by someone else (stale or cleared elements). Returning with clearArray: true is not reported: the caller gives the array up. |
| A17 | default(ImmutableArray<Result>) | clean | - | a default ImmutableArray is not a default Result; it fails on its own (IsDefault, NullReferenceException). |
| A18 | ImmutableArray<Result>.Builder.Count = n | caught: SPAR052 | missed | raising Count appends default elements. |
| A19 | Array.CreateInstance(typeof(Result), n) | caught: SPAR052 | missed | the reflection-style factory creates a zeroed array. |
| A20 | Array.Fill and Span.Fill with a value | clean | - | every slot gets a real Result. |
| A21 | Enumerable.Repeat<Result>(default, n) | caught: SPAR050 | caught | the repeated element is a default Result. |
| A22 | clearing a buffer slot with default to release references | flagged by design: SPAR050 | caught | a ring buffer that clears a slot on dequeue stores a default Result there. Store Result? (null) instead. |
| A23 | stackalloc PlanResult[2] | does not compile: CS0208 | - | Result holds a reference (TError), so it is a managed type and cannot be stack-allocated. |
| A24 | new List<Result>(capacity) filled with Add | clean | - | capacity is not count; nothing default is visible. |
| A25 | default(ArraySegment<Result>), default(Memory<Result>), Span<Result> = default | clean | - | these defaults are empty views, not default Results. |
| A26 | CollectionsMarshal.GetValueRefOrAddDefault on a Dictionary<K, Result> | caught: SPAR052 | missed | a missing key is added with a default Result. |
| A27 | copying into a larger array, or ToArray over a partially filled pool array | caught: SPAR052 | missed | the destination array creation is reported; the slots after the copied range stay default. |
| A28 | MemoryPool<Result>.Shared.Rent | caught: SPAR052 | missed | the rented memory is an ArrayPool array underneath, with default or stale elements. |
| F01 | a private instance field that nothing assigns | caught by the compiler or .NET analyzers: CS0649 | - | CS0649 (a warning, an error with TreatWarningsAsErrors). SPAR053 stays silent so the problem has one report. |
| F02 | a private field assigned only by a property setter | caught: SPAR053 | missed | readable before the first set; CS0649 is silent because something writes the field. |
| F03 | a public field that nothing in the assembly assigns | caught: SPAR053 | missed | CS0649 ignores public fields (another assembly could assign them); SPAR053 reports it. |
| F04 | a private static field that nothing assigns | caught by the compiler or .NET analyzers: CS0649 | - | CS0649 covers static fields too. |
| F05 | a static field assigned by an Initialize method | caught: SPAR053 | missed | readable before Initialize runs. |
| F06 | a { get; set; } auto-property not assigned by the constructor | caught: SPAR053 | missed | the backing field starts default. This is what CS8618 reports for a non-nullable reference. |
| F07 | a getter-only auto-property never assigned | caught: SPAR053 | missed | no compiler diagnostic exists for an unassigned getter-only auto-property of a struct type. |
| F08 | an init-only property that object initializers may omit | caught: SPAR053 | missed | new F08() without the initializer leaves it default. The fix is required. |
| F09 | a required property | clean | - | the compiler (CS9035) makes every object creation set it. |
| F10 | a required property and a [SetsRequiredMembers] constructor that does not assign it | caught: SPAR053 | missed | the attribute switches the compiler's required check off for that constructor. |
| F11 | assigned in every constructor, one chaining to another | clean | - | : this(…) carries the chained constructor's assignments. |
| F12 | assigned in both branches of an if/else | clean | - | every path assigns. |
| F13 | assigned in only one branch | caught: SPAR053 | missed | the other path leaves it default. |
| F14 | an early return before the assignment | caught: SPAR053 | missed | the return path leaves it default. |
| F15 | a field with an initializer | clean | - | initialized before any constructor body. |
| F16 | a primary constructor parameter stored by a field initializer | clean | - | the initializer runs as part of the primary constructor. |
| F17 | a positional record with a Result parameter | clean | - | the synthesized property is assigned from the parameter. |
| F18 | a record with a Result property declared in its body | caught: SPAR053 | missed | new F18() leaves it default unless an initializer sets it. |
| F19 | a struct constructor that does not assign the Result field | caught by the compiler or .NET analyzers: CS0649 | - | since C# 11 unassigned struct fields are zeroed, so the field is default after this constructor. Nothing writes it, so CS0649 reports it and SPAR053 does not (F44 is the same struct with a writer). |
| F20 | a struct with a public Result field and no constructor | clean | - | nothing to report at the declaration; default(F20) and new F20() are reported where they are written (S01–S04). |
| F21 | a positional record struct | clean | - | the declaration is fine; its parameterless new() is reported at the use (S06). |
| F22 | the opt-out: a Result? field that may be unset | clean | - | Nullable<Result> is null, not a default Result, and HasValue says which. |
| F23 | a [ThreadStatic] Result field with an initializer | caught by the compiler or .NET analyzers: CA2019 | - | the initializer runs once, on the thread that runs the static constructor; every other thread sees default. .NET's CA2019 reports it (on by default), so SPAR053 does not. |
| F24 | a static initializer that reads a static field declared after it | caught: SPAR053 | missed | static initializers run in text order, so s_fallback is still default when s_first is initialized. |
| F25 | a class field whose struct type holds a Result | caught: SPAR053 | missed | a default F25Pair holds a default Result. |
| F26 | assigned by a helper method the constructor calls | flagged by design: SPAR053 | missed | like CS8618, assignments in called methods do not count. Assign in the constructor or use Result?. |
| F27 | a protected base field assigned only by the derived constructor | flagged by design: SPAR053 | missed | each type's constructors are checked on their own, as CS8618 does. |
| F28 | assigned in try, catch rethrows | clean | - | a construction that throws produces no object. |
| F29 | a static field assigned by the static constructor | clean | - | the static constructor runs before any access. |
| F30 | a base constructor calls a virtual method that reads a derived field before it is assigned | runtime guard only | - | the derived constructor does assign the field, after the base constructor ran. CA2214 (not on by default) reports the virtual call; following it across types is beyond a per-type check. |
| F31 | the constructor publishes this before assigning the Result | runtime guard only | - | another thread, or the callee, can read the field while it is default. |
| F32 | an internal field that nothing assigns | caught by the compiler or .NET analyzers: CS0649 | - | CS0649 also covers internal fields. |
| F33 | a property using the field keyword, never assigned by the constructor | caught: SPAR053 | missed | the synthesized backing field starts default. |
| F34 | a partial class whose constructor in another part assigns the field | clean | - | the type is analysed as a whole. |
| F35 | assigned by a deconstruction in the constructor | clean | - | a tuple deconstruction assigns each target. |
| F36 | an expression-bodied constructor | clean | - | the expression body assigns the field. |
| F37 | a struct constructor chaining to the implicit this() and not assigning the Result | caught: SPAR053 | missed | this() zeroes every field. |
| F38 | a switch statement that assigns in every section, with a default section | clean | - | every path assigns. |
| F39 | a static initializer that reads a static field declared before it | clean | - | text order makes s_fallback initialized first. |
| F40 | assigned inside a loop that may not run | caught: SPAR053 | missed | zero iterations leave it default. |
| F41 | an internal field written only by another type | missed; runtime guard | - | SPAR053 leaves fields its type never writes to CS0649, and CS0649 is silent because another type writes it. Seeing that would need a compilation-wide pass; the runtime guard throws. |
| F42 | assigned by passing the field as an out argument in the constructor | clean | - | an out argument is definitely assigned when the call returns. |
| F43 | a [ThreadStatic] Result field assigned by the static constructor | caught: SPAR053 | missed | the static constructor runs on one thread; every other thread sees default. CA2019 covers only initializers. |
| F44 | a struct constructor that does not assign a Result field another member writes | caught: SPAR053 | missed | the constructor leaves it zeroed, and CS0649 is silent because Set writes it. |
| S01 | default(Pair) where Pair has a Result field | caught: SPAR050 | missed | the struct's Result field is default. |
| S02 | new Pair() without an explicit parameterless constructor | caught: SPAR050 | missed | same as default(Pair). |
| S03 | new Pair { Result = …, Id = 1 } | clean | - | the object initializer sets every member that holds a Result. |
| S04 | new Pair { Id = 1 } | caught: SPAR050 | missed | the initializer leaves the Result default. |
| S05 | a struct whose parameterless constructor assigns the Result, and default() of it | caught: SPAR050 | missed | new() runs the constructor (clean); default(…) still skips it (SPAR050). |
| S06 | new() of a positional record struct | caught: SPAR050 | missed | a record struct still has the zeroing parameterless constructor. |
| S07 | (int, PlanResult) t = default | caught: SPAR050 | missed | ValueTuple's Item2 is a default Result. |
| S08 | (1, default) converted to (int, PlanResult) | caught: SPAR050 | caught | the default element is target-typed to the Result. |
| S09 | default(KeyValuePair<string, PlanResult>) | caught: SPAR050 | missed | KeyValuePair stores the value inline. Its fields are private in metadata, so it is on a short known list. |
| S10 | default(ValueTask<PlanResult>) and new ValueTask<PlanResult>() | caught: SPAR050 | missed | a default ValueTask<T> is completed with default(T); awaiting it gives a default Result. |
| S11 | an [InlineArray] buffer of Results | caught: SPAR050 | missed | new Buffer() zeroes every element. The element field itself is not reported (no constructor to check). |
| S12 | default(Outer) where Outer holds a Pair that holds a Result | caught: SPAR050 | missed | containment is followed through nested structs. |
| S13 | default(Holder) with { Attempts = 1 } | caught: SPAR050 | missed | with copies the default Result. |
| S14 | this = default in a struct method | caught: SPAR050 | missed | resets every field, including the Result. |
| S15 | var (a, b) = default((PlanResult, int)) | caught: SPAR050 | missed | deconstructs a default Result. |
| S16 | default(Wrapper<PlanResult>) | caught: SPAR050 | missed | the constructed type's field is a Result. |
| S17 | holder with { Attempts = 2 } on a real value | clean | - | with copies a valid Result. |
| U01 | Unsafe.SkipInit(out PlanResult r) | caught: SPAR050 | missed | the local is not initialized at all: zero, or stack garbage under [SkipLocalsInit]. |
| U02 | [SkipLocalsInit] on a method with ordinary Result locals | clean | - | definite assignment still applies; the attribute only matters with stackalloc and Unsafe.SkipInit. |
| U03 | Unsafe.As<TFrom, PlanResult> reinterpreting other memory | caught: SPAR051 | missed | the bits may be anything, including zero (SPAR051, a warning: unsafe code is the author's call). |
| U04 | MemoryMarshal.Cast<byte, PlanResult> | runtime guard only | - | .NET itself throws ArgumentException for a type that holds references, so no default Result is produced. |
| U05 | RuntimeHelpers.GetUninitializedObject(typeof(PlanResult)) | caught: SPAR050 | missed | creates a zeroed (boxed) Result without running any constructor. |
| U06 | Activator.CreateInstance<PlanResult>() and Activator.CreateInstance(typeof(PlanResult)) | caught: CA2263, SPAR050 | missed | both run the zeroing parameterless constructor of the struct. |
| U07 | Unsafe.NullRef<PlanResult>() read through | runtime guard only | - | not a default Result: reading it is a NullReferenceException from the runtime. |
| P01 | TryGetValue(key, out var r) with the bool ignored, then r returned | caught: SPAR051 | missed | [MaybeNullWhen(false)] says r is default on a miss, and nothing checks the result. |
| P02 | r read in the branch where TryGetValue returned false | caught: SPAR051 | missed | the failing branch reads the default. |
| P03 | TryGetValue used as intended (if, early return, ?:, &&, assignment on miss) | clean | - | r is read only where the call succeeded, or after it was assigned. |
| P04 | the miss branch only logs, then r is read after the if | caught: SPAR051 | missed | the failing branch neither exits nor assigns, so the read after the if can see the default. |
| P05 | the bool result stored in a local and tested later | missed; runtime guard | - | following the bool through a local needs real flow analysis; the runtime guard throws. |
| P06 | FirstOrDefault, LastOrDefault, SingleOrDefault, ElementAtOrDefault without a fallback | caught: SPAR051 | missed | annotated T?; on a miss they return default(Result). SPAR051 offers First()/Last()/Single()/ElementAt(). |
| P07 | FirstOrDefault(fallback), GetValueOrDefault(key, fallback), First() | clean | - | the fallback is a real Result; First() throws on an empty sequence instead. |
| P08 | GetValueOrDefault(key) on a dictionary of Results | caught: SPAR051 | missed | a missing key gives default(Result). |
| P09 | Result?.GetValueOrDefault() without a fallback | caught: SPAR051 | missed | null becomes default(Result). With a fallback it is clean. |
| P10 | new Lazy<PlanResult>() without a factory | caught: SPAR050 | missed | Lazy<T> then creates the value with Activator.CreateInstance<T>(), which is default for a struct. |
| P11 | Interlocked.Exchange(ref field, value) on a Result field | runtime guard only | - | compiles (unconstrained T since .NET 9), but the runtime throws NotSupportedException for a struct that is not a primitive size. No default Result is produced (RuntimeProbes checks the exception). |
| P12 | Volatile.Read(ref field) of a Result field | does not compile: CS0452 | - | the generic overload is constrained to reference types. |
| P13 | Task.FromResult<PlanResult>(default) and Task.FromResult(default(PlanResult)) | caught: SPAR050 | caught | the default argument is a default Result. |
| P14 | ValueTask.FromResult<PlanResult>(default) and new ValueTask<PlanResult>(default(PlanResult)) | caught: SPAR050 | caught | the default argument is a default Result. |
| P15 | TaskCompletionSource<PlanResult>.SetResult(default) / TrySetResult(default) | caught: SPAR050 | caught | the default argument is a default Result. |
| P16 | new ThreadLocal<PlanResult>() without a factory | caught: SPAR051 | missed | Value is default on each thread until that thread sets it. |
| P17 | new AsyncLocal<PlanResult>() | caught: SPAR051 | missed | Value is default until the flow sets it (and in flows that never did). |
| P18 | new StrongBox<PlanResult>() without a value | caught: SPAR051 | missed | Value is default until set. With a value it is clean. |
| P19 | ChannelReader<PlanResult>.TryRead(out var r) with the bool ignored | caught: SPAR051 | missed | the same [MaybeNullWhen(false)] contract as TryGetValue. |
| P20 | Queue<PlanResult>.TryDequeue and ConcurrentQueue.TryPeek with the bool ignored | caught: SPAR051 | missed | the same [MaybeNullWhen(false)] contract. |
| P21 | while (queue.TryDequeue(out var r)) { … } | clean | - | the out variable is scoped to the loop, which runs only on success. |
| P22 | List<PlanResult>.Enumerator.Current read before MoveNext | runtime guard only | - | List's enumerator returns default(T) there; nothing in the signature says so. |
| P23 | dictionary[key] on a missing key | clean | - | throws KeyNotFoundException; no default Result appears. |
| P24 | MaxBy / Max over Results | clean | - | annotated T?, but for a non-nullable value type they throw on an empty sequence, so they are excluded. |
| P25 | List<PlanResult>.Find and Array.Find | caught: SPAR051 | missed | annotated T?; when nothing matches they return default(Result). |
| P26 | ImmutableArray<PlanResult>.FirstOrDefault() | caught: SPAR051 | missed | ImmutableArrayExtensions.FirstOrDefault has the same T? annotation. |
| P27 | DefaultIfEmpty() over Results | caught: SPAR051 | missed | an empty sequence becomes one default Result. Not caught by the annotation (it returns IEnumerable<T?>), so it is on the known list. |
| P28 | ConcurrentDictionary.GetOrAdd with a factory | clean | - | the factory supplies a real Result. |
| P29 | ConcurrentDictionary.TryRemove(key, out var r) with the bool ignored | caught: SPAR051 | missed | the same [MaybeNullWhen(false)] contract as TryGetValue. |
| G01 | a source generic method returning default(T)!, called with T = PlanResult | caught: SPAR054 | missed | SPAR054 joins the body's default(T) with the call's binding at compilation end. |
| G02 | a generic method declared T? (maybe default), called with PlanResult | caught: SPAR051 | missed | the T? annotation is SPAR051 at the call, from the signature alone. |
| G03 | Create<T>() where T : new(), called with PlanResult | caught: SPAR050 | missed | every struct satisfies new(), and new T() is then default(Result). Reported from the constraint. |
| G04 | Activator.CreateInstance<T>() inside a generic method, called with PlanResult | caught: SPAR054 | missed | the generic summary treats Activator.CreateInstance<T>() like default(T). |
| G05 | Box<PlanResult>.Item initialized with default! in a generic class | missed; runtime guard | - | the default(T) is in a property initializer of a generic type, not in a method a call reaches; the creation new Box<PlanResult>() is not followed into initializers. The runtime guard throws on Item. |
| G06 | Outer<T>() calls Make<T>(), called with PlanResult | caught: SPAR054 | missed | the summary follows T through calls between source generic methods. |
| G07 | Identity<T> and IsDefault<T> (EqualityComparer.Equals(value, default)) with PlanResult | clean | - | Identity never creates a default; IsDefault only compares against one. |
| G08 | Cache<PlanResult>.GetOrDefault() returning default! for the type's T | caught: SPAR054 | missed | the binding comes from the receiver's type arguments. |
| G09 | Zero<T>() where T : struct, called with PlanResult | caught: SPAR054 | missed | same summary; the struct constraint changes nothing. |
| G10 | a generic Try method with [MaybeNullWhen(false)] out T, used correctly and with the result ignored | caught: SPAR051 | missed | its own found = default is declared, not reported; ignoring the bool is SPAR051 at the call. |
| G11 | new Pool<PlanResult>() where Pool<T> requires T : new() | caught: SPAR050 | missed | the constraint binding is reported at the creation of the generic type. |
| G12 | default(T)! inside a lambda in a generic method, called with PlanResult | caught: SPAR054 | missed | the lambda belongs to the method, so its default counts for the method's T. |
| G13 | a type argument inferred from an argument, Outer chain through Identity | clean | - | Identity<T>(value) passes T through without creating a default. |
| L01 | an out parameter assigned default | caught: SPAR050 | caught | without [MaybeNullWhen] the caller has no reason to expect a default. |
| L02 | a Try method that assigns default to a [MaybeNullWhen(false)] out parameter | clean | false positive | the attribute declares the default; callers that read it on failure get SPAR051. |
| L03 | an out parameter never assigned | does not compile: CS0177 | - | CS0177. |
| L04 | an unassigned local passed by ref | does not compile: CS0165 | - | CS0165. |
| L05 | an unassigned local returned | does not compile: CS0165 | - | CS0165. |
| L06 | a pattern variable read where the pattern did not match | does not compile: CS0165 | - | CS0165: pattern variables are definitely assigned only where the pattern matched. |
| L07 | a switch expression whose discard arm is default | caught: SPAR050 | caught | the arm's default is target-typed to the Result. |
| L08 | default: return default; in a switch statement | caught: SPAR050 | caught | same operation. |
| L09 | maybe ?? default | caught: SPAR050 | caught | the fallback is a default Result. |
| L10 | ok ? value : default | caught: SPAR050 | caught | the false branch is a default Result. |
| L11 | a lambda returning default | caught: SPAR050 | caught | same operation inside the lambda. |
| L12 | a local function returning default | caught: SPAR050 | caught | same operation inside the local function. |
| L13 | yield return default in an iterator | caught: SPAR050 | caught | same operation. |
| L14 | an async lambda returning default | caught: SPAR050 | caught | same operation. |
| L15 | an expression-bodied property => default | caught: SPAR050 | caught | same operation. |
| L16 | record with { Last = default } | caught: SPAR050 | caught | the assigned value is a default Result. |
| L17 | an optional Result parameter whose default value is default or new() | caught: SPAR050 | caught | every caller that omits the argument passes a default Result. Reported at the parameter. |
| L18 | default! and (PlanResult)default | caught: SPAR050 | caught | the null-forgiving operator and a cast do not change the value. |
| L19 | PlanResult? r = default (null) | clean | - | the default of Nullable<Result> is null, not a default Result. |
| L20 | PlanResult? r = default(PlanResult) | caught: SPAR050 | caught | HasValue is true and the value inside is a default Result. |
| L21 | Result<PlanResult, RunError> built from default(PlanResult) | caught: SPAR050 | caught | the outer Result is a value, but the value is a default inner Result. |
| L22 | object o = default(PlanResult) | caught: SPAR050 | caught | a boxed default Result. |
| L23 | [value, default] as PlanResult[] | caught: SPAR050 | caught | the element is a default Result. |
| L24 | throw where a value cannot be produced | clean | - | no default value exists. |
| L25 | yield return default in an async iterator | caught: SPAR050 | caught | same operation. |
| L26 | r.Equals(default), r.Equals(default(PlanResult)), EqualityComparer<PlanResult>.Default.Equals(r, default) | clean | false positive | comparing against a default creates no Result anyone reads. Equals(default) passes a null object and is false. |
| L27 | r == default | does not compile: CS0019 | - | Result declares no == operator (CS0019). |
| L28 | [return: MaybeNull] PlanResult Find() => default, and a caller | caught: SPAR051 | missed | the declaration is clean (the attribute declares the default); the caller gets SPAR051. |
| L29 | a local assigned on every path before use | clean | - | definite assignment holds; no default value is created. |
| L30 | a struct holding a Result, reset with this = new() | caught: SPAR050 | missed | new() of a struct without a parameterless constructor is default. |
| X01 | a library method that returns default with no hint in its signature | runtime guard only | - | only the library's IL knows; the consumer sees an ordinary Result return. The runtime guard throws on use. |
| X02 | a library method marked [return: MaybeNull] | caught: SPAR051 | missed | the attribute is in metadata, so SPAR051 works across assemblies. |
| X03 | a library Try method with [MaybeNullWhen(false)], bool ignored | caught: SPAR051 | missed | the attribute is in metadata. |
| X04 | a library class whose public Result field nothing assigns | runtime guard only | - | the library was not built with the analyzer; the consumer only reads a field. |
| X05 | a library Create<T>() where T : new(), called with a Result | caught: SPAR050 | missed | the constraint is in the signature. |
| X06 | a library Make<T>() => default!, unannotated, called with a Result | runtime guard only | - | the body is IL in another assembly; SPAR054 needs the body in source. |
| X07 | a library Find<T>() declared T?, called with a Result | caught: SPAR051 | missed | the T? annotation is in metadata. |
| X08 | default(LibPair), a library struct with a private Result field | caught: SPAR050 | missed | depends on whether the analyzer can see private fields of a referenced struct (see RESULTS.md). |
| X09 | a call that omits a library method's optional Result parameter (= default) | caught: SPAR050 | caught | the omitted argument is an implicit default Result, reported at the call because the parameter is metadata. |
| X10 | a library method that returns an array of default Results | runtime guard only | - | the array creation is inside the library. |
| X11 | a third-party generator's output that returns default, and a generated unassigned field | runtime guard only | - | generated code is not analysed (GeneratedCodeAnalysisFlags.None): the user cannot fix it. The consumer's call looks ordinary. The generated file is Defaults.g.cs from CorpusGenerator. |
| C01 | values and error cases returned by union conversion | clean | - | the ordinary way to produce a Result. |
| C02 | Result.Success and default(Success) | clean | - | Success is a valueless marker struct; its default is the success. |
| C03 | a switch expression whose every arm is a value or an error | clean | - | no default arm value. |
| C04 | an interface-typed value converted by the union | clean | - | an ordinary union conversion. |
| C05 | async, Task.FromResult and ValueTask returns of real Results | clean | - | every returned Result is a value or an error. |
| C06 | collections built from values with Select, ToArray, ToList, ToDictionary | clean | - | every element comes from a value. |
| C07 | reading a Result: switch, TryGetValue/TryGetError with the bool ignored, Value, ToString | clean | - | TryGetValue's out argument is the T or the error, not a Result. |
| C08 | default of other types: CancellationToken, int, string, a class, a struct without Result | clean | - | only values that hold a Result are reported. |
| C09 | Lazy, ThreadLocal, StrongBox, TaskCompletionSource with real values | clean | - | each holder gets a Result through a factory or a value. |
| C10 | a class with required, initialized and constructor-assigned Result members | clean | - | every member is assigned before it can be read. |
| C11 | generic code over Results that never creates a default | clean | - | values flow in and out. |
| C12 | tuples and record structs built from values | clean | - | every Result element is a value. |
| C13 | Result? as the way to say "maybe unset": HasValue, ??, Value | clean | - | null is the unset state; a fallback is a real Result. |
| C14 | Result inside Task, IEnumerable, Dictionary, Func, with no default | clean | - | nothing creates a default. |
