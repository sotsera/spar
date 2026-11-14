# vscode-tsserver 6.0.3 (status: 6.0.3, host VS Code Electron as Node)

## js-csmap

### bad.ts: diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "@spar/app/copyJobs";
    
    export async function mistakes(): Promise<void> {
      const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
    
      // 1. a declared error is not handled
      planned.match({ success: () => 1, errors: {
          connectionNotFound: () => 2, tableMissing: () => 3,
          columnsMismatch: function(error: { readonly table: string; readonly differences: readonly ColumnDiff[]; }): unknown {
              throw new Error("Function not implemented.");
          }
      } });
    ```
- 11:99 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 14:74 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
  - fix: Change spelling to 'connectionId' [spelling]
    ```ts
      planned.match({ success: () => 1, errors: { connectionNotFound: e => e.connectionId, tableMissing: () => 3, columnsMismatch: () => 4 } });
    ```
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      planned.match({
          success: () => 1,
          errors: undefined
      });
    ```
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 23:51 TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
- 26:32 TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      await copyJobs.count().match({
          failure: () => 0,
          success: function(value: number): unknown {
              throw new Error("Function not implemented.");
          }
      });
    ```
- 29:9 TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
- 33:125 TS2322: Type '() => number' is not assignable to type 'never'.
- 36:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "@spar/app/copyJobs" [import]
    ```ts
    import { copyJobs, type CopyReport } from "@spar/app/copyJobs";
    ```
- 40:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "@spar/app/copyJobs";
    
    export async function mistakes(): Promise<void> {
      const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
    
      // 1. a declared error is not handled
      planned.match({ success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });
    
      // 2. a typo in an error name
      planned.match({ success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });
    
      // 3. a payload field that does not exist
      planned.match({ success: () => 1, errors: { connectionNotFound: e => e.connection, tableMissing: () => 3, columnsMismatch: () => 4 } });
    
      // 4. errors forgotten entirely
      planned.match({ success: () => 1 });
    
      // 5. reading the value without match
      console.log(planned.value);
    
      // 6. errors given to a command that declares none
      await copyJobs.count().match({ success: n => n, errors: {} });
    
      // 7. success forgotten
      await copyJobs.count().match({ failure: () => 0 });
    
      // 8. the matched value used without await
      const counted: number | undefined = copyJobs.count().match({ success: n => n });
      console.log(counted);
    
      // 9. every case handled plus one that is not declared
      planned.match({ success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnsMismatch: () => 4, columnMismatch: () => 5 } });
    
      // 10. control for quick fixes: a type used without its import
      const report: CopyReport | null = null;
      console.log(report);
    
      // 11. an empty errors object
      planned.match({ success: () => 1, errors: {
          connectionNotFound: function(error: { readonly connectionId: string; }): unknown {
              throw new Error("Function not implemented.");
          },
          tableMissing: function(error: { readonly connection: string; readonly table: string; }): unknown {
              throw new Error("Function not implemented.");
          },
          columnsMismatch: function(error: { readonly table: string; readonly differences: readonly ColumnDiff[]; }): unknown {
              throw new Error("Function not implemented.");
          }
      } });
    ```

### probe.ts

- **@dto-prop-literal**
  - hover:
    ```
    (property) CopyRecipe.source: string
    ---
    Name of the source connection.
    ```
  - definition: <spike>/contracts/CopyJobs.cs:13:12 `string Source,`
  - source definition: <spike>/contracts/CopyJobs.cs:13:12 `string Source,`
  - references: 6: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
  - rename: canRename=true; 6 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
- **@command**
  - hover:
    ```
    (property) plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>
    ---
    Checks the recipe against both connections and returns the steps.
    ```
  - definition: <spike>/contracts/CopyJobs.cs:67:46 `public Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe request, CancellationToken c`
  - source definition: <spike>/contracts/CopyJobs.cs:67:46 `public Task<Result<CopyPlan, PlanError>> PlanAsync(CopyRecipe request, CancellationToken c`
  - references: 7: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:47:12-47:16 `plan`]
- completions @errors-empty [6]:
  - columnsMismatch (property)
  - columnsMismatch(error) => `columnsMismatch(error) {⏎    $0⏎},` (property)
  - connectionNotFound (property)
  - connectionNotFound(error) => `connectionNotFound(error) {⏎    $0⏎},` (property)
  - tableMissing (property)
  - tableMissing(error) => `tableMissing(error) {⏎    $0⏎},` (property)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown
    ---
    The table does not exist on that connection.
    ```
- completions @errors-partial [4]:
  - columnsMismatch (property)
  - columnsMismatch(error) => `columnsMismatch(error) {⏎    $0⏎},` (property)
  - tableMissing (property)
  - tableMissing(error) => `tableMissing(error) {⏎    $0⏎},` (property)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown
    ---
    The table does not exist on that connection.
    ```
- **@case-key**
  - hover:
    ```
    (property) tableMissing: ((error: {
        readonly connection: string;
        readonly table: string;
    }) => unknown) & ((e: {
        readonly connection: string;
        readonly table: string;
    }) => string)
    ---
    The table does not exist on that connection.
    ```
  - definition: <spike>/contracts/CopyJobs.cs:40:26 `public sealed record TableMissing(string Connection, string Table) : PlanError;`
  - source definition: <spike>/contracts/CopyJobs.cs:40:26 `public sealed record TableMissing(string Connection, string Table) : PlanError;`
  - references: 11: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
  - rename: canRename=true; 11 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
- **@payload-field**
  - hover:
    ```
    (property) table: string
    ```
  - definition: <spike>/contracts/CopyJobs.cs:40:65 `public sealed record TableMissing(string Connection, string Table) : PlanError;`
  - source definition: <spike>/contracts/CopyJobs.cs:40:65 `public sealed record TableMissing(string Connection, string Table) : PlanError;`
  - references: 5: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:66-28:71 `table`]
  - rename: canRename=true; 5 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:66-28:71 `table`]
- **@sdk-match**
  - hover:
    ```
    (method) Result<CopyPlan, PlanError>.match<string, {
        connectionNotFound: () => number;
        tableMissing: () => boolean;
        columnsMismatch: () => null;
    }, undefined>(handlers: Handlers<CopyPlan, PlanError, string, {
        connectionNotFound: () => number;
        tableMissing: () => boolean;
        columnsMismatch: () => null;
    }, undefined>): string | number | boolean | null | undefined
    ---
    Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`.
    ```
  - definition: obj/spar/client/sdk/src/index.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<`
  - source definition: obj/spar/client/sdk/src/index.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<`
  - references: 14: obj/spar/client/sdk/index.d.ts ×1, frontend/src/app.ts ×2, frontend/src/bad.ts ×6, frontend/src/probe.ts ×4, frontend/src/types.ts ×1 [outside the app: obj/spar/client/sdk/index.d.ts:9:5-9:10 `match`]
- **@dto-prop-access**
  - hover:
    ```
    (property) CopyRecipe.source: string
    ---
    Name of the source connection.
    ```
  - definition: <spike>/contracts/CopyJobs.cs:13:12 `string Source,`
  - source definition: <spike>/contracts/CopyJobs.cs:13:12 `string Source,`
  - references: 6: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
  - rename: canRename=true; 6 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2 [outside the app: obj/spar/client/copyJobs.d.ts:7:12-7:18 `source`]
- **@contract**
  - hover:
    ```
    (alias) const copyJobs: {
        readonly plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>;
        readonly run: (request: RunCopy, options?: CallOptions) => Call<CopyReport, RunError>;
        readonly count: (options?: CallOptions) => Call<number>;
        readonly cancelAll: (options?: CallOptions) => Call<void>;
        readonly preview: (request: CopyRecipe, options?: CallOptions) => AsyncIterable<Result<PreviewRow, PreviewError>>;
        readonly progress: (request: ProgressRequest, options?: CallOptions) => AsyncIterable<Result<SubscriptionEvent<CopyProgress>>>;
    }
    import copyJobs
    ---
    Copies tables between connections.
    ```
  - definition: <spike>/contracts/CopyJobs.cs:64:21 `public sealed class CopyJobsService`
  - source definition: <spike>/contracts/CopyJobs.cs:64:21 `public sealed class CopyJobsService`
  - references: 25: frontend/src/probe.ts ×3, obj/spar/client/copyJobs.d.ts ×1, frontend/src/app.ts ×7, frontend/src/bad.ts ×5, obj/spar/client/index.d.ts ×1, frontend/src/main-barrel.ts ×2, frontend/src/main.ts ×2, frontend/src/types.ts ×4 [outside the app: obj/spar/client/copyJobs.d.ts:45:22-45:30 `copyJobs`; obj/spar/client/index.d.ts:2:10-2:18 `copyJobs`]
- **from the generated declaration** obj/spar/client/copyJobs.d.ts:28:13: rename canRename=true, 11 edits: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]; references 11: obj/spar/client/copyJobs.d.ts ×1, frontend/src/bad.ts ×4, frontend/src/app.ts ×1, frontend/src/main-barrel.ts ×1, frontend/src/main.ts ×1, frontend/src/probe.ts ×2, frontend/src/types.ts ×1 [outside the app: obj/spar/client/copyJobs.d.ts:28:12-28:24 `tableMissing`]
  - definition list detail: <spike>/contracts/CopyJobs.cs:40:26
