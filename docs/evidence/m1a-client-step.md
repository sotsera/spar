# M1a: the client step and the generated TypeScript

Recorded 2026-11-15. What it shows: the build writes each app's `@spar/app` package from the same analysis as the generated C#, the declarations compile at the strictest settings on TypeScript 6 and 7, deliberate mistakes fail with their exact codes, and a page in Chromium calls the backend through the generated modules. Decision: "Client step: the generated TypeScript modules".

## Environment

Windows 11 Pro 10.0.26320, SDK 11.0.100, `Microsoft.CodeAnalysis.CSharp` 5.9.0, `Microsoft.Build.Framework` and `Utilities.Core` 18.12.2 with `Microsoft.Build.TaskAuthoring.Analyzer` 18.13.0, Node 24.21.0, TypeScript 6.0.3 and 7.0.2, Playwright 1.64.1. Not run on Linux.

## What was added

| Path | Contents |
|---|---|
| `src/Spar.Sdk.Tasks/SparClient.cs` | The task: inputs from `@(Compile)`, `@(ReferencePath)`, `DefineConstants` and `Nullable`; problems reported at the contract's file; the package written and synchronised. |
| `src/Spar.Sdk.Tasks/Client/` | `ContractSource` (a compilation of the project at the preview language version, `ContractAnalysis` on each `[SparContract]` type, a reference cache by path, time and size), `ClientDocs` (summaries and record `<param>`s as one-line JSDoc), `ClientLayout` (TypeScript names, own and shared types, SPAR003 and SPAR040), `DeclarationEmitter`, `ModuleEmitter`, `ClientPackage` (files, `package.json`, write-if-changed by replacing each file in one step, stale files deleted). |
| `src/Spar.Sdk/Sdk/Sdk.targets` | For apps: the task project referenced so it builds first (nothing of it reaches the app), `SparWriteClientInventory` after `ResolveReferences` and `GenerateGlobalUsings`, `SparClient` before `CoreCompile` with the inventory, sources, references, SDK files and task assembly as inputs. |
| `src/Spar.Client/src/wire.ts`, `sdk.ts` | The field readers and writers; `invoke` takes the request's encoder and turns a refusal into `rejected` / `invalid-request` / `notStarted`. `tsconfig.json` sets `sourceRoot` so the maps point at `sdk/src/` inside the package. |
| `src/Spar.Generator/` | `CommandModel` gained the request's parameter name; `CatalogEmitter.Fingerprint` is shared with the client step. |
| `tests/Spar.Sdk.Tasks.Tests/` | 24 tests: emitter output (package layout, declarations, call types, error maps, documentation, field-by-field modules, `switch` error readers, LF only, a global-namespace type), names (SPAR040 twice, SPAR003 four times), and the TypeScript checks: the package installed as `node_modules/@spar/app` in a scratch app under `artifacts/client-check/`, deleted after each test. |
| `tests/apps/WireShapes/page/main.js`, `tests/WireShapes.EndToEndTests/` | The page imports `/spar/app/echo.js`, `/spar/app/spar.js` and `/spar/app/sdk/sdk.js` from the app's generated package; two new browser tests. |

## Results (observed)

| Check | Result |
|---|---|
| `dotnet test --solution Spar.slnx` | 213 of 213: Spar.Tests 123, Spar.Generator.Tests 25, Spar.Sdk.Tasks.Tests 24, WireShapes.Tests 21, Notebook.Tests 10, WireShapes.EndToEndTests 10 |
| TypeScript matrix (TypeScript 6.0.3 and 7.0.2 × bundler and NodeNext resolution; `strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `noPropertyAccessFromIndexSignature`, `noImplicitReturns`, `noUnusedLocals`, `verbatimModuleSyntax`, `skipLibCheck: false`) | the consumer compiles in all four; the eight mistakes fail in all four with exactly TS2322 (a `number` for a `long`), TS2540 (writing a read-only value), TS2345 (a number for a string request), TS2339 (an unknown command), TS2741 (a missing property; a missing error handler), TS2322 (a misspelled error case, through the SDK's `NoInfer` guard) and TS2353 (an `errors` object for a command without errors), each on its own line and nothing else |
| The `.js` in Node | `orders` exports `place, cancel, clear, count`, `stock` exports `find, list`, the fingerprint is a string, and the contract objects are frozen |
| Browser tests | 10 of 10: the generated fingerprint equals the backend's (`app.Fingerprint`), and a NaN `double` ends `rejected` / `invalid-request` / `notStarted` with nothing reaching the host and one console error naming the reason |
| Incremental build (a copy of WireShapes under `artifacts/`) | first build 1,329 ms with Spar already built; unchanged rebuild 752 ms at detailed verbosity with `Skipping target "SparClient" because all output files are up-to-date`; renaming the contract to `mirror` wrote 4 files, deleted `echo.js` and `echo.d.ts`, and changed the exports (1,273 ms); the next unchanged build 521 ms |
| `-mt` build of the solution | clean, no warnings |
| NativeAOT lane | 154 of 154 (Spar.Tests 123, WireShapes.Tests 21, Notebook.Tests 10) |
| The app's output | `samples/Notebook` Debug `bin` and the Release publish contain no Roslyn, generator or task assembly; its `project.assets.json` names neither project |
| Vitest | 119 of 119 (7 new wire tests); `npm run check` clean on both TypeScript versions |
| Size check | 82 source files, 0 errors, 0 warnings |

## Found and fixed while building

- A type in the global namespace kept `global::` in its TypeScript name (caught by the SDK-type name test).
- Several error cases with payloads redeclared `const object` in one `switch` scope, a syntax error; each case is a block now.
- The long reader accepted `-0`, which the C# reader refuses.

## Follow-up: reserved names

At the user's request (decision "Client step: reserved type names"). Observed: a type named `any`, `@string`, `@default` or `undefined` is SPAR003 at line 4 of its file, a type named `Call` or `CallOptions` is SPAR040 there, contracts named `wire`, `invoke`, `noErrors`, `pingDecoder` or `readItem` (beside a type `Item`) are SPAR003, two shared types with one name are SPAR040, and a contract that does not import a shared type may declare its own of that name. `tests/Spar.Sdk.Tasks.Tests` 37 of 37; `dotnet test` 226 of 226.

## Not done or not verified

- **Not built:** the `.d.ts.map` to the C# declarations (go-to-definition), the manifest, the contract snapshot and SPAR100, the differential test against `tsc --declaration`, `@spar/app/windows`, streams and subscriptions in the modules, the package-mode paths, and the SDK version as the package version (it is `0.0.0`).
- **Not run:** Linux, CI, and the IDE (no IDE build was tried with the task host).
- **Not measured:** the step's own time against the spike's 7–29 ms with a warm cache; in this repository it starts a task host each time it runs, which the spike measured at 460–520 ms.
- **Concurrent variants:** two variants writing one package at once is handled by replacing each file in one step, but no test runs two such builds at the same time.
