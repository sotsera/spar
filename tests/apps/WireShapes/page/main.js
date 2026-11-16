// The WireShapes test page: calls the app's backend over the loopback bridge through the app's generated
// @spar/app package, which the test serves under /spar/app/. Each function on `window.wire` returns plain JSON, so a
// Playwright test can compare it.
import { connectSpar, loopbackChannel } from "/spar/app/sdk/sdk.js";
import { echo } from "/spar/app/echo.js";
import { kinds } from "/spar/app/kinds.js";
import { ledger } from "/spar/app/ledger.js";
import { fingerprint } from "/spar/app/spar.js";

// A test may pretend the page was built against other contracts.
const contracts = new URLSearchParams(location.search).get("contracts") ?? fingerprint;
const connection = connectSpar(loopbackChannel(), contracts);

const describeFailure = error => ({ failure: { kind: error.kind, code: error.code, execution: error.execution, command: error.command } });

function check(name) {
  return echo.check({ name }).match({
    success: item => ({ value: item.name }),
    errors: { refused: error => ({ refused: error.reason }), empty: () => ({ empty: true }) },
    failure: describeFailure,
  });
}

/** Shapes from the test's JSON, where a `long` is a string; the page sends it as the `bigint` the module expects. */
function echoShapes(shapes) {
  return echo.shapes({ ...shapes, big: BigInt(shapes.big) }).match({
    success: value => ({ value: { ...value, big: String(value.big) } }),
    failure: describeFailure,
  });
}

function big(digits) {
  return echo.big({ name: digits }).match({
    success: value => ({ value: `${typeof value} ${value}` }),
    failure: describeFailure,
  });
}

/** A ratio parsed by the backend: a value that is not a number makes the command throw, and the page gets its correlation id. */
function ratio(text) {
  return echo.ratio({ name: text }).match({
    success: value => ({ value }),
    failure: error => ({ failure: { kind: error.kind, code: error.code, correlation: error.correlation ?? null } }),
  });
}

/** Starts a call that runs until cancelled, then aborts it once the call has had time to start. */
function abortWait(afterMs) {
  const controller = new AbortController();
  const outcome = echo.wait({ name: "x" }, { signal: controller.signal });
  setTimeout(() => controller.abort(), afterMs);
  return outcome.match({ success: () => ({ value: "finished" }), failure: describeFailure });
}

/** Many calls at once: they leave in order and each gets its own answer. */
async function many(count) {
  const names = Array.from({ length: count }, (_, i) => `n${i}`);
  const results = await Promise.all(names.map(check));
  return results.map(result => result.value);
}

/** A posting through the contract of a library the app lists in SparContracts; cents travel as strings. */
function post(account, cents) {
  return ledger.post({ account, cents: BigInt(cents) }).match({
    success: entry => ({ value: `${entry.account} ${typeof entry.cents} ${entry.cents}` }),
    errors: { overdrawn: error => ({ overdrawn: String(error.balance) }) },
    failure: describeFailure,
  });
}

/**
 * Every other kind of the wire-rules table, through the generated module both ways: `ulong` as `bigint`, `byte[]` as
 * `Uint8Array`, `decimal` and the dates and times as strings. Returns what came back, with the types the page saw.
 */
const allKinds = () => ({
  b: 255, sb: -128, s: -32768, us: 65535, ui: 4294967295, ul: 18446744073709551615n, f: 0.5, m: "-12.50", c: "é",
  day: "2026-02-28", time: "08:30:00.25", span: "-1.02:30:00", data: new Uint8Array([0, 1, 254, 255]), maybe: null,
  ids: [0n, null], price: "0.000",
});

function echoKinds() {
  return kinds.echo(allKinds()).match({
    success: value => ({
      value: {
        ...value,
        ul: `${typeof value.ul} ${value.ul}`,
        data: `${value.data.constructor.name} ${Array.from(value.data).join(",")}`,
        ids: value.ids.map(id => (id === null ? null : `${typeof id} ${id}`)),
      },
    }),
    failure: describeFailure,
  });
}

/** A value the module refuses before sending: a byte beyond 255. */
function sendBadByte() {
  return kinds.echo({ ...allKinds(), b: 256 }).match({ success: () => ({ value: "sent" }), failure: describeFailure });
}

const who = () => ledger.who().match({ success: value => ({ value }), failure: describeFailure });

/**
 * Small calls one after another, timed by the page from the call to its result: the loopback round trip of the budget
 * table (plan, "Provisional budgets"). The first `warmUp` are left out; returns the rest in milliseconds.
 */
async function roundTrips(count, warmUp) {
  const durations = [];
  for (let i = 0; i < warmUp + count; i++) {
    const started = performance.now();
    await echo.check({ name: "x" });
    if (i >= warmUp) {
      durations.push(performance.now() - started);
    }
  }

  return durations;
}

window.wire = { check, echoShapes, big, ratio, abortWait, many, post, who, echoKinds, sendBadByte, roundTrips, document: () => connection.document };

connection.ready();
document.getElementById("status").textContent = "ready";
