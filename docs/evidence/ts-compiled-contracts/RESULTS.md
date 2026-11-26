# Generated contract modules: `.ts` or compiled `.js` + `.d.ts`

> A spike. Its code was deleted with `spikes/` at the start of M2; it is in git history at [`spikes/ts-compiled-contracts/`](https://github.com/sotsera/spar/tree/24f5db0942833047c81fa692908410596f3b8960/spikes/ts-compiled-contracts) (commit `24f5db0`), and this record and its `results/` moved here. Paths under `spikes/` below name that history.

The question: the SDK ships compiled (decision "TypeScript SDK distribution", 2026-11-13). Should the C# generator also emit each contract module as `.js` + `.d.ts` (no `tsc` at build time) instead of `.ts`? This spike checks the editor experience with both language servers, the compiler across settings and versions, and bundling. It also tries a declaration map from the generated `.d.ts` to the C# source.

## Versions

| What | Version | How it was run |
|---|---|---|
| TypeScript 7 (native) | 7.0.2 (`typescript-go 7.0.2`) | `tsc.exe` and `tsc.exe --lsp --stdio` from this spike's `node_modules` |
| VS Code's bundled TypeScript | 6.0.3 (VS Code 1.146.0) | `tsserver.js` run by `Code.exe` with `ELECTRON_RUN_AS_NODE=1`, VS Code's default completion preferences. It ships no `tsc.js`, so `tools/tsc6.ts` runs its `typescript.js` compiler API (`getPreEmitDiagnostics`) |
| Vite | 8.3.8 (rolldown) | JS API `build()`, production, minified |
| Node | 24.21.0 | tools are `.ts` run directly |

`npm install --offline` from the npm cache; nothing was downloaded. `tools/clients.ts` and `tools/locate.ts` are copied from `spikes/ts-results` (plus `references` and tsserver's `findSourceDefinition`).

## Layout

- `sdk/`: form C (`core.ts`, `index.ts`) copied from `spikes/ts-results`. `tools/build.ts` compiles it with `tsc` 7 to `.js`, `.js.map`, `.d.ts`, `.d.ts.map`, and ships the sources in `sdk/src/` (not next to the `.d.ts`: a `core.ts` beside `core.d.ts` would be picked by resolution instead of the declaration).
- `generated/ts/` and `generated/js/`: what the generator would emit for `copyJobs` (plus `auditLog`, an unused contract, and a root barrel `index` for the tree-shaking check). The same JSDoc, standing for the C# XML docs, in both. The `.d.ts` declares `export declare const copyJobs: { readonly plan: (…) => Call<…>; … }`, the same shape `tsc` infers from the `.ts` (`as const`).
- `contracts/CopyJobs.cs`: the C# contract the modules stand for (records, a `closed record PlanError`, `CopyJobsService`). Not compiled; the target of the declaration map.
- `frontend/`: the consumer template (`app.ts`, `types.ts` with exact-type assertions, `bad.ts` with the 11 mistakes from `spikes/ts-results`, `probe.ts` with cursor markers, bundle entries `main*.ts`).
- `tools/build.ts` lays out three variants under `obj/variants/<variant>/`, as Spar does: `obj/spar/client/` is one `@spar/app` package (`"type": "module"`, `"sideEffects": false`, subpath exports `./sdk`, `./copyJobs`, `./auditLog`, `.`), and `frontend/package.json` depends on `"@spar/app": "file:../obj/spar/client"`. `npm install --offline` links it, so the real path is outside `node_modules`.
  - **`ts`**: `copyJobs.ts`; exports point to the `.ts`.
  - **`js`**: `copyJobs.js` + `copyJobs.d.ts`; exports `{ "types": "./copyJobs.d.ts", "default": "./copyJobs.js" }`.
  - **`js-csmap`**: `js` plus `copyJobs.d.ts.map`, whose `sources` is `../../../../../../contracts/CopyJobs.cs`, with start and end mappings for 13 identifiers: DTO `CopyRecipe` and its properties, `PlanError`, its three cases and two payload fields, `copyJobs` (to `class CopyJobsService`) and `plan` (to `PlanAsync`).
- `npm run build | probe | matrix | bundle` (`node tools/probe.ts <tsgo|vscode> <variant>`). Raw output: `obj/editor/<server>-<variant>.md`, `obj/matrix.md`, `obj/bundle.md`.

## Editor: side by side

Each server was run once per variant, with the frontend folder as the project root. Columns: **.ts** = `ts`, **.d.ts** = `js`, **+map** = `js-csmap`.

### Same in every variant (per server)

Apart from the definition positions below, the probe output is the same in all three variants. That covers diagnostics, quick fixes, completions, completion details and hovers (a text diff of the raw files after normalising `.ts`/`.d.ts`).

| Check | TypeScript 7.0.2 LSP | VS Code TypeScript 6.0.3 |
|---|---|---|
| completions in `errors: { }` | `connectionNotFound`, `tableMissing`, `columnsMismatch` | the same three, each also as a method snippet |
| completions after one case | the two remaining | the two remaining, plus snippets |
| completion details of `tableMissing` | the type, no doc comment | the type and "The table does not exist on that connection." |
| hover on a DTO property (`source:` in a literal, `recipe.source`) | `(property) CopyRecipe.source: string` + doc | the same |
| hover on `copyJobs.plan` | the signature + doc | the same |
| hover on a case key in a handler | the intersection type, no doc | the intersection type + doc |
| hover on `copyJobs` | the full object type + doc | the same |
| `bad.ts` diagnostics | the 11 codes from `spikes/ts-results` | the same codes; #7 is TS2345 with TS2741 inside |
| "Add missing properties" (#1, #11, and #4 → `errors: undefined`, #7) | not offered | offered, the same edit in all variants; adds `type ColumnDiff` to the import |
| other quick fixes | `Update import from "@spar/app/copyJobs"` (#10) | `Change spelling to 'connectionId'` (#3), `Update import` (#10) |

**No quick fix edits a generated file in any variant.** In `spikes/ts-results` (generated modules imported by relative path) tsserver offered `Declare property` and `Add index signature` for #3 and #5, editing `copyJobs.ts` and `sdk.ts`. Through the package, those are not offered, for `.ts` and `.d.ts` alike.

### Navigation and rename (differ by variant)

| Check | Server | .ts | .d.ts | +map |
|---|---|---|---|---|
| go to definition: case key `tableMissing` | both | `copyJobs.ts:28` | `copyJobs.d.ts:28` | **`CopyJobs.cs:40:26`** `record TableMissing(…)` |
| … payload field `e.table` | both | `copyJobs.ts:28:66` | `copyJobs.d.ts:28:66` | **`CopyJobs.cs:40:65`** `Table` |
| … DTO property `source` (literal and access) | both | `copyJobs.ts:7` | `copyJobs.d.ts:7` | **`CopyJobs.cs:13:12`** `Source` |
| … command `copyJobs.plan` | both | `copyJobs.ts:47` (the implementation) | `copyJobs.d.ts:47` | **`CopyJobs.cs:67:46`** `PlanAsync` |
| … `copyJobs` | both | `copyJobs.ts:45` | `copyJobs.d.ts:45` | **`CopyJobs.cs:64:21`** `CopyJobsService` |
| … SDK `match` | both | `sdk/src/index.ts:16` (the shipped source, via the SDK's `.d.ts.map`) | same | same |
| go to source definition (`findSourceDefinition`) | 6.0.3 | as definition | `plan` → `copyJobs.js:6`, `copyJobs` → `copyJobs.js:5`; type-only symbols: none | as definition (the `.cs`) |
| | 7.0.2 | not tested (no documented LSP method) | | |
| find all references: case key | 7.0.2 | 11: `copyJobs.ts` + 10 in the app | 11: `copyJobs.d.ts` + 10 | 11: **`CopyJobs.cs`** + 10 |
| | 6.0.3 | 11: `copyJobs.ts` + 10 | 11: `copyJobs.d.ts` + 10 | 11: `copyJobs.d.ts` + 10 (not mapped) |
| find all references: SDK `match` | 7.0.2 | 14, declaration at `sdk/src/index.ts` | same | same |
| | 6.0.3 | 14, declaration at `sdk/index.d.ts` | same | same |
| rename case key from the app | 7.0.2 | **allowed**, 11 edits, one in `copyJobs.ts` | **allowed**, one in `copyJobs.d.ts` | **allowed, one edit in `CopyJobs.cs:40:26-40:38` (`TableMissing`)** |
| | 6.0.3 | allowed, edits `copyJobs.ts` | allowed, edits `copyJobs.d.ts` | allowed, edits `copyJobs.d.ts` |
| rename DTO property `source` from the app (literal or access) | 7.0.2 | allowed, 6 edits incl. `copyJobs.ts` | incl. `copyJobs.d.ts` | **incl. `CopyJobs.cs:13:12-13:18` (`Source`)** |
| | 6.0.3 | incl. `copyJobs.ts` | incl. `copyJobs.d.ts` | incl. `copyJobs.d.ts` |
| rename payload field `e.table` | both | allowed, edits the generated file | same | 7.0.2: the `.cs` (`Table`); 6.0.3: the `.d.ts` |
| rename/references from inside the generated file | both | 11 locations | 11 | 11 (7.0.2 lists the `.cs` for the declaration) |

- **Rename is never refused.** The package's real path is outside `node_modules`, so neither server applies its "defined in a 'node_modules' folder" rule. A rename started in the app edits the generated `.ts` or `.d.ts`. That edit is lost on the next build, and the backend still sends the old name. This is the same hazard as in `spikes/ts-results`, and it does not depend on the variant.
- **With the C# map, TypeScript 7 maps rename edits into the `.cs` file.** The edit replaces only the C# declaration's identifier with the TypeScript name (lowercase, no C# semantics), not the C# references. 6.0.3 edits the `.d.ts` instead. The edit text was not captured, only its range.
- Side check (`obj/editor/*-js-preserveSymlinks.md`): with `"preserveSymlinks": true` the files appear under `node_modules`. Rename is then refused for `e.table` and `recipe.source` (`You cannot rename elements that are defined in a 'node_modules' folder.`) but still allowed for a case key and for `source:` in an object literal. That is inconsistent, so it is not a fix.

### The declaration map to C#

- **Both servers follow a hand-made `.d.ts.map` into a `.cs` file**, at the exact identifier, for go-to-definition. TypeScript 7 also maps references and rename locations; 6.0.3 maps definition and source definition only.
- Hovers and completion docs still come from the `.d.ts` JSDoc. The map changes navigation only.
- Not checked: what VS Code does with a definition in a `.cs` file (it should open it at that line, in the C# editor if one is installed). The servers return the location; the window was not opened.
- The generator has what it needs: Roslyn gives each symbol's file, line and column. The map's `sources` is relative to `obj/spar/client`, and it is regenerated with the module.

## Compiler matrix

`tools/matrix.ts`, full output `obj/matrix.md`. Three variants × {7.0.2, 6.0.3} × {permissive (`strict: false`), strictest} × `skipLibCheck` {true, false} × {`bundler` + `ESNext`, `NodeNext`} = 48 cells. Strictest means `strict`, `exactOptionalPropertyTypes`, `noUncheckedIndexedAccess`, `verbatimModuleSyntax`, `erasableSyntaxOnly`, `isolatedModules`, `noUnusedLocals`, `noUnusedParameters`, `noImplicitReturns`, `noImplicitOverride`, `noFallthroughCasesInSwitch`, `noPropertyAccessFromIndexSignature`, `allowUnreachableCode: false`, `allowUnusedLabels: false` and `noUncheckedSideEffectImports`.

| Result | All 48 cells |
|---|---|
| app (`app.ts`, `main.ts`, plus `types.ts` with its exact-type assertions under strictest) | **0 errors** |
| `bad.ts` | **11 errors**, the same 9 codes |
| `.ts` vs `.d.ts` vs `+map`, same compiler and flags | **identical diagnostic text** |
| 7.0.2 vs 6.0.3 | same codes and positions except #7, which 6.0.3 reports as TS2345 (with TS2741 inside) instead of TS2741. #4 also differs, but only because `tools/tsc6.ts` prints the elaboration on the same line |
| permissive vs strictest | only #8 differs: `Promise<number>` instead of `Promise<number \| undefined>` (no `strictNullChecks`) |
| time, 7.0.2 | ~60 ms with `skipLibCheck`, ~180 ms without (DOM lib checking), for every variant |

`NodeNext` resolves both the `.ts` export target and the `types`/`default` conditions. The client's `"type": "module"` and the `.js` extensions in the relative imports are enough.

### Who checks the generated code (fault injection)

One fault injected into one generated module; strictest settings, `bundler`. `fault.ts` assigns `copyJobs.plan(…)` to a `number`, so its TS2322 shows the type the app sees.

| Variant | Fault | `skipLibCheck` | 7.0.2 | 6.0.3 | Type the app sees |
|---|---|---|---|---|---|
| `.ts` | unused local in `copyJobs.ts` | true / false | **TS6133 in `../obj/spar/client/copyJobs.ts`** | same | `Call<CopyPlan, PlanError>` |
| `.ts` | undeclared type in `copyJobs.ts` | true / false | TS2552 | TS2552 | `Call<CopyPlanX, PlanError>` |
| `.d.ts` | undeclared type in `copyJobs.d.ts` | **true** | **nothing reported** | **nothing reported** | `Call<CopyPlanX, PlanError>` (an error type, silently) |
| `.d.ts` | the same | false | TS2552 | TS2552 | same |

- **A generated `.ts` is checked under the app's own flags**, even though it is reached through `node_modules`. An unused local in generated code becomes an error in the user's build. So the generator must stay clean under every flag of every TypeScript version, including flags added later.
- **A generated `.d.ts` is outside the app's flags**, like the SDK. With `skipLibCheck: true` (the usual template default), a broken declaration is not reported at all; the type silently degrades. Spar's own tests must catch these, with `skipLibCheck: false`.
- Nothing type-checks the generated `.js` against its `.d.ts`. They are two outputs of the generator. This module is trivial (one `invoke`/`stream` call per command), but drift between them is a new kind of generator bug that `.ts` cannot have.

## Bundling and tree shaking

`tools/bundle.ts`, Vite 8.3.8 production build, minified, per variant. The `auditLog` contract is in the same package, and its command name is the marker.

| Entry | `auditLog` in bundle | `copyJobs` in bundle | Size (each variant) |
|---|---|---|---|
| `main.ts`: `@spar/app/copyJobs` + `@spar/app/sdk` | no | yes | 1600 B, gzip 761 B |
| `main-barrel.ts`: `{ copyJobs }` from the root barrel `@spar/app` | **no** | yes | 1600 B, gzip 761 B |
| `main-audit.ts` (control: imports `auditLog`) | yes | no | 1030 B, gzip 546 B |

- **The bundles are byte-identical across `.ts`, `.d.ts` and `+map`** (`diff` of the output). Vite transpiles the linked `.ts` without configuration.
- Removing `"sideEffects": false` changed nothing here: rolldown drops the unused pure `export const … = { … }` on its own. Keep the field for other bundlers (not tested).
- Within an imported contract, all six commands are kept (one object literal), in every variant. Per-command shaking would need per-command exports. That is a separate question.

## Recommendation

**Emit the contract modules as `.js` + `.d.ts`, with a `.d.ts.map` to the C# declarations.** Make it conditional on two generator tests (below).

- **Nothing the user sees gets worse.** Completions, completion docs, hovers, diagnostics, quick fixes, rename and references are the same as with `.ts` in both servers. `tsc` 6 and 7 give identical diagnostics across all 48 settings. The bundles are byte-identical.
- **It matches the SDK decision.** The app's `tsconfig` and TypeScript version no longer compile Spar's output. Today an unused local, or a flag added in TypeScript 7.x, in generated code fails the user's build.
- **It enables C# navigation.** Go-to-definition from a case key, payload field, DTO property, command or contract lands on the C# declaration in both servers. With `.ts`, it lands on generated code.
- **What it costs, and what Spar must add:**
  1. A test that compiles every generated `.d.ts` (and a consumer of it) with `skipLibCheck: false` and the strictest flags under TypeScript 6 and 7, because users with `skipLibCheck: true` will never see a broken declaration. The CI matrix already decided can host it.
  2. A test that the `.js` matches the `.d.ts`: import the `.js` in Node and compare each contract's command names and wire names with the manifest. Optionally, in the generator's tests only, compile the equivalent `.ts` with `tsc` and compare the emitted `.d.ts`.
  3. More generator code: two files plus the map, and VLQ mappings from Roslyn locations.
- **The C# map has one sharp edge.** In TypeScript 7, a rename started in TypeScript edits the C# declaration's identifier, and only that. VS Code's default 6.0.3 edits the generated `.d.ts` instead. Rename from the TypeScript side is wrong in every variant, because the backend keeps the old name and the next build reverts the generated file. The guidance stays "rename in C#, then build". Ship the map and document that. If the TypeScript 7 behaviour proves harmful in practice, the map can be dropped with no other change.

## Observed, skipped, not tested

**Observed (this run):**

- The three variants built by `tools/build.ts` (SDK compiled with `tsc` 7.0.2; the client linked with `npm install --offline`; `node_modules/@spar/app` is a link to `obj/spar/client`).
- Both language servers, one process per server and variant, over the probes and `bad.ts` above (`obj/editor/*.md`). Also the `preserveSymlinks` side check on the `js` variant (afterwards `tools/build.ts` restored the `tsconfig`).
- The 48-cell matrix with 7.0.2 (`tsc.exe`) and 6.0.3 (compiler API), and the fault injection; each fault was restored afterwards.
- Vite 8.3.8 builds of three entries × three variants × two `sideEffects` settings, and the byte comparison.
- No `tsc.exe`, tsserver or Node process from this spike is left running (checked with `Win32_Process`).
- One run that drove the TypeScript 7 server over all three variants in one Node process hit the client's 5 s `shutdown` timeout. The client then killed the server and the run was lost. Each variant was then run in its own process, cleanly. Not investigated.

**Skipped:**

- `findSourceDefinition` for TypeScript 7: the LSP has no documented method for it.
- The rename edit text (only the ranges were recorded).
- `@types/node` is not in the offline cache. `tools/*.ts` run but are not type-checked.

**Not tested (needs a person, or later work):**

- VS Code itself: opening a `.cs` definition target, the rename preview, the lightbulb, and the TypeScript Native Preview extension.
- The Vite dev server and hot updates with `.js` vs `.ts` generated modules (the decision log covers hot updates of a linked package with `.ts`).
- Other bundlers (webpack, esbuild alone), test runners that do not transpile TypeScript from linked packages, and the `"sideEffects"` field outside rolldown.
- A declaration map produced by a real generator from Roslyn locations; this one is built from text searches in `tools/build.ts`.
- `isolatedDeclarations`, and TypeScript versions other than 6.0.3 and 7.0.2.
