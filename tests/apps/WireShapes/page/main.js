// The WireShapes test page: calls the app's backend over the loopback bridge through the compiled SDK, which the
// test serves under /spar/. Its decoders are written by hand until the client step generates them. Each function on
// `window.wire` returns plain JSON, so a Playwright test can compare it.
import { connectSpar, invoke, loopbackChannel, unknownErrorCase } from "/spar/sdk.js";

const contracts = new URLSearchParams(location.search).get("contracts") ?? "";
const connection = connectSpar(loopbackChannel(), contracts);

/** Reads an `Item`, and the `EchoError` cases, field by field. */
const itemDecoder = {
  value: json => {
    const parsed = JSON.parse(json);
    return { name: String(parsed.name) };
  },
  error: (code, payload) => {
    if (code === "refused") {
      return { reason: String(payload.reason) };
    }

    return code === "empty" ? undefined : unknownErrorCase;
  },
};

/** Keeps a value's JSON as it came, so the test sees exactly what the backend wrote. */
const rawDecoder = { value: json => json, error: () => unknownErrorCase };

const describeFailure = error => ({ failure: { kind: error.kind, code: error.code, execution: error.execution, command: error.command } });

function check(name) {
  return invoke("echo", "check", JSON.stringify({ name }), itemDecoder).match({
    success: item => ({ value: item.name }),
    errors: { refused: error => ({ refused: error.reason }), empty: () => ({ empty: true }) },
    failure: describeFailure,
  });
}

function echoShapes(shapes) {
  return invoke("echo", "shapes", JSON.stringify(shapes), rawDecoder).match({
    success: json => ({ value: JSON.parse(json) }),
    failure: describeFailure,
  });
}

function big(digits) {
  const decoder = { value: json => BigInt(JSON.parse(json)), error: () => unknownErrorCase };
  return invoke("echo", "big", JSON.stringify({ name: digits }), decoder).match({
    success: value => ({ value: `${typeof value} ${value}` }),
    failure: describeFailure,
  });
}

/** Starts a call that runs until cancelled, then aborts it once the call has had time to start. */
function abortWait(afterMs) {
  const controller = new AbortController();
  const outcome = invoke("echo", "wait", JSON.stringify({ name: "x" }), itemDecoder, { signal: controller.signal });
  setTimeout(() => controller.abort(), afterMs);
  return outcome.match({ success: () => ({ value: "finished" }), failure: describeFailure });
}

/** Many calls at once: they leave in order and each gets its own answer. */
async function many(count) {
  const names = Array.from({ length: count }, (_, i) => `n${i}`);
  const results = await Promise.all(names.map(check));
  return results.map(result => result.value);
}

window.wire = { check, echoShapes, big, abortWait, many, document: () => connection.document };

connection.ready();
document.getElementById("status").textContent = "ready";
