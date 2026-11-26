# tsgo-lsp 7.0.2 (serverInfo: typescript-go 7.0.2)

## a-method

### bad.ts diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 11:99 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 14:74 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 23:53 TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
- 26:34 TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
- 29:20 TS2339: Property 'match' does not exist on type 'Promise<Result<number, {}>>'.
- 29:37 TS7006: Parameter 'n' implicitly has an 'any' type.
- 32:125 TS2322: Type '() => number' is not assignable to type 'never'.
- 35:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js"
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 39:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 10 locations: forms/a-method/app.ts:18:7, forms/a-method/react.tsx:39:9, forms/a-method/probe.ts:13:90, forms/a-method/probe.ts:19:7, forms/a-method/probe.ts:27:44, forms/a-method/copyJobs.ts:17:12, forms/a-method/bad.ts:8:76, forms/a-method/bad.ts:11:76, forms/a-method/bad.ts:14:86, forms/a-method/bad.ts:32:76

### probe.ts

- completions @keys-empty [3]:
  - success (Field)
  - failure? => `failure` (Field)
  - errors (Field)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [2]:
  - failure? => `failure` (Field)
  - errors (Field)
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
- completions @keys-no-errors [2]:
  - success (Field)
  - failure? => `failure` (Field)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [2]:
  - match (Method)
  - unwrap (Method)
- hover @hover-success-param:
  ```
  ```typescript
  (parameter) p: CopyPlan
  ```
  
  ```
- definition @hover-success-param: forms/a-method/probe.ts:16:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
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
- definition @hover-case-key: forms/a-method/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 10 locations: forms/a-method/bad.ts:8:76, forms/a-method/bad.ts:11:76, forms/a-method/bad.ts:14:86, forms/a-method/bad.ts:32:76, forms/a-method/app.ts:18:7, forms/a-method/react.tsx:39:9, forms/a-method/probe.ts:13:90, forms/a-method/probe.ts:19:7, forms/a-method/probe.ts:27:44, forms/a-method/copyJobs.ts:17:12
- hover @hover-case-param:
  ```
  ```typescript
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
  
  ```
- definition @hover-case-param: forms/a-method/probe.ts:19:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  ```typescript
  (property) table: string
  ```
  
  ```
- definition @definition-payload-field: forms/a-method/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 4 locations: forms/a-method/copyJobs.ts:17:66, forms/a-method/app.ts:18:38, forms/a-method/react.tsx:39:46, forms/a-method/probe.ts:19:68
- hover @hover-failure-param:
  ```
  ```typescript
  (parameter) e: CallError
  ```
  
  ```
- definition @hover-failure-param: forms/a-method/probe.ts:22:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
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
- definition @hover-match: forms/a-method/sdk.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H`
- hover @hover-inferred-result:
  ```
  ```typescript
  const shown: string | number | boolean | null | undefined
  ```
  
  ```
- definition @hover-inferred-result: forms/a-method/probe.ts:25:9 `const shown = planned.match/*^hover-match*/({`

## b-function

### bad.ts diagnostics and quick fixes

- 9:38 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 12:100 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 15:75 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
- 18:18 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
- 21:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 24:52 TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
- 27:33 TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
- 30:9 TS2741: Property '[result]' is missing in type 'Promise<Result<number, {}>>' but required in type 'Result<unknown, object>'.
- 33:126 TS2322: Type '() => number' is not assignable to type 'never'.
- 36:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js"
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 40:38 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 9 locations: forms/b-function/app.ts:18:7, forms/b-function/react.tsx:40:9, forms/b-function/probe.ts:20:7, forms/b-function/probe.ts:28:44, forms/b-function/copyJobs.ts:17:12, forms/b-function/bad.ts:9:77, forms/b-function/bad.ts:12:77, forms/b-function/bad.ts:15:87, forms/b-function/bad.ts:33:77

### probe.ts

- completions @keys-empty [3]:
  - success (Field)
  - failure? => `failure` (Field)
  - errors (Field)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [2]:
  - failure? => `failure` (Field)
  - errors (Field)
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
- completions @keys-no-errors [2]:
  - success (Field)
  - failure? => `failure` (Field)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [0]:
- hover @hover-success-param:
  ```
  ```typescript
  (parameter) p: CopyPlan
  ```
  
  ```
- definition @hover-success-param: forms/b-function/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
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
- definition @hover-case-key: forms/b-function/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 9 locations: forms/b-function/react.tsx:40:9, forms/b-function/probe.ts:20:7, forms/b-function/probe.ts:28:44, forms/b-function/copyJobs.ts:17:12, forms/b-function/bad.ts:9:77, forms/b-function/bad.ts:12:77, forms/b-function/bad.ts:15:87, forms/b-function/bad.ts:33:77, forms/b-function/app.ts:18:7
- hover @hover-case-param:
  ```
  ```typescript
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
  
  ```
- definition @hover-case-param: forms/b-function/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  ```typescript
  (property) table: string
  ```
  
  ```
- definition @definition-payload-field: forms/b-function/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 4 locations: forms/b-function/copyJobs.ts:17:66, forms/b-function/app.ts:18:38, forms/b-function/react.tsx:40:46, forms/b-function/probe.ts:20:68
- hover @hover-failure-param:
  ```
  ```typescript
  (parameter) e: CallError
  ```
  
  ```
- definition @hover-failure-param: forms/b-function/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
  ```
  ```typescript
  (alias) function match<CopyPlan, PlanError, string, {
      connectionNotFound: () => number;
      tableMissing: () => boolean;
      columnsMismatch: () => null;
  }, undefined>(outcome: Result<CopyPlan, PlanError>, handlers: Handlers<CopyPlan, PlanError, string, {
      connectionNotFound: () => number;
      tableMissing: () => boolean;
      columnsMismatch: () => null;
  }, undefined>): string | number | boolean | null | undefined
  ```
  Runs the handler for this outcome and returns its value. Without `failure`, infrastructure failures go to the app-wide interceptor and `match` returns `undefined`.
  ```
- definition @hover-match: forms/b-function/sdk.ts:14:17 `export function match<T, E extends ErrorCases, S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F =`
- hover @hover-inferred-result:
  ```
  ```typescript
  const shown: string | number | boolean | null | undefined
  ```
  
  ```
- definition @hover-inferred-result: forms/b-function/probe.ts:26:9 `const shown = match/*^hover-match*/(planned, {`

## b-proto

### bad.ts diagnostics and quick fixes

- 9:38 TS2322: Type '{ connectionNotFound: () => number; tableMissing: () => number; }' is not assignable to type 'ErrorHandlers<PlanError> & { readonly columnsMismatch: (error: ApplicationError<"columnsMismatch", { readonly table: string; readonly differences: readonly ColumnDiff[]; }>) => unknown; readonly connectionNotFound: (error: ApplicationError<...>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => un...'.
      Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 12:100 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError> & { readonly columnsMismatch: (error: ApplicationError<"columnsMismatch", { readonly table: string; readonly differences: readonly ColumnDiff[]; }>) => unknown; readonly connectionNotFound: (error: ApplicationError<...>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => un...'. Did you mean to write 'columnsMismatch'?
- 15:75 TS2551: Property 'connection' does not exist on type 'ApplicationError<"connectionNotFound", { readonly connectionId: string; }>'. Did you mean 'connectionId'?
- 18:18 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type '{ readonly success: (value: CopyPlan) => unknown; readonly failure?: (error: CallError) => unknown; } & { readonly errors: ErrorHandlers<PlanError>; } & { ...; }'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
- 21:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 24:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js"
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 28:38 TS2322: Type '{}' is not assignable to type 'ErrorHandlers<PlanError> & { readonly columnsMismatch: (error: ApplicationError<"columnsMismatch", { readonly table: string; readonly differences: readonly ColumnDiff[]; }>) => unknown; readonly connectionNotFound: (error: ApplicationError<...>) => unknown; readonly tableMissing: (error: ApplicationError<...>) => un...'.
      Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': columnsMismatch, connectionNotFound, tableMissing

- rename from the generated declaration copyJobs.ts:12:24: canRename=false ({"code":-32803,"message":"You cannot rename this element."}); 0 locations: 

### probe.ts

- completions @keys-empty [3]:
  - success (Field)
  - failure? => `failure` (Field)
  - errors (Field)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [2]:
  - failure? => `failure` (Field)
  - errors (Field)
- completions @errors-empty [3]:
  - columnsMismatch (Field)
  - connectionNotFound (Field)
  - tableMissing (Field)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: ApplicationError<"tableMissing", {
        readonly connection: string;
        readonly table: string;
    }>) => unknown
    ```
- completions @errors-partial [2]:
  - columnsMismatch (Field)
  - tableMissing (Field)
  - details of tableMissing:
    ```
    (property) tableMissing: (error: ApplicationError<"tableMissing", {
        readonly connection: string;
        readonly table: string;
    }>) => unknown
    ```
- completions @keys-no-errors [3]:
  - success (Field)
  - failure? => `failure` (Field)
  - errors? => `errors` (Field)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [0]:
- hover @hover-success-param:
  ```
  ```typescript
  (parameter) p: CopyPlan
  ```
  
  ```
- definition @hover-success-param: forms/b-proto/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
  ```
  ```typescript
  (property) tableMissing: (e: ApplicationError<"tableMissing", {
      readonly connection: string;
      readonly table: string;
  }>) => string
  ```
  
  ```
- definition @hover-case-key: forms/b-proto/probe.ts:20:7 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- rename @hover-case-key: canRename=true; 1 locations: forms/b-proto/probe.ts:20:7
- hover @hover-case-param:
  ```
  ```typescript
  (parameter) e: ApplicationError<"tableMissing", {
      readonly connection: string;
      readonly table: string;
  }>
  ```
  
  ```
- definition @hover-case-param: forms/b-proto/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  ```typescript
  (property) table: string
  ```
  
  ```
- definition @definition-payload-field: forms/b-proto/copyJobs.ts:12:78 `| ApplicationError<"tableMissing", { readonly connection: string; readonly table: string }>`
- rename @definition-payload-field: canRename=true; 3 locations: forms/b-proto/app.ts:16:38, forms/b-proto/probe.ts:20:68, forms/b-proto/copyJobs.ts:12:78
- hover @hover-failure-param:
  ```
  ```typescript
  (parameter) e: CallError
  ```
  
  ```
- definition @hover-failure-param: forms/b-proto/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
  ```
  ```typescript
  (alias) function match<CopyPlan, PlanError, {
      readonly success: () => string;
      readonly errors: {
          connectionNotFound: () => number;
          tableMissing: () => boolean;
          columnsMismatch: () => null;
      };
  }>(outcome: Result<CopyPlan, PlanError>, handlers: {
      readonly success: () => string;
      readonly errors: {
          connectionNotFound: () => number;
          tableMissing: () => boolean;
          columnsMismatch: () => null;
      };
  } & {
      readonly errors: {
          readonly connectionNotFound: () => number;
          readonly tableMissing: () => boolean;
          readonly columnsMismatch: () => null;
      };
  }): MatchResult<{
      readonly success: () => string;
      readonly errors: {
          connectionNotFound: () => number;
          tableMissing: () => boolean;
          columnsMismatch: () => null;
      };
  }>
  ```
  Exhaustive over success and every declared error; infrastructure failures go to `failure` or the interceptor.
  ```
- definition @hover-match: forms/b-proto/sdk.ts:62:17 `export function match<T, E extends AnyApplicationError, const H extends Handlers<T, E>>(`
- hover @hover-inferred-result:
  ```
  ```typescript
  const shown: MatchResult<{
      readonly success: () => string;
      readonly errors: {
          connectionNotFound: () => number;
          tableMissing: () => boolean;
          columnsMismatch: () => null;
      };
  }>
  ```
  
  ```
- definition @hover-inferred-result: forms/b-proto/probe.ts:26:9 `const shown = match/*^hover-match*/(planned, {`

## c-call

### bad.ts diagnostics and quick fixes

- 8:37 TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
- 11:99 TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
- 14:74 TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
- 17:17 TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
      Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
- 20:23 TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
- 23:51 TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
- 26:32 TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
- 29:9 TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
- 33:125 TS2322: Type '() => number' is not assignable to type 'never'.
- 36:17 TS2304: Cannot find name 'CopyReport'.
  - fix: Update import from "./copyJobs.js"
    ```ts
    import { copyJobs, type CopyReport } from "./copyJobs.js";
    ```
- 40:37 TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch

- rename from the generated declaration copyJobs.ts:17:13: canRename=true; 11 locations: forms/c-call/react.tsx:38:9, forms/c-call/docs.ts:20:7, forms/c-call/probe.ts:14:90, forms/c-call/probe.ts:20:7, forms/c-call/probe.ts:28:44, forms/c-call/copyJobs.ts:17:12, forms/c-call/bad.ts:8:76, forms/c-call/bad.ts:11:76, forms/c-call/bad.ts:14:86, forms/c-call/bad.ts:33:76, forms/c-call/app.ts:18:7

### probe.ts

- completions @errors-on-call [3]:
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
- completions @keys-empty [3]:
  - success (Field)
  - failure? => `failure` (Field)
  - errors (Field)
  - details of success:
    ```
    (property) success: (value: CopyPlan) => unknown
    ```
- completions @keys-after-success [2]:
  - failure? => `failure` (Field)
  - errors (Field)
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
- completions @keys-no-errors [2]:
  - success (Field)
  - failure? => `failure` (Field)
  - details of success:
    ```
    (property) success: (value: number) => unknown
    ```
- completions @members [2]:
  - match (Method)
  - unwrap (Method)
- hover @hover-success-param:
  ```
  ```typescript
  (parameter) p: CopyPlan
  ```
  
  ```
- definition @hover-success-param: forms/c-call/probe.ts:17:14 `success: p/*^hover-success-param*/ => p,`
- hover @hover-case-key:
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
- definition @hover-case-key: forms/c-call/copyJobs.ts:17:12 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @hover-case-key: canRename=true; 11 locations: forms/c-call/docs.ts:20:7, forms/c-call/probe.ts:14:90, forms/c-call/probe.ts:20:7, forms/c-call/probe.ts:28:44, forms/c-call/copyJobs.ts:17:12, forms/c-call/bad.ts:8:76, forms/c-call/bad.ts:11:76, forms/c-call/bad.ts:14:86, forms/c-call/bad.ts:33:76, forms/c-call/app.ts:18:7, forms/c-call/react.tsx:38:9
- hover @hover-case-param:
  ```
  ```typescript
  (parameter) e: {
      readonly connection: string;
      readonly table: string;
  }
  ```
  
  ```
- definition @hover-case-param: forms/c-call/probe.ts:20:40 `tableMissing/*^hover-case-key*/: e/*^hover-case-param*/ => e.table/*^definition-payload-field*/,`
- hover @definition-payload-field:
  ```
  ```typescript
  (property) table: string
  ```
  
  ```
- definition @definition-payload-field: forms/c-call/copyJobs.ts:17:66 `readonly tableMissing: { readonly connection: string; readonly table: string };`
- rename @definition-payload-field: canRename=true; 5 locations: forms/c-call/docs.ts:20:40, forms/c-call/probe.ts:20:68, forms/c-call/copyJobs.ts:17:66, forms/c-call/app.ts:18:38, forms/c-call/react.tsx:38:46
- hover @hover-failure-param:
  ```
  ```typescript
  (parameter) e: CallError
  ```
  
  ```
- definition @hover-failure-param: forms/c-call/probe.ts:23:14 `failure: e/*^hover-failure-param*/ => e.message,`
- hover @hover-match:
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
- definition @hover-match: forms/c-call/sdk.ts:16:3 `match<S, H extends ErrorHandlers<E> = ErrorHandlers<E>, F = undefined>(handlers: Handlers<T, E, S, H`
- hover @hover-inferred-result:
  ```
  ```typescript
  const shown: string | number | boolean | null | undefined
  ```
  
  ```
- definition @hover-inferred-result: forms/c-call/probe.ts:26:9 `const shown = planned.match/*^hover-match*/({`
