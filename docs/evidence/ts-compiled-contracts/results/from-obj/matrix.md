# tsc matrix

app = src/app.ts + src/main.ts (+ src/types.ts with strict): must have 0 errors. bad = src/bad.ts (11 mistakes).

| variant | tsc | flags | skipLibCheck | resolution | app | bad (count: codes) | ms (app) |
|---|---|---|---|---|---|---|---|
| ts | 7.0.2 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 76 |
| ts | 7.0.2 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 96 |
| ts | 7.0.2 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 233 |
| ts | 7.0.2 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 198 |
| ts | 7.0.2 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 68 |
| ts | 7.0.2 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 68 |
| ts | 7.0.2 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 220 |
| ts | 7.0.2 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 196 |
| ts | 6.0.3 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 1391 |
| ts | 6.0.3 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 575 |
| ts | 6.0.3 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 923 |
| ts | 6.0.3 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 881 |
| ts | 6.0.3 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 576 |
| ts | 6.0.3 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 578 |
| ts | 6.0.3 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 816 |
| ts | 6.0.3 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 826 |
| js | 7.0.2 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 59 |
| js | 7.0.2 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 60 |
| js | 7.0.2 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 175 |
| js | 7.0.2 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 178 |
| js | 7.0.2 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 59 |
| js | 7.0.2 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 60 |
| js | 7.0.2 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 176 |
| js | 7.0.2 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 195 |
| js | 6.0.3 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 510 |
| js | 6.0.3 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 542 |
| js | 6.0.3 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 858 |
| js | 6.0.3 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 837 |
| js | 6.0.3 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 527 |
| js | 6.0.3 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 512 |
| js | 6.0.3 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 884 |
| js | 6.0.3 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 934 |
| js-csmap | 7.0.2 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 96 |
| js-csmap | 7.0.2 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 82 |
| js-csmap | 7.0.2 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 220 |
| js-csmap | 7.0.2 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 255 |
| js-csmap | 7.0.2 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 97 |
| js-csmap | 7.0.2 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 90 |
| js-csmap | 7.0.2 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 256 |
| js-csmap | 7.0.2 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 243 |
| js-csmap | 6.0.3 | permissive | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 760 |
| js-csmap | 6.0.3 | permissive | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 638 |
| js-csmap | 6.0.3 | permissive | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 1197 |
| js-csmap | 6.0.3 | permissive | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 1114 |
| js-csmap | 6.0.3 | strictest | true | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 730 |
| js-csmap | 6.0.3 | strictest | true | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 784 |
| js-csmap | 6.0.3 | strictest | false | bundler | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 1368 |
| js-csmap | 6.0.3 | strictest | false | nodenext | 0 | 11: TS2304 TS2322 TS2339 TS2345 TS2353 TS2551 TS2561 TS2739 TS2741 | 931 |

## Fault injection

One fault the generator might emit, injected into one generated module; strictest settings, bundler resolution.
`src/fault.ts` assigns the result of `copyJobs.plan(…)` to a `number`, so its TS2322 shows the type the app sees.

| variant | fault | tsc | skipLibCheck | other errors | type the app sees |
|---|---|---|---|---|---|
| ts | unused local in the generated .ts | 7.0.2 | true | TS6133 | `Call<CopyPlan, PlanError>` |
| ts | unused local in the generated .ts | 7.0.2 | false | TS6133 | `Call<CopyPlan, PlanError>` |
| ts | unused local in the generated .ts | 6.0.3 | true | TS6133 | `Call<CopyPlan, PlanError>` |
| ts | unused local in the generated .ts | 6.0.3 | false | TS6133 | `Call<CopyPlan, PlanError>` |
| ts | undeclared type in the generated .ts | 7.0.2 | true | TS2552 | `Call<CopyPlanX, PlanError>` |
| ts | undeclared type in the generated .ts | 7.0.2 | false | TS2552 | `Call<CopyPlanX, PlanError>` |
| ts | undeclared type in the generated .ts | 6.0.3 | true | TS2552 | `Call<CopyPlanX, PlanError>` |
| ts | undeclared type in the generated .ts | 6.0.3 | false | TS2552 | `Call<CopyPlanX, PlanError>` |
| js | undeclared type in the generated .d.ts | 7.0.2 | true | none | `Call<CopyPlanX, PlanError>` |
| js | undeclared type in the generated .d.ts | 7.0.2 | false | TS2552 | `Call<CopyPlanX, PlanError>` |
| js | undeclared type in the generated .d.ts | 6.0.3 | true | none | `Call<CopyPlanX, PlanError>` |
| js | undeclared type in the generated .d.ts | 6.0.3 | false | TS2552 | `Call<CopyPlanX, PlanError>` |

## Details

### bad.ts: ts 7.0.2 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: ts 7.0.2 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: ts 6.0.3 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: ts 6.0.3 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js 7.0.2 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js 7.0.2 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js 6.0.3 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js 6.0.3 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js-csmap 7.0.2 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js-csmap 7.0.2 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2741: Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js-csmap 6.0.3 permissive
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### bad.ts: js-csmap 6.0.3 strictest
```
src/bad.ts(8,37): error TS2741: Property 'columnsMismatch' is missing in type '{ connectionNotFound: () => number; tableMissing: () => number; }' but required in type 'ErrorHandlers<PlanError>'.
src/bad.ts(11,99): error TS2561: Object literal may only specify known properties, but 'columnMismatch' does not exist in type 'ErrorHandlers<PlanError>'. Did you mean to write 'columnsMismatch'?
src/bad.ts(14,74): error TS2551: Property 'connection' does not exist on type '{ readonly connectionId: string; }'. Did you mean 'connectionId'?
src/bad.ts(17,17): error TS2345: Argument of type '{ success: () => number; }' is not assignable to parameter of type 'Handlers<CopyPlan, PlanError, number, ErrorHandlers<PlanError>, undefined>'.   Property 'errors' is missing in type '{ success: () => number; }' but required in type '{ readonly errors: ErrorHandlers<PlanError>; }'.
src/bad.ts(20,23): error TS2339: Property 'value' does not exist on type 'Result<CopyPlan, PlanError>'.
src/bad.ts(23,51): error TS2353: Object literal may only specify known properties, and 'errors' does not exist in type '{ readonly success: (value: number) => number; readonly failure?: (error: CallError) => undefined; }'.
src/bad.ts(26,32): error TS2345: Argument of type '{ failure: () => number; }' is not assignable to parameter of type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.   Property 'success' is missing in type '{ failure: () => number; }' but required in type '{ readonly success: (value: number) => unknown; readonly failure?: (error: CallError) => number; }'.
src/bad.ts(29,9): error TS2322: Type 'Promise<number | undefined>' is not assignable to type 'number'.
src/bad.ts(33,125): error TS2322: Type '() => number' is not assignable to type 'never'.
src/bad.ts(36,17): error TS2304: Cannot find name 'CopyReport'.
src/bad.ts(40,37): error TS2739: Type '{}' is missing the following properties from type 'ErrorHandlers<PlanError>': connectionNotFound, tableMissing, columnsMismatch
```
### fault: unused local in the generated .ts, 7.0.2, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlan, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(66,7): error TS6133: 'unusedGeneratedLocal' is declared but its value is never read.
```
### fault: unused local in the generated .ts, 7.0.2, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlan, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(66,7): error TS6133: 'unusedGeneratedLocal' is declared but its value is never read.
```
### fault: unused local in the generated .ts, 6.0.3, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlan, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(66,7): error TS6133: 'unusedGeneratedLocal' is declared but its value is never read.
```
### fault: unused local in the generated .ts, 6.0.3, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlan, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(66,7): error TS6133: 'unusedGeneratedLocal' is declared but its value is never read.
```
### fault: undeclared type in the generated .ts, 7.0.2, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(47,60): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
### fault: undeclared type in the generated .ts, 7.0.2, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(47,60): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
### fault: undeclared type in the generated .ts, 6.0.3, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(47,60): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
### fault: undeclared type in the generated .ts, 6.0.3, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.ts(47,60): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
### fault: undeclared type in the generated .d.ts, 7.0.2, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
```
### fault: undeclared type in the generated .d.ts, 7.0.2, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.d.ts(47,71): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
### fault: undeclared type in the generated .d.ts, 6.0.3, skipLibCheck=true
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
```
### fault: undeclared type in the generated .d.ts, 6.0.3, skipLibCheck=false
```
src/fault.ts(2,14): error TS2322: Type 'Call<CopyPlanX, PlanError>' is not assignable to type 'number'.
../obj/spar/client/copyJobs.d.ts(47,71): error TS2552: Cannot find name 'CopyPlanX'. Did you mean 'CopyPlan'?
```
