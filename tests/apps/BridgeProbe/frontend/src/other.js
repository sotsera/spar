// The bridge probe's second page: a new document, so a new bridge session with its own id.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();
document.getElementById("status").textContent = "Ready";
const echoed = await probe.echo("second").unwrap();
await probe.note(`second document ${connection.document}, echo ${echoed}`).unwrap();

// The history step (M1c step 6): back to the first page, which the host cancels unless the app allowed it. The plan is
// read without counting a start of the first page.
const steps = (await probe.steps().unwrap()).split(",");
if (steps.includes("history")) {
  await probe.note("history: going back from the second document").unwrap();
  history.back();
  await new Promise(resolve => setTimeout(resolve, 2_000));
  await probe.note(`history: still at ${location.pathname}`).unwrap();
}
