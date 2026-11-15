// Type checks of the real Call and Result in the frameworks' own types (the checks of spikes/frontend-frameworks,
// rebuilt on the SDK). Compiled by `npm run check` with TypeScript 6 and 7; never run.
import type { ResourceLoaderParams, ResourceRef } from "@angular/core";
import type { UseQueryResult } from "@tanstack/react-query";
import { useQuery } from "@tanstack/react-query";
import type { Call, Result } from "../../src/sdk.js";
import { jobs, type Plan, type PlanErrors } from "./jobs.js";

type Equal<A, B> = (<T>() => T extends A ? 1 : 2) extends <T>() => T extends B ? 1 : 2 ? true : false;

const expectType = <T extends true>(): T => true as T;

/** A Call is a Promise: typescript-eslint's no-floating-promises flags a forgotten call with its default settings. */
export function aCallIsAPromise(call: Call<Plan, PlanErrors>): Promise<Result<Plan, PlanErrors>> {
  return call;
}

/** TanStack Query infers data from the Call as the Result the page matches, as at run time. */
export function useLatestPlan(): UseQueryResult<Result<Plan, PlanErrors>> {
  const query = useQuery({ queryKey: ["plan"], queryFn: ({ signal }) => jobs.plan({ source: "src" }, { signal }) });
  expectType<Equal<typeof query.data, Result<Plan, PlanErrors> | undefined>>();
  return query;
}

/** Angular's resource() takes a loader that returns a Call (it accepts any PromiseLike). */
export type PlanLoader = (params: ResourceLoaderParams<{ source: string }>) => PromiseLike<Result<Plan, PlanErrors>>;

export const planLoader: PlanLoader = ({ params, abortSignal }) => jobs.plan(params, { signal: abortSignal });

export type PlanResource = ResourceRef<Result<Plan, PlanErrors> | undefined>;
