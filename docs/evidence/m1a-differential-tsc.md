# M1a: the differential test of the generated JavaScript

Recorded 2026-11-16. What it shows: the `.js` and `.d.ts` the client step writes are exactly what `tsc --declaration` writes from a typed TypeScript twin of the same modules, under TypeScript 6.0.3 and 7.0.2, so every generated reader, writer and decoder type-checks against the declared types. Decisions: "Generated JavaScript is validated against tsc in Spar's own tests" (2026-11-13) and "No contract manifest for now; the differential tsc test built".

## What was added

| Path | Contents |
|---|---|
| `tests/Spar.Sdk.Tasks.Tests/TypeScriptTwin.cs` | From the client step's model (`ClientLayout`), one `.ts` per module: each contract, `shared` when contracts share types, and `spar`. Interfaces for records and errors; typed readers (`(value: unknown): Item`), writers (`(value: Item): string`) and error readers (returning `E[keyof E] \| typeof unknownErrorCase`); decoders typed `Decoder<T>`; the contract object with its declared type and `invoke<T, E>` given explicitly. Written on its own, sharing only the naming rules (`ClientNames`, `TypeScriptNames`), not the emitters. |
| `tests/Spar.Sdk.Tasks.Tests/TypeScript/compare.cjs` | Parses both outputs with TypeScript 6's parser, prints them without comments, and compares the tokens. It ignores parentheses around a lone arrow parameter and trailing commas, which the printer keeps from the source. From `tsc`'s declarations it drops function declarations and imports of `./sdk/…`: `shared.js` exports its readers and writers to the contract modules only, and Spar's declarations leave them out on purpose. |
| `tests/Spar.Sdk.Tasks.Tests/DifferentialTests.cs` | Four cases: TypeScript 6 and 7, each with two fixtures. The twin sits in a folder with the package's `package.json` and SDK, so it imports `@spar/app/sdk` and `./sdk/…` as the generated modules do; `tsconfig` is strict, `exactOptionalPropertyTypes`, `verbatimModuleSyntax`, `skipLibCheck: false`, `declaration`. |
| `tests/Spar.Sdk.Tasks.Tests/Fixtures.cs` | `Edges`, beside `Shop`: reserved words as property names (`class`, `default`) and as a parameter name (`@new`, which becomes `newValue`), a list of lists of nullable `int`, a nullable `long`, an empty record, a nullable record, and an error whose cases carry nothing. |

## Results (observed, Windows 11)

| Check | Result |
|---|---|
| `DifferentialTests` | 4 of 4 in 3.4 s |
| A temporary fault in the module emitter (`long` read with `wire.readInt32`) | all four cases fail, naming `orders.js`, `shared.js` and `odd.js` at the token |
| A temporary fault in the declaration emitter (`long` declared `number`) | all four cases fail, naming `orders.d.ts`, `shared.d.ts` and `odd.d.ts` at the token |
| `dotnet test` (solution) | 354 of 354 |
| `dotnet run eng/check-size.cs` | 200 files, clean |

The first runs showed only layout differences (an object literal on one line or several, `(options) =>` against `options =>`, trailing commas), which led to the token comparison; no difference in the code itself was found.

## Not covered

- **The `.d.ts.map` files** are not compared: `tsc` would map to the twin, not to the C#.
- **The JSDoc** is not compared: the twin carries none, and comments are dropped before comparing.
- **`@spar/app/windows`** does not exist yet (M1b).
