import { ApplicationRef, Component, createComponent, VERSION, type Type } from "@angular/core";
import { from } from "rxjs";
import { copyJobs } from "@spar/app/copyJobs";
import { count, log, script, scriptStream } from "@spar/app/testing";
import { delay, expect, finish, host, plan, test, waitFor } from "../../shared/harness";
import {
  AsyncPipePlan, FieldPlan, PromiseCallPlan, FieldPromise, FieldRootZone, FieldThen, fromSpar, ProgressRx, ProgressSignal, ResourcePlan, ResourceUnwrap, SignalPlan,
} from "./components";

@Component({ selector: "app-root", template: `<p>root</p>` })
export class Root {}

function mount<C>(appRef: ApplicationRef, type: Type<C>, inputs: Record<string, unknown> = {}) {
  const el = host();
  const ref = createComponent(type, { environmentInjector: appRef.injector, hostElement: el });
  for (const [k, v] of Object.entries(inputs)) ref.setInput(k, v);
  appRef.attachView(ref.hostView);
  return { el, ref, text: () => el.textContent.trim(), destroy: () => { ref.destroy(); el.remove(); } };
}

const progressItems = () => scriptStream("copyJobs.progress",
  { kind: "success", value: { type: "event", value: { jobId: "j", rows: 1000n } }, delayMs: 10 },
  { kind: "success", value: { type: "gap", dropped: 2 }, delayMs: 10 },
  { kind: "success", value: { type: "reconnecting", attempt: 1 }, delayMs: 10 },
  { kind: "success", value: { type: "resumed" }, delayMs: 10 },
  { kind: "success", value: { type: "event", value: { jobId: "j", rows: 3000n } }, delayMs: 10 },
  { kind: "hang" });

export async function runTests(appRef: ApplicationRef, mode: string): Promise<void> {
  const zone = (globalThis as { Zone?: { current: { name: string } } }).Zone;
  await test("environment", async () => ({ mode, angular: VERSION.full, zoneLoaded: zone !== undefined, zone: zone?.current.name ?? null, globalStylesApplied: getComputedStyle(document.body).marginTop === "3px" }));

  for (const [name, type, waitMs] of [
    ["signal set after await of a Call (OnPush)", SignalPlan, 0],
    ["plain field set after await of a Call", FieldPlan, 300],
    ["baseline: plain field set after await of a native Promise", FieldPromise, 300],
    ["plain field set in a Call's then() callback", FieldThen, 300],
    ["plain field set after await of a Call settled outside the Angular zone", FieldRootZone, 300],
  ] as const) {
    await test(name, async () => {
      script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
      const m = mount(appRef, type as Type<unknown>);
      if (waitMs === 0) {
        await waitFor("planned", () => m.text() === "planned 1", 2000, m.text);
        m.destroy();
        return { shown: "planned 1" };
      }
      await delay(waitMs);
      const shown = m.text();
      m.destroy();
      return { shown, updated: shown === "planned 1" };
    });
  }

  await test("resource() with a Call loader; a params change aborts the previous call", async () => {
    script("copyJobs.plan", { kind: "hang" }, { kind: "error", code: "tableMissing", payload: { connection: "src", table: "dbo.Customers" }, delayMs: 20 });
    const m = mount(appRef, ResourcePlan);
    await waitFor("loading", () => m.text() === "loading", 1000, m.text);
    m.ref.instance.tables.set(["dbo.Customers"]);
    await waitFor("value", () => m.text() === "no table dbo.Customers", 2000, m.text);
    const status = m.ref.instance.plan.status();
    m.destroy();
    expect(count("copyJobs.plan", "aborted") === 1, `aborted ${count("copyJobs.plan", "aborted")}`);
    return { status, starts: count("copyJobs.plan") };
  });

  await test("resource() destroyed while loading aborts the call", async () => {
    script("copyJobs.plan", { kind: "hang" });
    const m = mount(appRef, ResourcePlan);
    await waitFor("start", () => count("copyJobs.plan") === 1, 1000);
    m.destroy();
    await delay(20);
    expect(count("copyJobs.plan", "aborted") === 1, `aborted ${count("copyJobs.plan", "aborted")}`);
  });

  await test("resource() with an unwrap loader and a declared error: error state", async () => {
    script("copyJobs.plan", { kind: "error", code: "connectionNotFound", payload: { connectionId: "src" }, delayMs: 10 });
    const m = mount(appRef, ResourceUnwrap);
    await waitFor("error", () => m.text().startsWith("error"), 2000, m.text);
    const shown = m.text();
    m.destroy();
    return { shown, starts: count("copyJobs.plan") };
  });

  await test("async pipe with a Call (thenable)", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
    const m = mount(appRef, AsyncPipePlan);
    await waitFor("value", () => m.text() === "planned 1", 2000, m.text);
    m.destroy();
  });

  await test("RxJS from() over a subscription: unsubscribe does not stop it until the next item", async () => {
    scriptStream("copyJobs.progress",
      { kind: "success", value: { type: "event", value: { jobId: "j", rows: 1n } }, delayMs: 10 },
      { kind: "success", value: { type: "event", value: { jobId: "j", rows: 2n } }, delayMs: 250 },
      { kind: "hang" });
    let received = 0;
    const sub = from(copyJobs.progress("j")).subscribe(() => { received++; });
    await waitFor("first item", () => received === 1, 1000);
    sub.unsubscribe();
    const unsubscribedAt = performance.now();
    await delay(100);
    const stoppedAfter100ms = count("copyJobs.progress", "returned") + count("copyJobs.progress", "aborted");
    await delay(300);
    const stop = log.find(e => e.call === "copyJobs.progress" && (e.event === "returned" || e.event === "aborted"));
    return { received, stoppedAfter100ms, stoppedAfterMs: stop ? Math.round(stop.at - unsubscribedAt) : null, how: stop?.event ?? "still running" };
  });

  await test("RxJS fromSpar() (AbortController in the teardown): unsubscribe aborts at once", async () => {
    scriptStream("copyJobs.progress", { kind: "success", value: { type: "event", value: { jobId: "j", rows: 1n } }, delayMs: 10 }, { kind: "hang" });
    let received = 0;
    const sub = fromSpar(signal => copyJobs.progress("j", { signal })).subscribe(() => { received++; });
    await waitFor("first item", () => received === 1, 1000);
    sub.unsubscribe();
    await delay(10);
    expect(count("copyJobs.progress", "aborted") === 1, `aborted ${count("copyJobs.progress", "aborted")}`);
    return { received };
  });

  await test("component with fromSpar() subscription and component styles; DestroyRef aborts", async () => {
    progressItems();
    const m = mount(appRef, ProgressRx, { jobId: "j" });
    await waitFor("live", () => m.text() === "live: 3000 rows", 2000, m.text);
    const color = getComputedStyle(m.el.querySelector("p")!).color;
    m.destroy();
    await delay(10);
    expect(count("copyJobs.progress", "aborted") === 1, `aborted ${count("copyJobs.progress", "aborted")}`);
    return { componentStyleApplied: color === "rgb(0, 0, 255)", color, styleElements: document.querySelectorAll("style").length };
  });

  await test("toSignal(fromSpar(...)) renders and aborts on destroy", async () => {
    progressItems();
    const m = mount(appRef, ProgressSignal);
    await waitFor("rows", () => m.text() === "rows 3000", 2000, m.text);
    m.destroy();
    await delay(10);
    expect(count("copyJobs.progress", "aborted") === 1, `aborted ${count("copyJobs.progress", "aborted")}`);
  });

  await test("candidate Promise-subclass Call: resource(), async pipe, awaited plain field", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
    const m = mount(appRef, PromiseCallPlan);
    await delay(300);
    const shown = m.text();
    m.destroy();
    return { shown };
  });

  finish();
}
