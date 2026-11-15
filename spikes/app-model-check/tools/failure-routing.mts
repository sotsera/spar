// Run-time check of the 2026-11-14 failure rules: function form, kind object, interceptor, observe, unwrap,
// and the failure the SDK builds itself for an error case it does not know.
import { configureSpar, settle, unwrapOutcome, type CallError, type Outcome } from "../sdk/core.ts";

const seen: string[] = [];
configureSpar({ onFailure: e => seen.push(`onFailure:${e.kind}`), observe: e => seen.push(`observe:${e.kind}`) });
const named: string[] = [];
const call = { contract: "copyJobs", command: "plan" } as const;
const fail = (kind: CallError["kind"]): Outcome => ({ ok: false, error: { kind, code: kind, execution: "unknown", ...call } });
const check = (label: string, actual: unknown, expected: unknown) => {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? "ok  " : "FAIL"} ${label}: ${JSON.stringify(actual)}`);
  if (!ok) process.exitCode = 1;
};

seen.length = 0;
check("function form returns its value", settle(fail("failed"), { success: () => 0, failure: () => "handled" }), "handled");
check("function form: observe only", seen, ["observe:failed"]);

seen.length = 0;
check("kind object, listed kind", settle(fail("unavailable"), { success: () => 0, failure: { unavailable: () => "later" } }), "later");
check("kind object, listed kind: observe only", seen, ["observe:unavailable"]);

seen.length = 0;
check("kind object, other kind gives undefined", settle(fail("failed"), { success: () => 0, failure: { unavailable: () => "later" } }), undefined);
check("kind object, other kind: observe then interceptor", seen, ["observe:failed", "onFailure:failed"]);

seen.length = 0;
check("no failure handler gives undefined", settle(fail("transport"), { success: () => 0 }), undefined);
check("no failure handler: observe then interceptor", seen, ["observe:transport", "onFailure:transport"]);

seen.length = 0;
let thrown = "";
try { unwrapOutcome(fail("rejected")); } catch (e) { thrown = (e as Error).message; }
check("unwrap throws", thrown, "rejected");
check("unwrap: observe, never the interceptor", seen, ["observe:rejected"]);

seen.length = 0;
check("success never reaches the hooks", [settle({ ok: true, value: 5 }, { success: v => v }), seen.length], [5, 0]);

seen.length = 0;
const event = (name: string, payload: unknown): Outcome => ({ ok: true, event: name, payload });
check("event goes to its handler", settle(event("gap", { dropped: 3 }), { success: () => 0, events: { gap: (e: unknown) => (e as { dropped: number }).dropped } }), 3);
check("unhandled optional event gives undefined", settle(event("resumed", {}), { success: () => 0, events: { reset: () => 1 } }), undefined);
check("events never reach the failure hooks", seen, []);

seen.length = 0;
const undeclared: Outcome = { ok: false, code: "tableRenamed", payload: {}, call };
check(
  "undeclared error case is protocol / unknown-error-case, execution unknown",
  settle(undeclared, { success: () => 0, errors: { tableMissing: () => "known" }, failure: (e: unknown) => e }),
  { kind: "protocol", code: "unknown-error-case", execution: "unknown", contract: "copyJobs", command: "plan" });
check("undeclared error case: observe only, with a failure function", seen, ["observe:protocol"]);
check("declared error case goes to its handler", settle({ ok: false, code: "tableMissing", payload: {}, call }, { success: () => 0, errors: { tableMissing: () => "known" } }), "known");

seen.length = 0;
configureSpar({ onFailure: e => named.push(`${e.contract}.${e.command}`), observe: e => named.push(`observe ${e.contract}.${e.command}`) });
settle(fail("failed"), { success: () => 0 });
check("the hooks can say which call failed", named, ["observe copyJobs.plan", "copyJobs.plan"]);
