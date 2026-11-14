# tsgo-lsp 7.0.2 (serverInfo: typescript-go 7.0.2)

## js

### bad.ts: diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 11:99 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 14:74 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 23:51 TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
- 26:32 TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
- 29:9 TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
- 33:125 TS2322: Type '() => number' is not assignable to type 'never'.
- 36:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "@spar/app/copyJobs"
    ```ts
    import { copyJobs, type CopyReport } from "@spar/app/copyJobs";
    ```
- 40:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch

### probe.ts

- **@dto-prop-literal**
  - hover:
    ```
    ```typescript
    (property) CopyRecipe.source: string
    ```
    Name of the source connection.
    ```
  - definition: obj/spar/client/copyJobs.d.ts:7:12 `readonly source: string;`
  - source definition: (not requested: no documented LSP method)
  - references: 6: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
  - rename: canRename=true; 6 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
- **@command**
  - hover:
    ```
    ```typescript
    (property) plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>
    ```
    Checks the recipe against both connections and returns the steps.
    ```
  - definition: obj/spar/client/copyJobs.d.ts:47:12 `readonly plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>;`
  - source definition: (not requested: no documented LSP method)
  - references: 7: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:47:12-47:16 `plan`]
- completions @errors-empty [3]:
  - connectionNotFound (Field)
  - tableMissing (Field)
  - columnsMismatch (Field)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown
    ```
- completions @errors-partial [2]:
  - tableMissing (Field)
  - columnsMismatch (Field)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown
    ```
- **@case-key**
  - hover:
    ```
    ```typescript
    (property) tableMissing: ((error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown) & ((e: {
        readonly connection: string;
        readonly table: string;
    }) => string)
    ```
    
    ```
  - definition: obj/spar/client/copyJobs.d.ts:28:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
  - source definition: (not requested: no documented LSP method)
  - references: 11: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
  - rename: canRename=true; 11 edits: frontend/src/types.ts ×1, obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
- **@payload-field**
  - hover:
    ```
    ```typescript
    (property) table: string
    ```
    
    ```
  - definition: obj/spar/client/copyJobs.d.ts:28:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
  - source definition: (not requested: no documented LSP method)
  - references: 5: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:66-28:71 `table`]
  - rename: canRename=true; 5 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:66-28:71 `table`]
- **@sdk-match**
  - hover:
    ```
    ```typescript
    (method) Result<CopyPlan, PlanError>.match<string, {
        connectionNotFound: () => number;
        tableMissing: () => boolean;
        columnsMismatch: () => null;
    }, undefined>(handlers: Handlers<CopyPlan, PlanError, string, {
        connectionNotFound: () => number;
        tableMissing: () => boolean;
        columnsMismatch: () => null;
    }, undefined>): string | number | boolean | null | undefined
    ```
    Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`.
    ```
  - definition: obj/spar/client/sdk/src/index.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<`
  - source definition: (not requested: no documented LSP method)
  - references: 14: obj/spar/client/sdk/src/index.ts ×1, frontend/src/app.ts ×2, frontend/src/bad.ts ×6, frontend/src/probe.ts ×4, frontend/src/types.ts ×1 [outside the app: obj/spar/client/sdk/src/index.ts:16:3-16:8 `match`]
- **@dto-prop-access**
  - hover:
    ```
    ```typescript
    (property) CopyRecipe.source: string
    ```
    Name of the source connection.
    ```
  - definition: obj/spar/client/copyJobs.d.ts:7:12 `readonly source: string;`
  - source definition: (not requested: no documented LSP method)
  - references: 6: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
  - rename: canRename=true; 6 edits: frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, obj/spar/client/copyJobs.d.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
- **@contract**
  - hover:
    ```
    ```typescript
    (alias) const copyJobs: {
        readonly plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>;
        readonly run: (request: RunCopy, options?: CallOptions) => Call<CopyReport, RunError>;
        readonly count: (options?: CallOptions) => Call<number>;
        readonly cancelAll: (options?: CallOptions) => Call<void>;
        readonly preview: (request: CopyRecipe, options?: CallOptions) => AsyncIterable<Result<PreviewRow, PreviewError>>;
        readonly progress: (request: ProgressRequest, options?: CallOptions) => AsyncIterable<Result<SubscriptionEvent<CopyProgress>>>;
    }
    ```
    Copies tables between connections.
    ```
  - definition: obj/spar/client/copyJobs.d.ts:45:22 `export declare const copyJobs: {`
  - source definition: (not requested: no documented LSP method)
  - references: 25: frontend/src/probe.ts ×3, obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×7, frontend/src/bad.ts ×5, obj/spar/client/index.d.ts ×1, frontend/src/main-barrel.ts ×2, frontend/src/main.ts ×2, frontend/src/types.ts ×4 [outside the app: obj/spar/client/copyJobs.d.ts:45:22-45:30 `copyJobs`; obj/spar/client/index.d.ts:2:10-2:18 `copyJobs`]
- **from the generated declaration** obj/spar/client/copyJobs.d.ts:28:13: rename canRename=true, 11 edits: frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1, obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]; references 11: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
  - definition list detail: obj/spar/client/copyJobs.d.ts:28:12
