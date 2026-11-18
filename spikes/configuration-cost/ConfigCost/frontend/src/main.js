// The spike's page: signals ui-ready, asks the backend for the run's details and reports them to standard error.
import { connectSpar, hostChannel } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { probe } from "@spar/app/probe";

const connection = connectSpar(hostChannel(), fingerprint);
connection.ready();

const details = await probe.details().unwrap();
document.getElementById("status").textContent = details;
await probe.report(`details ${details}`);
await probe.report("done");
