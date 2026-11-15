// Vue 3 and Pinia with the real Call and Result (the checks of spikes/frontend-frameworks, rebuilt on the SDK).
// Vue proxies objects in ref() and reactive(), and a proxy cannot read #private fields; Result and Call carry a
// Symbol.toStringTag, which Vue's reactive() takes as "not mine to proxy", so they stay raw and keep working.
import { createPinia, defineStore, setActivePinia } from "pinia";
import { beforeEach, describe, expect, test } from "vitest";
import { isProxy, reactive, ref, shallowRef } from "vue";
import type { Result } from "../../src/sdk.js";
import type { FakeHost } from "../fakeHost.js";
import { answerPlan, connectPage, settle } from "./host.js";
import { jobs, type Plan, type PlanErrors } from "./jobs.js";

let host: FakeHost;

beforeEach(() => {
  host = connectPage();
  setActivePinia(createPinia());
});

async function planResult(tables: number): Promise<Result<Plan, PlanErrors>> {
  const call = jobs.plan({ source: "src" });
  answerPlan(host, tables);
  return call;
}

const describePlan = (result: Result<Plan, PlanErrors>): string =>
  result.match({ success: plan => `${plan.tables} tables`, errors: { tableMissing: error => `no table ${error.table}` } });

describe("Vue", () => {
  test("ref() and reactive() keep a Result raw, so match reads its private state", async () => {
    const result = await planResult(3);

    const held = ref(result);
    const state = reactive({ result });

    expect(isProxy(held.value)).toBe(false);
    expect(held.value).toBe(result);
    expect(state.result).toBe(result);
    expect(describePlan(held.value as Result<Plan, PlanErrors>)).toBe("3 tables");
    expect(describePlan(state.result as Result<Plan, PlanErrors>)).toBe("3 tables");
  });

  test("a reactive array of results keeps each one raw", async () => {
    const items = ref<Result<Plan, PlanErrors>[]>([]);

    items.value.push(await planResult(1), await planResult(2));

    expect(items.value.map(item => describePlan(item as Result<Plan, PlanErrors>))).toEqual(["1 tables", "2 tables"]);
  });

  test("ref(call) keeps the Call awaitable", async () => {
    const held = ref(jobs.plan({ source: "src" }));
    answerPlan(host, 4);

    const result = await held.value;

    expect(isProxy(held.value)).toBe(false);
    expect(describePlan(result)).toBe("4 tables");
  });

  test("a Pinia store can hold a Result in its state", async () => {
    const usePlans = defineStore("plans", { state: () => ({ last: null as Result<Plan, PlanErrors> | null }) });
    const store = usePlans();

    store.last = await planResult(5);
    await settle();

    expect(describePlan(store.last as Result<Plan, PlanErrors>)).toBe("5 tables");
  });

  test("shallowRef keeps the Result's type as well, with no cast (the guidance for Vue)", async () => {
    const held = shallowRef(await planResult(6));

    const typed: Result<Plan, PlanErrors> = held.value;

    expect(describePlan(typed)).toBe("6 tables");
  });
});
