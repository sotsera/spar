// @vitest-environment happy-dom
// Angular with the real Call and Result (the checks of docs/evidence/frontend-frameworks, rebuilt on the SDK): resource()
// takes a Call as its loader, passes its abort signal to the call, and aborts the previous call when its params change.
// Angular's packages ship partially compiled; outside the Angular CLI they need the JIT compiler loaded first.
import "@angular/compiler";
import { ApplicationRef, provideZonelessChangeDetection, resource, runInInjectionContext, signal, type EnvironmentInjector, type ResourceRef } from "@angular/core";
import { createApplication } from "@angular/platform-browser";
import { beforeEach, describe, expect, test } from "vitest";
import type { Result } from "../../src/sdk.js";
import type { FakeHost } from "../fakeHost.js";
import { answerPlan, connectPage, settle } from "./host.js";
import { jobs, type Plan, type PlanErrors } from "./jobs.js";

let host: FakeHost;

beforeEach(() => {
  host = connectPage();
});

/** A zoneless standalone application with no component: its root injector is where an app's resources live. */
async function injector(): Promise<EnvironmentInjector> {
  const application = await createApplication({ providers: [provideZonelessChangeDetection()] });
  return application.injector;
}

/** Runs the effects resource() relies on, as the app's change detection would. */
async function flush(root: EnvironmentInjector): Promise<void> {
  await settle();
  root.get(ApplicationRef).tick();
  await settle();
}

describe("Angular", () => {
  test("resource() loads through a Call and holds the Result", async () => {
    const root = await injector();
    const plans: ResourceRef<Result<Plan, PlanErrors> | undefined> = runInInjectionContext(root, () =>
      resource({ loader: ({ abortSignal }) => jobs.plan({ source: "src" }, { signal: abortSignal }) }));

    await flush(root);
    answerPlan(host, 3);
    await flush(root);

    const result = plans.value();
    expect(result?.match({ success: plan => plan.tables, errors: { tableMissing: () => -1 } })).toBe(3);
  });

  test("a change of params aborts the previous call, which the page cancels with the host", async () => {
    const root = await injector();
    const source = signal("first");
    runInInjectionContext(root, () =>
      resource({ params: () => ({ source: source() }), loader: ({ params, abortSignal }) => jobs.plan(params, { signal: abortSignal }) }));

    await flush(root);
    const firstCall = host.last("call")!.id;
    source.set("second");
    await flush(root);

    expect(host.sent.some(message => message.type === "cancel" && message.id === firstCall)).toBe(true);
    expect(host.last("call")!.payload).toBe("{\"source\":\"second\"}");
  });
});
