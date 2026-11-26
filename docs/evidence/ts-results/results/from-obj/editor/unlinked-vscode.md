# vscode-tsserver 6.0.3 (status: 6.0.3, host VS Code Electron as Node)

## a-method

### bad.ts diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
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
  - fix: Declare property 'connection' [fixMissingMember]
    edits forms/a-method/copyJobs.ts:15:33: `connection: any;`
    edits forms/a-method/copyJobs.ts:15:64: ``
  - fix: Add index signature for property 'connection' [fixMissingMember]
    edits forms/a-method/copyJobs.ts:15:33: `[x: string]: any;`
    edits forms/a-method/copyJobs.ts:15:64: ``
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      planned.match({
          success: () => 1,
          errors: undefined
      });
    ```
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
  - fix: Declare method 'value' [fixMissingMember]
    edits forms/a-method/sdk.ts:7:52: `value(value: any) {⏎      throw new Error("Method not implemented.");⏎  }`
  - fix: Declare property 'value' [fixMissingMember]
    edits forms/a-method/sdk.ts:9:1: `value: any;`
  - fix: Add index signature for property 'value' [fixMissingMember]
    edits forms/a-method/sdk.ts:7:52: `[x: string]: any;`
- 23:53 TS2322: Type '{}' is not assignable to type 'never'.
- 26:34 TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type 'Handlers<number, {}, unknown, ErrorHandlers<{}>, number>'.
      Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      (await copyJobs.count()).match({
          failure: () => 0,
          success: function(value: number): unknown {
              throw new Error("Function not implemented.");
          }
      });
    ```
- 29:20 TS2339: Property 'match' does not exist on type 'Promise<Result<number, {}>>'.
  - fix: Add 'await' [addMissingAwait]
    ```ts
      (await
            // 8. the promise matched without await
            copyJobs.count()).match({ success: n => n });
    ```
- 29:37 TS7006: Parameter 'n' implicitly has an 'any' type.
  - fix: Infer parameter types from usage [inferFromUsage]
    ```ts
      copyJobs.count().match({ success: (n): any => n });
    ```
- 32:125 TS2322: Type '() => number' is not assignable to type 'never'.
- 35:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js" [import]
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 39:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
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
      (await copyJobs.count()).match({ success: n => n, errors: {} });
    
      // 7. success forgotten
      (await copyJobs.count()).match({ failure: () => 0 });
    
      // 8. the promise matched without await
      copyJobs.count().match({ success: n => n });
    
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

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 3 locations: forms/a-method/copyJobs.ts:17:12, forms/a-method/bad.ts:8:76, forms/a-method/bad.ts:11:76

### probe.ts

- completions @keys-empty [5]:
  - errors (property)
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [3]:
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
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
- completions @keys-no-errors [5]:
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [2]:
  - match (method)
  - unwrap (method)
- hover @hover-success-param:
  ```
  (parameter) p: CopyPlan
  ```
- definition @hover-success-param: forms/a-method/probe.ts:16:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
  ```
  (property) tableMissing: (e: {
      readonly connection: string;
      readonly table: string;
  }) => string
  ```
- definition @hover-case-key: forms/a-method/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 1 locations: forms/a-method/probe.ts:19:7
- hover @hover-case-param:
  ```
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
- definition @hover-case-param: forms/a-method/probe.ts:19:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  (property) table: string
  ```
- definition @definition-payload-field: forms/a-method/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 4 locations: forms/a-method/copyJobs.ts:17:66, forms/a-method/app.ts:18:38, forms/a-method/react.tsx:39:46, forms/a-method/probe.ts:19:68
- hover @hover-failure-param:
  ```
  (parameter) e: CallError
  ```
- definition @hover-failure-param: forms/a-method/probe.ts:22:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
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
- definition @hover-match: forms/a-method/sdk.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H`
- hover @hover-inferred-result:
  ```
  const shown: string | number | boolean | null | undefined
  ```
- definition @hover-inferred-result: forms/a-method/probe.ts:25:9 `const shown = planned.match/*^hover-match*/({`

## b-function

### bad.ts diagnostics and quick fixes

- 9:38 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
    export async function mistakes(): Promise<void> {
      const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
    
      // 1. a declared error is not handled
      match(planned, { success: () => 1, errors: {
          connectionNotFound: () => 2, tableMissing: () => 3,
          columnsMismatch: function(error: { readonly table: string; readonly differences: readonly ColumnDiff[]; }): unknown {
              throw new Error("Function not implemented.");
          }
      } });
    ```
- 12:100 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 15:75 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
  - fix: Change spelling to 'connectionId' [spelling]
    ```ts
      match(planned, { success: () => 1, errors: { connectionNotFound: e => e.connectionId, tableMissing: () => 3, columnsMismatch: () => 4 } });
    ```
  - fix: Declare property 'connection' [fixMissingMember]
    edits forms/b-function/copyJobs.ts:15:33: `connection: any;`
    edits forms/b-function/copyJobs.ts:15:64: ``
  - fix: Add index signature for property 'connection' [fixMissingMember]
    edits forms/b-function/copyJobs.ts:15:33: `[x: string]: any;`
    edits forms/b-function/copyJobs.ts:15:64: ``
- 18:18 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      match(planned, {
          success: () => 1,
          errors: undefined
      });
    ```
- 21:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
  - fix: Declare method 'value' [fixMissingMember]
    edits forms/b-function/sdk.ts:9:56: `value(value: any): unknown;`
  - fix: Declare property 'value' [fixMissingMember]
    edits forms/b-function/sdk.ts:9:56: `value: any;`
  - fix: Add index signature for property 'value' [fixMissingMember]
    edits forms/b-function/sdk.ts:9:56: `[x: string]: any;`
- 24:52 TS2322: Type '{}' is not assignable to type 'never'.
- 27:33 TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type 'Handlers<number, {}, unknown, ErrorHandlers<{}>, number>'.
      Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      match(await copyJobs.count(), {
          failure: () => 0,
          success: function(value: number): unknown {
              throw new Error("Function not implemented.");
          }
      });
    ```
- 30:9 TS2345: Argument of type 'Promise<Result<number, {}>>' is not assignable to parameter of type 'Result<unknown, object>'.
      Property '[result]' is missing in type 'Promise<Result<number, {}>>' but required in type 'Result<unknown, object>'.
  - fix: Add 'await' [addMissingAwait]
    ```ts
      match(await copyJobs.count(), { success: n => n });
    ```
- 33:126 TS2322: Type '() => number' is not assignable to type 'never'.
- 36:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js" [import]
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 40:38 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
    export async function mistakes(): Promise<void> {
      const planned = await copyJobs.plan({ source: "a", target: "b", tables: [] });
    
      // 1. a declared error is not handled
      match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });
    
      // 2. a typo in an error name
      match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });
    
      // 3. a payload field that does not exist
      match(planned, { success: () => 1, errors: { connectionNotFound: e => e.connection, tableMissing: () => 3, columnsMismatch: () => 4 } });
    
      // 4. errors forgotten entirely
      match(planned, { success: () => 1 });
    
      // 5. reading the value without match
      console.log(planned.value);
    
      // 6. errors given to a command that declares none
      match(await copyJobs.count(), { success: n => n, errors: {} });
    
      // 7. success forgotten
      match(await copyJobs.count(), { failure: () => 0 });
    
      // 8. the promise matched without await
      match(copyJobs.count(), { success: n => n });
    
      // 9. every case handled plus one that is not declared
      match(planned, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnsMismatch: () => 4, columnMismatch: () => 5 } });
    
      // 10. control for quick fixes: a type used without its import
      const report: CopyReport | null = null;
      console.log(report);
    
      // 11. an empty errors object
      match(planned, { success: () => 1, errors: {
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

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 3 locations: forms/b-function/copyJobs.ts:17:12, forms/b-function/bad.ts:9:77, forms/b-function/bad.ts:12:77

### probe.ts

- completions @keys-empty [5]:
  - errors (property)
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [3]:
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
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
- completions @keys-no-errors [5]:
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [0]:
- hover @hover-success-param:
  ```
  (parameter) p: CopyPlan
  ```
- definition @hover-success-param: forms/b-function/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
  ```
  (property) tableMissing: (e: {
      readonly connection: string;
      readonly table: string;
  }) => string
  ```
- definition @hover-case-key: forms/b-function/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 1 locations: forms/b-function/probe.ts:20:7
- hover @hover-case-param:
  ```
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
- definition @hover-case-param: forms/b-function/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  (property) table: string
  ```
- definition @definition-payload-field: forms/b-function/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 4 locations: forms/b-function/copyJobs.ts:17:66, forms/b-function/app.ts:18:38, forms/b-function/react.tsx:40:46, forms/b-function/probe.ts:20:68
- hover @hover-failure-param:
  ```
  (parameter) e: CallError
  ```
- definition @hover-failure-param: forms/b-function/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
  ```
  (alias) match<CopyPlan, PlanError, string, {
      connectionNotFound: () => number;
      tableMissing: () => boolean;
      columnsMismatch: () => null;
  }, undefined>(outcome: Result<CopyPlan, PlanError>, handlers: Handlers<CopyPlan, PlanError, string, {
      connectionNotFound: () => number;
      tableMissing: () => boolean;
      columnsMismatch: () => null;
  }, undefined>): string | number | boolean | null | undefined
  import match
  ---
  Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`.
  ```
- definition @hover-match: forms/b-function/sdk.ts:14:17 `export function match<T, E extends ErrorCases, S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F =`
- hover @hover-inferred-result:
  ```
  const shown: string | number | boolean | null | undefined
  ```
- definition @hover-inferred-result: forms/b-function/probe.ts:26:9 `const shown = match/*^hover-match*/(planned, {`

## b-proto

### bad.ts diagnostics and quick fixes

- 9:38 TS2322: Type '{ connectionNotFound: () => number; tableMissing: () => number; }' is not assignable to type 'ErrorHandlers<PlanError> & { readonly connectionNotFound: (error: ApplicationError<"connectionNotFound", { readonly connectionId: string; }>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => unknown; readonly columnsMismatch: (error: ApplicationError<...>) => unknown; }'.
      Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 12:100 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError> & { readonly connectionNotFound: (error: ApplicationError<"connectionNotFound", { readonly connectionId: string; }>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => unknown; readonly columnsMismatch: (error: ApplicationError<...>) => unknown; }'. Did you mean to write 'columnsMismatch'?
- 15:75 TS2551: Property 'connection' does not exist on type 'ApplicationError<"connectionNotFound", { readonly connectionId: string; }>'. Did you mean 'connectionId'?
  - fix: Change spelling to 'connectionId' [spelling]
    ```ts
      match(planned, { success: () => 1, errors: { connectionNotFound: e => e.connectionId, tableMissing: () => 3, columnsMismatch: () => 4 } });
    ```
- 18:18 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type '{ readonly success: (value: CopyPlan) => unknown; readonly failure?: (error: CallError) => unknown; } & { readonly errors: ErrorHandlers<PlanError>; } & { ...; }'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      match(planned, {
          success: () => 1,
          errors: undefined
      });
    ```
- 21:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
  - fix: Declare method 'value' [fixMissingMember]
    edits forms/b-proto/sdk.ts:29:68: `value(value: any): unknown;`
  - fix: Declare property 'value' [fixMissingMember]
    edits forms/b-proto/sdk.ts:29:68: `value: any;`
  - fix: Add index signature for property 'value' [fixMissingMember]
    edits forms/b-proto/sdk.ts:29:68: `[x: string]: any;`
- 24:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js" [import]
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 28:38 TS2322: Type '{}' is not assignable to type 'ErrorHandlers<PlanError> & { readonly connectionNotFound: (error: ApplicationError<"connectionNotFound", { readonly connectionId: string; }>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => unknown; readonly columnsMismatch: (error: ApplicationError<...>) => unknown; }'.
      Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch

- rename from the generated declaration copyJobs.ts:12:24: canRename=false (You cannot rename this element.); 0 locations: 

### probe.ts

- completions @keys-empty [5]:
  - errors (property)
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [3]:
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
- completions @errors-empty [3]:
  - columnsMismatch (property)
  - connectionNotFound (property)
  - tableMissing (property)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: ApplicationError<"tableMissing", {
        readonly connection: string;
        readonly table: string;
    }>) => unknown
    ```
- completions @errors-partial [2]:
  - columnsMismatch (property)
  - tableMissing (property)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: ApplicationError<"tableMissing", {
        readonly connection: string;
        readonly table: string;
    }>) => unknown
    ```
- completions @keys-no-errors [5]:
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [0]:
- hover @hover-success-param:
  ```
  (parameter) p: CopyPlan
  ```
- definition @hover-success-param: forms/b-proto/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
  ```
  (property) tableMissing: (e: ApplicationError<"tableMissing", {
      readonly connection: string;
      readonly table: string;
  }>) => string
  ```
- definition @hover-case-key: forms/b-proto/probe.ts:20:7 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- rename @hover-case-key: canRename=true; 1 locations: forms/b-proto/probe.ts:20:7
- hover @hover-case-param:
  ```
  (parameter) e: ApplicationError<"tableMissing", {
      readonly connection: string;
      readonly table: string;
  }>
  ```
- definition @hover-case-param: forms/b-proto/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  (property) table: string
  ```
- definition @definition-payload-field: forms/b-proto/copyJobs.ts:12:78 `| ApplicationError<"tableMissing", { readonly connection: string; readonly table: string }>`
- rename @definition-payload-field: canRename=true; 3 locations: forms/b-proto/copyJobs.ts:12:78, forms/b-proto/app.ts:16:38, forms/b-proto/probe.ts:20:68
- hover @hover-failure-param:
  ```
  (parameter) e: CallError
  ```
- definition @hover-failure-param: forms/b-proto/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
  ```
  (alias) match<CopyPlan, PlanError, {
      readonly success: () => string;
      readonly errors: {
          readonly connectionNotFound: () => number;
          readonly tableMissing: () => boolean;
          readonly columnsMismatch: () => null;
      };
  }>(outcome: Result<CopyPlan, PlanError>, handlers: {
      readonly success: () => string;
      readonly errors: {
          readonly connectionNotFound: () => number;
          readonly tableMissing: () => boolean;
          readonly columnsMismatch: () => null;
      };
  } & {
      readonly errors: {
          readonly connectionNotFound: () => number;
          readonly tableMissing: () => boolean;
          readonly columnsMismatch: () => null;
      };
  }): MatchResult<...>
  import match
  ---
  Exhaustive over success and every declared error; infrastructure failures go to `failure` or the interceptor.
  ```
- definition @hover-match: forms/b-proto/sdk.ts:62:17 `export function match<T, E extends AnyApplicationError, const H extends Handlers<T, E>>(`
- hover @hover-inferred-result:
  ```
  const shown: MatchResult<{
      readonly success: () => string;
      readonly errors: {
          readonly connectionNotFound: () => number;
          readonly tableMissing: () => boolean;
          readonly columnsMismatch: () => null;
      };
  }>
  ```
- definition @hover-inferred-result: forms/b-proto/probe.ts:26:9 `const shown = match/*^hover-match*/(planned, {`

## c-call

### bad.ts diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
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
  - fix: Declare property 'connection' [fixMissingMember]
    edits forms/c-call/copyJobs.ts:15:33: `connection: any;`
    edits forms/c-call/copyJobs.ts:15:64: ``
  - fix: Add index signature for property 'connection' [fixMissingMember]
    edits forms/c-call/copyJobs.ts:15:33: `[x: string]: any;`
    edits forms/c-call/copyJobs.ts:15:64: ``
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
  - fix: Add missing properties [fixMissingProperties]
    ```ts
      planned.match({
          success: () => 1,
          errors: undefined
      });
    ```
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
  - fix: Declare method 'value' [fixMissingMember]
    edits forms/c-call/sdk.ts:7:52: `value(value: any) {⏎      throw new Error("Method not implemented.");⏎  }`
  - fix: Declare property 'value' [fixMissingMember]
    edits forms/c-call/sdk.ts:9:1: `value: any;`
  - fix: Add index signature for property 'value' [fixMissingMember]
    edits forms/c-call/sdk.ts:7:52: `[x: string]: any;`
- 23:51 TS2322: Type '{}' is not assignable to type 'never'.
- 26:32 TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type 'Handlers<number, {}, unknown, ErrorHandlers<{}>, number>'.
      Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
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
  - fix: Update import from "./copyJobs.js" [import]
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 40:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
  - fix: Add missing properties [fixMissingProperties]
    ```ts
    import { copyJobs, type ColumnDiff } from "./copyJobs.js";
    
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

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 3 locations: forms/c-call/copyJobs.ts:17:12, forms/c-call/bad.ts:8:76, forms/c-call/bad.ts:11:76

### probe.ts

- completions @errors-on-call [6]:
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
- completions @keys-empty [5]:
  - errors (property)
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [3]:
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
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
- completions @keys-no-errors [5]:
  - success (property)
  - success(value) => `success(value) {⏎    $0⏎},` (property)
  - errors (property)
  - failure (property)
  - failure(error) => `failure(error) {⏎    $0⏎},` (property)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [2]:
  - match (method)
  - unwrap (method)
- hover @hover-success-param:
  ```
  (parameter) p: CopyPlan
  ```
- definition @hover-success-param: forms/c-call/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
  ```
  (property) tableMissing: (e: {
      readonly connection: string;
      readonly table: string;
  }) => string
  ```
- definition @hover-case-key: forms/c-call/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 1 locations: forms/c-call/probe.ts:20:7
- hover @hover-case-param:
  ```
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
- definition @hover-case-param: forms/c-call/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  (property) table: string
  ```
- definition @definition-payload-field: forms/c-call/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 4 locations: forms/c-call/copyJobs.ts:17:66, forms/c-call/app.ts:18:38, forms/c-call/react.tsx:38:46, forms/c-call/probe.ts:20:68
- hover @hover-failure-param:
  ```
  (parameter) e: CallError
  ```
- definition @hover-failure-param: forms/c-call/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
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
- definition @hover-match: forms/c-call/sdk.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H`
- hover @hover-inferred-result:
  ```
  const shown: string | number | boolean | null | undefined
  ```
- definition @hover-inferred-result: forms/c-call/probe.ts:26:9 `const shown = planned.match/*^hover-match*/({`
