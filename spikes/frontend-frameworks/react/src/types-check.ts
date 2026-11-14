// Type-level evidence only (never imported by the page). Each `Equal` line must hold for `tsc` to pass.
import { QueryClient, queryOptions, useQuery, type QueryFunction } from "@tanstack/react-query";
import { use } from "react";
import type { Call, Result } from "@spar/app/sdk";
import { copyJobs, type CopyPlan, type PlanError } from "@spar/app/copyJobs";

type Equal<A, B> = (<X>() => X extends A ? 1 : 2) extends (<X>() => X extends B ? 1 : 2) ? true : false;
const assert = <T extends true>(_: T): void => {};
declare const recipe: Parameters<typeof copyJobs.plan>[0];

// queryFn returning the Call: TanStack's QueryFunction is `T | Promise<T>`, so T is inferred as the Call itself,
// although at run time TanStack awaits it and stores the Result.
export const callOptions = queryOptions({ queryKey: ["a"], queryFn: ({ signal }) => copyJobs.plan(recipe, { signal }) });
type FnData<O> = O extends { queryFn?: QueryFunction<infer T, infer _K, infer _P> | undefined } ? T : never;
assert<Equal<FnData<typeof callOptions>, Call<CopyPlan, PlanError>>>(true);

// async + await gives the Result, matching run time.
export const awaitedOptions = queryOptions({ queryKey: ["b"], queryFn: async ({ signal }) => await copyJobs.plan(recipe, { signal }) });
assert<Equal<Awaited<ReturnType<NonNullable<typeof awaitedOptions.queryFn>>>, Result<CopyPlan, PlanError>>>(true);

// unwrap gives the value.
export const unwrapOptions = queryOptions({ queryKey: ["c"], queryFn: ({ signal }) => copyJobs.plan(recipe, { signal }).unwrap() });
assert<Equal<Awaited<ReturnType<NonNullable<typeof unwrapOptions.queryFn>>>, CopyPlan>>(true);

export function useData() {
  const q = useQuery(callOptions);
  // Typed as Call, so match() here is typed as returning a Promise, but at run time q.data is a Result and match is synchronous.
  const typed = q.data?.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0, tableMissing: () => 0 } });
  assert<Equal<typeof typed, Promise<number | undefined> | undefined>>(true);
  return typed;
}

// React's use() unwraps a PromiseLike: the Call gives a Result.
export function useResult(call: Call<CopyPlan, PlanError>) {
  const r = use(call);
  assert<Equal<typeof r, Result<CopyPlan, PlanError>>>(true);
  return r;
}

export const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });

// Candidate SDK change: Call as a real Promise subclass. TanStack then infers the Result, as at run time.
declare class PromiseCall<T, E extends object = {}> extends Promise<Result<T, E>> {
  readonly match: Call<T, E>["match"];
  readonly unwrap: Call<T, E>["unwrap"];
}
declare const planAsPromise: (request: typeof recipe, options?: { signal?: AbortSignal }) => PromiseCall<CopyPlan, PlanError>;
export const promiseOptions = queryOptions({ queryKey: ["d"], queryFn: ({ signal }) => planAsPromise(recipe, { signal }) });
assert<Equal<FnData<typeof promiseOptions>, Result<CopyPlan, PlanError>>>(true);
