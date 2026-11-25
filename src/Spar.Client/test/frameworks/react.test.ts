// @vitest-environment happy-dom
// React 19 with the real Call and Result (decision "Opaque results in TypeScript"; the checks of
// docs/evidence/frontend-frameworks, rebuilt on the SDK): use(call) suspends and resolves, declared errors render through
// match with no error boundary, and unwrap hands the boundary a SparError.
import { Component, Suspense, act, createElement as h, use, type ReactNode } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { SparError, type Call } from "../../src/sdk.js";
import type { FakeHost } from "../fakeHost.js";
import { answerPlan, answerTableMissing, connectPage } from "./host.js";
import { jobs, type Plan, type PlanErrors } from "./jobs.js";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

let host: FakeHost;
let container: HTMLElement;
let root: Root;

beforeEach(() => {
  host = connectPage();
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

/** Reads the call with use() and renders it through match. */
function PlanView({ call }: { readonly call: Call<Plan, PlanErrors> }): ReactNode {
  const result = use(call);
  return h("p", null, result.match({
    success: plan => `${plan.tables} tables`,
    errors: { tableMissing: error => `no table ${error.table}` },
  }));
}

/** Reads unwrap's promise with use(): a declared error is thrown to the boundary. */
function UnwrappedView({ promise }: { readonly promise: Promise<Plan> }): ReactNode {
  return h("p", null, `${use(promise).tables} tables`);
}

class Boundary extends Component<{ readonly children: ReactNode }, { readonly error?: unknown }> {
  override state: { readonly error?: unknown } = {};

  static getDerivedStateFromError(error: unknown): { readonly error: unknown } {
    return { error };
  }

  override render(): ReactNode {
    const { error } = this.state;
    if (error === undefined) {
      return this.props.children;
    }

    return h("p", null, error instanceof SparError ? `boundary: ${(error.error as { code: string }).code}` : "boundary: other");
  }
}

async function render(node: ReactNode): Promise<void> {
  await act(async () => root.render(h(Suspense, { fallback: h("p", null, "loading") }, node)));
}

describe("React", () => {
  test("use(call) suspends, then shows the value through match, with one backend call", async () => {
    const call = jobs.plan({ source: "src" });

    await render(h(PlanView, { call }));
    expect(container.textContent).toBe("loading");

    await act(async () => answerPlan(host, 3));

    expect(container.textContent).toBe("3 tables");
    expect(host.sent.filter(m => m.type === "call")).toHaveLength(1);
  });

  test("a declared error renders through match, with no error boundary", async () => {
    const call = jobs.plan({ source: "src" });

    await render(h(Boundary, null, h(PlanView, { call })));
    await act(async () => answerTableMissing(host, "dbo.Orders"));

    expect(container.textContent).toBe("no table dbo.Orders");
  });

  test("use(call.unwrap()) hands a declared error to the error boundary as a SparError", async () => {
    const promise = jobs.plan({ source: "src" }).unwrap();
    promise.catch(() => undefined);

    await render(h(Boundary, null, h(UnwrappedView, { promise })));
    await act(async () => answerTableMissing(host, "dbo.Orders"));

    expect(container.textContent).toBe("boundary: tableMissing");
  });

  test("a Call is a Promise React can annotate: it is not frozen", () => {
    const call = jobs.plan({ source: "src" });

    expect(call).toBeInstanceOf(Promise);
    expect(Object.isFrozen(call)).toBe(false);
  });
});
