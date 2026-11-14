import { Component, StrictMode, Suspense, use, useEffect, useState, version, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider, useMutation, useQuery } from "@tanstack/react-query";
import { Call, SparError, type Result } from "@spar/app/sdk";
import { copyJobs, type CopyPlan, type PlanError } from "@spar/app/copyJobs";
import { count, log, script, scriptStream } from "@spar/app/testing";
import { delay, expect, finish, host, intercepted, plan, recipe, test, waitFor } from "../../shared/harness";
import logo from "../../shared/logo.svg";
import { PromiseCall } from "../../shared/promise-call";
import "./app.css";

const dev = import.meta.env.DEV;

function mount(node: ReactNode, strict = false) {
  const el = host();
  const root = createRoot(el);
  root.render(strict ? <StrictMode>{node}</StrictMode> : node);
  return { el, text: () => el.textContent ?? "", unmount: () => { root.unmount(); el.remove(); } };
}

class Boundary extends Component<{ children: ReactNode }, { error: unknown }> {
  override state = { error: undefined as unknown };
  static getDerivedStateFromError(error: unknown) { return { error }; }
  override render() {
    const e = this.state.error;
    if (e === undefined) return this.props.children;
    return <p>boundary: {e instanceof SparError ? `SparError ${String(e.error.code)}` : e instanceof Error ? `${e.name} ${e.message}` : String(e)}</p>;
  }
}

const describePlan = (result: Result<CopyPlan, PlanError>) => result.match({
  success: p => `planned ${p.steps.length}`,
  errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` },
  failure: e => `failure ${e.kind}`,
});

function PlanView({ call }: { readonly call: PromiseLike<Result<CopyPlan, PlanError>> }) {
  return <p>{describePlan(use(call))}</p>;
}

function PlanViewNoFailure({ call }: { readonly call: Call<CopyPlan, PlanError> }) {
  const shown = use(call).match({
    success: p => `planned ${p.steps.length}`,
    errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` },
  });
  return <p>shown={String(shown)}</p>;
}

let lastCall: Call<CopyPlan, PlanError> | undefined;
function Holder({ noFailure = false }: { readonly noFailure?: boolean }) {
  const [call] = useState(() => (lastCall = copyJobs.plan(recipe)));
  return <Suspense fallback={<p>loading</p>}>{noFailure ? <PlanViewNoFailure call={call} /> : <PlanView call={call} />}</Suspense>;
}

function UnwrapHolder() {
  const [promise] = useState(() => copyJobs.plan(recipe).unwrap());
  return <Boundary><Suspense fallback={<p>loading</p>}><UnwrapView promise={promise} /></Suspense></Boundary>;
}
function UnwrapView({ promise }: { readonly promise: Promise<CopyPlan> }) {
  return <p>planned {use(promise).steps.length}</p>;
}

function Uncached() {
  return <p>{describePlan(use(copyJobs.plan(recipe)))}</p>;
}
function UncachedPromise() {
  return <p>{describePlan(use(Promise.resolve(copyJobs.plan(recipe))))}</p>;
}

/** A Call made non-extensible, to see what use() needs from the thenable. */
class FrozenCall<T, E extends object> extends Call<T, E> {
  constructor(result: Promise<Result<T, E>>) { super(result); Object.freeze(this); }
}
/** The candidate SDK change for Vue: a Symbol.toStringTag on Call (and Result). */
class TaggedCall<T, E extends object> extends Call<T, E> {
  get [Symbol.toStringTag]() { return "SparCall"; }
}
function FrozenHolder({ tagged = false }: { readonly tagged?: boolean }) {
  const [call] = useState(() => {
    const inner = Promise.resolve(copyJobs.plan(recipe));
    return tagged ? new TaggedCall<CopyPlan, PlanError>(inner) : new FrozenCall<CopyPlan, PlanError>(inner);
  });
  return <Boundary><Suspense fallback={<p>loading</p>}><PlanView call={call} /></Suspense></Boundary>;
}

function QueryUnwrap({ id }: { readonly id: string }) {
  const q = useQuery({ queryKey: ["plan", id], queryFn: ({ signal }) => copyJobs.plan(recipe, { signal }).unwrap() });
  if (q.status === "pending") return <p>pending</p>;
  if (q.status === "error") return <p>error {q.error instanceof SparError ? String(q.error.error.code) : String(q.error)} failures={q.failureCount}</p>;
  return <p>planned {q.data.steps.length}</p>;
}

function QueryCall({ id }: { readonly id: string }) {
  // The Call itself as the query result: TanStack awaits it, so data is the Result at run time.
  const q = useQuery({ queryKey: ["planCall", id], queryFn: ({ signal }) => copyJobs.plan(recipe, { signal }) });
  if (q.status !== "success") return <p>{q.status}</p>;
  const data: unknown = q.data;
  return <p>{describePlan(data as Result<CopyPlan, PlanError>)} isCall={String(data instanceof Call)}</p>;
}

function QueryPromiseCall({ id }: { readonly id: string }) {
  // Candidate SDK change: with a Promise subclass, TanStack infers the Result as data (no cast).
  const q = useQuery({ queryKey: ["planPromiseCall", id], queryFn: ({ signal }) => PromiseCall.of(copyJobs.plan(recipe, { signal })) });
  return <p>{q.status === "success" ? describePlan(q.data) : q.status}</p>;
}

function PromiseCallHolder() {
  const [call] = useState(() => PromiseCall.of(copyJobs.plan(recipe)));
  return <Suspense fallback={<p>loading</p>}><PlanView call={call} /></Suspense>;
}

function QueryAwaited({ id }: { readonly id: string }) {
  // async + await: the data type is the Result, as at run time.
  const q = useQuery({ queryKey: ["planAwaited", id], queryFn: async ({ signal }) => await copyJobs.plan(recipe, { signal }) });
  return <p>{q.status === "success" ? describePlan(q.data) : q.status}</p>;
}

function QueryMatch({ id }: { readonly id: string }) {
  const q = useQuery({
    queryKey: ["planMatch", id],
    queryFn: ({ signal }) => copyJobs.plan(recipe, { signal }).match({
      success: p => p.steps.length,
      errors: { connectionNotFound: () => -1, tableMissing: () => -2 },
    }),
  });
  return <p>{q.status} {q.status === "error" ? q.error.message : String(q.data)}</p>;
}

function MutationRun() {
  const m = useMutation({ mutationFn: (planId: string) => copyJobs.run({ planId, truncateTarget: false }).unwrap() });
  useEffect(() => { m.mutate("p1"); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return <p>{m.status}</p>;
}

const history: string[] = [];

/** docs/app-model.md 6.2, unchanged apart from recording each status. */
function CopyProgressView({ jobId, ignoreAborted }: { readonly jobId: string; readonly ignoreAborted: boolean }) {
  const [rows, setRows] = useState(0n);
  const [status, setStatusState] = useState("connecting");
  const setStatus = (s: string) => { history.push(s); setStatusState(s); };

  useEffect(() => {
    const abort = new AbortController();
    void (async () => {
      for await (const item of copyJobs.progress(jobId, { signal: abort.signal })) {
        item.match({
          success: e => {
            switch (e.type) {
              case "event": setRows(e.value.rows); setStatus("live"); break;
              case "gap": setStatus(`${e.dropped} updates skipped`); break;
              case "reconnecting": setStatus(`reconnecting (attempt ${e.attempt})`); break;
              case "reset": setRows(0n); break;
              case "resumed": setStatus("live"); break;
            }
          },
          failure: e => { if (!ignoreAborted || !abort.signal.aborted) setStatus(e.message); },
        });
      }
    })();
    return () => abort.abort();
  }, [jobId]); // eslint-disable-line react-hooks/exhaustive-deps

  return <p aria-live="polite">{status}: {rows.toString()} rows</p>;
}

function UnaryEffect({ onProblem }: { readonly onProblem: (s: string) => void }) {
  const [total, setTotal] = useState<number>();
  useEffect(() => {
    const controller = new AbortController();
    void copyJobs.plan(recipe, { signal: controller.signal }).match({
      success: p => setTotal(p.steps.length),
      errors: { connectionNotFound: () => onProblem("nf"), tableMissing: () => onProblem("tm") },
      failure: e => { if (e.kind !== "cancelled") onProblem(e.message); },
    });
    return () => controller.abort();
  }, [onProblem]);
  return <p>total={String(total)}</p>;
}

function client(retry?: false) {
  return new QueryClient({ defaultOptions: { queries: { retryDelay: 1, ...(retry === false ? { retry } : {}) } } });
}

async function run(): Promise<void> {
  await test("environment", async () => ({ dev, react: version, logoIsUrl: !logo.startsWith("data:"), logo }));

  await test("use(call) suspends, then renders success", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 80 });
    const m = mount(<Holder />);
    await waitFor("fallback", () => m.text() === "loading", 1000, m.text);
    await waitFor("success", () => m.text() === "planned 1", 2000, m.text);
    const keys = Object.keys(lastCall!);
    m.unmount();
    return { starts: count("copyJobs.plan"), ownKeysAfterUse: keys };
  });

  await test("use(call) with a declared error renders it through match (no boundary)", async () => {
    script("copyJobs.plan", { kind: "error", code: "tableMissing", payload: { connection: "src", table: "dbo.Orders" }, delayMs: 20 });
    const m = mount(<Boundary><Holder /></Boundary>);
    await waitFor("error text", () => m.text() === "no table dbo.Orders", 2000, m.text);
    m.unmount();
  });

  await test("use(call) with an infrastructure failure and no failure handler: interceptor, undefined", async () => {
    script("copyJobs.plan", { kind: "failure", error: { kind: "transport", code: "lost", message: "bridge lost", execution: "unknown" }, delayMs: 20 });
    const m = mount(<Holder noFailure />);
    await waitFor("render", () => m.text().startsWith("shown="), 2000, m.text);
    const shown = m.text();
    m.unmount();
    expect(intercepted.length === 1, `interceptor calls ${intercepted.length}`);
    return { shown, intercepted: intercepted.map(e => e.code) };
  });

  await test("use(call.unwrap()) with a declared error reaches the error boundary", async () => {
    script("copyJobs.plan", { kind: "error", code: "connectionNotFound", payload: { connectionId: "src" }, delayMs: 20 });
    const m = mount(<UnwrapHolder />);
    await waitFor("boundary", () => m.text().startsWith("boundary"), 2000, m.text);
    const shown = m.text();
    m.unmount();
    expect(shown === "boundary: SparError connectionNotFound", shown);
  });

  for (const kind of ["call", "promise"] as const) {
    await test(`use() with a ${kind === "call" ? "Call" : "native Promise (baseline)"} created during render (uncached), inside Suspense`, async () => {
      script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
      const pageErrors: string[] = [];
      const onError = (e: ErrorEvent) => { pageErrors.push(e.message); };
      window.addEventListener("error", onError);
      const m = mount(<Boundary><Suspense fallback={<p>loading</p>}>{kind === "call" ? <Uncached /> : <UncachedPromise />}</Suspense></Boundary>);
      await delay(600);
      window.removeEventListener("error", onError);
      const shown = m.text();
      m.unmount();
      return { shown, transportStarts: count("copyJobs.plan"), pageErrors };
    });
  }

  await test("use() with a frozen Call (non-extensible thenable)", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
    const m = mount(<FrozenHolder />);
    await waitFor("render", () => m.text() !== "loading" && m.text() !== "", 2000, m.text);
    const shown = m.text();
    m.unmount();
    return { shown };
  });

  await test("use() with a Call carrying Symbol.toStringTag (candidate SDK change)", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 });
    const m = mount(<FrozenHolder tagged />);
    await waitFor("render", () => m.text() === "planned 1", 2000, m.text);
    m.unmount();
  });

  for (const retry of [undefined, false] as const) {
    await test(`TanStack queryFn unwrap, declared error, retry ${retry === false ? "false" : "default"}`, async () => {
      script("copyJobs.plan", { kind: "error", code: "tableMissing", payload: { connection: "src", table: "t" } });
      const qc = client(retry);
      const m = mount(<QueryClientProvider client={qc}><QueryUnwrap id={`u${String(retry)}`} /></QueryClientProvider>);
      await waitFor("error", () => m.text().startsWith("error"), 3000, m.text);
      const shown = m.text();
      m.unmount();
      qc.clear();
      return { shown, transportStarts: count("copyJobs.plan") };
    });
  }

  await test("TanStack queryFn unwrap, success", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 10 });
    const qc = client(false);
    const m = mount(<QueryClientProvider client={qc}><QueryUnwrap id="ok" /></QueryClientProvider>);
    await waitFor("data", () => m.text() === "planned 1", 2000, m.text);
    m.unmount();
    qc.clear();
  });

  await test("TanStack queryFn returns the Call: declared error is data, never retried", async () => {
    script("copyJobs.plan", { kind: "error", code: "tableMissing", payload: { connection: "src", table: "t" } });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><QueryCall id="c" /></QueryClientProvider>);
    await waitFor("data", () => m.text().startsWith("no table"), 2000, m.text);
    const shown = m.text();
    m.unmount();
    qc.clear();
    return { shown, transportStarts: count("copyJobs.plan") };
  });

  await test("TanStack queryFn async/await of the Call: Result data", async () => {
    script("copyJobs.plan", { kind: "success", value: plan });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><QueryAwaited id="a" /></QueryClientProvider>);
    await waitFor("data", () => m.text() === "planned 1", 2000, m.text);
    m.unmount();
    qc.clear();
  });

  await test("TanStack queryFn returns match() without failure, infrastructure failure", async () => {
    script("copyJobs.plan", { kind: "failure", error: { kind: "transport", code: "lost", message: "bridge lost", execution: "unknown" } });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><QueryMatch id="m" /></QueryClientProvider>);
    await waitFor("settled", () => !m.text().startsWith("pending"), 2000, m.text);
    await delay(100);
    const shown = m.text();
    m.unmount();
    qc.clear();
    return { shown, transportStarts: count("copyJobs.plan"), intercepted: intercepted.length };
  });

  await test("TanStack cancelQueries aborts the call through its signal, no retry, no error state", async () => {
    script("copyJobs.plan", { kind: "hang" });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><QueryUnwrap id="cancel" /></QueryClientProvider>);
    await waitFor("start", () => count("copyJobs.plan") === 1, 1000);
    await qc.cancelQueries({ queryKey: ["plan", "cancel"] });
    await delay(150);
    const state = qc.getQueryState(["plan", "cancel"]);
    const shown = m.text();
    m.unmount();
    qc.clear();
    expect(count("copyJobs.plan", "aborted") === 1, `aborted ${count("copyJobs.plan", "aborted")}`);
    return { shown, status: state?.status, fetchStatus: state?.fetchStatus, transportStarts: count("copyJobs.plan") };
  });

  await test("TanStack unmount of the only observer aborts a pending call", async () => {
    script("copyJobs.plan", { kind: "hang" });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><QueryUnwrap id="unmount" /></QueryClientProvider>);
    await waitFor("start", () => count("copyJobs.plan") === 1, 1000);
    m.unmount();
    await delay(50);
    qc.clear();
    expect(count("copyJobs.plan", "aborted") === 1, `aborted ${count("copyJobs.plan", "aborted")}`);
  });

  await test("TanStack useMutation unwrap, declared error: mutations do not retry by default", async () => {
    script("copyJobs.run", { kind: "error", code: "planOutdated", payload: { reason: "r" } });
    const qc = client();
    const m = mount(<QueryClientProvider client={qc}><MutationRun /></QueryClientProvider>);
    await waitFor("error", () => m.text() === "error", 2000, m.text);
    await delay(50);
    m.unmount();
    qc.clear();
    return { transportStarts: count("copyJobs.run") };
  });

  for (const ignoreAborted of [false, true]) {
    await test(`subscription in useEffect under StrictMode (${ignoreAborted ? "failure ignores own abort" : "docs/app-model.md 6.2 as written"})`, async () => {
      history.length = 0;
      scriptStream("copyJobs.progress",
        { kind: "success", value: { type: "event", value: { jobId: "j", rows: 1000n } }, delayMs: 40 },
        { kind: "success", value: { type: "gap", dropped: 3 }, delayMs: 10 },
        { kind: "success", value: { type: "reconnecting", attempt: 1 }, delayMs: 10 },
        { kind: "success", value: { type: "resumed" }, delayMs: 10 },
        { kind: "success", value: { type: "reset" }, delayMs: 10 },
        { kind: "success", value: { type: "event", value: { jobId: "j", rows: 2000n } }, delayMs: 10 },
        { kind: "hang" });
      const m = mount(<CopyProgressView jobId="j" ignoreAborted={ignoreAborted} />, true);
      await delay(5);
      const early = m.text();
      await waitFor("live", () => m.text() === "live: 2000 rows", 2000, m.text);
      const starts = count("copyJobs.progress"), abortedWhileMounted = count("copyJobs.progress", "aborted");
      m.unmount();
      await delay(20);
      return { early, history: [...history], starts, abortedWhileMounted, abortedAfterUnmount: count("copyJobs.progress", "aborted") };
    });
  }

  await test("unary call in useEffect under StrictMode: first call aborted, cancelled ignored", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 30 });
    const problems: string[] = [];
    const onProblem = (s: string) => { problems.push(s); };
    const m = mount(<UnaryEffect onProblem={onProblem} />, true);
    await waitFor("total", () => m.text() === "total=1", 2000, m.text);
    m.unmount();
    return { starts: count("copyJobs.plan"), aborted: count("copyJobs.plan", "aborted"), problems, intercepted: intercepted.length };
  });

  await test("candidate Promise-subclass Call: use() and TanStack (declared error is data, no retry)", async () => {
    script("copyJobs.plan", { kind: "success", value: plan, delayMs: 20 }, { kind: "error", code: "tableMissing", payload: { connection: "src", table: "t" } });
    const a = mount(<PromiseCallHolder />);
    await waitFor("use", () => a.text() === "planned 1", 2000, a.text);
    a.unmount();
    const qc = client();
    const b = mount(<QueryClientProvider client={qc}><QueryPromiseCall id="p" /></QueryClientProvider>);
    await waitFor("data", () => b.text() === "no table t", 2000, b.text);
    b.unmount();
    qc.clear();
    return { transportStarts: count("copyJobs.plan"), isPromise: PromiseCall.of(Promise.resolve(copyJobs.plan(recipe))) instanceof Promise };
  });

  await test("img asset from Vite with assetsInlineLimit 0", async () => {
    const img = document.createElement("img");
    img.src = logo;
    document.body.append(img);
    await new Promise<void>((resolve, reject) => { img.onload = () => resolve(); img.onerror = () => reject(new Error(`img failed: ${logo}`)); });
    return { src: logo };
  });

  window.__log = log;
  finish();
}

void run();
