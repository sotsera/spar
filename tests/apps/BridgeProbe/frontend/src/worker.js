// A module worker the host serves (M1c step 4): its own response carries the policy, so eval and new Function are
// blocked here as in the page, and a fetch to another origin is refused by connect-src. On WebKitGTK the refusal in a
// worker raises no violation event and sends no report (docs/evidence/linux-gpu-csp).
const results = {};
try {
  results.eval = String(eval("1 + 1"));
} catch (error) {
  results.eval = `blocked (${error.name})`;
}

try {
  results.function = String(new Function("return 2")());
} catch (error) {
  results.function = `blocked (${error.name})`;
}

fetch("https://blocked.invalid/from-worker").then(
  () => {
    results.fetch = "reached";
    postMessage(results);
  },
  error => {
    results.fetch = `refused (${error.name})`;
    postMessage(results);
  });
