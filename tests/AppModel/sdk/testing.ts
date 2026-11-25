// Stand-in for `@spar/app/testing`: the public types of src/Spar.Client/src/testing.ts and the generated `fakeSpar`,
// over this check's stand-in `Call`. Only the types matter here: the document's tests are compiled, not run.
import type { Call } from "./c-call/sdk.js";
import type { CallError, ErrorCases } from "./core.js";

export type Command = (...args: never[]) => PromiseLike<unknown>;
export type RequestOf<F> = F extends (...args: infer A) => unknown ? (A extends [infer R, ...unknown[]] ? R : undefined) : never;
export type ValueOf<F> = F extends (...args: never[]) => Call<infer T, infer _E> ? T : never;
export type ErrorsOf<F> = F extends (...args: never[]) => Call<infer _T, infer E> ? E : never;
export type ValueArgs<T> = [T] extends [void] ? [] : [value: T];
export type PayloadArgs<P> = [P] extends [undefined] ? [] : [payload: P];
export type ScriptedFailure = Pick<CallError, "kind" | "code" | "execution">;

declare const replyBrand: unique symbol;

export interface Reply {
  readonly [replyBrand]: true;
}

export interface Replies<T, E extends ErrorCases> {
  succeed(...value: ValueArgs<T>): Reply;
  fail<K extends keyof E & string>(code: K, ...payload: PayloadArgs<E[K]>): Reply;
  failWith(error: ScriptedFailure): Reply;
  hang(): Reply;
}

export interface Script<R, T, E extends ErrorCases> {
  succeed(...value: ValueArgs<T>): void;
  fail<K extends keyof E & string>(code: K, ...payload: PayloadArgs<E[K]>): void;
  failWith(error: ScriptedFailure): void;
  hang(): void;
  answer(handler: (request: R, reply: Replies<T, E>) => Reply | Promise<Reply>): void;
}

export interface FakeBackendUpdate {
  readonly types?: readonly string[];
}

export interface FakeSpar {
  on<F extends Command>(command: F): Script<RequestOf<F>, ValueOf<F>, ErrorsOf<F>>;
  calls<F extends Command>(command: F): readonly RequestOf<F>[];
  backendUpdate(update?: FakeBackendUpdate): void;
}

export declare function fakeSpar(): FakeSpar;
