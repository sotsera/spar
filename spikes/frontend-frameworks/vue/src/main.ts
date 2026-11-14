import { createApp, defineComponent, h, isProxy, isReactive, markRaw, reactive, ref, shallowRef, Suspense, toRaw, version, type Component } from "vue";
import { createPinia, defineStore, setActivePinia } from "pinia";
import { Call, Result } from "@spar/app/sdk";
import { copyJobs, type CopyPlan, type PlanError, type PreviewRow } from "@spar/app/copyJobs";
import { count, script, scriptStream, successResult, type Outcome } from "@spar/app/testing";
import { delay, expect, finish, host, plan, recipe, test, waitFor } from "../../shared/harness";
import logo from "../../shared/logo.svg";
import PlanRef from "./components/PlanRef.vue";
import AsyncPlan from "./components/AsyncPlan.vue";
import ProgressView from "./components/ProgressView.vue";
import StyleBind from "./components/StyleBind.vue";

const show = (r: Pick<Result<CopyPlan, PlanError>, "match">) => r.match({
  success: p => `planned ${p.steps.length}`,
  errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` },
  failure: e => `failure ${e.kind}`,
});

/** Runs `body`; returns "ok: <value>" or the thrown error, so a test can record what happened. */
async function attempt(body: () => unknown): Promise<string> {
  try { return `ok: ${String(await body())}`; } catch (e) { return e instanceof Error ? `${e.name}: ${e.message}` : String(e); }
}

const ok: Outcome = { ok: true, value: plan };

// Candidate SDK changes, as subclasses of the unchanged prototype.
class TaggedResult<T, E extends object = {}> extends Result<T, E> {
  get [Symbol.toStringTag]() { return "SparResult"; }
}
class TaggedCall<T, E extends object = {}> extends Call<T, E> {
  get [Symbol.toStringTag]() { return "SparCall"; }
}
class FrozenResult<T, E extends object = {}> extends Result<T, E> {
  constructor(outcome: Outcome) { super(outcome); Object.freeze(this); }
}
class PromiseCall<T, E extends object = {}> extends Promise<Result<T, E>> {
  static override get [Symbol.species]() { return Promise; }
  match(handlers: never): Promise<unknown> { return this.then(r => r.match(handlers)); }
}

function mount(component: Component, props: Record<string, unknown> = {}, wrapSuspense = false) {
  const el = host();
  const errors: string[] = [];
  const app = createApp(wrapSuspense
    ? defineComponent({ setup: () => () => h(Suspense, null, { default: () => h(component, props), fallback: () => h("p", "loading") }) })
    : defineComponent({ setup: () => () => h(component, props) }));
  app.config.errorHandler = e => { errors.push(e instanceof Error ? `${e.name}: ${e.message}` : String(e)); };
  app.use(createPinia());
  app.mount(el);
  return { el, errors, text: () => el.textContent, unmount: () => { app.unmount(); el.remove(); } };
}

async function run(): Promise<void> {
  await test("environment", async () => ({ vue: version, prod: import.meta.env.PROD, logo }));

  await test("ref(result).value.match: Vue proxies the Result, #private read fails", async () => {
    const r = ref(successResult(plan));
    return { isReactive: isReactive(r.value), match: await attempt(() => r.value.match({ success: p => p.steps.length })), unwrap: await attempt(() => r.value.unwrap().steps.length) };
  });

  await test("reactive({ result }).result.match", async () => {
    const state = reactive({ result: successResult(plan) });
    return { match: await attempt(() => state.result.match({ success: p => p.steps.length })) };
  });

  await test("ref(call): await and match through the proxy", async () => {
    script("copyJobs.plan", { kind: "success", value: plan });
    const r = ref(copyJobs.plan(recipe));
    return { isProxy: isProxy(r.value), await: await attempt(async () => show(await r.value)), match: await attempt(() => r.value.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0, tableMissing: () => 0 } })) };
  });

  await test("fixes without SDK change: shallowRef, markRaw, toRaw", async () => {
    const a = shallowRef(successResult(plan));
    const b = ref(markRaw(successResult(plan)));
    const c = ref(successResult(plan));
    return {
      shallowRef: await attempt(() => a.value.match({ success: p => p.steps.length })),
      markRaw: await attempt(() => b.value.match({ success: p => p.steps.length })),
      toRaw: await attempt(() => toRaw(c.value).match({ success: p => p.steps.length })),
    };
  });

  await test("stream items pushed into ref([]) and read back", async () => {
    scriptStream("copyJobs.preview", { kind: "success", value: { cells: ["a", null] } }, { kind: "success", value: { cells: ["b", null] } });
    const items = ref<Result<PreviewRow>[]>([]);
    const raw = shallowRef<readonly Result<PreviewRow>[]>([]);
    const values = ref<PreviewRow[]>([]);
    for await (const item of copyJobs.preview(recipe)) {
      items.value.push(item);
      raw.value = [...raw.value, item];
      item.match({ success: row => { values.value.push(row); } }); // match on arrival, keep plain data
    }
    return {
      refArray: await attempt(() => items.value[0]?.match({ success: r => r.cells[0] })),
      shallowRefArray: await attempt(() => raw.value[0]?.match({ success: r => r.cells[0] })),
      plainValues: values.value.map(v => v.cells[0]).join(","),
    };
  });

  await test("candidate SDK changes in ref()/reactive(): Symbol.toStringTag, frozen Result, Promise subclass", async () => {
    script("copyJobs.plan", { kind: "success", value: plan });
    const tagged = ref(new TaggedResult<CopyPlan, PlanError>(ok));
    const inArray = reactive([new TaggedResult<CopyPlan, PlanError>(ok)]);
    const taggedCall = ref(new TaggedCall<CopyPlan, PlanError>(Promise.resolve(copyJobs.plan(recipe))));
    const frozen = ref(new FrozenResult<CopyPlan, PlanError>(ok));
    const promiseCall = ref(new PromiseCall<CopyPlan, PlanError>(resolve => { resolve(copyJobs.plan(recipe)); }));
    return {
      tagged: `${String(isReactive(tagged.value))} ${await attempt(() => show(tagged.value))}`,
      taggedInReactiveArray: await attempt(() => show(inArray[0]!)),
      taggedCallAwait: await attempt(async () => show(await taggedCall.value)),
      taggedCallMatch: await attempt(() => taggedCall.value.match({ success: p => p.steps.length, errors: { connectionNotFound: () => 0, tableMissing: () => 0 } })),
      frozen: `${String(isReactive(frozen.value))} ${await attempt(() => show(frozen.value))}`,
      promiseSubclassAwait: `${String(isProxy(promiseCall.value))} ${await attempt(async () => show(await promiseCall.value))}`,
    };
  });

  await test("Pinia: a Result in an option store's state, markRaw, and a setup store with shallowRef", async () => {
    setActivePinia(createPinia());
    const usePlans = defineStore("plans", { state: () => ({ last: undefined as Result<CopyPlan, PlanError> | undefined }) });
    const useSetup = defineStore("plansSetup", () => ({ last: shallowRef<Result<CopyPlan, PlanError>>() }));
    const tagged = defineStore("plansTagged", { state: () => ({ last: undefined as Result<CopyPlan, PlanError> | undefined }) });
    const s = usePlans(), t = useSetup(), u = tagged();
    s.last = successResult(plan);
    const plain = await attempt(() => s.last && show(s.last));
    s.last = markRaw(successResult(plan));
    const raw = await attempt(() => s.last && show(s.last));
    t.last = successResult(plan);
    u.last = new TaggedResult<CopyPlan, PlanError>(ok);
    return { optionStore: plain, optionStoreMarkRaw: raw, setupStoreShallowRef: await attempt(() => t.last && show(t.last)), optionStoreTagged: await attempt(() => u.last && show(u.last)) };
  });

  for (const mode of ["ref", "shallowRef"] as const) {
    await test(`component: Result in ${mode}, rendered with match in the template`, async () => {
      script("copyJobs.plan", { kind: "success", value: plan, delayMs: 10 });
      const m = mount(PlanRef, { mode });
      await waitFor("render or error", () => m.text() === "planned 1" || m.errors.length > 0, 2000, m.text);
      const shown = m.text();
      m.unmount();
      return { shown, errors: m.errors };
    });
  }

  await test("<Suspense> with top-level await of a Call in <script setup>", async () => {
    script("copyJobs.plan", { kind: "error", code: "tableMissing", payload: { connection: "src", table: "dbo.Orders" }, delayMs: 50 });
    let unmounted = false;
    const m = mount(AsyncPlan, { onUnmounted: () => { unmounted = true; } }, true);
    await waitFor("fallback", () => m.text() === "loading", 1000, m.text);
    await waitFor("text", () => m.text() === "no table dbo.Orders", 2000, m.text);
    m.unmount();
    expect(unmounted, "onUnmounted registered after the await did not run");
    return { errors: m.errors };
  });

  await test("composables: subscription and call end with the component (onScopeDispose aborts)", async () => {
    script("copyJobs.plan", { kind: "hang" });
    scriptStream("copyJobs.progress",
      { kind: "success", value: { type: "event", value: { jobId: "j", rows: 1000n } }, delayMs: 10 },
      { kind: "success", value: { type: "gap", dropped: 2 }, delayMs: 10 },
      { kind: "success", value: { type: "reconnecting", attempt: 1 }, delayMs: 10 },
      { kind: "success", value: { type: "resumed" }, delayMs: 10 },
      { kind: "success", value: { type: "reset" }, delayMs: 10 },
      { kind: "success", value: { type: "event", value: { jobId: "j", rows: 3000n } }, delayMs: 10 },
      { kind: "hang" });
    const m = mount(ProgressView, { jobId: "j" });
    await waitFor("live", () => m.text() === "live: 3000 rows; planning", 2000, m.text);
    m.unmount();
    await delay(20);
    return { progressAborted: count("copyJobs.progress", "aborted"), planAborted: count("copyJobs.plan", "aborted"), errors: m.errors };
  });

  await test(":style string binding and v-bind() in <style> under the CSP", async () => {
    const m = mount(StyleBind);
    await delay(20);
    const p = m.el.querySelector("p")!;
    const style = getComputedStyle(p);
    const shown = { color: style.color, borderTopWidth: style.borderTopWidth };
    m.unmount();
    expect(shown.color === "rgb(255, 0, 0)" && shown.borderTopWidth === "3px", JSON.stringify(shown));
    return shown;
  });

  finish();
}

void run();
