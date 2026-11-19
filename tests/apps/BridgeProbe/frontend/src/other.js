// The bridge probe's second page: a new document, so a new bridge session with its own id.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();
document.getElementById("status").textContent = "Ready";
const echoed = await probe.echo("second").unwrap();
await probe.note(`second document ${connection.document}, echo ${echoed}`).unwrap();
