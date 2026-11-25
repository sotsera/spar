# Corpus results, SDK 11.0.100, 2026-11-14

199 cases. Expected = the markers in the case file. 'New matches' = the new analyzer's build gives
exactly the expected diagnostics (IDs and lines), no more and no fewer. Baseline = the SPAR050 prototype.

| Kind | Cases | New matches | Baseline caught | Baseline extra SPAR050 |
|---|---|---|---|---|
| detect | 112 | 112 | 28 | 2 |
| clean | 57 | 57 | 0 | 3 |
| missed | 3 | 3 | 0 | 0 |
| runtime | 11 | 11 | 0 | 0 |
| impossible | 7 | 7 | 0 | 0 |
| by-design | 4 | 4 | 1 | 0 |
| compiler | 5 | 5 | 0 | 0 |

| Case | Kind | What | Expected | Baseline (prototype) | New | New matches | Why |
|---|---|---|---|---|---|---|---|
| P01 | detect | TryGetValue(key, out var r) with the bool ignored, then r returned | SPAR051 | - (missed) | SPAR051 | yes | [MaybeNullWhen(false)] says r is default on a miss, and nothing checks the result. |
| P02 | detect | r read in the branch where TryGetValue returned false | SPAR051 | - (missed) | SPAR051 | yes | the failing branch reads the default. |
| P03 | clean | TryGetValue used as intended (if, early return, ?:, &&, assignment on miss) | - | - | - | yes | r is read only where the call succeeded, or after it was assigned. |
| P04 | detect | the miss branch only logs, then r is read after the if | SPAR051 | - (missed) | SPAR051 | yes | the failing branch neither exits nor assigns, so the read after the if can see the default. |
| P05 | missed | the bool result stored in a local and tested later | - | - | - | yes | following the bool through a local needs real flow analysis; the runtime guard throws. |
| P06 | detect | FirstOrDefault, LastOrDefault, SingleOrDefault, ElementAtOrDefault without a fallback | SPAR051 | - (missed) | SPAR051 | yes | annotated T?; on a miss they return default(Result). SPAR051 offers First()/Last()/Single()/ElementAt(). |
| P07 | clean | FirstOrDefault(fallback), GetValueOrDefault(key, fallback), First() | - | - | - | yes | the fallback is a real Result; First() throws on an empty sequence instead. |
| P08 | detect | GetValueOrDefault(key) on a dictionary of Results | SPAR051 | - (missed) | SPAR051 | yes | a missing key gives default(Result). |
| P09 | detect | Result?.GetValueOrDefault() without a fallback | SPAR051 | - (missed) | SPAR051 | yes | null becomes default(Result). With a fallback it is clean. |
| P10 | detect | new Lazy<PlanResult>() without a factory | SPAR050 | - (missed) | SPAR050 | yes | Lazy<T> then creates the value with Activator.CreateInstance<T>(), which is default for a struct. |
| P11 | runtime | Interlocked.Exchange(ref field, value) on a Result field | - | - | - | yes | compiles (unconstrained T since .NET 9), but the runtime throws NotSupportedException for a struct that is not a primitive size. No default Result is produced (RuntimeProbes checks the exception). |
| P12 | impossible | Volatile.Read(ref field) of a Result field | CS0452 | CS0452 | CS0452 | yes | the generic overload is constrained to reference types. |
| P13 | detect | Task.FromResult<PlanResult>(default) and Task.FromResult(default(PlanResult)) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the default argument is a default Result. |
| P14 | detect | ValueTask.FromResult<PlanResult>(default) and new ValueTask<PlanResult>(default(PlanResult)) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the default argument is a default Result. |
| P15 | detect | TaskCompletionSource<PlanResult>.SetResult(default) / TrySetResult(default) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the default argument is a default Result. |
| P16 | detect | new ThreadLocal<PlanResult>() without a factory | SPAR051 | - (missed) | SPAR051 | yes | Value is default on each thread until that thread sets it. |
| P17 | detect | new AsyncLocal<PlanResult>() | SPAR051 | - (missed) | SPAR051 | yes | Value is default until the flow sets it (and in flows that never did). |
| P18 | detect | new StrongBox<PlanResult>() without a value | SPAR051 | - (missed) | SPAR051 | yes | Value is default until set. With a value it is clean. |
| P19 | detect | ChannelReader<PlanResult>.TryRead(out var r) with the bool ignored | SPAR051 | - (missed) | SPAR051 | yes | the same [MaybeNullWhen(false)] contract as TryGetValue. |
| P20 | detect | Queue<PlanResult>.TryDequeue and ConcurrentQueue.TryPeek with the bool ignored | SPAR051 | - (missed) | SPAR051 | yes | the same [MaybeNullWhen(false)] contract. |
| P21 | clean | while (queue.TryDequeue(out var r)) { … } | - | - | - | yes | the out variable is scoped to the loop, which runs only on success. |
| P22 | runtime | List<PlanResult>.Enumerator.Current read before MoveNext | - | - | - | yes | List's enumerator returns default(T) there; nothing in the signature says so. |
| P23 | clean | dictionary[key] on a missing key | - | - | - | yes | throws KeyNotFoundException; no default Result appears. |
| P24 | clean | MaxBy / Max over Results | - | - | - | yes | annotated T?, but for a non-nullable value type they throw on an empty sequence, so they are excluded. |
| P25 | detect | List<PlanResult>.Find and Array.Find | SPAR051 | - (missed) | SPAR051 | yes | annotated T?; when nothing matches they return default(Result). |
| P26 | detect | ImmutableArray<PlanResult>.FirstOrDefault() | SPAR051 | - (missed) | SPAR051 | yes | ImmutableArrayExtensions.FirstOrDefault has the same T? annotation. |
| P27 | detect | DefaultIfEmpty() over Results | SPAR051 | - (missed) | SPAR051 | yes | an empty sequence becomes one default Result. Not caught by the annotation (it returns IEnumerable<T?>), so it is on the known list. |
| P28 | clean | ConcurrentDictionary.GetOrAdd with a factory | - | - | - | yes | the factory supplies a real Result. |
| P29 | detect | ConcurrentDictionary.TryRemove(key, out var r) with the bool ignored | SPAR051 | - (missed) | SPAR051 | yes | the same [MaybeNullWhen(false)] contract as TryGetValue. |
| A01 | detect | new PlanResult[count] returned | SPAR052 | - (missed) | SPAR052 | yes | every element is default until assigned. |
| A02 | clean | new PlanResult[0] | CA1825 | CA1825 | CA1825 | yes | an empty array has no element to read. (.NET's CA1825 suggests Array.Empty.) |
| A03 | clean | new PlanResult[] { … } and new PlanResult[2] { … } | - | - | - | yes | the compiler requires one initializer element per slot. |
| A04 | clean | collection expressions and spreads | - | - | - | yes | every element comes from a value. |
| A05 | clean | new PlanResult[n] filled by the for loop that follows it | - | - | - | yes | the recognised fill shape writes every element before the array can be read (FillLoop.cs). |
| A06 | detect | a fill loop that can skip an element | SPAR052 | - (missed) | SPAR052 | yes | continue skips the assignment, so the skipped slot stays default. Not the recognised shape. |
| A07 | by-design | new PlanResult[n] filled with Parallel.For | SPAR052 | - (missed) | SPAR052 | yes | correct code, but the analyzer cannot prove the delegate writes every slot. Rewrite with Select/ToArray or suppress at the site. |
| A08 | detect | new PlanResult[2, 3] | SPAR052 | - (missed) | SPAR052 | yes | every element of a rectangular array starts default. |
| A09 | clean | new PlanResult[3][] (jagged outer array) | - | - | - | yes | the elements are null arrays, not Results. |
| A10 | detect | Array.Resize growing an array | SPAR052 | - (missed) | SPAR052 | yes | the new slots are default. |
| A11 | detect | GC.AllocateUninitializedArray and GC.AllocateArray | SPAR052 | - (missed) | SPAR052 | yes | for a type holding references the runtime zeroes the array, so every element is default. |
| A12 | detect | Array.Clear on a Result array | SPAR052 | - (missed) | SPAR052 | yes | clearing writes default into each slot. |
| A13 | detect | Span<Result>.Clear() and Span<Result>.Fill(default) | CA1855, SPAR050, SPAR052 | CA1855, SPAR050 (missed) | CA1855, SPAR050, SPAR052 | yes | Clear writes default (SPAR052); Fill(default) passes a default Result (SPAR050 on the argument). CA1855 (.NET) suggests Clear() for Fill(default), which SPAR052 then reports. |
| A14 | detect | CollectionsMarshal.SetCount on a List<Result> | SPAR052 | - (missed) | SPAR052 | yes | growing the count exposes default slots. |
| A15 | clean | CollectionsMarshal.AsSpan on a List<Result> | - | - | - | yes | the span covers only the Count elements, which were added as values. |
| A16 | detect | ArrayPool<Result>.Shared.Rent, and Return with clearArray | SPAR052 | - (missed) | SPAR052 | yes | a rented array is new (default elements) or was returned by someone else (stale or cleared elements). Returning with clearArray: true is not reported: the caller gives the array up. |
| A17 | clean | default(ImmutableArray<Result>) | - | - | - | yes | a default ImmutableArray is not a default Result; it fails on its own (IsDefault, NullReferenceException). |
| A18 | detect | ImmutableArray<Result>.Builder.Count = n | SPAR052 | - (missed) | SPAR052 | yes | raising Count appends default elements. |
| A19 | detect | Array.CreateInstance(typeof(Result), n) | SPAR052 | - (missed) | SPAR052 | yes | the reflection-style factory creates a zeroed array. |
| A20 | clean | Array.Fill and Span.Fill with a value | - | - | - | yes | every slot gets a real Result. |
| A21 | detect | Enumerable.Repeat<Result>(default, n) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the repeated element is a default Result. |
| A22 | by-design | clearing a buffer slot with default to release references | SPAR050 | SPAR050 (caught) | SPAR050 | yes | a ring buffer that clears a slot on dequeue stores a default Result there. Store Result? (null) instead. |
| A23 | impossible | stackalloc PlanResult[2] | CS0208 | CS0208 | CS0208 | yes | Result holds a reference (TError), so it is a managed type and cannot be stack-allocated. |
| A24 | clean | new List<Result>(capacity) filled with Add | - | - | - | yes | capacity is not count; nothing default is visible. |
| A25 | clean | default(ArraySegment<Result>), default(Memory<Result>), Span<Result> = default | - | - | - | yes | these defaults are empty views, not default Results. |
| A26 | detect | CollectionsMarshal.GetValueRefOrAddDefault on a Dictionary<K, Result> | SPAR052 | - (missed) | SPAR052 | yes | a missing key is added with a default Result. |
| A27 | detect | copying into a larger array, or ToArray over a partially filled pool array | SPAR052 | - (missed) | SPAR052 | yes | the destination array creation is reported; the slots after the copied range stay default. |
| A28 | detect | MemoryPool<Result>.Shared.Rent | SPAR052 | - (missed) | SPAR052 | yes | the rented memory is an ArrayPool array underneath, with default or stale elements. |
| C01 | clean | values and error cases returned by union conversion | - | - | - | yes | the ordinary way to produce a Result. |
| C02 | clean | Result.Success and default(Success) | - | - | - | yes | Success is a valueless marker struct; its default is the success. |
| C03 | clean | a switch expression whose every arm is a value or an error | - | - | - | yes | no default arm value. |
| C04 | clean | an interface-typed value converted by the union | - | - | - | yes | an ordinary union conversion. |
| C05 | clean | async, Task.FromResult and ValueTask returns of real Results | - | - | - | yes | every returned Result is a value or an error. |
| C06 | clean | collections built from values with Select, ToArray, ToList, ToDictionary | - | - | - | yes | every element comes from a value. |
| C07 | clean | reading a Result: switch, TryGetValue/TryGetError with the bool ignored, Value, ToString | - | - | - | yes | TryGetValue's out argument is the T or the error, not a Result. |
| C08 | clean | default of other types: CancellationToken, int, string, a class, a struct without Result | - | - | - | yes | only values that hold a Result are reported. |
| C09 | clean | Lazy, ThreadLocal, StrongBox, TaskCompletionSource with real values | - | - | - | yes | each holder gets a Result through a factory or a value. |
| C10 | clean | a class with required, initialized and constructor-assigned Result members | - | - | - | yes | every member is assigned before it can be read. |
| C11 | clean | generic code over Results that never creates a default | - | - | - | yes | values flow in and out. |
| C12 | clean | tuples and record structs built from values | - | - | - | yes | every Result element is a value. |
| C13 | clean | Result? as the way to say "maybe unset": HasValue, ??, Value | - | - | - | yes | null is the unset state; a fallback is a real Result. |
| C14 | clean | Result inside Task, IEnumerable, Dictionary, Func, with no default | - | - | - | yes | nothing creates a default. |
| E01 | detect | => default (spikes/result-api DefaultValue.cs) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the default literal converted to a Result is an IDefaultValueOperation of a Result type. |
| E02 | detect | return default; in an async method | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation inside the async state machine body. |
| E03 | detect | Result<CancelError> => default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | Result`1 is recognised like Result`2. |
| E04 | detect | new() of a Result | SPAR050 | SPAR050 (caught) | SPAR050 | yes | new() on a struct without an explicit parameterless constructor is default. |
| E05 | detect | new Result<…>[4] | SPAR052 | - (missed) | SPAR052 | yes | an array creation without an initializer leaves every element default (SPAR052, a warning: the array may still be filled before it is read). |
| E06 | detect | a field assigned by a method but not by the constructor | SPAR053 | - (missed) | SPAR053 | yes | between construction and Set, Last reads a default Result. CS0649 is silent because Set writes the field. |
| X01 | runtime | a library method that returns default with no hint in its signature | - | - | - | yes | only the library's IL knows; the consumer sees an ordinary Result return. The runtime guard throws on use. |
| X02 | detect | a library method marked [return: MaybeNull] | SPAR051 | - (missed) | SPAR051 | yes | the attribute is in metadata, so SPAR051 works across assemblies. |
| X03 | detect | a library Try method with [MaybeNullWhen(false)], bool ignored | SPAR051 | - (missed) | SPAR051 | yes | the attribute is in metadata. |
| X04 | runtime | a library class whose public Result field nothing assigns | - | - | - | yes | the library was not built with the analyzer; the consumer only reads a field. |
| X05 | detect | a library Create<T>() where T : new(), called with a Result | SPAR050 | - (missed) | SPAR050 | yes | the constraint is in the signature. |
| X06 | runtime | a library Make<T>() => default!, unannotated, called with a Result | - | - | - | yes | the body is IL in another assembly; SPAR054 needs the body in source. |
| X07 | detect | a library Find<T>() declared T?, called with a Result | SPAR051 | - (missed) | SPAR051 | yes | the T? annotation is in metadata. |
| X08 | detect | default(LibPair), a library struct with a private Result field | SPAR050 | - (missed) | SPAR050 | yes | depends on whether the analyzer can see private fields of a referenced struct (see RESULTS.md). |
| X09 | detect | a call that omits a library method's optional Result parameter (= default) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the omitted argument is an implicit default Result, reported at the call because the parameter is metadata. |
| X10 | runtime | a library method that returns an array of default Results | - | - | - | yes | the array creation is inside the library. |
| X11 | runtime | a third-party generator's output that returns default, and a generated unassigned field | - | - | - | yes | generated code is not analysed (GeneratedCodeAnalysisFlags.None): the user cannot fix it. The consumer's call looks ordinary. The generated file is Defaults.g.cs from CorpusGenerator. |
| F01 | compiler | a private instance field that nothing assigns | CS0649 | CS0649 | CS0649 | yes | CS0649 (a warning, an error with TreatWarningsAsErrors). SPAR053 stays silent so the problem has one report. |
| F02 | detect | a private field assigned only by a property setter | SPAR053 | - (missed) | SPAR053 | yes | readable before the first set; CS0649 is silent because something writes the field. |
| F03 | detect | a public field that nothing in the assembly assigns | SPAR053 | - (missed) | SPAR053 | yes | CS0649 ignores public fields (another assembly could assign them); SPAR053 reports it. |
| F04 | compiler | a private static field that nothing assigns | CS0649 | CS0649 | CS0649 | yes | CS0649 covers static fields too. |
| F05 | detect | a static field assigned by an Initialize method | SPAR053 | - (missed) | SPAR053 | yes | readable before Initialize runs. |
| F06 | detect | a { get; set; } auto-property not assigned by the constructor | SPAR053 | - (missed) | SPAR053 | yes | the backing field starts default. This is what CS8618 reports for a non-nullable reference. |
| F07 | detect | a getter-only auto-property never assigned | SPAR053 | - (missed) | SPAR053 | yes | no compiler diagnostic exists for an unassigned getter-only auto-property of a struct type. |
| F08 | detect | an init-only property that object initializers may omit | SPAR053 | - (missed) | SPAR053 | yes | new F08() without the initializer leaves it default. The fix is required. |
| F09 | clean | a required property | - | - | - | yes | the compiler (CS9035) makes every object creation set it. |
| F10 | detect | a required property and a [SetsRequiredMembers] constructor that does not assign it | SPAR053 | - (missed) | SPAR053 | yes | the attribute switches the compiler's required check off for that constructor. |
| F11 | clean | assigned in every constructor, one chaining to another | - | - | - | yes | : this(…) carries the chained constructor's assignments. |
| F12 | clean | assigned in both branches of an if/else | - | - | - | yes | every path assigns. |
| F13 | detect | assigned in only one branch | SPAR053 | - (missed) | SPAR053 | yes | the other path leaves it default. |
| F14 | detect | an early return before the assignment | SPAR053 | - (missed) | SPAR053 | yes | the return path leaves it default. |
| F15 | clean | a field with an initializer | - | - | - | yes | initialized before any constructor body. |
| F16 | clean | a primary constructor parameter stored by a field initializer | - | - | - | yes | the initializer runs as part of the primary constructor. |
| F17 | clean | a positional record with a Result parameter | - | - | - | yes | the synthesized property is assigned from the parameter. |
| F18 | detect | a record with a Result property declared in its body | SPAR053 | - (missed) | SPAR053 | yes | new F18() leaves it default unless an initializer sets it. |
| F19 | compiler | a struct constructor that does not assign the Result field | CS0649 | CS0649 | CS0649 | yes | since C# 11 unassigned struct fields are zeroed, so the field is default after this constructor. Nothing writes it, so CS0649 reports it and SPAR053 does not (F44 is the same struct with a writer). |
| F20 | clean | a struct with a public Result field and no constructor | - | - | - | yes | nothing to report at the declaration; default(F20) and new F20() are reported where they are written (S01–S04). |
| F21 | clean | a positional record struct | - | - | - | yes | the declaration is fine; its parameterless new() is reported at the use (S06). |
| F22 | clean | the opt-out: a Result? field that may be unset | - | - | - | yes | Nullable<Result> is null, not a default Result, and HasValue says which. |
| F23 | compiler | a [ThreadStatic] Result field with an initializer | CA2019 | CA2019 | CA2019 | yes | the initializer runs once, on the thread that runs the static constructor; every other thread sees default. .NET's CA2019 reports it (on by default), so SPAR053 does not. |
| F24 | detect | a static initializer that reads a static field declared after it | SPAR053 | - (missed) | SPAR053 | yes | static initializers run in text order, so s_fallback is still default when s_first is initialized. |
| F25 | detect | a class field whose struct type holds a Result | SPAR053 | - (missed) | SPAR053 | yes | a default F25Pair holds a default Result. |
| F26 | by-design | assigned by a helper method the constructor calls | SPAR053 | - (missed) | SPAR053 | yes | like CS8618, assignments in called methods do not count. Assign in the constructor or use Result?. |
| F27 | by-design | a protected base field assigned only by the derived constructor | SPAR053 | - (missed) | SPAR053 | yes | each type's constructors are checked on their own, as CS8618 does. |
| F28 | clean | assigned in try, catch rethrows | - | - | - | yes | a construction that throws produces no object. |
| F29 | clean | a static field assigned by the static constructor | - | - | - | yes | the static constructor runs before any access. |
| F30 | runtime | a base constructor calls a virtual method that reads a derived field before it is assigned | - | - | - | yes | the derived constructor does assign the field, after the base constructor ran. CA2214 (not on by default) reports the virtual call; following it across types is beyond a per-type check. |
| F31 | runtime | the constructor publishes this before assigning the Result | - | - | - | yes | another thread, or the callee, can read the field while it is default. |
| F32 | compiler | an internal field that nothing assigns | CS0649 | CS0649 | CS0649 | yes | CS0649 also covers internal fields. |
| F33 | detect | a property using the field keyword, never assigned by the constructor | SPAR053 | - (missed) | SPAR053 | yes | the synthesized backing field starts default. |
| F34 | clean | a partial class whose constructor in another part assigns the field | - | - | - | yes | the type is analysed as a whole. |
| F35 | clean | assigned by a deconstruction in the constructor | - | - | - | yes | a tuple deconstruction assigns each target. |
| F36 | clean | an expression-bodied constructor | - | - | - | yes | the expression body assigns the field. |
| F37 | detect | a struct constructor chaining to the implicit this() and not assigning the Result | SPAR053 | - (missed) | SPAR053 | yes | this() zeroes every field. |
| F38 | clean | a switch statement that assigns in every section, with a default section | - | - | - | yes | every path assigns. |
| F39 | clean | a static initializer that reads a static field declared before it | - | - | - | yes | text order makes s_fallback initialized first. |
| F40 | detect | assigned inside a loop that may not run | SPAR053 | - (missed) | SPAR053 | yes | zero iterations leave it default. |
| F41 | missed | an internal field written only by another type | - | - | - | yes | SPAR053 leaves fields its type never writes to CS0649, and CS0649 is silent because another type writes it. Seeing that would need a compilation-wide pass; the runtime guard throws. |
| F42 | clean | assigned by passing the field as an out argument in the constructor | - | - | - | yes | an out argument is definitely assigned when the call returns. |
| F43 | detect | a [ThreadStatic] Result field assigned by the static constructor | SPAR053 | - (missed) | SPAR053 | yes | the static constructor runs on one thread; every other thread sees default. CA2019 covers only initializers. |
| F44 | detect | a struct constructor that does not assign a Result field another member writes | SPAR053 | - (missed) | SPAR053 | yes | the constructor leaves it zeroed, and CS0649 is silent because Set writes it. |
| L01 | detect | an out parameter assigned default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | without [MaybeNullWhen] the caller has no reason to expect a default. |
| L02 | clean | a Try method that assigns default to a [MaybeNullWhen(false)] out parameter | - | SPAR050 | - | yes | the attribute declares the default; callers that read it on failure get SPAR051. |
| L03 | impossible | an out parameter never assigned | CS0177 | CS0177 | CS0177 | yes | CS0177. |
| L04 | impossible | an unassigned local passed by ref | CS0165 | CS0165 | CS0165 | yes | CS0165. |
| L05 | impossible | an unassigned local returned | CS0165 | CS0165 | CS0165 | yes | CS0165. |
| L06 | impossible | a pattern variable read where the pattern did not match | CS0165 | CS0165 | CS0165 | yes | CS0165: pattern variables are definitely assigned only where the pattern matched. |
| L07 | detect | a switch expression whose discard arm is default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the arm's default is target-typed to the Result. |
| L08 | detect | default: return default; in a switch statement | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation. |
| L09 | detect | maybe ?? default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the fallback is a default Result. |
| L10 | detect | ok ? value : default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the false branch is a default Result. |
| L11 | detect | a lambda returning default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation inside the lambda. |
| L12 | detect | a local function returning default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation inside the local function. |
| L13 | detect | yield return default in an iterator | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation. |
| L14 | detect | an async lambda returning default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation. |
| L15 | detect | an expression-bodied property => default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation. |
| L16 | detect | record with { Last = default } | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the assigned value is a default Result. |
| L17 | detect | an optional Result parameter whose default value is default or new() | SPAR050 | SPAR050 (caught) | SPAR050 | yes | every caller that omits the argument passes a default Result. Reported at the parameter. |
| L18 | detect | default! and (PlanResult)default | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the null-forgiving operator and a cast do not change the value. |
| L19 | clean | PlanResult? r = default (null) | - | - | - | yes | the default of Nullable<Result> is null, not a default Result. |
| L20 | detect | PlanResult? r = default(PlanResult) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | HasValue is true and the value inside is a default Result. |
| L21 | detect | Result<PlanResult, RunError> built from default(PlanResult) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the outer Result is a value, but the value is a default inner Result. |
| L22 | detect | object o = default(PlanResult) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | a boxed default Result. |
| L23 | detect | [value, default] as PlanResult[] | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the element is a default Result. |
| L24 | clean | throw where a value cannot be produced | - | - | - | yes | no default value exists. |
| L25 | detect | yield return default in an async iterator | SPAR050 | SPAR050 (caught) | SPAR050 | yes | same operation. |
| L26 | clean | r.Equals(default), r.Equals(default(PlanResult)), EqualityComparer<PlanResult>.Default.Equals(r, default) | - | SPAR050 | - | yes | comparing against a default creates no Result anyone reads. Equals(default) passes a null object and is false. |
| L27 | impossible | r == default | CS0019 | CS0019 | CS0019 | yes | Result declares no == operator (CS0019). |
| L28 | detect | [return: MaybeNull] PlanResult Find() => default, and a caller | SPAR051 | SPAR050 (missed) | SPAR051 | yes | the declaration is clean (the attribute declares the default); the caller gets SPAR051. |
| L29 | clean | a local assigned on every path before use | - | - | - | yes | definite assignment holds; no default value is created. |
| L30 | detect | a struct holding a Result, reset with this = new() | SPAR050 | - (missed) | SPAR050 | yes | new() of a struct without a parameterless constructor is default. |
| G01 | detect | a source generic method returning default(T)!, called with T = PlanResult | SPAR054 | - (missed) | SPAR054 | yes | SPAR054 joins the body's default(T) with the call's binding at compilation end. |
| G02 | detect | a generic method declared T? (maybe default), called with PlanResult | SPAR051 | - (missed) | SPAR051 | yes | the T? annotation is SPAR051 at the call, from the signature alone. |
| G03 | detect | Create<T>() where T : new(), called with PlanResult | SPAR050 | - (missed) | SPAR050 | yes | every struct satisfies new(), and new T() is then default(Result). Reported from the constraint. |
| G04 | detect | Activator.CreateInstance<T>() inside a generic method, called with PlanResult | SPAR054 | - (missed) | SPAR054 | yes | the generic summary treats Activator.CreateInstance<T>() like default(T). |
| G05 | missed | Box<PlanResult>.Item initialized with default! in a generic class | - | - | - | yes | the default(T) is in a property initializer of a generic type, not in a method a call reaches; the creation new Box<PlanResult>() is not followed into initializers. The runtime guard throws on Item. |
| G06 | detect | Outer<T>() calls Make<T>(), called with PlanResult | SPAR054 | - (missed) | SPAR054 | yes | the summary follows T through calls between source generic methods. |
| G07 | clean | Identity<T> and IsDefault<T> (EqualityComparer.Equals(value, default)) with PlanResult | - | - | - | yes | Identity never creates a default; IsDefault only compares against one. |
| G08 | detect | Cache<PlanResult>.GetOrDefault() returning default! for the type's T | SPAR054 | - (missed) | SPAR054 | yes | the binding comes from the receiver's type arguments. |
| G09 | detect | Zero<T>() where T : struct, called with PlanResult | SPAR054 | - (missed) | SPAR054 | yes | same summary; the struct constraint changes nothing. |
| G10 | detect | a generic Try method with [MaybeNullWhen(false)] out T, used correctly and with the result ignored | SPAR051 | - (missed) | SPAR051 | yes | its own found = default is declared, not reported; ignoring the bool is SPAR051 at the call. |
| G11 | detect | new Pool<PlanResult>() where Pool<T> requires T : new() | SPAR050 | - (missed) | SPAR050 | yes | the constraint binding is reported at the creation of the generic type. |
| G12 | detect | default(T)! inside a lambda in a generic method, called with PlanResult | SPAR054 | - (missed) | SPAR054 | yes | the lambda belongs to the method, so its default counts for the method's T. |
| G13 | clean | a type argument inferred from an argument, Outer chain through Identity | - | - | - | yes | Identity<T>(value) passes T through without creating a default. |
| S01 | detect | default(Pair) where Pair has a Result field | SPAR050 | - (missed) | SPAR050 | yes | the struct's Result field is default. |
| S02 | detect | new Pair() without an explicit parameterless constructor | SPAR050 | - (missed) | SPAR050 | yes | same as default(Pair). |
| S03 | clean | new Pair { Result = …, Id = 1 } | - | - | - | yes | the object initializer sets every member that holds a Result. |
| S04 | detect | new Pair { Id = 1 } | SPAR050 | - (missed) | SPAR050 | yes | the initializer leaves the Result default. |
| S05 | detect | a struct whose parameterless constructor assigns the Result, and default() of it | SPAR050 | - (missed) | SPAR050 | yes | new() runs the constructor (clean); default(…) still skips it (SPAR050). |
| S06 | detect | new() of a positional record struct | SPAR050 | - (missed) | SPAR050 | yes | a record struct still has the zeroing parameterless constructor. |
| S07 | detect | (int, PlanResult) t = default | SPAR050 | - (missed) | SPAR050 | yes | ValueTuple's Item2 is a default Result. |
| S08 | detect | (1, default) converted to (int, PlanResult) | SPAR050 | SPAR050 (caught) | SPAR050 | yes | the default element is target-typed to the Result. |
| S09 | detect | default(KeyValuePair<string, PlanResult>) | SPAR050 | - (missed) | SPAR050 | yes | KeyValuePair stores the value inline. Its fields are private in metadata, so it is on a short known list. |
| S10 | detect | default(ValueTask<PlanResult>) and new ValueTask<PlanResult>() | SPAR050 | - (missed) | SPAR050 | yes | a default ValueTask<T> is completed with default(T); awaiting it gives a default Result. |
| S11 | detect | an [InlineArray] buffer of Results | SPAR050 | - (missed) | SPAR050 | yes | new Buffer() zeroes every element. The element field itself is not reported (no constructor to check). |
| S12 | detect | default(Outer) where Outer holds a Pair that holds a Result | SPAR050 | - (missed) | SPAR050 | yes | containment is followed through nested structs. |
| S13 | detect | default(Holder) with { Attempts = 1 } | SPAR050 | - (missed) | SPAR050 | yes | with copies the default Result. |
| S14 | detect | this = default in a struct method | SPAR050 | - (missed) | SPAR050 | yes | resets every field, including the Result. |
| S15 | detect | var (a, b) = default((PlanResult, int)) | SPAR050 | - (missed) | SPAR050 | yes | deconstructs a default Result. |
| S16 | detect | default(Wrapper<PlanResult>) | SPAR050 | - (missed) | SPAR050 | yes | the constructed type's field is a Result. |
| S17 | clean | holder with { Attempts = 2 } on a real value | - | - | - | yes | with copies a valid Result. |
| U01 | detect | Unsafe.SkipInit(out PlanResult r) | SPAR050 | - (missed) | SPAR050 | yes | the local is not initialized at all: zero, or stack garbage under [SkipLocalsInit]. |
| U02 | clean | [SkipLocalsInit] on a method with ordinary Result locals | - | - | - | yes | definite assignment still applies; the attribute only matters with stackalloc and Unsafe.SkipInit. |
| U03 | detect | Unsafe.As<TFrom, PlanResult> reinterpreting other memory | SPAR051 | - (missed) | SPAR051 | yes | the bits may be anything, including zero (SPAR051, a warning: unsafe code is the author's call). |
| U04 | runtime | MemoryMarshal.Cast<byte, PlanResult> | - | - | - | yes | .NET itself throws ArgumentException for a type that holds references, so no default Result is produced. |
| U05 | detect | RuntimeHelpers.GetUninitializedObject(typeof(PlanResult)) | SPAR050 | - (missed) | SPAR050 | yes | creates a zeroed (boxed) Result without running any constructor. |
| U06 | detect | Activator.CreateInstance<PlanResult>() and Activator.CreateInstance(typeof(PlanResult)) | CA2263, SPAR050 | CA2263 (missed) | CA2263, SPAR050 | yes | both run the zeroing parameterless constructor of the struct. |
| U07 | runtime | Unsafe.NullRef<PlanResult>() read through | - | - | - | yes | not a default Result: reading it is a NullReferenceException from the runtime. |
