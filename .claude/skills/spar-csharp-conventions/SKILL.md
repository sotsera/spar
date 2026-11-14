---
name: spar-csharp-conventions
description: Write or review Spar C# using the repository's syntax, feature layout, member order, ownership, and performance conventions.
---

# Write Spar C#

**Readable first.** Write code a reader understands on the first pass. Prefer clear over clever and short over dense.
- Give each method one purpose. Prefer early returns to deep nesting, and named locals to nested calls and long chains.
- Keep one statement per line, and keep lines to about 120 characters.
- Avoid one-line tricks that pack several decisions together.
- Comment why, not what.
- When a performance need forces denser code, keep it local and explain it in a comment.

- Match the affected project's conventions. New files use four-space indentation, braces on their own lines, and file-scoped namespaces. Use PascalCase for types and members, camelCase for locals and parameters, `_camelCase` for private fields, and an `I` prefix for interfaces.
- Keep contracts, DTOs, services and private helpers near the capability they serve. Keep small projects flat where folders add little. When reorganizing files, preserve public namespaces, wire identities and build inclusion.
- Use block bodies for workflows. Use expression bodies for simple forwarding or accessors.
- Separate distinct steps in a method with a blank line. Keep related statements together. Name intermediate results when a nested expression or long chain hides the steps.
- Choose records, classes or structs by their semantics. Make resource ownership and disposal explicit. Keep native work on its owning UI thread, and keep it short there: never block that thread with backend work, I/O or synchronous waits. Use `spar-native-interop`, `spar-webview2-hosting` or `spar-webkitgtk-hosting` for that boundary.
- Mark only the members that use pointers `unsafe`, never the whole type: C# 15 rejects `await` inside an `unsafe` type (CS8652). Don't iterate a `ReadOnlySpan<T>` collection expression across an `await` (CS4007); use a static array.
- Answer CA1416 with `[SupportedOSPlatform("…")]` on the platform-specific code or assembly, not a suppression.
- Fix generated C# in its generator, not in the output. Check which style rules the project actually enforces before relying on a formatter or analyzer.
- Never silence a diagnostic in source (`#pragma`, `[SuppressMessage]`). An agreed override is a path-scoped section of the root `.editorconfig`, with its reason; tests that need one keep the affected files in their own folder (`AGENTS.md`).

## Results, services and cancellation

- **`Result`.** `Result<T, TError>` and `Result<TError>` are `readonly struct` C# 15 unions. Errors are `closed record` hierarchies with nested sealed cases.
  - Return a value or an error case directly, and `Result.Success` for a command without a value. Never create a result with `default` or `new()` (SPAR050–SPAR054); declare a member that may be empty `Result<…>?`.
  - Read a result with one exhaustive `switch` over the success type and every error case, with no discard arm, so a new case fails the build (SPAR055, CS8509).
  - The contract rules are in `spar-contract-generation`; the app's view is in `docs/app-model.md` section 3.1.
- **Services.** Register through `IServiceCollection` (Microsoft DI). Each command runs in its own scope; per-call data comes from the scoped `ISparCall`, never from an ambient `AsyncLocal`. A singleton must not depend on a scoped service: scope validation fails it at start.
- **Cancellation.** Forward the `CancellationToken` to every call that takes one (CA2016 is an error), or pass `CancellationToken.None` on purpose with a comment. A cancelled or timed-out call does not prove the work stopped.

## Member order

Arrange declared members in this order when writing or reviewing a C# type:

1. Constants
2. Static fields
3. Instance fields
4. Properties, then indexers, then events
5. Constructors, with the static constructor first
6. Methods
7. Nested types

Within each group, order by accessibility: public, protected, protected internal, internal, private protected, private. Keep related overloads together. Otherwise keep the existing order.

Keep an order that something depends on: field or property initialization, native layout, COM vtable slots, or a serialization contract. Keep generated and externally defined layouts unchanged. When reviewing, explain any exception that isn't obvious. Local declarations follow their use and data dependencies.

## Performance

Keep overhead low in startup, dispatch, serialization and native callbacks, and keep the code clear while removing unnecessary work and allocations.

- Use `ReadOnlySpan<T>` or `Span<T>` for synchronous slicing, parsing and copying over existing buffers, and keep the storage alive while the span is used. Use `ReadOnlyMemory<T>`, `Memory<T>` or a buffer with an explicit owner when data must survive an `await` or a native callback.
- Avoid unnecessary copies, boxing, captured delegates and repeated materialization on hot paths. Pre-size collections when the count is known. Use a direct loop or a static callback when it stays clear and does less work.
- Prefer `Task` for ordinary async APIs. Use `ValueTask`, pooling or small bounded stack allocations only when a measured benefit justifies the extra lifetime rules. Return rented buffers after their last use, and keep cancellation, cleanup and native ownership correct.
- Verify performance-sensitive changes with a representative workload. Compare elapsed time and allocations, and run the actual runtime or published executable when the change affects it.
