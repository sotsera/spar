// Which `match` signature infers mixed handler returns and still reports good diagnostics?
interface CallError { readonly message: string }
interface PlanError {
  readonly connectionNotFound: { readonly connectionId: string };
  readonly tableMissing: { readonly connection: string; readonly table: string };
  readonly columnsMismatch: { readonly table: string };
}
type Fn = (...args: never[]) => unknown;
type Ret<F> = F extends (...args: never[]) => infer R ? R : never;

// V3: reverse mapped type over the handlers' return types.
declare function v3<E, S, R extends { readonly [K in keyof E]: unknown }, F = undefined>(
  e: E,
  handlers: { readonly success: (v: number) => S; readonly errors: { readonly [K in keyof R]: (error: E[K & keyof E]) => R[K] } & { readonly [K in Exclude<keyof R, keyof E>]: never }; readonly failure?: (e: CallError) => F }): S | R[keyof R] | F;

// V4: the errors object as one type parameter, extra keys mapped to never.
type ErrorHandlers<E> = { readonly [K in keyof E]: (error: E[K]) => unknown };
declare function v4<E, S, H extends ErrorHandlers<E>, F = undefined>(
  e: E,
  handlers: { readonly success: (v: number) => S; readonly errors: H & { readonly [K in Exclude<keyof H, keyof E>]: never }; readonly failure?: (e: CallError) => F }): S | { [K in keyof H]: Ret<H[K]> }[keyof H] | F;

// V5: like V4 with `const` and NoInfer on the exactness part.
declare function v5<E, S, const H extends ErrorHandlers<E>, F = undefined>(
  e: E,
  handlers: { readonly success: (v: number) => S; readonly errors: H & NoInfer<{ readonly [K in Exclude<keyof H, keyof E>]: never }>; readonly failure?: (e: CallError) => F }): S | { [K in keyof H]: Ret<H[K]> }[keyof H] | F;

declare const pe: PlanError;
type Equal<A, B> = (<V>() => V extends A ? 1 : 2) extends (<V>() => V extends B ? 1 : 2) ? true : false;
function expectType<A, B>(_: Equal<A, B>): void {}

const m3 = v3(pe, { success: n => n, errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table.length > 0, columnsMismatch: () => null } });
expectType<typeof m3, number | string | boolean | null | undefined>(true);
const m4 = v4(pe, { success: n => n, errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table.length > 0, columnsMismatch: () => null } });
expectType<typeof m4, number | string | boolean | null | undefined>(true);
const m5 = v5(pe, { success: n => n, errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table.length > 0, columnsMismatch: () => null } });
expectType<typeof m5, number | string | boolean | null | undefined>(true);
const f5 = v5(pe, { success: n => n, errors: { connectionNotFound: () => 1, tableMissing: () => 2, columnsMismatch: () => 3 }, failure: () => "x" });
expectType<typeof f5, number | string>(true);

// missing
v3(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });
v4(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });
v5(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3 } });
// typo
v3(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });
v4(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });
v5(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatch: () => 4 } });
// typo plus missing
v5(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnMismatchh: () => 4 } });
// wrong field
v5(pe, { success: () => 1, errors: { connectionNotFound: e => e.connection, tableMissing: () => 3, columnsMismatch: () => 4 } });
// forgotten
v5(pe, { success: () => 1 });

// V6: V5 without `const`.
declare function v6<E, S, H extends ErrorHandlers<E>, F = undefined>(
  e: E,
  handlers: { readonly success: (v: number) => S; readonly errors: H & NoInfer<{ readonly [K in Exclude<keyof H, keyof E>]: never }>; readonly failure?: (e: CallError) => F }): S | { [K in keyof H]: Ret<H[K]> }[keyof H] | F;
const m6 = v6(pe, { success: n => n, errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table.length > 0, columnsMismatch: () => null } });
expectType<typeof m6, number | string | boolean | null | undefined>(true);
// V7: V5 without the exactness part (does excess checking come for free?).
declare function v7<E, S, const H extends ErrorHandlers<E>, F = undefined>(
  e: E,
  handlers: { readonly success: (v: number) => S; readonly errors: H; readonly failure?: (e: CallError) => F }): S | { [K in keyof H]: Ret<H[K]> }[keyof H] | F;
const m7 = v7(pe, { success: n => n, errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table.length > 0, columnsMismatch: () => null } });
expectType<typeof m7, number | string | boolean | null | undefined>(true);
v7(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnsMismatch: () => 3, columnMismatch: () => 4 } });
v5(pe, { success: () => 1, errors: { connectionNotFound: () => 2, tableMissing: () => 3, columnsMismatch: () => 3, columnMismatch: () => 4 } });
