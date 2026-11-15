// The WireShapes test page: calls the app's backend over the loopback bridge through the app's generated
// @spar/app package, which the test serves under /spar/app/. Each function on `window.wire` returns plain JSON, so a
// Playwright test can compare it.
import { connectSpar, loopbackChannel } from "/spar/app/sdk/sdk.js";
import { echo } from "/spar/app/echo.js";
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

window.wire = { check, echoShapes, big, ratio, abortWait, many, document: () => connection.document };

connection.ready();
document.getElementById("status").textContent = "ready";
