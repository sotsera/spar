# C# `Result` API for commands

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/result-api/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/result-api) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

**Question.** What is the C# side of `Result<T, TError>` that commands return, where `TError` is a C# 15 `closed record` with nested sealed cases? It must suit command authors, C# callers and tests, the generated dispatch and serializer, and NativeAOT with warnings as errors. What do `default(Result<…>)` and an unset field mean?

## Summary and recommendation

**Recommended: a `readonly struct` declared as a C# 15 custom union through a union member provider (form `UnionStruct`).**

- **Authors** write `return plan;`, `return new PlanError.TableMissing(…);` and, for a command without a value, `return Result.Success;`. These are compiler union conversions, not user-defined operators. They work in sync, `Task`, `ValueTask`, lambdas, switch arms, target-typed `?:` and generic code. Unlike user-defined operators, they also work when the value's static type is an interface: `Result<IReadOnlyList<PlanStep>, PlanError> r = steps;` is CS0029 with implicit operators, in both the class and the struct form.
- **Callers and tests** get one exhaustive `switch` over the result itself: the value type and every error case, with no default arm. A missing case or a missing value arm is CS8509 (an error here). `await call switch { … }` works directly. For assertions, `result.Value` throws with the error in its message, `result.Error` throws with the value in its message, and `Assert.IsType<PlanError.TableMissing>(result.Error)` returns the typed case. **Plain xunit.v3 asserts are enough; Spar needs no assertion helpers and no `Task<Result>` extensions.**
- **`default(Result<…>)` is a distinct invalid state.** Every member except `ToString` throws `InvalidOperationException("default(Result) is neither a value nor an error. …")`, and so does a union `switch` over it, because the members the compiler uses for patterns throw too. If the generated writer reads the result inside its buffered write, a default result becomes the existing `response-serialization` failure with `execution: "completed"`, so nothing new is needed. This was not run through a bridge here. At compile time, a 40-line Spar analyzer (prototype here, `SPAR050`) reports `default`, `default(Result<…>)` and `new()`. Array elements and never-assigned fields stay runtime-only.
- **Costs** (NativeAOT, 20-command sample, dispatch with one async method per command): the struct allocates nothing for the result; the class forms add 32–40 B per call. On an async call (about 290–330 B in total) the forms differ by less than 10 B. The struct forms make the executable **about 17–19 KB larger than the class forms** for this sample. The ILC map shows the premium is about 8 KB of shared async machinery over the canonical struct, plus about 1.6 KB for each distinct value-type success type (6 here). Commands that return records add almost nothing. The union provider adds 3.5 KB over a plain struct. Per-call timing is the same within noise (about 120–180 ns per synchronous dispatch for every form). In BenchmarkDotNet, creating and reading an error is 0.75 ns for the struct against 3.5 ns for the class, and awaiting a synchronous `ValueTask` result is 5–7 ns slower for the struct.
- **App-model impact:** the code in `docs/app-model.md` stays as it is. Its return statements and signatures compile unchanged against this form in `Shared/CopyJobsService.cs`, a copy of the command bodies with the connection store replaced by a dictionary. The text changes are listed under [App-model changes](#app-model-changes).

The struct is chosen over the class (`UnionClass`) for its value semantics, which match `ValueTask<T>` and `Nullable<T>`, and because it allocates nothing per call. That would also hold per item if streams get declared errors (still open). The class gives compile-time detection of an unset result for free, through nullable analysis (5 of 6 probe constructs, against 4 of 6 for the struct with the analyzer). It also makes the executable about 19 KB smaller for 20 commands. **If the user weighs executable size and analyzer-free detection above per-call allocation, `UnionClass` is the alternative.** It has the same public API, and every caller-side finding here applies to it as well.

### Public surface

```csharp
namespace Spar;

/// A value of T or one case of TError (a closed record). A C# union: values and error cases convert implicitly,
/// and a switch over the result is exhaustive when it handles T and every error case.
/// default(Result<T, TError>) is neither: every member except ToString throws on it.
[Union]
public readonly struct Result<T, TError> : Result<T, TError>.IUnionMembers
    where TError : class
{
    public bool IsSuccess { get; }
    public bool IsError { get; }

    /// Throws InvalidOperationException when the result is an error; the message shows the error.
    public T Value { get; }

    /// Throws InvalidOperationException when the result is a value; the message shows the value.
    public TError Error { get; }

    public bool TryGetValue([MaybeNullWhen(false)] out T value);
    public bool TryGetError([NotNullWhen(true)] out TError? error);

    /// "Success(…)", "Error(…)" or "default(Result): neither a value nor an error".
    public override string ToString();

    /// Used by the compiler for union conversions and patterns, never called directly. Kept on a provider
    /// interface so that the result's own Value can be typed T (a union's Value must otherwise be object).
    public interface IUnionMembers
    {
        object? Value { get; }
        bool HasValue { get; }
        static Result<T, TError> Create(T value);
        static Result<T, TError> Create(TError error);   // throws ArgumentNullException on null
        bool TryGetValue([MaybeNullWhen(false)] out T value);
        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}

/// A command with declared errors and no value: Success or one case of TError.
[Union]
public readonly struct Result<TError> : Result<TError>.IUnionMembers
    where TError : class
{
    public bool IsSuccess { get; }
    public bool IsError { get; }
    public TError Error { get; }
    public bool TryGetError([NotNullWhen(true)] out TError? error);
    public override string ToString();

    public interface IUnionMembers
    {
        object? Value { get; }
        bool HasValue { get; }
        static Result<TError> Create(Success success);
        static Result<TError> Create(TError error);
        bool TryGetValue(out Success value);
        bool TryGetValue([NotNullWhen(true)] out TError? value);
    }
}

/// The success of a command that returns no value: `return Result.Success;`, and `Success =>` in a switch.
public readonly record struct Success;

public static class Result
{
    public static Success Success { get; }
}
```

The full source is `Forms/UnionStruct/Result.cs`. It also has `Match` and `Switch`, which were kept only to compare the forms. **Leave them out of Spar.** The `switch` expression does the same job, is exhaustive per error case and needs no delegates. They can be added later without breaking anything. No `Success(…)`/`Failure(…)` factories are needed either: every place that needs a `Result` has a target type. The probes create every kind of result without one.

Usage, as tested in `Tests/`:

```csharp
// Author
return new PlanError.TableMissing(recipe.Source, table);
return new CopyPlan(Guid.CreateVersion7(), steps);
return Result.Success;                                    // Task<Result<CancelError>>

// Caller
var text = await copyJobs.PlanAsync(recipe, cancellationToken) switch
{
    CopyPlan plan => $"{plan.Steps.Count} steps",
    PlanError.ConnectionNotFound e => $"no connection {e.ConnectionId}",
    PlanError.TableMissing e => $"{e.Table} is missing",
};

// Test (plain xunit.v3)
var plan = result.Value;                                   // fails the test with the error in the message
var missing = Assert.IsType<PlanError.TableMissing>(result.Error);
Assert.Equal(new PlanError.TableMissing("src", "dbo.Gone"), result.Error);
```

### What the generator needs

- **Recognise** `Spar.Result\`2` and `Spar.Result\`1` by metadata name. `Result<TError>` is written as `{"ok":true}` with no `value` key (the void rule), and its TypeScript is `Call<void, E>`.
- **Write** without reflection and without the provider interface:

  ```csharp
  writer.WriteStartObject();
  if (result.TryGetError(out var error))
  {
      writer.WriteBoolean("ok"u8, false);
      writer.WritePropertyName("error"u8);
      WritePlanError(writer, error);          // switch over the closed cases: "code", payload
  }
  else
  {
      writer.WriteBoolean("ok"u8, true);
      writer.WritePropertyName("value"u8);
      WriteCopyPlan(writer, result.Value);
  }
  writer.WriteEndObject();
  ```

  The same text compiles against all four forms. They produced byte-identical JSON for 60 responses (FNV hash `d2042e6edab2b416` in every variant).
- **Read the result inside the buffered writer stage,** so a default result (it throws there) ends as `response-serialization` with `execution: "completed"`. A command that returned default did complete. Whether that needs its own `kind` is part of the open `CallError.kind` list.
- **Emit one small async method per command,** selected by a non-async switch, not one async method that awaits every command. The single method's state machine keeps an awaiter field per result type, and every async call boxes all of them. That doubled async allocations to about 600 B per call, and to 775 B with struct results (`results/size-OneSwitch.txt`).
- **New diagnostics to consider:**
  - a value type that overlaps the error type: `Result<PlanError, PlanError>` is CS0457 on every form, and `Result<object, E>` compiles but is not a wire type anyway;
  - SPAR050 for default results (struct form).

## Method

Everything is under `spikes/result-api/`, built with SDK 11.0.100 (C# 15) and the repository's `Directory.Build.props` (nullable, warnings as errors, code style). No analyzer severity was changed and nothing was suppressed.

| Path | Contents |
|---|---|
| `Forms/{Class,Struct,UnionStruct,UnionClass}/Result.cs` | The four candidates, with one shared public API (`IsSuccess`, `IsError`, `Value`, `Error`, `TryGetValue`, `TryGetError`, `Match`, `Switch`, `ToString`), `Result<TError>`, `Success` and `Result.Success`. `Class`: sealed class with implicit operators (the app-model stub, extended). `Struct`: readonly struct with implicit operators and a state byte (0 = default). `UnionStruct` and `UnionClass`: `[Union]` with a nested `IUnionMembers` provider (static `Create` factories, `object? Value`, `HasValue`, `TryGetValue` per case). |
| `Shared/` | The app-model contract types and the `CopyJobsService` command bodies (DI replaced by a dictionary). |
| `Probes/` + `run-probes.ps1` | 11 compile cases × 4 forms, each built separately and its diagnostics recorded (`results/probes.txt`), plus a pass with the analyzer. |
| `Tests/` | xunit.v3 project, built once per form (`-p:Form=…`), that calls the commands in-process. `Union/` holds tests that only the union forms compile. The `Explicit` tests fail on purpose, to record the assertion messages (`results/test-failure-messages.txt`). |
| `Size/` + `run-size.ps1` | `generate.ps1` writes a 20-command sample into each variant's `obj/generated/`: commands written as an author would write them, plus a dispatch and `Utf8JsonWriter` code shaped like the generated code. There are 14 record values and 6 scalar values (`long`, `int`, `bool`, `Guid`, `double`, `string`), 10 `Task` and 10 `ValueTask` commands, and 20 two-case error hierarchies. The variants are `Plain` (no declared errors), `Class`, `Struct`, `UnionStruct` and `UnionClass`. Each is published with NativeAOT (default ILC settings), then run as the exe and under the JIT. They measure JSON equality, allocated bytes per call and time per call. |
| `Bench/` | `ResultBench.csproj`: BenchmarkDotNet 0.15.8, CoreCLR job, `DontEnforcePowerPlan()`. The four forms are referenced as separate assemblies through extern aliases. Artifacts are in `artifacts/result-api/bdn/`. |
| `Size/map-summary.ps1` | Reads the ILC map files of a publish with `-p:IlcGenerateMapFile=true` and adds up the `Result` symbols. |
| `Analyzers/` | The SPAR050 prototype (`DefaultResultAnalyzer`). |
| `Probes/FileBased/union_*.cs` | The first file-based probes of C# 15 unions as the outer `Result` (`dotnet run <file>`). `union_provider.cs` is the member-provider shape. Its allocation line runs at tier 0, see problem 4. |

Commands to reproduce, from the repository root:

```
pwsh spikes/result-api/run-probes.ps1
pwsh spikes/result-api/run-size.ps1 -DispatchShape PerCommand
pwsh spikes/result-api/run-size.ps1 -DispatchShape OneSwitch
pwsh spikes/result-api/Size/generate.ps1 -DispatchShape PerCommand
dotnet publish spikes/result-api/Size/UnionStruct/Size.UnionStruct.csproj -c Release -p:IlcGenerateMapFile=true   (and Class, Struct, UnionClass)
pwsh spikes/result-api/Size/map-summary.ps1
dotnet test --project spikes/result-api/Tests/Tests.csproj -p:Form=UnionStruct --results-directory spikes/result-api/Tests/obj/TestResults
dotnet run --project spikes/result-api/Bench/ResultBench.csproj -c Release
```

## Results

### 1. Compile-time behaviour (`results/probes.txt`)

| Case | Class | Struct | UnionStruct | UnionClass |
|---|---|---|---|---|
| Conversions: sync, `Task`, `ValueTask`, lambda, switch arms, `?:`, scalar and value-type `T`, generic `T`, `Result.Success` | compiles | compiles | compiles | compiles |
| Value whose static type is an interface (`IReadOnlyList<PlanStep>`) | **CS0029** | **CS0029** | compiles | compiles |
| Reading: `TryGetValue`/`TryGetError`, `IsError` then `Error`, `Match`, property patterns, extracted `error switch` without a default arm | compiles, no nullable warnings | same | same | same |
| Extracted error switch missing a case | CS8509 | CS8509 | CS8509 | CS8509 |
| One switch over the result (`CopyPlan p => …, PlanError.X e => …`) | CS8121 (not a union) | CS8121 | compiles | compiles |
| …missing an error case, or missing the value arm | n/a | n/a | CS8509, CS8509 | CS8509, CS8509 |
| Switch over `Result<CopyPlan?, PlanError>` without a `null` arm | n/a | n/a | CS8655 | CS8655 |
| Same, generic unconstrained `TValue` | n/a | n/a | CS8655 (needs `where TValue : notnull`, or a `null` arm) | CS8655 |
| `Result<PlanError, PlanError>` | CS0457 | CS0457 | CS0457 | CS0457 |
| `ShouldBeError<TCase>()` helper | classic extension and C# 14 extension block both need all three type arguments (CS1061 with one) | same | same | same |

**Default and unset values** (`Probes/Cases/DefaultValue.cs`):

| Construct | Class / UnionClass | Struct / UnionStruct | Struct forms + SPAR050 |
|---|---|---|---|
| `=> default;` | CS8603 | silent | SPAR050 |
| `return default;` in an async method | CS8603 | silent | SPAR050 |
| `Result<CancelError> … => default;` | CS8603 | silent | SPAR050 |
| `new()` | CS1729 (no such constructor) | silent | SPAR050 |
| `new Result<…>[4]` | silent | silent | silent |
| field never assigned in the constructor | CS8618 | silent | silent |

An unassigned local is CS0165 on every form. At run time, a default class result is a `NullReferenceException` at the first member access. A default struct result throws `InvalidOperationException` with the message above, on every member and in a union `switch` (`Tests`, `DefaultThrowsOnEveryRead`, `SwitchOverDefaultThrowsTheResultMessage`).

### 2. Tests (`Tests/`)

| Form | Debug | Release |
|---|---|---|
| Class | 11 passed | 11 passed |
| Struct | 12 passed | 12 passed |
| UnionStruct | 18 passed | 18 passed |
| UnionClass | 16 passed | 16 passed |

In every run, 4 deliberately failing tests are `Explicit` and skipped. Their messages, run separately (`results/test-failure-messages.txt`):

- `result.Value` on an error: `The result is an error, not a value: TableMissing { Connection = src, Table = dbo.Gone }.`
- `Assert.IsType<PlanError.TableMissing>(result.Error)` on a success: `The result is a value, not an error: CopyPlan { PlanId = …, Steps = System.Collections.Generic.List`1[…] }.`
- `Assert.IsType` with the wrong case: xunit's own message with expected and actual case types.
- `Assert.Equal` on a case: both records printed.

The union forms read a value-type value without boxing once the JIT has optimised the code: 0 B for 1,000 switches over `Result<long, PlanError>`. See problem 4 for the unoptimised code.

### 3. Size and allocations (NativeAOT, `results/size-PerCommand.txt`)

Executable size, 20 commands:

| Variant | Bytes | vs Plain | vs the class form |
|---|---|---|---|
| Plain (no declared errors) | 1,517,568 | 0 | |
| Class | 1,599,488 | +81,920 | |
| Struct | 1,616,384 | +98,816 | +16,896 (vs Class) |
| UnionClass | 1,601,024 | +83,456 | +1,536 (vs Class) |
| UnionStruct | 1,619,968 | +102,400 | +18,944 (vs UnionClass) |

The +82 KB from Plain to Class covers 20 error hierarchies, their writers and `Result`. The struct premium comes from generic instantiation. For a class `Result`, `Task<Result<…>>` and the async builders are reference instantiations, which share code. For a struct `Result`, each `Task<Result<…>>`, `ValueTask<Result<…>>` and builder is instantiated over the struct. The one-switch dispatch shape gives the same ordering (`results/size-OneSwitch.txt`).

Allocated bytes per call, averaged over the commands, NativeAOT. Under the JIT (`dotnet run -c Release`), the synchronous rows are identical except for the union forms' tier-0 residue (problem 4), and the async rows vary by about ±10 B:

| Path | Plain | Class | Struct | UnionStruct | UnionClass |
|---|---|---|---|---|---|
| sync success, `Task` commands | 120.0 | 159.2 | 142.4 | 142.4 | 166.4 |
| sync success, `ValueTask` commands | 60.0 | 92.8 | **60.0** | **60.0** | 100.0 |
| sync error, `Task` commands | — | 128.0 | 111.2 | 111.2 | 135.2 |
| sync error, `ValueTask` commands | — | 56.8 | **24.0** | **24.0** | 64.0 |
| async (one `Task.Yield`), success | 293.4 | 324.7 | 323.7 | 323.6 | 331.5 |
| async, error | — | 290.7 | 289.4 | 290.0 | 297.8 |

In the `ValueTask` error row, 24 B is the error record itself. The class forms add one 32–40 B object per result (`UnionClass` has one more field). A struct `Result` is stored inline in `Task<Result<…>>`, which makes that task larger than `Task<T>`. So in the `Task` rows the struct forms add 22.4 B over Plain, against 39.2 B for Class, rather than nothing.

Time per synchronous dispatch, NativeAOT, best of five runs of 40,000 calls:

- success: 157 ns (Plain), 169 (Class), 176 (Struct), 169 (UnionStruct), 170 (UnionClass);
- error: 124 ns (Class), 122 (Struct), 123 (UnionStruct), 124 (UnionClass).

The one-switch run gave UnionStruct 207/214 ns against 169/123 here, while the other forms moved by less than 10 ns. One value can move by up to 40 ns between runs, so the forms are equal within noise.

### 4. BenchmarkDotNet (CoreCLR, `artifacts/result-api/bdn/`)

Full table: `results/bdn-coreclr.md`. The job had 2 launches × 15 iterations.

| Benchmark | Class | Struct | UnionClass | UnionStruct |
|---|---|---|---|---|
| create an error, then `TryGetError` and switch over the case | 3.52 ns, 32 B | 0.76 ns, 0 B | 3.69 ns, 40 B | 0.75 ns, 0 B |
| await a `ValueTask<Result>` that completed synchronously, then read it | 11.0 ns, 32 B | 15.7 ns, 0 B | 11.4 ns, 40 B | 18.6 ns (±3.0), 0 B |
| create a success, then read it | 0.00 ns, 0 B | 0.00 ns | 0.01 ns | 0.00 ns |

- **The success row measures nothing.** The JIT inlined and folded it, and for the class form it also removed the allocation: .NET 11 stack-allocates the object when it does not escape. The dispatch in the size apps does let the result escape, and there the class allocates.
- `UnionStructErrorSwitch` (the union `switch` instead of `TryGetError`) measured 0.01 ns, so it was folded as well.
- **Awaiting** a struct result costs 5–7 ns more than a class result, because the larger `ValueTask<Result<…>>` is copied through the builder and the awaiter. Against 120–180 ns for a whole dispatch and about 0.2 ms for a bridge call, every difference in this table is negligible. The allocations are what differ.

### 5. Where the struct's extra size comes from (`results/map-summary.txt`)

The ILC map files of the per-command builds give the bytes of the methods whose symbols mention `Spar.Result`:

| Form | Result method code | canonical instantiation (`Result<__Canon, __Canon>`, shared by every reference-type `T`) | the 6 value-type `T` | methods |
|---|---|---|---|---|
| Class | 5,120 | 875 | 4,245 | 17 |
| Struct | 21,630 | 8,086 | 13,544 | 256 |
| UnionStruct | 21,986 | 8,239 | 13,747 | 260 |
| UnionClass | 5,394 | 837 | 4,557 | 24 |

With a struct, `Task<>`, `ValueTask<>`, their awaiters and the async builders are instantiated over `Result<…>`. For the 14 record values that is one shared canonical copy (about 8 KB, paid once). For each distinct value-type `T` it is a separate copy: about 2.3 KB per type for the struct, against about 0.7 KB for the class. **So the premium grows with the number of distinct value-type success types (`long`, `int`, `bool`, `Guid`, …), not with the number of commands.** Commands returning records add almost nothing once the canonical copy exists. Estimate for the struct over the class: about 7 KB fixed plus about 1.6 KB per distinct value-type `T`. `ToString` is the largest single method in each instantiation (about 500 B), because of the interpolated strings.

## Problems met

1. **A union's own `Value` must be `object`.** Declaring `public T Value` on a `[Union]` type is CS9386. A nested `public interface IUnionMembers` moves the compiler's members (factories, `Value`, `HasValue`, `TryGetValue`) off the type, which frees the type's own surface (C# 15 proposal, "Union member providers"). One blog post said member providers were not implemented in an earlier preview. SDK 11.0.100 has them.
2. **A success holding `null` must not have a union value.** The spec requires `HasValue` to be false exactly when `Value` is null, and `TryGetValue` to return true only for a non-null value. The first version returned `HasValue = true` for a null success, so a `switch` with a `null` arm threw. Fixed. A null success now takes the `null` arm, and the public `TryGetValue` still reports it as a success.
3. **Implicit operators do not convert from interface-typed expressions.** User-defined conversions from an interface type are not considered, so `IReadOnlyList<T>` values are CS0029 in the class and struct forms. Union conversions have no such rule. *(New pitfall; not added to `docs/pitfalls.md` because this task writes only under the spike.)*
4. **Unoptimised JIT code boxes a value-type `T` for `value is not null`.** A test that counted allocations over 1,000 switches saw 24,000 B in Debug and in Release (tier 0), and 0 B with `DOTNET_TieredCompilation=0`. The provider now records whether the value is null once, in the constructor. The JIT run of the size app still shows +5–7 B per call on average for the union forms (the 2–3 value-type commands per group, 24 B each, boxed once per construction at tier 0). NativeAOT shows none. *(New pitfall.)*
5. **BenchmarkDotNet needs project names that are unique in the repository.** `Bench.csproj` clashed with `spikes/serializer-bench/Bench/Bench.csproj` ("Found more than one matching project file"), so it was renamed `ResultBench.csproj`. *(New pitfall.)*
6. **An analyzer whose descriptor uses a literal id needs release-tracking files** (RS2008, an error here). `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` were added as `AdditionalFiles`, which is the fix, not a suppression.
7. **One async dispatch method for every command boxes every awaiter** (see "What the generator needs").

## Not tested

- IDE behaviour: completions, the `IUnionMembers` interface showing up in IntelliSense (`[EditorBrowsable(Never)]` may hide it; not checked), the SPAR050 squiggle, and union-switch code fixes in Visual Studio and VS Code.
- A managed C# client that reads results off the wire. Union conversions should let generated code build results without factories, but no reader was written.
- Streams or subscriptions with `Result` items (still open in the plan).
- An analyzer rule for never-assigned fields or arrays of `Result`.
- Linux, and BenchmarkDotNet NativeAOT jobs. NativeAOT allocations and times come from the size apps, not from BDN.
- Background load on the machine was not sampled during the runs.
- Executable size beyond 20 commands. The per-value-type estimate comes from one mix, 14 records and 6 scalars.

## App-model changes

The code in `docs/app-model.md` does not change. The return statements of `PlanAsync` and `RunAsync` compile as written against the recommended form. `IConnections.TestAsync` is a signature of the same shape; it was not compiled here. If the user adopts it:

- **Section 3.2, the "Returns" bullet** (line 212): add `Task<Result<TError>>` (and `ValueTask<Result<TError>>`) for a command with errors and no value. Plan section 2, "Commands", "Returns", gets the same addition.
- **Section 3.2, the "Conversions" bullet** (line 213): add `return Result.Success;`, and say that these are union conversions, so they also work for interface-typed values such as `IReadOnlyList<T>`.
- **Section 3.1** (after line 124): a bullet on reading a result in C#: one exhaustive `switch` over the result (`CopyPlan plan => …, PlanError.TableMissing e => …`); `Value` and `Error` that throw with the other side in the message; `TryGetValue`/`TryGetError`; `default` throws, reported by SPAR050. Optionally a small `ExhaustiveChecks`-style example.
- **Section 7** (line 463): remove "the C# `Result<T, TError>` API, and what `default(Result<T, TError>)` means".
- **The compiled check:** `spikes/app-model-check/Spar.Stubs/Result.cs` becomes the recommended struct. `SqlTool/ExhaustiveChecks.cs` gains a union switch over `Result<CopyPlan, PlanError>`, a `Result<TError>` command, and a `Missing` method that still converts a case.
- **Plan section 2, "Errors"** (the paragraph that says "The C# `Result<T, TError>` API is not defined yet"): replace it with the chosen API and the `default` rule.
