// The WireShapes test page: calls the app's backend over the loopback bridge through the app's generated
// @spar/app package, which the test serves under /spar/app/. Each function on `window.wire` returns plain JSON, so a
// Playwright test can compare it.
import { connectSpar, loopbackChannel } from "/spar/app/sdk/sdk.js";
import { echo } from "/spar/app/echo.js";
import { kinds } from "/spar/app/kinds.js";
import { ledger } from "/spar/app/ledger.js";
import { streams } from "/spar/app/streams.js";
import { fingerprint } from "/spar/app/spar.js";
import { blobRoundTrip, exportCsv, readTimes, resources, showPhotos } from "./files.js";

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
  ids: [0n, null], price: "0.000", level: "High",
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

/** A member name the enum does not have, refused the same way. */
function sendBadLevel() {
  return kinds.echo({ ...allKinds(), level: "Urgent" }).match({ success: () => ({ value: "sent" }), failure: describeFailure });
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

/** What a stream or subscription item is, as plain JSON: a row or tick number, an event, the error or the failure. */
const itemOf = item => item.match({
  success: value => (typeof value.n === "bigint" ? Number(value.n) : value.n),
  errors: { unreadable: error => `unreadable ${error.at}` },
  events: { reset: () => "reset", gap: event => `gap ${event.dropped}`, reconnecting: event => `reconnecting ${event.attempt}`, resumed: () => "resumed" },
  failure: error => `${error.kind} ${error.code}`,
});

const itemOfRows = item => item.match({ success: row => row.n, failure: error => `${error.kind} ${error.code}` });

/** All `count` rows of a stream, timed by the page from the call to its last row: whether they came whole and in order. */
async function rows(count) {
  const started = performance.now();
  let next = 0;
  let ordered = true;
  for await (const item of streams.rows(count)) {
    const n = itemOfRows(item);
    ordered &&= n === next;
    next++;
  }

  return { received: next, ordered, ms: performance.now() - started };
}

/** Rows until the declared error that ends the stream. */
async function readUntil(failAt) {
  const seen = [];
  for await (const item of streams.read(failAt)) {
    seen.push(itemOf(item));
  }

  return seen;
}

/** Leaves the loop after `take` rows: the break waits for the backend to confirm it stopped. */
async function breakEarly(take) {
  let taken = 0;
  for await (const _ of streams.rows(1_000_000_000)) {
    if (++taken === take) {
      break;
    }
  }

  return taken;
}

/** Three live ticks, then an abort: the subscription ends with the backend's confirmed cancel as its last item. */
async function ticksThenAbort() {
  const controller = new AbortController();
  const seen = [];
  for await (const item of streams.ticks(0, { signal: controller.signal })) {
    seen.push(itemOf(item));
    if (seen.length === 3) {
      controller.abort();
    }
  }

  return seen;
}

/**
 * `perSecond` ticks offered over one second: how many the page took, whether any were dropped, and how long from the
 * subscription to its last tick, timed by the page.
 */
async function burst(perSecond) {
  const controller = new AbortController();
  const started = performance.now();
  let received = 0;
  let gaps = 0;
  let ms = 0;
  for await (const item of streams.burst(perSecond, { signal: controller.signal })) {
    const seen = itemOf(item);
    if (typeof seen === "number") {
      received++;
      if (received === perSecond) {
        ms = performance.now() - started;
        controller.abort();
      }
    } else if (seen.startsWith("gap")) {
      gaps++;
    }
  }

  return { received, gaps, ms };
}

window.wire = {
  check, echoShapes, big, ratio, abortWait, many, post, who, echoKinds, sendBadByte, sendBadLevel, roundTrips,
  rows, readUntil, breakEarly, ticksThenAbort, burst,
  blobRoundTrip, exportCsv, resources, showPhotos, readTimes,
  document: () => connection.document,
};

connection.ready();
document.getElementById("status").textContent = "ready";

/**
 * Subscribes to live ticks for a test that drops the channel and restores it: `window.ticksSeen` collects what the
 * page sees (values, events, the last failure) until `stopTicks()`.
 */
window.ticksSeen = [];
let ticksControl;
window.startTicks = () => {
  ticksControl = new AbortController();
  void (async () => {
    for await (const item of streams.ticks(0, { signal: ticksControl.signal })) {
      window.ticksSeen.push(itemOf(item));
    }
  })();
};
window.stopTicks = () => ticksControl?.abort();
